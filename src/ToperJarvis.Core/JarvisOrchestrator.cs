using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Memory;
using ToperJarvis.Abstractions.Speech;
using ToperJarvis.Abstractions.Tools;
using ToperJarvis.Core.Prompting;
using ToperJarvis.Llm;
using ToperJarvis.Speech.Vad;

namespace ToperJarvis.Core;

/// <summary>
/// Centralny orchestrator pętli głosowej. Łączy przechwytywanie audio, wykrywanie słowa-klucza,
/// VAD, STT, LLM (streaming + tool-calling) oraz TTS z nakładaniem syntezy na generowanie.
/// </summary>
public sealed class JarvisOrchestrator : IAssistantOrchestrator, IDisposable
{
    private readonly IAudioCapture _capture;
    private readonly IWakeWordDetector _wakeWord;
    private readonly ISpeechToText _stt;
    private readonly ITextToSpeech _tts;
    private readonly IChatClient _chat;
    private readonly SystemPromptProvider _prompt;
    private readonly IMemoryStore _memory;
    private readonly ILogger<JarvisOrchestrator> _logger;
    private readonly AudioOptions _audio;
    private readonly LlmOptions _llm;
    private readonly ChatOptions _chatOptions;
    private readonly IReadOnlyDictionary<string, string> _lexicon;

    private readonly List<ChatMessage> _history = new();
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private CancellationTokenSource? _turnCts;
    private VadBuffer? _vad;
    private bool _started;

    // Push-to-talk: bufor nagrania między wciśnięciem a puszczeniem klawisza.
    private readonly List<float> _pttBuffer = new();
    private volatile bool _pttActive;

    public JarvisOrchestrator(
        IAudioCapture capture,
        IWakeWordDetector wakeWord,
        ISpeechToText stt,
        ITextToSpeech tts,
        IChatClient chat,
        SystemPromptProvider prompt,
        IMemoryStore memory,
        IEnumerable<IJarvisTool> tools,
        IOptions<JarvisOptions> options,
        ILogger<JarvisOrchestrator> logger)
    {
        _capture = capture;
        _wakeWord = wakeWord;
        _stt = stt;
        _tts = tts;
        _chat = chat;
        _prompt = prompt;
        _memory = memory;
        _logger = logger;
        _audio = options.Value.Audio;
        _llm = options.Value.Llm;
        _lexicon = options.Value.Tts.Lexicon;
        // Mózgiem jest zdalny agent Hermes (Hektor) — to ON wywołuje narzędzia (lokalne przez MCP,
        // resztę własne). ToperJarvis nie wysyła już własnej listy narzędzi do modelu.
        _chatOptions = new ChatOptions();
    }

    public AssistantState State { get; private set; } = AssistantState.Idle;

    public event EventHandler<AssistantState>? StateChanged;
    public event EventHandler<TranscriptEntry>? TranscriptAdded;
    public event EventHandler<double>? TurnCompleted;

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _capture.Start();
        _wakeWord.Detected += OnWakeWordDetected;
        _wakeWord.Start();
        SetState(AssistantState.Idle);
        _logger.LogInformation("Jarvis uruchomiony — czekam na słowo-klucz.");
    }

    public void Stop()
    {
        if (!_started)
            return;

        _started = false;
        _wakeWord.Detected -= OnWakeWordDetected;
        _capture.FrameAvailable -= OnVadFrame;
        _wakeWord.Stop();
        _capture.Stop();
    }

    private void OnWakeWordDetected(object? sender, EventArgs e)
    {
        // Reaguj wyłącznie, gdy jesteśmy bezczynni — ignoruj w trakcie rozmowy.
        if (State != AssistantState.Idle)
            return;

        _vad = new VadBuffer(_audio);
        _capture.FrameAvailable += OnVadFrame;
        SetState(AssistantState.Listening);
    }

    private void OnVadFrame(object? sender, AudioFrame frame)
    {
        var utterance = _vad?.Process(frame.Samples);
        if (utterance is null)
            return;

        // Koniec wypowiedzi — odepnij VAD i przejdź do przetwarzania.
        _capture.FrameAvailable -= OnVadFrame;
        _ = ProcessUtteranceAsync(utterance);
    }

    private async Task ProcessUtteranceAsync(float[] samples)
    {
        try
        {
            SetState(AssistantState.Transcribing);
            var text = await _stt.TranscribeAsync(samples);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogInformation("Pusta transkrypcja — powrót do nasłuchu.");
                SetState(AssistantState.Idle);
                return;
            }

            await ProcessTextAsync(text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd przetwarzania wypowiedzi.");
            SetState(AssistantState.Idle);
        }
    }

    public void BeginPushToTalk()
    {
        if (!_started || _pttActive || State != AssistantState.Idle)
            return;

        _pttActive = true;
        lock (_pttBuffer)
            _pttBuffer.Clear();

        _capture.FrameAvailable += OnPttFrame;
        SetState(AssistantState.Listening);
        _logger.LogInformation("Push-to-talk: nasłuch (przytrzymano klawisz).");
    }

    public void EndPushToTalk()
    {
        if (!_pttActive)
            return;

        _pttActive = false;
        _capture.FrameAvailable -= OnPttFrame;

        float[] samples;
        lock (_pttBuffer)
            samples = _pttBuffer.ToArray();

        var seconds = samples.Length / (float)_capture.SampleRate;
        _logger.LogInformation("Push-to-talk: koniec, nagrano {Seconds:F1} s.", seconds);

        // Zbyt krótkie (przypadkowe tknięcie) — ignoruj.
        if (samples.Length < _capture.SampleRate * 0.3)
        {
            SetState(AssistantState.Idle);
            return;
        }

        _ = ProcessUtteranceAsync(samples);
    }

    private void OnPttFrame(object? sender, AudioFrame frame)
    {
        lock (_pttBuffer)
            _pttBuffer.AddRange(frame.Samples);
    }

    public async Task SubmitTextAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            await ProcessTextAsync(text, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd przetwarzania komendy tekstowej.");
            SetState(AssistantState.Idle);
        }
    }

    private async Task ProcessTextAsync(string userText, CancellationToken ct)
    {
        await _turnGate.WaitAsync(ct);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        // Token tury: powiązany z zewnętrznym ct + wewnętrzny, by Interrupt() (Esc) mógł przerwać.
        _turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _turnCts.Token;
        try
        {
            AddTranscript(TranscriptRole.User, userText);
            EnsureSystemPrompt();
            _history.Add(new ChatMessage(ChatRole.User, userText));

            SetState(AssistantState.Thinking);

            // Streaming: Hektor strumieniuje samą odpowiedź (tool-progress i narracja „Let me check…"
            // są wyłączone po stronie API-servera Hermesa), więc TTS rusza już ze zdaniem 1.
            var accumulator = new SentenceAccumulator();
            var (ttsChannel, ttsWorker) = StartTtsWorker(token);
            var assistant = new System.Text.StringBuilder();
            var spoke = false;
            try
            {
                await foreach (var update in _chat.GetStreamingResponseAsync(_history, _chatOptions, token))
                {
                    var delta = update.Text;
                    if (string.IsNullOrEmpty(delta))
                        continue;

                    assistant.Append(delta);
                    foreach (var sentence in accumulator.Add(delta))
                    {
                        // Normalizuj pod TTS: usuń Markdown, zastosuj leksykon wymowy. Puste pomiń.
                        var speech = SpeechNormalizer.Normalize(sentence, _lexicon);
                        if (string.IsNullOrWhiteSpace(speech))
                            continue;
                        if (!spoke)
                        {
                            spoke = true;
                            SetState(AssistantState.Speaking);
                        }
                        await ttsChannel.Writer.WriteAsync(speech, token);
                    }
                }

                if (accumulator.Flush() is { } tail)
                {
                    var speech = SpeechNormalizer.Normalize(tail, _lexicon);
                    if (!string.IsNullOrWhiteSpace(speech))
                    {
                        if (!spoke)
                            SetState(AssistantState.Speaking);
                        await ttsChannel.Writer.WriteAsync(speech, token);
                    }
                }

                // Pokaż pełną odpowiedź (surowy Markdown — UI renderuje) po wygenerowaniu.
                var full = assistant.ToString().Trim();
                if (full.Length > 0)
                {
                    _history.Add(new ChatMessage(ChatRole.Assistant, full));
                    AddTranscript(TranscriptRole.Assistant, full);
                }
            }
            finally
            {
                // Zawsze domknij kanał i zaczekaj na workera, nawet przy wyjątku/anulowaniu —
                // inaczej worker zawisłby na ReadAllAsync.
                ttsChannel.Writer.TryComplete();
                try
                {
                    await ttsWorker;
                }
                catch (OperationCanceledException)
                {
                    // anulowanie odtwarzania — ignorujemy
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Przerwane przez użytkownika (Esc) lub zewnętrzny ct — to normalne zakończenie tury.
            _logger.LogInformation("Tura przerwana.");
        }
        finally
        {
            stopwatch.Stop();
            _turnCts.Dispose();
            _turnCts = null;
            TurnCompleted?.Invoke(this, stopwatch.Elapsed.TotalMilliseconds);
            _turnGate.Release();
            SetState(AssistantState.Idle);
        }
    }

    /// <summary>Przerywa bieżącą turę — anuluje wywołanie LLM i odtwarzanie TTS (Esc w UI).</summary>
    public void Interrupt()
    {
        try { _turnCts?.Cancel(); }
        catch (ObjectDisposedException) { /* tura już zakończona */ }
    }

    /// <summary>
    /// Uruchamia konsumenta kolejki TTS. Zdania są odtwarzane sekwencyjnie, ale generowanie
    /// kolejnych przez LLM trwa równolegle — to skraca odczuwaną latencję.
    /// </summary>
    private (Channel<string> channel, Task worker) StartTtsWorker(CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var worker = Task.Run(async () =>
        {
            await foreach (var sentence in channel.Reader.ReadAllAsync(ct))
                await _tts.SpeakAsync(sentence, ct);
        }, ct);

        return (channel, worker);
    }

    private void EnsureSystemPrompt()
    {
        // Personę i pamięć dostarcza Hektor (Hermes) po swojej stronie. Wysyłamy tylko lekki system
        // message z wytycznymi formy pod TTS (Hermes je honoruje) — raz, na początku historii.
        if (_history.Count != 0)
            return;

        var prompt = _llm.SystemPrompt;
        if (!string.IsNullOrWhiteSpace(prompt))
            _history.Add(new ChatMessage(ChatRole.System, prompt));
    }

    private void AddTranscript(TranscriptRole role, string text) =>
        TranscriptAdded?.Invoke(this, new TranscriptEntry(role, text));

    private void SetState(AssistantState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        Stop();
        _turnGate.Dispose();
    }
}
