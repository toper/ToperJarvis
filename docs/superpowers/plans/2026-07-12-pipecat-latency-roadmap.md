# Przyspieszenie pętli głosowej STT→LLM→TTS (roadmapa 1–4) — Plan implementacji

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Skrócić odczuwalną latencję rozmowy z Jarvisem, przenosząc do .NET wzorce Pipecat: neuronowy endpointing (Smart Turn v3 + Silero VAD), streaming PCM w TTS, cache fraz + filler, oraz barge-in z AEC.

**Architecture:** Zmiany przyrostowe w istniejącym `JarvisOrchestrator`. Wprowadzamy abstrakcję endpointingu (`IEndpointDetector`), tak że obecny `VadBuffer` (RMS) i nowy `NeuralEndpointDetector` (ONNX) są wymienne przez konfigurację. Modele ONNX (Silero VAD, Smart Turn v3) uruchamiane przez `Microsoft.ML.OnnxRuntime` na CPU, z własną ekstrakcją log-mel walidowaną złotym wektorem. TTS przechodzi na strumień PCM z Pipera do `BufferedWaveProvider`. Dekorator cache'ujący owija `ITextToSpeech`. Barge-in włącza nasłuch w stanie `Speaking` z AEC czyszczącym echo głośników.

**Tech Stack:** .NET 10, xUnit, `Microsoft.ML.OnnxRuntime`, NAudio 2.2.1, Whisper.net 1.9.1, Microsoft.Extensions.AI, Piper, ONNX (Silero VAD, Smart Turn v3).

## Global Constraints

- Wszystkie projekty: **net10.0** (App/Platform.Windows: **net10.0-windows**); `Nullable`, `ImplicitUsings`, `LangVersion=latest` dziedziczone z `Directory.Build.props` — nie nadpisywać.
- Testy: **xUnit 2.5.3**, gołe `Assert.*` — **bez FluentAssertions, bez Moq/NSubstitute**. Fakes pisane ręcznie. Nazwy metod testowych: **polski snake_case** (np. `Cisza_po_mowie_konczy_ture`).
- Testy DI: prawdziwy `ServiceCollection` + `Options.Create(...)`.
- Nowe modele/biblioteki natywne dokłada się **tylko do `src/ToperJarvis.Speech`** (jedyny projekt z NAudio/Whisper/ONNX-transitive).
- Commity: częste, po każdym zielonym teście. Format: `feat:`/`refactor:`/`test:`/`chore:` + opis PL. **Bez stopki Co-Authored-By/Claude.**
- Assets (modele .onnx) trafiają do `assets/` i są kopiowane do output (`CopyToOutputDirectory`), analogicznie do istniejących `assets/whisper`, `assets/piper`.
- Konfiguracja: sub-sekcje w `JarvisOptions` (`Jarvis:...`), wiązane przez `IOptions<JarvisOptions>`; serwisy czytają `.Value.<Sekcja>`.
- Whisper.net i Piper mają wzorzec „brak pliku modelu → log ostrzeżenie, degradacja zamiast wyjątku" — nowe komponenty trzymają ten sam styl.

---

## Struktura plików (co powstaje / co się zmienia)

**Faza 1 — endpointing (ONNX):**
- Create: `src/ToperJarvis.Speech/Endpointing/IEndpointDetector.cs` — wspólny kontrakt endpointingu.
- Modify: `src/ToperJarvis.Speech/Vad/VadBuffer.cs` — implementuje `IEndpointDetector`.
- Create: `src/ToperJarvis.Speech/Endpointing/WhisperMelSpectrogram.cs` — ekstrakcja log-mel (80 binów) zgodna z Whisperem.
- Create: `src/ToperJarvis.Speech/Endpointing/SileroVadModel.cs` — wrapper ONNX Silero VAD.
- Create: `src/ToperJarvis.Speech/Endpointing/SmartTurnModel.cs` — wrapper ONNX Smart Turn v3.
- Create: `src/ToperJarvis.Speech/Endpointing/NeuralEndpointDetector.cs` — łączy Silero (bramka mowy) + Smart Turn (koniec tury).
- Create: `src/ToperJarvis.Speech/Endpointing/EndpointDetectorFactory.cs` — wybór implementacji wg configu.
- Modify: `src/ToperJarvis.Abstractions/Configuration/JarvisOptions.cs` — `AudioOptions.EndpointEngine`, nowa `SmartTurnOptions`.
- Modify: `src/ToperJarvis.Speech/SpeechServiceCollectionExtensions.cs` — rejestracja fabryki.
- Modify: `src/ToperJarvis.Core/JarvisOrchestrator.cs` — użycie fabryki zamiast `new VadBuffer(_audio)`.
- Modify: `src/ToperJarvis.Speech/ToperJarvis.Speech.csproj` — `Microsoft.ML.OnnxRuntime`.
- Test: `tests/ToperJarvis.Core.Tests/Speech/WhisperMelSpectrogramTests.cs`, `SmartTurnModelTests.cs`, `NeuralEndpointDetectorTests.cs`, `EndpointDetectorFactoryTests.cs`.
- Create (fixture): `tests/ToperJarvis.Core.Tests/Fixtures/mel_golden.json`.

**Faza 2 — streaming PCM Piper:**
- Modify: `src/ToperJarvis.Speech/Tts/PiperTextToSpeech.cs` — protokół `--output-raw`, streaming do `BufferedWaveProvider`.
- Create: `src/ToperJarvis.Speech/Tts/RawPcmPlayer.cs` — testowalny odtwarzacz strumienia PCM.
- Modify: `JarvisOptions.cs` — `TtsOptions.SampleRate`, `TtsOptions.Streaming`.
- Test: `tests/ToperJarvis.Core.Tests/Speech/RawPcmPlayerTests.cs`.

**Faza 3 — cache + filler:**
- Create: `src/ToperJarvis.Speech/Tts/CachingTextToSpeech.cs` — dekorator ITextToSpeech.
- Modify: `SpeechServiceCollectionExtensions.cs` — owinięcie dekoratorem.
- Modify: `JarvisOrchestrator.cs` — natychmiastowy filler po endpointingu.
- Modify: `JarvisOptions.cs` — `TtsOptions.FillerPhrases`, `TtsOptions.CacheEnabled`.
- Test: `tests/ToperJarvis.Core.Tests/Speech/CachingTextToSpeechTests.cs`.

**Faza 4 — barge-in + AEC:**
- Create: `src/ToperJarvis.Speech/Audio/IEchoCanceller.cs` + `WebRtcEchoCanceller.cs` (lub `WindowsAecCapture`).
- Modify: `NAudioCapture.cs` — wpięcie AEC (referencja far-end).
- Modify: `JarvisOrchestrator.cs` — nasłuch endpointingu w stanie `Speaking`, przerwanie przy mowie usera.
- Modify: `JarvisOptions.cs` — `AudioOptions.BargeInEnabled`, `AudioOptions.AecEngine`.
- Test: `tests/ToperJarvis.Core.Tests/Speech/BargeInTests.cs` (na fakes).

---

# FAZA 1 — Neuronowy endpointing (Smart Turn v3 + Silero VAD)

Cel: usunąć stałe 0,7 s martwej ciszy, zastępując RMS-owy `VadBuffer` detektorem, który wie, że użytkownik *skończył myśl*.

### Task 1.1: Abstrakcja `IEndpointDetector` + `VadBuffer` ją implementuje

**Files:**
- Create: `src/ToperJarvis.Speech/Endpointing/IEndpointDetector.cs`
- Modify: `src/ToperJarvis.Speech/Vad/VadBuffer.cs`
- Test: `tests/ToperJarvis.Core.Tests/Speech/VadBufferTests.cs` (istnieje — dodać jeden test kontraktu)

**Interfaces:**
- Produces: `interface IEndpointDetector { float[]? Process(ReadOnlySpan<float> chunk); void Reset(); }`. Semantyka identyczna z dzisiejszym `VadBuffer.Process`: `null` w trakcie nasłuchu, kompletna wypowiedź `float[]` przy końcu tury.

- [ ] **Step 1: Napisz interfejs**

```csharp
// src/ToperJarvis.Speech/Endpointing/IEndpointDetector.cs
namespace ToperJarvis.Speech.Endpointing;

/// <summary>
/// Wykrywa koniec wypowiedzi użytkownika. Zwraca null w trakcie nasłuchu,
/// a kompletny bufor wypowiedzi (float[] 16 kHz mono) gdy tura się kończy.
/// </summary>
public interface IEndpointDetector
{
    float[]? Process(ReadOnlySpan<float> chunk);
    void Reset();
}
```

- [ ] **Step 2: `VadBuffer` implementuje interfejs**

W `src/ToperJarvis.Speech/Vad/VadBuffer.cs` zmień deklarację i dodaj using:
```csharp
using ToperJarvis.Speech.Endpointing;
// ...
public sealed class VadBuffer : IEndpointDetector
```
Sygnatury `Process`/`Reset` już pasują — nic więcej nie zmieniaj.

- [ ] **Step 3: Test kontraktu** — dopisz do `VadBufferTests.cs`:

```csharp
[Fact]
public void VadBuffer_jest_IEndpointDetector()
{
    IEndpointDetector detector = new VadBuffer(Options());
    Assert.Null(detector.Process(SilenceChunk()));
}
```
(dodaj `using ToperJarvis.Speech.Endpointing;` na górze pliku)

- [ ] **Step 4: Uruchom testy**

Run: `dotnet test tests/ToperJarvis.Core.Tests --filter FullyQualifiedName~VadBufferTests`
Expected: PASS (wszystkie, łącznie z nowym)

- [ ] **Step 5: Commit**

```bash
git add src/ToperJarvis.Speech/Endpointing/IEndpointDetector.cs src/ToperJarvis.Speech/Vad/VadBuffer.cs tests/ToperJarvis.Core.Tests/Speech/VadBufferTests.cs
git commit -m "refactor: abstrakcja IEndpointDetector, VadBuffer jako implementacja bazowa"
```

---

### Task 1.2: Dodaj ONNX Runtime + opcje konfiguracji + assets

**Files:**
- Modify: `src/ToperJarvis.Speech/ToperJarvis.Speech.csproj`
- Modify: `src/ToperJarvis.Abstractions/Configuration/JarvisOptions.cs`
- Test: `tests/ToperJarvis.Core.Tests/Speech/SmartTurnOptionsTests.cs` (nowy, prosty)

**Interfaces:**
- Produces: `AudioOptions.EndpointEngine` (string, `"rms"` domyślnie), klasa `SmartTurnOptions { string ModelPath; string SileroVadPath; float CompletionThreshold; double MaxTurnSeconds; double VadSilenceSeconds; }`, właściwość `JarvisOptions.SmartTurn`.

- [ ] **Step 1: Dodaj pakiet ONNX Runtime**

W `ToperJarvis.Speech.csproj`, w `<ItemGroup>` z PackageReference dodaj:
```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.20.1" />
```

- [ ] **Step 2: Dodaj opcje** — w `JarvisOptions.cs` dopisz właściwość w klasie root (przy pozostałych):
```csharp
public SmartTurnOptions SmartTurn { get; set; } = new();
```
Dodaj pole do `AudioOptions` (obok istniejących progów):
```csharp
    // "rms" = energetyczny VadBuffer (domyślny), "smartturn" = neuronowy endpointing (ONNX).
    public string EndpointEngine { get; set; } = "rms";
```
Dodaj nową klasę (obok innych *Options):
```csharp
public sealed class SmartTurnOptions
{
    public string ModelPath { get; set; } = "assets/smartturn/smart-turn-v3.1-cpu.onnx";
    public string SileroVadPath { get; set; } = "assets/silero/silero_vad.onnx";
    // Próg prawdopodobieństwa "user skończył turę".
    public float CompletionThreshold { get; set; } = 0.5f;
    // Twarde odcięcie długości tury (bezpiecznik jak MaxSpeechSeconds).
    public double MaxTurnSeconds { get; set; } = 30.0;
    // Ile ciszy (wg Silero) wyzwala zapytanie do Smart Turn o koniec tury.
    public double VadSilenceSeconds { get; set; } = 0.2;
}
```

- [ ] **Step 3: Test opcji** — `tests/ToperJarvis.Core.Tests/Speech/SmartTurnOptionsTests.cs`:
```csharp
using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.Core.Tests.Speech;

public class SmartTurnOptionsTests
{
    [Fact]
    public void Domyslne_wartosci_sa_sensowne()
    {
        var o = new JarvisOptions();
        Assert.Equal("rms", o.Audio.EndpointEngine);
        Assert.EndsWith(".onnx", o.SmartTurn.ModelPath);
        Assert.InRange(o.SmartTurn.CompletionThreshold, 0f, 1f);
    }
}
```

- [ ] **Step 4: Build + test**

Run: `dotnet build src/ToperJarvis.Speech && dotnet test tests/ToperJarvis.Core.Tests --filter FullyQualifiedName~SmartTurnOptions`
Expected: build OK (pakiet ONNX pobrany), test PASS

- [ ] **Step 5: Commit**

```bash
git add src/ToperJarvis.Speech/ToperJarvis.Speech.csproj src/ToperJarvis.Abstractions/Configuration/JarvisOptions.cs tests/ToperJarvis.Core.Tests/Speech/SmartTurnOptionsTests.cs
git commit -m "chore: ONNX Runtime + opcje SmartTurn/EndpointEngine"
```

---

### Task 1.3: SPIKE — pobierz modele i potwierdź sygnaturę I/O (bez placeholderów w dół planu)

To zadanie usuwa jedyne prawdziwe niewiadome researchu (kształt tensorów, parametry mel). Wynik zapisujesz do pliku notatki, którym karmisz kolejne taski.

**Files:**
- Create: `assets/smartturn/smart-turn-v3.1-cpu.onnx` (pobrany)
- Create: `assets/silero/silero_vad.onnx` (pobrany)
- Create: `docs/superpowers/notes/smartturn-io.md` (ustalenia)

- [ ] **Step 1: Pobierz modele**

```bash
mkdir -p assets/smartturn assets/silero
curl -L -o assets/smartturn/smart-turn-v3.1-cpu.onnx https://huggingface.co/pipecat-ai/smart-turn-v3/resolve/main/smart-turn-v3.1-cpu.onnx
curl -L -o assets/silero/silero_vad.onnx https://huggingface.co/onnx-community/silero-vad/resolve/main/onnx/model.onnx
```
(jeśli nazwy plików w repo się zmienią, wziąć najnowszy `*-cpu.onnx` ze strony `https://huggingface.co/pipecat-ai/smart-turn-v3/tree/main`)

- [ ] **Step 2: Wypisz sygnaturę grafu ONNX**

Napisz jednorazowy skrypt diagnostyczny w teście-eksploracyjnym lub użyj `dotnet-script`/małego programu:
```csharp
using var session = new Microsoft.ML.OnnxRuntime.InferenceSession("assets/smartturn/smart-turn-v3.1-cpu.onnx");
foreach (var i in session.InputMetadata)  System.Console.WriteLine($"IN  {i.Key} {i.Value.ElementType} [{string.Join(",", i.Value.Dimensions)}]");
foreach (var o in session.OutputMetadata) System.Console.WriteLine($"OUT {o.Key} {o.Value.ElementType} [{string.Join(",", o.Value.Dimensions)}]");
```

- [ ] **Step 3: Ustal parametry preprocessingu z `inference.py`**

Otwórz `https://github.com/pipecat-ai/smart-turn/blob/main/inference.py` i `model.py`. Zanotuj w `docs/superpowers/notes/smartturn-io.md`:
- nazwę wejścia i kształt (np. `input_features [1,80,T]` czy surowe PCM `[1,N]`),
- liczbę binów mel (80 vs 128), `n_fft`, `hop_length`, `sample_rate` (spodziewane: 80, 400, 160, 16000),
- czy mel jest paddowany do stałej liczby ramek (np. 3000) czy do długości 8 s,
- nazwę wyjścia i jak z niego liczyć prawdopodobieństwo (sigmoid? bezpośrednio?),
- to samo dla Silero VAD (wejście `input [1,N]` + `state`/`sr`, wyjście prob).

- [ ] **Step 4: Zdecyduj ścieżkę mel**

Jeśli graf przyjmuje **surowe PCM** (mel w grafie) → Task 1.4 (mel w C#) **pomijasz**, a `SmartTurnModel` karmisz PCM. Jeśli przyjmuje **mel** → realizujesz Task 1.4. Odnotuj decyzję w nocie.

- [ ] **Step 5: Commit**

```bash
git add assets/smartturn/.gitattributes assets/silero/.gitattributes docs/superpowers/notes/smartturn-io.md
git commit -m "chore: modele Smart Turn v3 + Silero VAD, ustalenia sygnatury I/O"
```
(uwaga: pliki .onnx dodaj do Git LFS lub `.gitignore` + skrypt pobierania, zależnie od polityki repo dla dużych plików — patrz jak trzymane są `assets/whisper/*.bin`)

---

### Task 1.4: Ekstrakcja log-mel (tylko jeśli SPIKE wykazał, że graf wymaga mel)

**Files:**
- Create: `src/ToperJarvis.Speech/Endpointing/WhisperMelSpectrogram.cs`
- Create (fixture): `tests/ToperJarvis.Core.Tests/Fixtures/mel_golden.json`
- Test: `tests/ToperJarvis.Core.Tests/Speech/WhisperMelSpectrogramTests.cs`

**Interfaces:**
- Produces: `static float[] WhisperMelSpectrogram.Compute(ReadOnlySpan<float> pcm16k, int nMels = 80, int nFft = 400, int hop = 160)` → spłaszczony `[nMels * frames]` log-mel, zgodny z Whisperem (parametry z noty SPIKE).

- [ ] **Step 1: Wygeneruj złoty wektor referencyjny (Python, jednorazowo)**

Na maszynie z `inference.py`: dla ustalonego wejścia (np. 1 s sinusoidy 440 Hz, 16 kHz, amplituda 0.1) policz mel tym samym kodem co model i zrzuć do `mel_golden.json`: `{"pcm": [...], "mel": [...], "nMels":80, "nFft":400, "hop":160}`. To jest wyrocznia testu.

- [ ] **Step 2: Napisz test walidujący (RED)**

```csharp
using System.Text.Json;
using ToperJarvis.Speech.Endpointing;

namespace ToperJarvis.Core.Tests.Speech;

public class WhisperMelSpectrogramTests
{
    private sealed record Golden(float[] Pcm, float[] Mel, int NMels, int NFft, int Hop);

    [Fact]
    public void Mel_zgodny_ze_zlotym_wektorem_referencyjnym()
    {
        var json = File.ReadAllText("Fixtures/mel_golden.json");
        var g = JsonSerializer.Deserialize<Golden>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var mel = WhisperMelSpectrogram.Compute(g.Pcm, g.NMels, g.NFft, g.Hop);

        Assert.Equal(g.Mel.Length, mel.Length);
        for (var i = 0; i < mel.Length; i++)
            Assert.True(Math.Abs(mel[i] - g.Mel[i]) < 1e-2f,
                $"bin {i}: {mel[i]} vs {g.Mel[i]}");
    }
}
```
Ustaw fixture jako kopiowany do output w `.csproj`:
```xml
<ItemGroup>
  <None Update="Fixtures/mel_golden.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
</ItemGroup>
```

- [ ] **Step 3: Run — RED**

Run: `dotnet test tests/ToperJarvis.Core.Tests --filter FullyQualifiedName~WhisperMelSpectrogram`
Expected: FAIL (brak `WhisperMelSpectrogram`)

- [ ] **Step 4: Implementuj mel**

`WhisperMelSpectrogram.Compute`: (1) padding/reflect wg Whispera, (2) okno Hanna długości `nFft`, przesuw `hop`, (3) magnituda STFT (DFT/`System.Numerics` lub Cooley-Tukey radix-2 na `nFft`=400 → dopad do 512), (4) filtrbank mel 16 kHz/`nMels` (fmin=0, fmax=8000, skala mel Slaneya jak librosa/whisper), (5) `log10(max(mel, 1e-10))`, (6) normalizacja Whispera: `mel = (mel - mel.max()) ` i `clamp` do `[-4,0]` wg noty SPIKE. **Dokładne stałe wziąć z `inference.py`** (zanotowane w Task 1.3) — złoty wektor jest bramką poprawności.

- [ ] **Step 5: Run — GREEN**

Run: `dotnet test tests/ToperJarvis.Core.Tests --filter FullyQualifiedName~WhisperMelSpectrogram`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/ToperJarvis.Speech/Endpointing/WhisperMelSpectrogram.cs tests/ToperJarvis.Core.Tests/Speech/WhisperMelSpectrogramTests.cs tests/ToperJarvis.Core.Tests/Fixtures/mel_golden.json tests/ToperJarvis.Core.Tests/ToperJarvis.Core.Tests.csproj
git commit -m "feat: ekstrakcja log-mel zgodna z Whisperem (walidacja zlotym wektorem)"
```

---

### Task 1.5: Wrapper ONNX Smart Turn v3

**Files:**
- Create: `src/ToperJarvis.Speech/Endpointing/SmartTurnModel.cs`
- Test: `tests/ToperJarvis.Core.Tests/Speech/SmartTurnModelTests.cs`

**Interfaces:**
- Produces: `sealed class SmartTurnModel : IDisposable`; ctor `(string modelPath, ILogger)`; `float PredictCompletion(ReadOnlySpan<float> pcm16k)` → prawdopodobieństwo [0..1], że użytkownik skończył. Wewnątrz: padding do 8 s (zera z przodu), mel (Task 1.4) lub surowe PCM (wg SPIKE), inference, sigmoid jeśli trzeba. Brak modelu → zwraca `1.0f` (degradacja: zachowuj się jak „koniec", żeby nie zawiesić tury) i loguje ostrzeżenie raz.

- [ ] **Step 1: Test na fake'owym/realnym modelu (RED)**

Test integracyjny na realnym modelu (pomijany, gdy brak pliku — wzorzec jak reszta):
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using ToperJarvis.Speech.Endpointing;

namespace ToperJarvis.Core.Tests.Speech;

public class SmartTurnModelTests
{
    private const string ModelPath = "assets/smartturn/smart-turn-v3.1-cpu.onnx";

    [Fact]
    public void Brak_modelu_degraduje_do_konca_tury()
    {
        using var m = new SmartTurnModel("nie/istnieje.onnx", NullLogger<SmartTurnModel>.Instance);
        var prob = m.PredictCompletion(new float[16000]);
        Assert.Equal(1.0f, prob);
    }

    [Fact]
    public void Cisza_daje_wysokie_prawdopodobienstwo_konca()
    {
        if (!File.Exists(ModelPath)) return; // pominięcie gdy model niedostępny w CI
        using var m = new SmartTurnModel(ModelPath, NullLogger<SmartTurnModel>.Instance);
        var prob = m.PredictCompletion(new float[16000]); // 1 s ciszy
        Assert.InRange(prob, 0f, 1f);
    }
}
```

- [ ] **Step 2: Run — RED**

Run: `dotnet test tests/ToperJarvis.Core.Tests --filter FullyQualifiedName~SmartTurnModel`
Expected: FAIL (brak typu)

- [ ] **Step 3: Implementuj wrapper**

Klucz: `InferenceSession` z `SessionOptions { InterOpNumThreads=1, ExecutionMode=ORT_SEQUENTIAL, GraphOptimizationLevel=ORT_ENABLE_ALL }` (rekomendacja z bloga). Padding: jeśli `pcm.Length < 8*16000`, alokuj `float[128000]` i skopiuj audio **na koniec** (zera z przodu); jeśli dłuższe — weź ostatnie 128000. Wejście = mel(pcm) lub pcm wg SPIKE. Nazwy wejść/wyjść z noty. Wynik → sigmoid jeśli logit. Lazy-load + lock jak w `WhisperSpeechToText.EnsureProcessor`.

- [ ] **Step 4: Run — GREEN**

Run: `dotnet test tests/ToperJarvis.Core.Tests --filter FullyQualifiedName~SmartTurnModel`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ToperJarvis.Speech/Endpointing/SmartTurnModel.cs tests/ToperJarvis.Core.Tests/Speech/SmartTurnModelTests.cs
git commit -m "feat: wrapper ONNX Smart Turn v3 (predykcja konca tury)"
```

---

### Task 1.6: Wrapper ONNX Silero VAD

**Files:**
- Create: `src/ToperJarvis.Speech/Endpointing/SileroVadModel.cs`
- Test: `tests/ToperJarvis.Core.Tests/Speech/SileroVadModelTests.cs`

**Interfaces:**
- Produces: `sealed class SileroVadModel : IDisposable`; ctor `(string modelPath, ILogger)`; `float IsSpeech(ReadOnlySpan<float> frame)` → prawdopodobieństwo mowy [0..1] dla ramki (Silero oczekuje 512 próbek @16 kHz); `void Reset()` (czyści stan LSTM `h`/`c`). Brak modelu → `IsSpeech` zwraca `1.0f` (degradacja: traktuj wszystko jak mowę, żeby endpointing spadł na próg czasu Smart Turn), log raz.

- [ ] **Step 1: Test (RED)** — analogiczny wzorzec „brak modelu degraduje" + „na realnym modelu zwraca [0..1]” (jak Task 1.5, `return` gdy brak pliku). Napisz `Brak_modelu_degraduje_do_mowy` (Assert 1.0f) i `Reset_nie_rzuca`.

- [ ] **Step 2: Run — RED** → FAIL (brak typu)

- [ ] **Step 3: Implementuj** — Silero v5 ONNX: wejścia `input [1,512]`, `state [2,1,128]`, `sr` (int64=16000); wyjścia `output` (prob) + `stateN`. Trzymaj `state` między ramkami, `Reset()` zeruje. Wg dokładnych nazw z SPIKE.

- [ ] **Step 4: Run — GREEN**

- [ ] **Step 5: Commit**
```bash
git add src/ToperJarvis.Speech/Endpointing/SileroVadModel.cs tests/ToperJarvis.Core.Tests/Speech/SileroVadModelTests.cs
git commit -m "feat: wrapper ONNX Silero VAD"
```

---

### Task 1.7: `NeuralEndpointDetector` — złożenie Silero + Smart Turn

**Files:**
- Create: `src/ToperJarvis.Speech/Endpointing/NeuralEndpointDetector.cs`
- Test: `tests/ToperJarvis.Core.Tests/Speech/NeuralEndpointDetectorTests.cs`

**Interfaces:**
- Consumes: `IEndpointDetector` (1.1), `SileroVadModel` (1.6), `SmartTurnModel` (1.5), `SmartTurnOptions`, `AudioOptions`.
- Produces: `sealed class NeuralEndpointDetector : IEndpointDetector`; ctor `(SileroVadModel vad, SmartTurnModel turn, SmartTurnOptions opts, int sampleRate)`.

Logika: buforuj mowę odkąd Silero wykryje mowę. Gdy Silero pokaże ciszę przez `VadSilenceSeconds`, wywołaj `SmartTurn.PredictCompletion(bufor)`. Jeśli `>= CompletionThreshold` → zwróć bufor (koniec tury) i `Reset()`. Jeśli nie — kontynuuj nasłuch (użytkownik zrobił tylko pauzę). Bezpiecznik: `MaxTurnSeconds` → wymuś koniec. Silero pracuje na ramkach 512; bufor wejściowy 1600 (100 ms) trzeba pociąć na ramki 512 (bufor przejściowy na resztę).

- [ ] **Step 1: Test na fake'ach (RED)** — wstrzyknij deterministyczne fake'i przez małe interfejsy albo testuj przez publiczne wejście z realnymi modelami pominięte gdy brak plików. Preferencja: wydziel wewnętrzną logikę progową do czystej metody testowalnej bez ONNX. Test:
```csharp
[Fact]
public void Mowa_potem_cisza_z_wysokim_completion_konczy_ture() { /* podaj sekwencję ramek, oczekuj null...null...float[] */ }

[Fact]
public void Pauza_z_niskim_completion_nie_konczy_tury() { /* Smart Turn < próg → dalej null */ }
```
(Aby to było wykonalne bez ONNX: `NeuralEndpointDetector` przyjmuje dwa delegaty/interfejsy `Func<ReadOnlySpan<float>,float>` dla VAD i completion — w produkcji podpięte do modeli, w teście fake'i. Zdefiniuj minimalne `IVadProbe`/`ITurnProbe` lub delegaty.)

- [ ] **Step 2: Run — RED** → FAIL

- [ ] **Step 3: Implementuj** wg logiki wyżej. Reset zeruje bufor, licznik ciszy i `SileroVadModel.Reset()`.

- [ ] **Step 4: Run — GREEN**

- [ ] **Step 5: Commit**
```bash
git add src/ToperJarvis.Speech/Endpointing/NeuralEndpointDetector.cs tests/ToperJarvis.Core.Tests/Speech/NeuralEndpointDetectorTests.cs
git commit -m "feat: NeuralEndpointDetector (Silero VAD + Smart Turn v3)"
```

---

### Task 1.8: Fabryka + DI + wpięcie w orkiestrator

**Files:**
- Create: `src/ToperJarvis.Speech/Endpointing/EndpointDetectorFactory.cs`
- Modify: `src/ToperJarvis.Speech/SpeechServiceCollectionExtensions.cs`
- Modify: `src/ToperJarvis.Core/JarvisOrchestrator.cs`
- Test: `tests/ToperJarvis.Core.Tests/Speech/EndpointDetectorFactoryTests.cs`

**Interfaces:**
- Produces: `interface IEndpointDetectorFactory { IEndpointDetector Create(); }` + `sealed class EndpointDetectorFactory : IEndpointDetectorFactory`. `Create()` wg `AudioOptions.EndpointEngine`: `"smartturn"` → `NeuralEndpointDetector` (modele singletony), inaczej → `new VadBuffer(_audio)`. Nowa instancja per tura (stan bufora).
- Consumes (orchestrator): `IEndpointDetectorFactory` zamiast bezpośredniego `new VadBuffer`.

- [ ] **Step 1: Test fabryki (RED)**
```csharp
[Theory]
[InlineData("rms")]
[InlineData("smartturn")]
public void Fabryka_tworzy_detektor_wg_configu(string engine) { /* Options.Create, Assert.IsAssignableFrom<IEndpointDetector> */ }

[Fact]
public void Smartturn_bez_modeli_nadal_zwraca_dzialajacy_detektor() { /* degradacja, nie wyjątek */ }
```

- [ ] **Step 2: Run — RED** → FAIL

- [ ] **Step 3: Implementuj fabrykę**, zarejestruj modele + fabrykę w `AddJarvisSpeech`:
```csharp
services.AddSingleton<SileroVadModel>(sp => { var o = sp.GetRequiredService<IOptions<JarvisOptions>>().Value; return new SileroVadModel(o.SmartTurn.SileroVadPath, sp.GetRequiredService<ILogger<SileroVadModel>>()); });
services.AddSingleton<SmartTurnModel>(sp => { var o = sp.GetRequiredService<IOptions<JarvisOptions>>().Value; return new SmartTurnModel(o.SmartTurn.ModelPath, sp.GetRequiredService<ILogger<SmartTurnModel>>()); });
services.AddSingleton<IEndpointDetectorFactory, EndpointDetectorFactory>();
```

- [ ] **Step 4: Wepnij w orkiestrator** — w `JarvisOrchestrator`:
  - dodaj pole `private readonly IEndpointDetectorFactory _endpointFactory;` i parametr ctora (przed `IOptions<JarvisOptions> options`);
  - zmień typ `_vad` z `VadBuffer?` na `IEndpointDetector?`;
  - w `OnWakeWordDetected` zmień `_vad = new VadBuffer(_audio);` → `_vad = _endpointFactory.Create();`.
  - Zaktualizuj konstruktor testów orkiestratora, jeśli powstaną.

- [ ] **Step 5: Run — build + testy**

Run: `dotnet build && dotnet test tests/ToperJarvis.Core.Tests`
Expected: PASS (całość)

- [ ] **Step 6: Commit**
```bash
git add src/ToperJarvis.Speech/Endpointing/EndpointDetectorFactory.cs src/ToperJarvis.Speech/SpeechServiceCollectionExtensions.cs src/ToperJarvis.Core/JarvisOrchestrator.cs tests/ToperJarvis.Core.Tests/Speech/EndpointDetectorFactoryTests.cs
git commit -m "feat: fabryka endpointingu + wpiecie neuronowego detektora w orkiestrator"
```

- [ ] **Step 7: Weryfikacja end-to-end (ręczna)** — ustaw `Jarvis:Audio:EndpointEngine=smartturn`, uruchom app (`/run` lub `dotnet run --project src/ToperJarvis.App`), zmierz czas od końca mowy do stanu `Transcribing` przed/po (log `TurnCompleted` / dodaj tymczasowy log). Oczekiwane: krótsza pauza niż 0,7 s.

---

# FAZA 2 — Streaming PCM w Piper (time-to-first-audio)

Cel: przestać czekać na cały plik WAV zdania — grać PCM w miarę jak Piper go produkuje.

### Task 2.1: Opcje TTS dla streamingu

**Files:**
- Modify: `src/ToperJarvis.Abstractions/Configuration/JarvisOptions.cs`

**Interfaces:**
- Produces: `TtsOptions.Streaming` (bool, domyślnie `true`), `TtsOptions.SampleRate` (int, domyślnie `22050` — natywny sample rate modeli Piper).

- [ ] **Step 1: Dodaj pola** do `TtsOptions`:
```csharp
    public bool Streaming { get; set; } = true;
    public int SampleRate { get; set; } = 22050;
```
- [ ] **Step 2: Build** — `dotnet build src/ToperJarvis.Abstractions` → OK
- [ ] **Step 3: Commit**
```bash
git add src/ToperJarvis.Abstractions/Configuration/JarvisOptions.cs
git commit -m "chore: opcje TtsOptions.Streaming/SampleRate"
```

---

### Task 2.2: `RawPcmPlayer` — testowalny odtwarzacz strumienia PCM

**Files:**
- Create: `src/ToperJarvis.Speech/Tts/RawPcmPlayer.cs`
- Test: `tests/ToperJarvis.Core.Tests/Speech/RawPcmPlayerTests.cs`

**Interfaces:**
- Produces: `sealed class RawPcmPlayer`; ctor `(int deviceNumber, int sampleRate)`; `Task PlayAsync(Stream pcmStream, CancellationToken ct)` — czyta surowe 16-bit PCM mono ze `Stream`, wpycha do `BufferedWaveProvider`, gra przez `WaveOutEvent`; kończy gdy stream wyczerpany i bufor odtworzony; `ct` przerywa natychmiast (kluczowe dla barge-in w Fazie 4).

- [ ] **Step 1: Test na MemoryStream (RED)** — nie testujemy dźwięku, tylko że metoda czyta cały strumień i kończy bez wyjątku oraz reaguje na anulowanie:
```csharp
using ToperJarvis.Speech.Tts;

namespace ToperJarvis.Core.Tests.Speech;

public class RawPcmPlayerTests
{
    [Fact]
    public async Task Pusty_strumien_konczy_sie_bez_bledu()
    {
        var player = new RawPcmPlayer(deviceNumber: -1, sampleRate: 22050);
        using var ms = new MemoryStream(Array.Empty<byte>());
        await player.PlayAsync(ms, CancellationToken.None);
    }

    [Fact]
    public async Task Anulowanie_przerywa_odtwarzanie()
    {
        var player = new RawPcmPlayer(-1, 22050);
        using var ms = new MemoryStream(new byte[22050 * 2 * 5]); // 5 s ciszy
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);
        await player.PlayAsync(ms, cts.Token); // nie może wisieć 5 s
    }
}
```
(WaveMapper `-1` i cisza — bezpieczne na CI bez realnego wyjścia; jeśli CI nie ma urządzenia audio, oznacz `[Trait]` do pominięcia lub użyj krótkich buforów.)

- [ ] **Step 2: Run — RED** → FAIL (brak typu)

- [ ] **Step 3: Implementuj** — `BufferedWaveProvider(new WaveFormat(sampleRate,16,1)) { BufferDuration = TimeSpan.FromSeconds(10), DiscardOnBufferOverflow = false }`. Pętla: czytaj ~3200 B ze streamu → `AddSamples`; `WaveOutEvent.Init/Play` po pierwszym chunku; gdy stream się skończy i `BufferedBytes == 0` → stop. `ct.Register(() => waveOut.Stop())` + pętla respektuje `ct`. Wzoruj się na obecnym `PlayAsync` (TaskCompletionSource + PlaybackStopped) z `PiperTextToSpeech.cs:178-196`.

- [ ] **Step 4: Run — GREEN**

- [ ] **Step 5: Commit**
```bash
git add src/ToperJarvis.Speech/Tts/RawPcmPlayer.cs tests/ToperJarvis.Core.Tests/Speech/RawPcmPlayerTests.cs
git commit -m "feat: RawPcmPlayer (streaming PCM przez BufferedWaveProvider)"
```

---

### Task 2.3: Przełącz `PiperTextToSpeech` na `--output-raw`

**Files:**
- Modify: `src/ToperJarvis.Speech/Tts/PiperTextToSpeech.cs`

**Interfaces:**
- Consumes: `RawPcmPlayer` (2.2), `TtsOptions.Streaming`/`SampleRate` (2.1).
- Zachowuje: publiczny `ITextToSpeech.SpeakAsync(string, CancellationToken)` bez zmian sygnatury.

- [ ] **Step 1: Zmień argumenty procesu** — w `EnsureProcess` zamień `--json-input` na tryb raw. Piper: `--output-raw` wypisuje surowe PCM (16-bit, mono, sample rate modelu) na stdout, tekst na stdin (linia = jedno wypowiedzenie). Usuń `--json-input`; zostaw `--model` i `--length_scale`, dodaj `--output-raw`.

- [ ] **Step 2: Zmień `SpeakAsync`** — gdy `_options.Streaming`: napisz linię tekstu do stdin, a `piper.StandardOutput.BaseStream` (surowy PCM) podaj do `new RawPcmPlayer(_output.DeviceNumber, _options.SampleRate).PlayAsync(...)`. **Uwaga na framing:** przy trwałym procesie i wielu zdaniach trzeba wiedzieć, gdzie kończy się PCM danego zdania. Piper w trybie raw nie wstawia separatora → **dla trybu streaming uruchamiaj Piper per-zdanie** (proces krótkotrwały, stdout = całe PCM tego zdania, EOF = koniec), rezygnując z trwałego procesu tylko w tej ścieżce. Zmierz koszt spawnu; jeśli istotny, alternatywa: zostań przy trwałym procesie + `--json-input` (plik) i graj plik przez `RawPcmPlayer` czytający WAV strumieniowo (mniejszy zysk). Zdecyduj na podstawie pomiaru i zanotuj.

- [ ] **Step 3: Zachowaj fallback** — gdy `Streaming == false`, zostaw dotychczasową ścieżkę `--json-input` + `PlayAsync(plik)`.

- [ ] **Step 4: Build + testy istniejące**

Run: `dotnet build && dotnet test tests/ToperJarvis.Core.Tests`
Expected: PASS (brak regresji; RawPcmPlayer pokryty w 2.2)

- [ ] **Step 5: Weryfikacja ręczna** — uruchom app, powiedz coś dłuższego; potwierdź, że pierwsze słowo Jarvisa pada szybciej niż przed zmianą i mowa jest płynna.

- [ ] **Step 6: Commit**
```bash
git add src/ToperJarvis.Speech/Tts/PiperTextToSpeech.cs
git commit -m "feat: streaming PCM w Piper (--output-raw) z fallbackiem na plik"
```

---

# FAZA 3 — Cache TTS + natychmiastowy filler

Cel: powtarzalne frazy bez ponownej syntezy; natychmiastowy „wypełniacz" maskujący TTFT LLM-a.

### Task 3.1: Opcje cache/filler

**Files:**
- Modify: `src/ToperJarvis.Abstractions/Configuration/JarvisOptions.cs`

**Interfaces:**
- Produces: `TtsOptions.CacheEnabled` (bool, `true`), `TtsOptions.FillerPhrases` (`List<string>`, domyślnie `["Chwileczkę.", "Już sprawdzam."]`).

- [ ] **Step 1: Dodaj pola**
```csharp
    public bool CacheEnabled { get; set; } = true;
    public List<string> FillerPhrases { get; set; } = new() { "Chwileczkę.", "Już sprawdzam." };
```
- [ ] **Step 2: Build** → OK
- [ ] **Step 3: Commit**
```bash
git add src/ToperJarvis.Abstractions/Configuration/JarvisOptions.cs
git commit -m "chore: opcje cache TTS + frazy filler"
```

---

### Task 3.2: `CachingTextToSpeech` — dekorator

**Files:**
- Create: `src/ToperJarvis.Speech/Tts/CachingTextToSpeech.cs`
- Test: `tests/ToperJarvis.Core.Tests/Speech/CachingTextToSpeechTests.cs`

**Interfaces:**
- Consumes: `ITextToSpeech` (dekorowany), `TtsOptions`.
- Produces: `sealed class CachingTextToSpeech : ITextToSpeech`; ctor `(ITextToSpeech inner, TtsOptions opts, ILogger)`. Cache PCM po znormalizowanym tekście; przy `SpeakAsync` — jeśli w cache, odtwórz z bufora, inaczej deleguj do `inner` i (dla fraz z `FillerPhrases`) zapamiętaj. `Task WarmupAsync()` — wstępnie syntezuje `FillerPhrases`.

- [ ] **Step 1: Test na fake ITextToSpeech (RED)** — sprawdza, że drugie wywołanie tej samej frazy filler nie woła `inner` ponownie:
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;
using ToperJarvis.Speech.Tts;

namespace ToperJarvis.Core.Tests.Speech;

public class CachingTextToSpeechTests
{
    private sealed class CountingTts : ITextToSpeech
    {
        public int Calls;
        public Task SpeakAsync(string text, CancellationToken ct = default) { Calls++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Fraza_filler_syntezowana_tylko_raz()
    {
        var inner = new CountingTts();
        var opts = new TtsOptions { CacheEnabled = true, FillerPhrases = new() { "Chwileczkę." } };
        var tts = new CachingTextToSpeech(inner, opts, NullLogger<CachingTextToSpeech>.Instance);

        await tts.SpeakAsync("Chwileczkę.");
        await tts.SpeakAsync("Chwileczkę.");

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Nowa_fraza_zawsze_deleguje()
    {
        var inner = new CountingTts();
        var tts = new CachingTextToSpeech(inner, new TtsOptions(), NullLogger<CachingTextToSpeech>.Instance);
        await tts.SpeakAsync("raz");
        await tts.SpeakAsync("dwa");
        Assert.Equal(2, inner.Calls);
    }
}
```
(Uwaga: model cache oparty na *odtwarzaniu bufora PCM* wymaga, by `CachingTextToSpeech` samo grało PCM. Dla testowalności bez audio: cache przechowuje fakt „już syntezowano" + bufor, a odtwarzanie deleguje do `RawPcmPlayer`. W teście `inner.Calls` liczy realne syntezy — trafienie w cache pomija `inner`. Jeśli prostsze: cache trzyma PCM z Fazy 2 i gra sam; wtedy fake musi zwracać PCM — rozszerz `ITextToSpeech` o wariant zwracający bufor **tylko jeśli** okaże się konieczne. Preferencja: trzymać cache na poziomie „fraza→PCM bytes" i grać `RawPcmPlayer`em.)

- [ ] **Step 2: Run — RED** → FAIL

- [ ] **Step 3: Implementuj** — `ConcurrentDictionary<string,byte[]>` (klucz = `SpeechNormalizer.Normalize` lub trim/lower). Dla fraz filler i krótkich powtarzalnych — zapamiętuj PCM (wymaga ścieżki syntezy zwracającej PCM; jeśli obecny `inner` tylko gra, w pierwszej wersji cache'uj *decyzję* i deleguj, a pełne PCM-cache dołóż gdy Faza 2 wystawi metodę syntezy do bufora). `CacheEnabled == false` → czysta delegacja.

- [ ] **Step 4: Run — GREEN**

- [ ] **Step 5: Commit**
```bash
git add src/ToperJarvis.Speech/Tts/CachingTextToSpeech.cs tests/ToperJarvis.Core.Tests/Speech/CachingTextToSpeechTests.cs
git commit -m "feat: CachingTextToSpeech (cache fraz filler/powtarzalnych)"
```

---

### Task 3.3: Rejestracja dekoratora + filler w orkiestratorze

**Files:**
- Modify: `src/ToperJarvis.Speech/SpeechServiceCollectionExtensions.cs`
- Modify: `src/ToperJarvis.Core/JarvisOrchestrator.cs`

**Interfaces:**
- Consumes: `CachingTextToSpeech` (3.2), `TtsOptions.FillerPhrases`.

- [ ] **Step 1: Owiń rejestrację TTS** — zmień `services.AddSingleton<ITextToSpeech, PiperTextToSpeech>();` na rejestrację konkretu + dekorator:
```csharp
services.AddSingleton<PiperTextToSpeech>();
services.AddSingleton<ITextToSpeech>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<JarvisOptions>>().Value.Tts;
    var inner = sp.GetRequiredService<PiperTextToSpeech>();
    return opts.CacheEnabled
        ? new CachingTextToSpeech(inner, opts, sp.GetRequiredService<ILogger<CachingTextToSpeech>>())
        : inner;
});
```

- [ ] **Step 2: Filler w orkiestratorze** — w `ProcessUtteranceAsync`, po udanej transkrypcji a przed `ProcessTextAsync` (albo na początku `ProcessTextAsync` po `SetState(Thinking)`), jeśli `_tts` to potrafi, wypowiedz losowy filler „fire-and-forget", tak by grał w tle podczas oczekiwania na pierwszy token. Dodaj pole z frazami z configu; wybór bez `Random.Shared` w gorącej ścieżce nie jest krytyczny — może być round-robin. Zadbaj, by filler był przerwany/pominięty gdy pierwszy realny token nadejdzie szybko (nie nakładać mowy — filler i pierwsze zdanie idą przez ten sam `Channel`/kolejkę TTS, więc naturalnie się szeregują).

- [ ] **Step 3: Build + testy**

Run: `dotnet build && dotnet test`
Expected: PASS

- [ ] **Step 4: Weryfikacja ręczna** — zadaj pytanie wymagające myślenia/narzędzi; potwierdź, że Jarvis od razu mówi filler, potem płynnie przechodzi w odpowiedź.

- [ ] **Step 5: Commit**
```bash
git add src/ToperJarvis.Speech/SpeechServiceCollectionExtensions.cs src/ToperJarvis.Core/JarvisOrchestrator.cs
git commit -m "feat: dekorator cache TTS w DI + natychmiastowy filler maskujacy TTFT"
```

---

# FAZA 4 — Barge-in + AEC (największe ryzyko)

Cel: użytkownik może wejść Jarvisowi w słowo głosem; AEC zapobiega przerywaniu samemu sobie.

### Task 4.1: SPIKE — wybór i weryfikacja AEC na docelowym sprzęcie

**Files:**
- Create: `docs/superpowers/notes/aec-decision.md`

- [ ] **Step 1: Zbadaj wbudowany AEC Windows 11** — sprawdź, czy urządzenie wejściowe wystawia `IAcousticEchoCancellationControl` (WASAPI, Win11 22000+). Prototyp: otwórz capture przez WASAPI w kategorii „communications", spróbuj `SetEchoCancellationRenderEndpoint`. Odnotuj, czy sterownik wspiera.

- [ ] **Step 2: Prototyp WebRTC APM** — dodaj tymczasowo `SoundFlow.Extensions.WebRtc.Apm`, sprawdź, czy da się podać far-end (odtwarzany PCM) do `ProcessReverseStream` niezależnie od grafu SoundFlow (albo czy trzeba poprowadzić capture przez SoundFlow). Zmierz jakość tłumienia echa mówiąc do Jarvisa w trakcie jego mowy.

- [ ] **Step 3: Zdecyduj** — zapisz w `aec-decision.md`: wybrany silnik (`windows` / `webrtc` / `speex`), uzasadnienie, parametry (`aecLatencyMs`), oraz czy start Fazy 4 idzie z pełnym AEC czy najpierw z „half-duplex + wysoki próg" (tani barge-in bez AEC).

- [ ] **Step 4: Commit**
```bash
git add docs/superpowers/notes/aec-decision.md
git commit -m "chore: decyzja o silniku AEC dla barge-in"
```

---

### Task 4.2: Abstrakcja `IEchoCanceller` + implementacja wg decyzji

**Files:**
- Create: `src/ToperJarvis.Speech/Audio/IEchoCanceller.cs`
- Create: `src/ToperJarvis.Speech/Audio/<Wybrany>EchoCanceller.cs`
- Modify: `JarvisOptions.cs` — `AudioOptions.AecEngine` (`"off"` domyślnie), `AudioOptions.BargeInEnabled` (`false` domyślnie).
- Test: `tests/ToperJarvis.Core.Tests/Speech/EchoCancellerTests.cs`

**Interfaces:**
- Produces: `interface IEchoCanceller { void SubmitFarEnd(ReadOnlySpan<float> played); float[] ProcessNearEnd(ReadOnlySpan<float> captured); void Reset(); }`. `"off"` → `NullEchoCanceller` (near-end bez zmian).

- [ ] **Step 1: Test NullEchoCanceller (RED)** — `ProcessNearEnd` zwraca wejście 1:1; `SubmitFarEnd` no-op.
- [ ] **Step 2: Run — RED** → FAIL
- [ ] **Step 3: Implementuj** interfejs + `NullEchoCanceller` + wybrany real (wg 4.1). Real: bufor referencji far-end, wyrównanie wg `aecLatencyMs`, ramki 10 ms.
- [ ] **Step 4: Run — GREEN**
- [ ] **Step 5: Commit**
```bash
git add src/ToperJarvis.Speech/Audio/IEchoCanceller.cs src/ToperJarvis.Speech/Audio/*EchoCanceller.cs src/ToperJarvis.Abstractions/Configuration/JarvisOptions.cs tests/ToperJarvis.Core.Tests/Speech/EchoCancellerTests.cs
git commit -m "feat: abstrakcja AEC + implementacja wg decyzji SPIKE"
```

---

### Task 4.3: Wpięcie AEC w capture + far-end z odtwarzania

**Files:**
- Modify: `src/ToperJarvis.Speech/Audio/NAudioCapture.cs`
- Modify: `src/ToperJarvis.Speech/Tts/RawPcmPlayer.cs` (emisja far-end)
- Modify: `SpeechServiceCollectionExtensions.cs` (rejestracja `IEchoCanceller`)

**Interfaces:**
- Consumes: `IEchoCanceller` (4.2).
- Far-end: `RawPcmPlayer`/TTS przekazuje odtwarzane próbki do `IEchoCanceller.SubmitFarEnd` tuż przed wysłaniem na kartę.
- Near-end: `NAudioCapture.OnDataAvailable` przepuszcza próbki przez `ProcessNearEnd` przed `FrameAvailable`.

- [ ] **Step 1: Rejestracja** `services.AddSingleton<IEchoCanceller>(...)` wg `AudioOptions.AecEngine`.
- [ ] **Step 2: Near-end** — w `NAudioCapture` wstrzyknij `IEchoCanceller`, w `OnDataAvailable` zastosuj `ProcessNearEnd` na skonwertowanych float32 przed `AudioFrame`.
- [ ] **Step 3: Far-end** — w `RawPcmPlayer.PlayAsync` przed `AddSamples` konwertuj chunk na float i `SubmitFarEnd`.
- [ ] **Step 4: Build + testy** → PASS (brak regresji przy `AecEngine=off`, bo Null przepuszcza)
- [ ] **Step 5: Commit**
```bash
git add src/ToperJarvis.Speech/Audio/NAudioCapture.cs src/ToperJarvis.Speech/Tts/RawPcmPlayer.cs src/ToperJarvis.Speech/SpeechServiceCollectionExtensions.cs
git commit -m "feat: AEC w torze capture (near-end) + far-end z odtwarzania"
```

---

### Task 4.4: Barge-in w orkiestratorze — nasłuch w stanie Speaking

**Files:**
- Modify: `src/ToperJarvis.Core/JarvisOrchestrator.cs`
- Test: `tests/ToperJarvis.Core.Tests/Speech/BargeInTests.cs`

**Interfaces:**
- Consumes: `IEndpointDetector` (1.1), `Interrupt()` (istnieje), `AudioOptions.BargeInEnabled`.

Logika: gdy `BargeInEnabled` i `State == Speaking`, utrzymuj równoległy lekki detektor mowy (Silero/`IEndpointDetector`) na strumieniu capture (po AEC). Wykrycie mowy usera → `Interrupt()` (kasuje `_turnCts`, przez co `Channel`/`RawPcmPlayer` się zatrzymują) i natychmiast rozpocznij nową turę nasłuchu (jak po wake-word). Dziś `OnWakeWordDetected` ignoruje nie-Idle i `_turnGate` serializuje — trzeba dodać ścieżkę „przerwij bieżącą turę i przejmij".

- [ ] **Step 1: Test na fakes (RED)** — zbuduj orkiestrator z fake capture/STT/TTS/chat; wprowadź go w `Speaking`, wstrzyknij „mowę" w strumień; oczekuj, że `Interrupt` został wywołany (stan wraca do `Listening`/`Idle`, bieżąca tura anulowana). Wykorzystaj istniejące zdarzenia `StateChanged`.
```csharp
[Fact]
public async Task Mowa_uzytkownika_w_trakcie_odpowiedzi_przerywa_ture() { /* ... */ }

[Fact]
public async Task Bez_barge_in_mowa_nie_przerywa() { /* BargeInEnabled=false → tura trwa */ }
```

- [ ] **Step 2: Run — RED** → FAIL

- [ ] **Step 3: Implementuj** — dodaj detektor barge-in podpięty do `FrameAvailable` w stanie `Speaking` (osobny `IEndpointDetector` lub proste `SileroVadModel.IsSpeech` z progiem + minimalnym czasem, by uniknąć fałszywek). Po wykryciu: `Interrupt()`, odczekaj zwolnienie `_turnGate`, wystartuj nasłuch nowej tury. Zadbaj o brak wyścigu z `finally` w `ProcessTextAsync` (które robi `SetState(Idle)` i `_turnGate.Release()`).

- [ ] **Step 4: Run — GREEN**

- [ ] **Step 5: Build + pełne testy** → PASS

- [ ] **Step 6: Weryfikacja ręczna** — włącz `BargeInEnabled=true`, `AecEngine=<wybrany>`; w trakcie mowy Jarvisa powiedz coś; potwierdź, że (a) Jarvis milknie i słucha, (b) **nie** przerywa sam sobie własnym głosem (AEC działa).

- [ ] **Step 7: Commit**
```bash
git add src/ToperJarvis.Core/JarvisOrchestrator.cs tests/ToperJarvis.Core.Tests/Speech/BargeInTests.cs
git commit -m "feat: barge-in (nasluch w Speaking + przerwanie tury glosem)"
```

---

## Self-Review (wynik)

**Pokrycie specu:** ①→Faza 1, ②→Faza 2, ③→Faza 3, ④→Faza 4. Deep-dive 1 (Smart Turn/ONNX/mel/ryzyko preprocessingu) → Taski 1.3–1.7. Deep-dive 2 (AEC: Win11/WebRTC/Speex + half-duplex fallback) → Taski 4.1–4.4. Deep-dive 3 (streaming Whisper odłożony) → świadomie **poza planem**, zgodnie z werdyktem researchu (krok 5 warunkowy).

**Skan placeholderów:** Niewiadome techniczne (kształt tensorów ONNX, parametry mel, wsparcie AEC sprzętu) są celowo zamknięte w **taskach SPIKE (1.3, 4.1)** z konkretnymi krokami i komendami — to nie „TODO", tylko zadania weryfikacyjne, których wynik zasila kod kolejnych tasków. Kod testów i implementacji dla części deterministycznych podany wprost.

**Spójność typów:** `IEndpointDetector.Process/Reset` spójne między VadBuffer/Neural/Factory; `SmartTurnModel.PredictCompletion`, `SileroVadModel.IsSpeech/Reset`, `RawPcmPlayer.PlayAsync`, `IEchoCanceller.{SubmitFarEnd,ProcessNearEnd,Reset}`, `CachingTextToSpeech` (ITextToSpeech) — używane zgodnie w miejscach konsumpcji.

**Uwaga o zależności między fazami:** Task 3.2 (pełny cache PCM) i Task 4.3 (far-end) zakładają, że Faza 2 wystawia ścieżkę syntezy do bufora PCM. Jeśli w Fazie 2 zostanie ścieżka „per-zdanie proces", warto w Task 2.3 wydzielić metodę `Task<byte[]> SynthesizeAsync(string)` w Piper, z której korzystają i odtwarzanie, i cache — rozważyć przy implementacji 2.3.
