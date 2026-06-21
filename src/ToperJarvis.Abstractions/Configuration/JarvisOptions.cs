namespace ToperJarvis.Abstractions.Configuration;

/// <summary>
/// Główny korzeń konfiguracji aplikacji (sekcja "Jarvis" w appsettings.json).
/// Migracja odpowiednika z _Old/config/api_keys.json.
/// </summary>
public sealed class JarvisOptions
{
    public const string SectionName = "Jarvis";

    public LlmOptions Llm { get; set; } = new();
    public VisionOptions Vision { get; set; } = new();
    public SttOptions Stt { get; set; } = new();
    public TtsOptions Tts { get; set; } = new();
    public WakeWordOptions WakeWord { get; set; } = new();
    public AudioOptions Audio { get; set; } = new();
    public BrowserOptions Browser { get; set; } = new();
    public MediaOptions Media { get; set; } = new();
}

/// <summary>Przetwarzanie multimediów (file_processor — audio/wideo przez ffmpeg).</summary>
public sealed class MediaOptions
{
    /// <summary>Ścieżka do ffmpeg. Domyślnie „ffmpeg" (z PATH).</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";
}

/// <summary>Sterowanie przeglądarką (narzędzie browser_control, Playwright).</summary>
public sealed class BrowserOptions
{
    /// <summary>
    /// Katalog trwałego profilu przeglądarki. Pusty = dedykowany profil w
    /// <c>%LOCALAPPDATA%\ToperJarvis\browser</c>. Aby użyć realnego profilu, wskaż katalog
    /// „User Data" zainstalowanej przeglądarki (wymaga jej zamknięcia — blokada profilu).
    /// </summary>
    public string UserDataDir { get; set; } = "";

    /// <summary>
    /// Kanał przeglądarki: "chrome" lub "msedge" (zainstalowana), pusty = wbudowany Chromium Playwright.
    /// </summary>
    public string Channel { get; set; } = "";

    /// <summary>Tryb bez okna. Domyślnie false — asystent działa na widocznym pulpicie.</summary>
    public bool Headless { get; set; }
}

/// <summary>Zdalny LLM — vLLM z API zgodnym z OpenAI.</summary>
public sealed class LlmOptions
{
    /// <summary>Bazowy adres API zgodnego z OpenAI, np. http://192.168.7.30:8000/v1.</summary>
    public string BaseUrl { get; set; } = "http://192.168.7.30:8000/v1";

    /// <summary>Identyfikator modelu wystawionego przez vLLM (np. Qwen3).</summary>
    public string Model { get; set; } = "Qwen3";

    /// <summary>Klucz API (vLLM zwykle akceptuje dowolny placeholder).</summary>
    public string ApiKey { get; set; } = "not-needed";

    /// <summary>Limit czasu pojedynczego żądania (sekundy).</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Liczba ponowień przy błędach przejściowych (5xx/408/429). 0 = bez ponowień.</summary>
    public int MaxRetries { get; set; } = 2;
}

/// <summary>Model wizji (multimodalny). Domyślnie ten sam endpoint co LLM.</summary>
public sealed class VisionOptions
{
    /// <summary>Adres endpointu wizji. Pusty = użyj <see cref="LlmOptions.BaseUrl"/>.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Model VL. Pusty = użyj modelu LLM.</summary>
    public string Model { get; set; } = "";

    public string ApiKey { get; set; } = "not-needed";

    /// <summary>
    /// Maksymalna liczba tokenów odpowiedzi. Wizja jest wołana z wyłączonym myśleniem
    /// (<c>enable_thinking:false</c>), więc opisy są zwięzłe.
    /// </summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>Limit czasu pojedynczego żądania wizji (sekundy).</summary>
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>Rozpoznawanie mowy offline (Whisper.net).</summary>
public sealed class SttOptions
{
    /// <summary>Ścieżka do modelu ggml Whisper (np. assets/whisper/ggml-base.bin).</summary>
    public string ModelPath { get; set; } = "assets/whisper/ggml-base.bin";

    /// <summary>Język rozpoznawania (np. "pl"); "auto" = autodetekcja.</summary>
    public string Language { get; set; } = "pl";
}

/// <summary>Synteza mowy offline (Piper).</summary>
public sealed class TtsOptions
{
    /// <summary>Ścieżka do pliku wykonywalnego Piper (piper.exe).</summary>
    public string PiperPath { get; set; } = "assets/piper/piper.exe";

    /// <summary>Ścieżka do modelu głosu .onnx (głos polski — Geralt).</summary>
    public string VoiceModelPath { get; set; } = "assets/piper/geralt.onnx";

    /// <summary>Tempo mowy (1.0 = normalne).</summary>
    public double Speed { get; set; } = 1.0;
}

/// <summary>Wykrywanie słowa-klucza.</summary>
public sealed class WakeWordOptions
{
    /// <summary>
    /// Silnik wykrywania: "openwakeword" (domyślny, open-source, bez klucza) lub "porcupine".
    /// </summary>
    public string Engine { get; set; } = "openwakeword";

    /// <summary>
    /// Nazwa modelu openWakeWord (wbudowany w pakiet), np. "hey_jarvis_v0.1".
    /// </summary>
    public string Model { get; set; } = "hey_jarvis_v0.1";

    /// <summary>AccessKey Picovoice — wymagany tylko dla silnika "porcupine".</summary>
    public string AccessKey { get; set; } = "";

    /// <summary>Wbudowane słowo-klucz Porcupine (dla silnika "porcupine").</summary>
    public string Keyword { get; set; } = "jarvis";

    /// <summary>Czułość/próg detekcji 0..1 (wyższa = więcej detekcji, więcej fałszywych).</summary>
    public float Sensitivity { get; set; } = 0.5f;
}

/// <summary>Parametry przechwytywania audio i detekcji aktywności głosowej (VAD).</summary>
public sealed class AudioOptions
{
    public int SampleRate { get; set; } = 16000;

    /// <summary>Próg RMS uznania ramki za mowę (port z _Old/main.py).</summary>
    public double SpeechThreshold { get; set; } = 0.008;

    /// <summary>Próg RMS uznania ramki za ciszę (histereza).</summary>
    public double SilenceThreshold { get; set; } = 0.004;

    /// <summary>Czas ciszy kończący wypowiedź (sekundy).</summary>
    public double SilenceSeconds { get; set; } = 0.7;

    /// <summary>Minimalny czas mowy (sekundy).</summary>
    public double MinSpeechSeconds { get; set; } = 0.3;

    /// <summary>Maksymalny czas mowy (sekundy).</summary>
    public double MaxSpeechSeconds { get; set; } = 30.0;
}
