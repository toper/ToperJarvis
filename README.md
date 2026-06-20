# ToperJarvis

Lokalny asystent głosowy dla Windows (.NET 8 + Avalonia) — następca prototypu w Pythonie
(`_Old/`). Aplikacja siedzi w trayu, działa w trybie okienkowym lub pełnoekranowym i nasłuchuje
komend głosowych po wykryciu słowa-klucza **„Jarvis"**.

## Pętla działania

```
"Jarvis" (wake-word) → nasłuch (VAD) → STT → LLM (tool-calling) → wykonanie narzędzia → TTS
```

## Stos technologiczny

| Obszar | Technologia |
|---|---|
| UI | Avalonia 11 (MVVM), SkiaSharp HUD, wbudowany TrayIcon |
| Wake-word | Picovoice Porcupine („Jarvis") |
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
_Old/                          — referencyjny prototyp w Pythonie
```

## Wymagania środowiska

- .NET SDK 8+
- Endpoint vLLM (Qwen3) pod `http://192.168.7.30:8000/v1`
- Model Whisper w `assets/whisper/`
- `piper.exe` + głos w `assets/piper/`
- AccessKey Picovoice (Porcupine) w `appsettings.Local.json`

## Status

🚧 W trakcie przepisywania z Pythona na .NET. Zobacz plan w historii projektu.
