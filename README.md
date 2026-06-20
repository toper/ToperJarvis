# ToperJarvis

Lokalny asystent głosowy dla Windows (.NET 10 + Avalonia). Aplikacja siedzi w trayu, działa
w trybie okienkowym lub pełnoekranowym i nasłuchuje komend głosowych po wykryciu słowa-klucza
**„Jarvis"**.

## Pętla działania

```
"Jarvis" (wake-word) → nasłuch (VAD) → STT → LLM (tool-calling) → wykonanie narzędzia → TTS
```

## Stos technologiczny

| Obszar | Technologia |
|---|---|
| UI | Avalonia 12 (MVVM), SkiaSharp HUD, wbudowany TrayIcon |
| Wake-word | openWakeWord („hey jarvis", open-source, bez klucza) — opcjonalnie Picovoice Porcupine |
| STT | Whisper.net (offline) |
| TTS | Piper (głos polski — Geralt) |
| LLM | vLLM / Qwen3 (API zgodne z OpenAI) `http://192.168.7.30:8000` |
| Audio | NAudio + VAD (RMS) |
| Narzędzia | Microsoft.Extensions.AI function-calling |

## Struktura

```
src/
  ToperJarvis.App              — Avalonia (tray, okno, HUD)
  ToperJarvis.Core             — orchestrator pętli, pamięć, agent
  ToperJarvis.Speech           — wake-word, audio, VAD, STT, TTS
  ToperJarvis.Llm              — klient LLM, prompt, streaming zdań
  ToperJarvis.Tools            — narzędzia (ITool) + dispatcher
  ToperJarvis.Abstractions     — interfejsy, modele, eventy
  ToperJarvis.Platform.Windows — implementacje zależne od Windows
tests/                         — testy jednostkowe
assets/                        — modele, ikony, prompt (poza repo)
```

## Wymagania środowiska

- .NET SDK 10+
- Endpoint vLLM (Qwen3) pod `http://192.168.7.30:8000/v1`
- Model Whisper w `assets/whisper/`
- `piper.exe` + głos w `assets/piper/`
- Wake-word („hey jarvis") działa od razu — modele openWakeWord są wbudowane, **bez klucza**.
  AccessKey Picovoice jest potrzebny tylko, gdy ustawisz `WakeWord:Engine = porcupine`.

Szczegółowa instrukcja dostarczenia plików lokalnych (Whisper, Piper + głos) — zob. [`assets/SETUP.md`](assets/SETUP.md).

## Uruchomienie

```powershell
cd src\ToperJarvis.App
dotnet run
```

Otworzy się okno i pojawi ikona w trayu. Powiedz „hey jarvis", potem komendę — albo wpisz
komendę w polu tekstowym i Enter. Konfigurację lokalną (ścieżki modeli, endpoint LLM) trzymaj
w `appsettings.Local.json` (poza repo) — przykład w `appsettings.Local.example.json`.

## Status

🚧 W aktywnym rozwoju. Pełna pętla głosowa (wake-word → STT → LLM → narzędzia → TTS) działa
lokalnie po dostarczeniu modeli Whisper/Piper i uruchomieniu endpointu vLLM.
