# Task 2.3 — decyzja: streaming PCM (`--output-raw`) per zdanie vs trwały proces + cały WAV

## Kontekst

Obecny `PiperTextToSpeech` utrzymuje JEDEN trwały proces `piper.exe --json-input`,
żeby nie ładować modelu głosu (63 MB) przy każdym zdaniu. Piper pisze cały WAV per
zdanie, dopiero potem odtwarzamy. `--output-raw` wypisuje surowe PCM na stdout w
miarę syntezy — ale w trybie `--json-input` (trwały proces) nie ma delimitera
między zdaniami, więc streaming wymagałby NOWEGO procesu per zdanie (czyli
ponownego ładowania modelu). Pytanie: czy time-to-first-audio ze streamingu per
zdanie wygrywa z narzutem ponownego ładowania modelu?

## Pomiary

Środowisko: Windows, `assets/piper/piper.exe`, model `geralt.onnx` (63 MB),
config: `sample_rate: 22050`, mono, 16-bit PCM (`geralt.onnx.json`). Zdania:
krótkie „Już sprawdzam pogodę na jutro.” i dłuższe (ok. 4x więcej znaków).
5 powtórzeń (cold) / 8 powtórzeń (warm), mediana.

| Pomiar | Krótkie zdanie | Długie zdanie |
|---|---|---|
| **COLD**, nowy proces, `--output_raw`, czas do 1. bajtu PCM na stdout | **1287 ms** | **1477 ms** |
| **COLD**, nowy proces, `--output_raw`, czas do końca strumienia (cały PCM) | ~1320 ms | ~1506 ms |
| **COLD**, nowy proces, `--output_file` (cały WAV zapisany) | **1292 ms** | **1522 ms** |
| **WARM**, trwały proces (`--json-input`), czas request→WAV gotowy | **86,5 ms** | **344,5 ms** |

Narzut ponownego ładowania modelu (cold total − warm) ≈ **1292 − 86,5 ≈ 1205 ms**
dla krótkiego zdania i ≈ **1522 − 344,5 ≈ 1178 ms** dla długiego. Spójne ~1,2 s
niezależnie od długości zdania — to koszt załadowania modelu ONNX, nie syntezy.

Kluczowa obserwacja: różnica między „czas do 1. bajtu” a „czas do końca
strumienia” w trybie `--output_raw` jest znikoma (~30–40 ms na ~1,3–1,5 s
całości). Piper NIE strumieniuje audio przyrostowo w sposób użyteczny dla nas —
w praktyce cała synteza kończy się, zanim dane trafiają na stdout, więc
`--output_raw` nie daje żadnej przewagi „time-to-first-audio” nad zapisem
całego WAV-a przy tym samym (zimnym) starcie procesu.

## Analiza

- **OBECNIE (trwały proces + cały WAV)**: użytkownik czeka ~86 ms (krótkie
  zdanie) / ~345 ms (długie) od wysłania tekstu do gotowego WAV-a, potem start
  odtwarzania. Model załadowany raz na starcie aplikacji.
- **STREAMING per zdanie (nowy proces + `--output_raw`)**: użytkownik czeka
  ~1287–1477 ms do 1. bajtu PCM — i to na KAŻDE zdanie, bo nie da się utrzymać
  trwałego procesu i jednocześnie strumieniować (brak delimitera). To ~15x
  gorzej niż obecne rozwiązanie, mimo że teoretycznie streaming powinien dawać
  wcześniejszy start audio.
- Rozważony hybrydowy wariant „pierwsze zdanie przez osobny proces streamujący,
  reszta przez trwały proces” nie ma sensu: pierwsze zdanie i tak płaci ~1,2 s
  narzutu ładowania modelu, czyli byłoby WOLNIEJSZE od obecnego (trwały proces
  już ma model załadowany, więc pierwsze zdanie po starcie aplikacji też jest
  szybkie, o ile proces wystartował wcześniej / w tle).
- Koszt ładowania modelu (~1,2 s) całkowicie dominuje nad korzyścią ze
  streamingu (~30–40 ms wcześniejszy pierwszy bajt w ramach tego samego
  procesu). Streaming PCM miałby sens tylko, gdyby dało się go połączyć z
  trwałym procesem (np. przez własny delimiter/protokół binarny do Pipera),
  co wykracza poza zakres 2.3 i wymagałoby forka/patcha Pipera.

## Rekomendacja: **(B) streaming przegrywa — pomiń 2.3, zostań przy trwałym procesie + całym WAV**

Uzasadnienie liczbowe: koszt ponownego załadowania modelu (~1,2 s) jest
~15x większy niż całkowity czas syntezy w trwałym procesie dla krótkiego
zdania (86,5 ms) i ~3,5x większy niż dla długiego zdania (344,5 ms). Sam
`--output_raw` nie strumieniuje audio wystarczająco wcześnie, by to
zrekompensować (różnica pierwszy-bajt vs koniec strumienia to tylko ~30–40 ms).
Nie ma scenariusza w obecnej architekturze (bez zmian w samym Piperze), w
którym per-zdaniowy streaming biłby trwały proces. Task 2.3 (streaming PCM)
nie powinien być implementowany — obecne podejście (trwały proces
`--json-input` + zapis WAV) jest właściwe i najszybsze.
