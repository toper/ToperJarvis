# Uruchomienie ToperJarvis — co dostarczyć lokalnie

Te pliki są **poza repo** (duże modele / sekrety). Umieść je tutaj, a ścieżki są już
ustawione w `src/ToperJarvis.App/appsettings.Local.json`.

## 1. Wake-word „hey jarvis" — NIC nie trzeba (domyślnie)
- Domyślny silnik to **openWakeWord** (open-source). Modele są wbudowane w aplikację —
  **nie potrzebujesz żadnego klucza ani plików**. Słowo-klucz to „hey jarvis".
- (Opcjonalnie) Jeśli wolisz Picovoice Porcupine: ustaw w `appsettings.Local.json`
  `Jarvis:WakeWord:Engine = "porcupine"` i wklej **AccessKey** z https://console.picovoice.ai/
  do `Jarvis:WakeWord:AccessKey`.

## 2. Model STT — Whisper (offline)
- Pobierz model ggml, np. **ggml-base.bin** (mały, szybki) lub `ggml-small.bin` (lepsza jakość PL):
  https://huggingface.co/ggerganov/whisper.cpp/tree/main
- Zapisz jako: `assets/whisper/ggml-base.bin`

## 3. TTS — Piper + polski głos (Geralt)
- Pobierz Piper dla Windows (`piper_windows_amd64.zip`) z:
  https://github.com/rhasspy/piper/releases
  → rozpakuj `piper.exe` do `assets/piper/piper.exe`
- Pobierz polski głos **Geralt** (Jacek Rozenek): https://github.com/willovex/geralt-piper-voice
  (katalog `model/`) → skopiuj plik `.onnx` i towarzyszący `.onnx.json` do `assets/piper/`
  jako `geralt.onnx` i `geralt.onnx.json`.
- Alternatywa (oficjalny głos PL): `pl_PL-darkman-medium.onnx` + `.json` z
  https://huggingface.co/rhasspy/piper-voices/tree/main/pl/pl_PL/darkman/medium

## 4. LLM — vLLM (Qwen3)
- Endpoint musi być osiągalny: `http://192.168.7.30:8000/v1` (API zgodne z OpenAI).
- Sprawdź np.: `curl http://192.168.7.30:8000/v1/models`

## 5. Uruchomienie
```
cd D:\GIT\ToperJarvis\src\ToperJarvis.App
dotnet run
```
Okno otworzy się i pojawi ikona w trayu. Powiedz „Jarvis", potem komendę — albo wpisz komendę w polu tekstowym i Enter.

## Narzędzia opcjonalne

- **`browser_control`** (sterowanie przeglądarką) używa Playwright. Przy pierwszym użyciu pobierz
  binaria przeglądarki: w katalogu projektu `dotnet build`, a następnie
  `pwsh bin/Debug/net10.0/playwright.ps1 install chromium` (lub `playwright install chromium`,
  jeśli masz CLI Playwrighta). Domyślnie używany jest dedykowany profil w
  `%LOCALAPPDATA%\ToperJarvis\browser`; aby użyć realnego profilu/zainstalowanej przeglądarki,
  ustaw `Jarvis:Browser:UserDataDir` i `Jarvis:Browser:Channel` (`chrome`/`msedge`).

## Docelowa zawartość assets/
```
assets/
  whisper/ggml-base.bin
  piper/piper.exe
  piper/geralt.onnx
  piper/geralt.onnx.json
```

## Budowanie wersji do dystrybucji (self-contained)

Skrypt `scripts/publish.ps1` buduje samodzielny plik wykonywalny (bez wymaganego .NET na
maszynie docelowej):

```powershell
pwsh scripts/publish.ps1                       # Release, win-x64 → publish/
pwsh scripts/publish.ps1 -ReadyToRun           # + szybszy start (większy plik)
pwsh scripts/publish.ps1 -Output C:\Dist\Toper # inny katalog wyjściowy
```

Skrypt: publikuje single-file self-contained, kopiuje obok `assets/` (jeśli istnieje lokalnie)
i usuwa deweloperski `appsettings.Local.json`.

Wymagania na maszynie docelowej:
- osiągalny endpoint vLLM (`Jarvis:Llm:BaseUrl` w `appsettings.json`),
- zawartość `assets/` (modele Whisper/Piper — jak wyżej),
- **ffmpeg** na PATH — tylko jeśli używasz audio/wideo w `file_processor`
  (lub ustaw `Jarvis:Media:FfmpegPath`).
