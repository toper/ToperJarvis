# Smart Turn v3 + Silero VAD — sygnatura I/O i preprocessing (Task 1.3 SPIKE)

Ustalenia z inspekcji realnych grafów ONNX (`onnxruntime` w Pythonie 3.14,
`Microsoft.ML.OnnxRuntime` 1.20.1 użyje tych samych nazw/kształtów) oraz źródeł
`pipecat-ai/smart-turn` (branch `main`, stan na 2026-07-12).

Modele pobrane do (gitignored, patrz `.gitignore` + `assets/download-endpointing-models.ps1`):
- `assets/smartturn/smart-turn-v3.1-cpu.onnx` (8 679 180 B)
- `assets/silero/silero_vad.onnx` (2 243 022 B)

## DECYZJA (do Task 1.4/1.5/1.6)

**Smart Turn v3.1: graf przyjmuje PRECOMPUTED LOG-MEL, nie surowe PCM.**
→ **Task 1.4 (ekstrakcja mela w C#) JEST WYMAGANY.** Trzeba dokładnie
odtworzyć Whisper-owy log-mel (parametry niżej) w .NET, bo w Pythonie robi to
`WhisperFeatureExtractor` z biblioteki `transformers`, a w naszym stosie nie
mamy tej zależności.

**Silero VAD: graf przyjmuje SUROWE PCM.** → mel nie dotyczy VAD-a, karmimy
go bezpośrednio próbkami float32.

---

## 1. Smart Turn v3.1 (`smart-turn-v3.1-cpu.onnx`)

### Sygnatura grafu (potwierdzona `InferenceSession.get_inputs()/get_outputs()`)

| | nazwa | typ | kształt |
|---|---|---|---|
| IN  | `input_features` | `float32` | `[batch, 80, 800]` (batch dynamiczny, `80` i `800` stałe) |
| OUT | `logits`         | `float32` | `[batch, 1]` |

Uwaga: mimo nazwy `logits`, **wyjście jest JUŻ po sigmoidzie** — potwierdzone
empirycznie (dummy input o skali 0.001..10000 zawsze dawał wynik w (0,1);
zera na wejściu dają ok. 0.979). Zgadza się to z komentarzem w `predict.py`
repo źródłowego: *"ONNX model returns sigmoid probabilities"*. **Nie stosować
dodatkowego sigmoida w C# — użyć wartości wyjściowej wprost jako
prawdopodobieństwa.**

Próg decyzyjny w kodzie referencyjnym (`predict.py`):
```python
prediction = 1 if probability > 0.5 else 0   # 1 = Complete (koniec wypowiedzi)
```
→ **próg completion = 0.5**.

### Preprocessing (dokładnie, z `inference.py` + `audio_utils.py` repo `pipecat-ai/smart-turn`)

Uwaga: w repo źródłowym NIE MA pliku `model.py` (404) — architektura modelu
(trening) jest w `train.py`/`train_modal.py`, ale do inferencji ONNX to
nieistotne — liczy się tylko preprocessing przed grafem, opisany w
`inference.py`.

1. **Wejście:** surowy PCM float32, sample rate **16000 Hz**, mono, zakres
   docelowy [-1, 1] (w `predict.py`/CLI normalizowane przez
   `audio / max(abs(audio))` jeśli przekracza 1.0 — to dzieje się PRZED
   krokiem 2, nie jest częścią feature extractora).

2. **Przytnij/wypaduj do dokładnie 8 sekund** (`truncate_audio_to_last_n_seconds`,
   `audio_utils.py`):
   - `max_samples = 8 * 16000 = 128000`
   - jeśli dłużej niż 128000 próbek → weź **ostatnie** 128000 (obcinaj od
     początku, zachowaj koniec — end of turn zwykle jest na końcu).
   - jeśli krócej → **dopaduj zerami NA POCZĄTKU** (`np.pad(audio, (padding, 0))`,
     `mode='constant', constant_values=0`) tak, by długość = 128000.
   - jeśli dokładnie 128000 → bez zmian.

3. **`WhisperFeatureExtractor(chunk_length=8)` z HF `transformers`**, wywołany z
   `sampling_rate=16000, padding="max_length", max_length=128000, truncation=True,
   do_normalize=True`. Realne parametry ekstraktora (odczytane z instancji):
   - `feature_size` (liczba binów mel) = **80**
   - `hop_length` = **160**
   - `n_fft` = **400**
   - `sampling_rate` = **16000**
   - `chunk_length` = 8 → `n_samples` = 128000, `nb_max_frames` = **800**
   - `dither` = 0.0 (brak ditheringu)
   - `padding_value` = 0.0

   Kroki liczenia (`_np_extract_fbank_features` + `__call__`, dokładny odczyt
   źródła `transformers`):

   a. **Normalizacja waveformu PRZED melem** (bo `do_normalize=True`):
      ```
      normed = (x - mean(x[:length])) / sqrt(var(x[:length]) + 1e-7)
      ```
      liczone po CAŁYM buforze 128000 próbek (attention_mask = same jedynki,
      bo długość po kroku 2 już = max_length, więc `length = 128000`).
      **To jest zero-mean/unit-variance normalizacja SUROWEGO PCM, różna od
      normalizacji log-mela w kroku (d) niżej — obie muszą być zaimplementowane.**

   b. **STFT**: okno **Hann** długości `n_fft=400`, `hop_length=160`,
      power spectrogram (`power=2.0`, czyli moduł^2 zespolonego STFT),
      bez ditheringu. Liczba ramek: `1 + n_samples/hop_length = 801`, ale
      **ostatnia ramka jest odrzucana** (`log_spec[:, :-1]`) → finalnie **800 ramek**.
      (STFT centrowany — biblioteka HF dopełnia sygnał tak, jak robi to Whisper
      oryginalnie; w C# odtworzyć klasyczne centered STFT z paddingiem
      `n_fft/2` po obu stronach przed liczeniem pierwszej/ostatniej ramki,
      zgodnie z konwencją `torch.stft(..., center=True)` / `librosa.stft`.)

   c. **Filtry mel**: `mel_filter_bank(num_frequency_bins=1+n_fft/2=201,
      num_mel_filters=80, min_frequency=0.0, max_frequency=8000.0,
      sampling_rate=16000, norm="slaney", mel_scale="slaney")` — czyli
      **skala mel typu Slaney** (nie HTK!), znormalizowana filtrami Slaney
      (tak jak librosa `mel(..., norm='slaney', htk=False)`), zakres
      częstotliwości 0–8000 Hz (Nyquist dla 16 kHz).

   d. **Log + normalizacja** (dokładny kod źródłowy):
      ```python
      log_spec = log10(power_mel_spectrogram)   # log_mel="log10"
      log_spec = maximum(log_spec, log_spec.max() - 8.0)
      log_spec = (log_spec + 4.0) / 4.0
      ```
      (standardowa normalizacja log-mela Whispera — dynamic range clip do 80 dB,
      potem przeskalowanie do orientacyjnie [-1, 1]).

   e. Wynik: `input_features` kształtu `[80, 800]` (mel_bins x frames) na
      przykład; w kodzie Pythona robi się `squeeze(0)` + `expand_dims(axis=0)`
      żeby dostać batch=1 → finalnie `[1, 80, 800]` float32, dokładnie zgodne
      z metadanymi grafu ONNX.

4. **Wejście do sesji ONNX:** `session.run(None, {"input_features": input_features})`.
5. **Wyjście:** `outputs[0][0].item()` = prawdopodobieństwo (już po sigmoidzie,
   patrz wyżej). `> 0.5` → Complete.

### Do zaimplementowania w Task 1.4 (mel w C#)
- Padding/truncation do 128000 próbek @16kHz (koniec zachowany, zera na początku).
- Zero-mean/unit-variance normalizacja SUROWEGO sygnału (cały bufor 128000).
- STFT: Hann(400), hop=160, n_fft=400, center=True, power=2.0, 801 ramek → obetnij do 800.
- Mel filterbank: 80 filtrów, 201 binów częstotliwości, 0–8000 Hz, slaney scale + slaney norm.
- log10 → clamp do (max-8) → `(x+4)/4`.
- Wyjściowy tensor `[1, 80, 800]` float32, nazwa wejścia grafu: `input_features`.

---

## 2. Silero VAD (`silero_vad.onnx`, wariant `onnx-community/silero-vad`)

### Sygnatura grafu (potwierdzona `InferenceSession.get_inputs()/get_outputs()`)

| | nazwa | typ | kształt |
|---|---|---|---|
| IN  | `input` | `float32` | `[batch, N]` (dynamiczny — akceptuje zarówno 512, jak i 576 próbek w testach) |
| IN  | `state` | `float32` | `[2, batch, 128]` |
| IN  | `sr`    | `int64`   | scalar `[]` |
| OUT | `output`  | `float32` | `[batch, 1]` — prawdopodobieństwo mowy |
| OUT | `stateN`  | `float32` | `[2, batch, 128]` — nowy stan do przekazania w kolejnym wywołaniu |

### Preprocessing i użycie (z referencyjnego wrappera `record_and_predict.py` w repo `smart-turn`)

- **Sample rate: 16000 Hz**, mono, PCM float32 w zakresie [-1, 1]
  (konwersja z int16: `int16.astype(float32) / 32768.0`).
- **Rozmiar ramki (chunk): 512 próbek** przy 16 kHz (to jest wymóg
  oryginalnego modelu Silero VAD, nie tego pliku ONNX per se — plik toleruje
  inne długości, ale referencyjny model był trenowany/używany z 512).
- **Kontekst (context) 64 próbki**: przed każdym wywołaniem doklej **ostatnie
  64 próbki poprzedniego chunku** przed bieżącym chunkiem 512 →
  **`input` ma faktycznie 576 próbek** (`[1, 576]`), nie 512:
  ```python
  x = concatenate((context[64], chunk[512]), axis=1)  # → [1, 576]
  # po wywołaniu: context = x[:, -64:]  (ostatnie 64 próbki na kolejny raz)
  ```
  (potwierdzone empirycznie: graf działa też z samym 512, ale referencyjna
  implementacja i najlepsza zgodność z oryginalnym Silero VAD wymaga
  konkatenacji kontekstu 64 → 576. **Task 1.6 powinien użyć wariantu 576.**)
- **Stan (`state`)**: float32 `[2, 1, 128]`, inicjalizowany zerami na starcie
  strumienia; aktualizowany po każdym wywołaniu (`out, state = session.run(...)`
  — drugi element wyjścia to nowy stan, podstawiany jako `state` w kolejnym
  wywołaniu). Referencyjny kod resetuje stan co 5 s (`MODEL_RESET_STATES_TIME
  = 5.0`) żeby uniknąć dryfu przy długich strumieniach ciszy — Task 1.6 może
  przyjąć podobną politykę resetu.
- **`sr`**: skalar `int64` = `16000` (informuje graf o trybie 16 kHz vs 8 kHz;
  wpływa wewnętrznie na dobór okna/downsamplingu w grafie Silero).
- **Wyjście**: `output[0][0]` = prawdopodobieństwo mowy (już w [0,1], nie
  logit — sprawdzone empirycznie, wartości dla zer na wejściu ≈ 0.012).
  **Próg VAD w kodzie referencyjnym: `VAD_THRESHOLD = 0.5`.**

### Do zaimplementowania w Task 1.6 (wrapper Silero)
- Bufor rolling-context 64 próbek między wywołaniami (stan trzymany w wrapperze).
- Inicjalizacja `state` = zera `[2,1,128]`, aktualizacja co wywołanie z drugiego
  wyjścia grafu.
- `sr` = stała `int64` tensor ze skalarem 16000.
- Brak dodatkowego sigmoida — `output` to już prawdopodobieństwo.
- Sugerowany reset stanu po ~5 s ciszy (opcjonalnie, do potwierdzenia w 1.6).

---

## Źródła

- `https://raw.githubusercontent.com/pipecat-ai/smart-turn/main/inference.py`
- `https://raw.githubusercontent.com/pipecat-ai/smart-turn/main/predict.py`
- `https://raw.githubusercontent.com/pipecat-ai/smart-turn/main/audio_utils.py`
- `https://raw.githubusercontent.com/pipecat-ai/smart-turn/main/record_and_predict.py`
  (zawiera referencyjny wrapper Silero VAD — w repo smart-turn NIE MA osobnego
  `model.py`, ta wiedza była błędnym założeniem brief'u zadania)
- Źródło `transformers.WhisperFeatureExtractor.__call__` /
  `_np_extract_fbank_features` / `zero_mean_unit_var_norm` (pakiet `transformers`,
  zainstalowany lokalnie w Python 3.14 do weryfikacji parametrów).
- Bezpośrednia inspekcja grafów ONNX przez `onnxruntime.InferenceSession`
  (`get_inputs()`/`get_outputs()`) na pobranych plikach — patrz sekcje wyżej.
- Empiryczny test wartości wyjścia (skala wejścia 0.001–10000, zera) — obie
  sieci zwracają wartości ściśle w (0,1), potwierdzając sigmoid już wewnątrz grafu.
