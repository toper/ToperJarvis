# ToperJarvis

Lokalny asystent głosowy dla Windows (.NET 10 + Avalonia). Aplikacja siedzi w trayu, działa
w trybie okienkowym lub pełnoekranowym i reaguje na komendy głosowe — po wykryciu słowa-klucza
**„Hey Jarvis"** albo po przytrzymaniu globalnego skrótu **push-to-talk** (domyślnie prawy Ctrl).
Centralny ekran to animowany HUD w stylu J.A.R.V.I.S z telemetrią na żywo.

## Pętla działania

```
"Hey Jarvis" (wake-word)  ┐
                          ├→ nasłuch (VAD) → STT → LLM (tool-calling) → narzędzie → TTS
push-to-talk (przytrzymaj)┘
```

## Stos technologiczny

| Obszar | Technologia |
|---|---|
| UI | Avalonia 12 (MVVM), HUD na SkiaSharp, wbudowany TrayIcon |
| Wake-word | openWakeWord („hey jarvis", open-source, bez klucza) — opcjonalnie Picovoice Porcupine |
| Push-to-talk | globalny low-level keyboard hook (Windows), przytrzymaj klawisz = nasłuch |
| STT | Whisper.net — **GPU/CUDA** (NVIDIA, np. RTX 5070 Ti), z fallbackiem na CPU |
| TTS | Piper (głos polski — Geralt), jeden trwały proces (`--json-input`) dla niskiej latencji |
| LLM | vLLM / Qwen3 (API zgodne z OpenAI) `http://192.168.7.30:8000`; domyślnie `/no_think` (szybciej) |
| Audio | NAudio + VAD (RMS); wybór mikrofonu i wyjścia z poziomu traya |
| Telemetria HUD | Home Assistant (temperatury), DGX przez SSH `nvidia-smi` (GPU util/moc), lokalna kamera (OpenCvSharp) |
| Narzędzia | Microsoft.Extensions.AI function-calling |

## HUD / telemetria

Centralny orb pokazuje stan asystenta (GOTOWY/SŁUCHAM/MYŚLĘ/MÓWIĘ) i reaguje na mikrofon.
Wokół niego prezentowane są na żywo:

- **GPU AI / MOC AI** — obciążenie i pobór mocy GPU serwera DGX (SSH + `nvidia-smi`),
- **CPU / RAM** lokalnej maszyny, **PRZETWARZANIE** — czas ostatniej tury (ms),
- **temperatury z Home Assistant** (gabinet, NAS/QNAP, dom, salon — konfigurowalne),
- **mini-podgląd z kamery** (lokalny WebCam),
- zegar, linie przepływu danych, animowane pierścienie.

## Struktura

```
src/
  ToperJarvis.App              — Avalonia (tray, okno, HUD, telemetria: HA/DGX/kamera, logi)
  ToperJarvis.Core             — orchestrator pętli, pamięć, agent
  ToperJarvis.Speech           — wake-word, audio, VAD, STT (CUDA), TTS (trwały Piper)
  ToperJarvis.Llm              — klient LLM, prompt, streaming zdań
  ToperJarvis.Tools            — narzędzia (ITool) + dispatcher
  ToperJarvis.Abstractions     — interfejsy, modele, eventy
  ToperJarvis.Platform.Windows — implementacje zależne od Windows (zrzut ekranu, hotkey PTT)
tests/                         — testy jednostkowe
assets/                        — modele, ikony, prompt (poza repo)
```

## Wymagania środowiska

- .NET SDK 10+
- Endpoint vLLM (Qwen3) pod `http://192.168.7.30:8000/v1`
- Model Whisper w `assets/whisper/`; `piper.exe` + głos w `assets/piper/`
- Wake-word („hey jarvis") działa od razu — modele openWakeWord są wbudowane, **bez klucza**.
  AccessKey Picovoice jest potrzebny tylko, gdy ustawisz `WakeWord:Engine = porcupine`.
- STT na GPU wymaga karty NVIDIA + sterowników CUDA; bez niej Whisper zadziała na CPU.
- Telemetria HUD jest opcjonalna — bez konfiguracji panele pokazują „—".

Szczegółowa instrukcja plików lokalnych (Whisper, Piper + głos) — zob. [`assets/SETUP.md`](assets/SETUP.md).

## Konfiguracja

Konfigurację lokalną i sekrety trzymaj w `appsettings.Local.json` (poza repo) — przykład w
`appsettings.Local.example.json`. Kluczowe sekcje (`Jarvis:…`):

| Klucz | Opis |
|---|---|
| `Llm:EnableThinking` | `false` (domyślnie) dopisuje `/no_think` do Qwen3 — krótszy czas odpowiedzi |
| `PushToTalk:Enabled` / `Key` | push-to-talk i klawisz (domyślnie `RightCtrl`) |
| `Audio:InputDeviceName` / `OutputDeviceName` | wybrane urządzenia (ustawiane też z traya) |
| `WakeWord:Sensitivity` / `TriggerLevel` | czułość i liczba ramek progu (domyślnie 0.6 / 1) |
| `HomeAssistant:BaseUrl` / `Token` / `Sensors` | adres, token i lista encji do pokazania |
| `Dgx:Host` / `User` | monitoring GPU przez SSH (klucz z `~/.ssh`) |
| `Camera:Enabled` / `DeviceIndex` | mini-podgląd z lokalnej kamery |

## Uruchomienie

```powershell
cd src\ToperJarvis.App
dotnet run
```

Otworzy się okno i pojawi ikona w trayu. Powiedz **„Hey Jarvis"** i komendę, przytrzymaj
**prawy Ctrl** i mów, albo wpisz komendę w polu i Enter. Z traya wybierzesz mikrofon i wyjście
audio. Aplikacja to `WinExe` — logi (m.in. score wake-worda, poziom mikrofonu, błędy) trafiają
do pliku `logs/jarvis.log` obok exe.

## Status

🚧 W aktywnym rozwoju. Pełna pętla głosowa (wake-word / push-to-talk → STT → LLM → narzędzia → TTS)
działa lokalnie po dostarczeniu modeli Whisper/Piper i uruchomieniu endpointu vLLM.
