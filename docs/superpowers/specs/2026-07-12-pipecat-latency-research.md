# Research: przyspieszenie STT → LLM → TTS w Jarvisie na wzór Pipecat

**Data:** 2026-07-12
**Autor:** research na zlecenie (Jaroslaw Karlik)
**Status:** research zakończony → wejście do planu implementacji
**Zakres:** co zaczerpnąć z [Pipecat](https://docs.pipecat.ai/overview/pipecat), żeby skrócić odczuwalną latencję pętli głosowej Jarvisa. **Kierunek przyjęty: wszystko w .NET (bez Pythona), wzorce + gotowe modele ONNX.**

---

## 1. Punkt wyjścia — obecny pipeline Jarvisa

Orkiestracja: `src/ToperJarvis.Core/JarvisOrchestrator.cs` (`IAssistantOrchestrator`), start z `App.axaml.cs:42-49`.

| Etap | Implementacja | Charakter |
|---|---|---|
| 0. Capture | `NAudioCapture` (`Speech/Audio/NAudioCapture.cs`) — WaveInEvent, mono 16 kHz 16-bit, bufor 100 ms | strumień `AudioFrame` (fan-out) |
| 1. Start tury | wake-word (openWakeWord `hey_jarvis`, alt. Porcupine) lub PTT (RightCtrl, `WindowsPushToTalkHotkey`) | tylko gdy `State == Idle` |
| 2. Endpointing | `VadBuffer` (`Speech/Vad/VadBuffer.cs`) — **energetyczny RMS** z histerezą; koniec po **0,7 s ciszy** (`SilenceSeconds`) | **batch** — nic nie leci do STT przed końcem |
| 3. STT | `WhisperSpeechToText` (`Speech/Stt/WhisperSpeechToText.cs`) — Whisper.net (whisper.cpp) **CUDA**, model `ggml-base.bin`, `pl` | **batch** — cała wypowiedź w jednym `ProcessAsync(float[])` |
| 4. LLM | Hektor na Hermes (OpenAI-compatible vLLM `192.168.7.30:8000`), `GetStreamingResponseAsync` | **streaming token-po-tokenie**; tooling po stronie Hektora (MCP) |
| 5. TTS | `SentenceAccumulator` → `Channel<string>` worker → `PiperTextToSpeech` (piper.exe `--json-input`, głos `geralt.onnx`) | **zdanie-po-zdaniu, całe WAV** potem odtwarzanie |

### Gdzie realnie tracimy czas (wąskie gardła)
- **A. Endpointing:** stałe **0,7 s ciszy** dodawane do każdej tury, na prymitywnym progu RMS. To *czysta strata* — największy pojedynczy koszt odczuwalny.
- **B. STT batch:** transkrypcja startuje dopiero po całej wypowiedzi; brak partiali.
- **C. Brak nakładania STT↔LLM:** pełne STT przed pierwszym tokenem.
- **D. TTS whole-WAV:** Piper pisze cały plik zdania, dopiero potem odtwarzanie; brak streamingu PCM; kolejne zdania sekwencyjnie.
- **E. Brak barge-in:** `_turnGate` + „wake tylko w Idle" — nie da się wejść w słowo głosem (tylko Esc → `Interrupt()`).

### Co już mamy dobrze (główny trik Pipecat już zrobiony)
LLM→TTS jest **pipeline'owany**: `SentenceAccumulator` emituje zdanie gdy tylko się domknie, `Channel` worker syntezuje je gdy model generuje kolejne. To najważniejsza optymalizacja Pipecat — u nas już działa.

---

## 2. Model Pipecat — czego się uczymy

Pipecat (Python) osiąga round-trip **500–800 ms** dzięki **architekturze przepływu ramek**: pipeline współbieżnych procesorów, przez które płyną drobne ramki (audio/tekst/kontrola). Etapy się **nakładają** zamiast czekać na siebie. Kluczowe mechanizmy do zapożyczenia (jako wzorce, nie kod):

1. **Nakładanie etapów** — mamy dla LLM→TTS; brakuje dla capture→STT→LLM.
2. **Sentence-level TTS** — mamy.
3. **Interruptions / barge-in** — user mówi → zaległe ramki (w tym TTS) kasowane. **Brakuje.**
4. **Smart Turn Detection** — neuronowy endpointing zamiast progu ciszy. **Brakuje.**
5. **TTS cache** dla powtarzalnych fraz. **Brakuje.**

---

## 3. DEEP-DIVE 1 — Smart Turn v3 jako model ONNX w .NET

### Który model wziąć
- **Źródło oficjalne:** [`pipecat-ai/smart-turn-v3` (HuggingFace)](https://huggingface.co/pipecat-ai/smart-turn-v3/tree/main). Pliki:
  - `smart-turn-v3.0.onnx` (8,76 MB)
  - `smart-turn-v3.1-cpu.onnx` (8,68 MB) — **rekomendowany do CPU**
  - `smart-turn-v3.1-gpu.onnx` (32,4 MB) — dla GPU
  - Repo aktualizuje się dalej (README wspomina już v3.2) — przy implementacji wziąć najnowszy stabilny `*-cpu.onnx`.
- **Alternatywa:** [`onnx-community/smart-turn-v3-ONNX`](https://huggingface.co/onnx-community/smart-turn-v3-ONNX) (export pod transformers.js; README pusty — traktować jako plan B).
- **Kod referencyjny:** [`github.com/pipecat-ai/smart-turn`](https://github.com/pipecat-ai/smart-turn) — pliki `model.py`, `inference.py`, `predict.py` (to jest nasz wzorzec preprocessingu do przepisania na C#).

### Charakterystyka modelu
- **Architektura:** enkoder **Whisper Tiny** (39M) + liniowa głowa klasyfikacyjna z Smart Turn v2 → razem **8M parametrów**, kwantyzacja **int8 (QAT)**.
- **Wejście:** **16 kHz mono PCM, do 8 s**. Jeśli krócej — **padding zerami z PRZODU** (audio na końcu wektora). To dokładnie pasuje do naszego formatu z `NAudioCapture` (16 kHz mono).
- **Wyjście:** prawdopodobieństwo „user skończył turę" → próg decyzyjny.
- **Języki:** 23, w tym **🇵🇱 polski** (potwierdzone) — istotne, bo Jarvis mówi po polsku.
- **Latencja:** CPU ~12–60 ms (int8), GPU 3–7 ms. Preprocessing (mel) ~3 ms. Dla nas: pomijalne względem zysku 0,3–0,6 s.

### Wykonalność w .NET — ocena
- **Inference:** trywialne. `Microsoft.ML.OnnxRuntime` (mamy już CUDA w stacku); dla tak małego modelu **CPU EP w zupełności wystarczy** (int8, ~15 ms), co upraszcza wdrożenie (bez konkurencji o GPU z Whisperem).
- **⚠️ GŁÓWNE RYZYKO: preprocessing log-mel.** Enkoder Whisper przyjmuje **log-mel spektrogram (80 binów)**, nie surowe PCM. Graf ONNX Smart Turn najprawdopodobniej **NIE zawiera** ekstrakcji cech — `inference.py` liczy mel na zewnątrz (torch/transformers `WhisperFeatureExtractor`). Czyli w .NET musimy **zaimplementować ekstrakcję log-mel zgodną z Whisperem** (STFT, hann window, filtry mel 16 kHz/80, log, normalizacja) albo znaleźć export ONNX z melem „wbudowanym" w graf.
  - **Zadania do potwierdzenia w implementacji** (z `inference.py`): dokładny kształt tensora wejściowego (np. `[1, 80, N]`), czy mel jest paddowany do stałej długości (Whisper tiny bazowo 3000 ramek = 30 s; przy 8 s trzeba sprawdzić czy pad do 3000 czy do 800), nazwy wejść/wyjść grafu, próg decyzyjny.
  - **Mitygacja A (preferowana):** port ekstrakcji mel do C# — istnieją referencyjne implementacje (whisper.cpp `log_mel_spectrogram`, kilka portów C#). ~1 plik, dobrze testowalny (porównanie do wyjścia Pythona na tych samych próbkach).
  - **Mitygacja B (fallback):** sprawdzić czy `onnx-community` lub inny export ma mel w grafie → wtedy karmimy surowe PCM i problem znika.
  - **Mitygacja C (ostateczność):** mikroskopijny sidecar Python tylko dla Smart Turn — sprzeczne z kierunkiem, odradzane.

### Jak wpiąć w Jarvisa
- Nowy analizator obok/zamiast `VadBuffer`: Silero VAD (ONNX) jako tania bramka „jest mowa", a **Smart Turn** wywoływany na buforze bieżącej tury, gdy VAD wykryje pauzę — decyduje „koniec czy user jeszcze mówi". Zastępuje sztywne `SilenceSeconds = 0,7`.
- **Silero VAD** też jest ONNX (plan: dołożyć razem ze Smart Turn; oba przez ten sam ONNX Runtime). To podnosi jakość detekcji mowy vs obecny RMS.

---

## 4. DEEP-DIVE 2 — AEC (echo cancellation) dla barge-in

### Dlaczego to warunek konieczny barge-in
Barge-in = nasłuch mikrofonu **w trakcie** mówienia Jarvisa. Bez AEC mikrofon łapie własny głos Jarvisa (z głośników) → VAD/Smart Turn uznają to za mowę usera → **Jarvis przerywa sam sobie**. AEC usuwa z sygnału mikrofonu to, co właśnie gra w głośnikach.

### Fakt kluczowy (ułatwiający): mamy sygnał referencyjny
Jarvis **sam odtwarza** TTS przez NAudio → mamy dokładny PCM „far-end" (to, co gra). AEC potrzebuje: sygnał near-end (mikrofon) + far-end (odtwarzane) + znajomość opóźnienia systemowego. Referencję mamy za darmo; trudność to **wyrównanie czasowe** near/far.

### Opcje dla .NET / Windows 11 (host to Windows 11 Pro 26200)

**Opcja AEC-1 — Wbudowany AEC Windows 11 (rekomendowana do ewaluacji jako pierwsza).**
[`IAcousticEchoCancellationControl`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iacousticechocancellationcontrol) (Win32/WASAPI, Windows 11 22000+). System robi AEC, my tylko wskazujemy render endpoint jako referencję ([Windows 11 APO APIs](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/windows-11-apis-for-audio-processing-objects)). Nowość: model-based (ML) echo cancellation w Microsoft Audio Stack.
- ➕ OS robi całą robotę, najlepsza integracja, brak własnej biblioteki DSP, brak ręcznego wyrównywania.
- ➖ Wymaga Win11 + wsparcia sterownika/urządzenia; NAudio `WasapiCapture` nie eksponuje tego wprost → potrzebny **własny interop** (ustawienie kategorii „communications"/raw i `IAcousticEchoCancellationControl.SetEchoCancellationRenderEndpoint`). Ryzyko: nie każde urządzenie/sterownik wspiera.

**Opcja AEC-2 — WebRTC APM przez [`SoundFlow.Extensions.WebRtc.Apm`](https://www.nuget.org/packages/SoundFlow.Extensions.WebRtc.Apm) (najlepsza jakość, przenośna).**
WebRTC AEC3 (ten sam, co w przeglądarkach) — `WebRtcApmModifier(aecEnabled: true, aecMobileMode: false, aecLatencyMs: 40, nsEnabled: true, ...)`. Pakiet .NET 8, ma też NS/AGC/HPF.
- ➕ Sprawdzony AEC3 (usuwa 20–40 dB echa), NS + AGC gratis, cross-platform, aktywnie utrzymywany.
- ➖ API zaprojektowane pod graf audio SoundFlow (`MicrophoneDataProvider`/`SoundPlayer`) — trzeba albo poprowadzić capture przez SoundFlow, albo sięgnąć do natywnego bindingu APM bezpośrednio i podawać far-end do `ProcessReverseStream`. Ramki 10 ms, trzeba karmić near i far.

**Opcja AEC-3 — [`SpeexDSPSharp.Core`](https://www.nuget.org/packages/SpeexDSPSharp.Core) (najprostsza ręczna).**
`SpeexDSPEchoCanceler` — podajesz ramki near + far, dostajesz sygnał bez echa. Lekki, cross-platform (Win/Linux/Android/iOS).
- ➕ Minimalny, łatwy do wpięcia ręcznie z NAudio (mamy pełną kontrolę nad buforami).
- ➖ Starszy algorytm, słabszy od AEC3 przy trudniejszej akustyce; sami zarządzamy wyrównaniem near/far i długością ogona echa.

**Pomocniczo (nie AEC, ale przydatne):** [`WebRtcVadSharp`](https://www.nuget.org/packages/WebRtcVadSharp) — WebRTC VAD (GMM) jako lepsza od RMS bramka mowy, gdyby Silero okazał się zbędny.

### Rekomendacja AEC
1. **Najpierw zweryfikować Opcję AEC-1 (Windows 11 native)** — jeśli działa na sprzęcie usera, to najmniej kodu i najlepsza integracja.
2. **Fallback: AEC-2 (WebRTC APM)** — jeśli native zawiedzie lub ma być przenośnie; najlepsza jakość spośród bibliotek.
3. **AEC-3 (Speex)** tylko jako lekka alternatywa, gdyby APM sprawiał problemy z integracją.
4. **Etap pośredni bez AEC:** „half-duplex z bramką" — w trakcie TTS nasłuch tylko na *głośną* mowę (wysoki próg) do wykrycia intencji przerwania; po wykryciu — pauza TTS i pełny nasłuch. Tani sposób na *pierwszą wersję* barge-in bez ryzyka samo-przerywania, choć gorszy niż prawdziwe AEC.

---

## 5. DEEP-DIVE 3 — Czy da się zrobić streaming w Whisper.net?

### Stan faktyczny
- [`Whisper.net`](https://github.com/sandrohanea/whisper.net) (aktualnie 1.9.x) opakowuje **whisper.cpp**, który **nie ma prawdziwego API streamingowego** — przetwarza podany bufor w całości i zwraca segmenty. `ProcessAsync(float[])` (używane już w `WhisperSpeechToText.cs`) jest **batchowe**.
- „Streaming" w whisper.cpp (przykład `stream`) to **sliding-window + ponowna transkrypcja** okna, nie inkrementalny dekoder.

### Jak wyglądałby streaming w .NET (gdybyśmy chcieli)
Wzorzec **sliding-window + LocalAgreement-2**:
- Co ~0,5 s wołać `ProcessAsync` na przesuwającym się oknie ~5–8 s bieżącej mowy.
- Emitować jako „pewny" tylko prefiks potwierdzony przez 2 kolejne iteracje; przewijać bufor po potwierdzonym zdaniu.
- Da się zbudować na istniejącym `ProcessAsync(float[])` — **bez zmian w samym Whisper.net**.

### Koszt i ryzyka
- **Wielokrotna re-transkrypcja** tego samego audio → N× więcej pracy GPU/CPU na turę (na modelu `base` CUDA wykonalne, ale nie darmowe; konkurencja o GPU z resztą).
- **Gorsza dokładność partiali**, granice słów/zdań się „ślizgają", trzeba stitchingu.
- Referencyjne projekty (`ufal/whisper_streaming`) osiągają ~3,3 s latencji — czyli streaming Whispera i tak nie jest „natychmiastowy".

### ⚖️ Werdykt: **NAJNIŻSZE ROI, najwyższy nakład — odłożyć.**
Powód: odczuwalna latencja Jarvisa jest zdominowana przez (1) **0,7 s martwej ciszy** (endpointing) i (2) **TTFT LLM-a**, a **nie** przez czas liczenia STT (batch `base` CUDA na krótkiej wypowiedzi to ~0,1–0,3 s). Naprawa endpointingu (Deep-dive 1) usuwa większość „czekania" znacznie taniej niż streaming STT.
- Jeśli **kiedykolwiek** czas samego STT stanie się wąskim gardłem: taniej niż hakowanie Whisper.net będzie (a) mniejszy/szybszy model (distil/`small`→`base`), albo (b) **Opcja 3 — sidecar `faster-whisper`/`whisper_streaming`** (CTranslate2). To jednak wprowadza Python — świadomy kompromis na przyszłość, poza obecnym kierunkiem.

---

## 6. Roadmapa (kolejność wg ROI ÷ wysiłek)

| # | Zmiana | Cel (wąskie gardło) | Wysiłek | Ryzyko | Zysk |
|---|---|---|---|---|---|
| **1** | **Smart Turn v3 + Silero VAD (ONNX)** zamiast RMS `VadBuffer` | A (0,7 s martwej ciszy) | Średni (ryzyko = port mel do C#) | Niskie/Średnie | **0,3–0,6 s na każdej turze** |
| **2** | **Streaming PCM w Piper** (`--output-raw` → `BufferedWaveProvider`) | D | Niski/Średni | Niskie | Krótszy time-to-first-audio, płynność |
| **3** | **TTS cache** dla fraz-wypełniaczy (+ natychmiastowy filler po endpointingu maskujący TTFT) | „ociężałość" + TTFT | Niski | ~zero | Percepcja natychmiastowej reakcji |
| **4** | **Barge-in** (nasłuch w trakcie TTS → flush `Channel`/stop `WaveOut`/`Interrupt`) + **AEC** | E | Średni | **Średni (AEC)** | Naturalne przerywanie głosem |
| **5** | **Streaming STT** (sliding-window) — **tylko jeśli po 1–4 nadal za wolno** | B, C | Wysoki | Średni/Wysoki | Niepewny; nakładanie STT↔LLM |

**Sekwencja rekomendowana:** 1 → 2 → 3 → 4 → (5 warunkowo).
Uzasadnienie: 1 i 2/3 dają najszybciej odczuwalny efekt niskim kosztem; 4 wymaga AEC (główne ryzyko), więc po zbudowaniu bazy; 5 tylko warunkowo.

---

## 7. Rekomendacja końcowa
- **Robić:** kroki 1–4 w .NET, przyrostowo, każdy osobno testowalny i mierzalny (mierzyć: czas od końca mowy do pierwszego dźwięku TTS, przed/po).
- **Nie robić teraz:** pełnego streaming STT (5) ani side-caru Pythonowego — brak uzasadnienia ROI dopóki 1–4 nie wyczerpane.
- **Największe pojedyncze ryzyko techniczne:** port ekstrakcji log-mel do C# dla Smart Turn (krok 1) oraz dobór/integracja AEC (krok 4) — oba mają zdefiniowane fallbacki powyżej.

## 8. Źródła
- Pipecat: [overview](https://docs.pipecat.ai/overview/pipecat), [Smart Turn](https://docs.pipecat.ai/api-reference/server/utilities/turn-detection/smart-turn-overview), [Whisper STT](https://docs.pipecat.ai/api-reference/server/services/stt/whisper), [Frames](https://docs.pipecat.ai/api-reference/server/frames/overview.md)
- Smart Turn v3: [model (HF)](https://huggingface.co/pipecat-ai/smart-turn-v3), [repo](https://github.com/pipecat-ai/smart-turn), [blog v3 „12ms"](https://www.daily.co/blog/announcing-smart-turn-v3-with-cpu-inference-in-just-12ms/), [blog v3.1](https://www.daily.co/blog/improved-accuracy-in-smart-turn-v3-1/)
- AEC: [IAcousticEchoCancellationControl](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iacousticechocancellationcontrol), [Windows 11 APO APIs](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/windows-11-apis-for-audio-processing-objects), [SoundFlow.Extensions.WebRtc.Apm](https://www.nuget.org/packages/SoundFlow.Extensions.WebRtc.Apm), [SpeexDSPSharp.Core](https://www.nuget.org/packages/SpeexDSPSharp.Core), [WebRtcVadSharp](https://www.nuget.org/packages/WebRtcVadSharp)
- Whisper.net: [repo](https://github.com/sandrohanea/whisper.net), [NuGet](https://www.nuget.org/packages/Whisper.net/), [ufal/whisper_streaming](https://github.com/ufal/whisper_streaming)
