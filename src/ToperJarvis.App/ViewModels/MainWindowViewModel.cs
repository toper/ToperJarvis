using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;
using ToperJarvis.App.Controls;
using ToperJarvis.App.Services;

namespace ToperJarvis.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAssistantOrchestrator _orchestrator;

    [ObservableProperty]
    private string _stateText = "Bezczynny";

    [ObservableProperty]
    private AssistantState _state = AssistantState.Idle;

    [ObservableProperty]
    private string _inputText = string.Empty;

    /// <summary>Bieżący poziom sygnału mikrofonu (0..1) dla wskaźnika w HUD.</summary>
    [ObservableProperty]
    private double _micLevel;

    /// <summary>Czas przetwarzania ostatniej komendy (ms) — telemetria HUD.</summary>
    [ObservableProperty]
    private double _lastTurnMs;

    /// <summary>Odczyty telemetrii (HA, DGX) do HUD.</summary>
    [ObservableProperty]
    private IReadOnlyList<HudReadout> _telemetry = Array.Empty<HudReadout>();

    /// <summary>Mini-podgląd z kamery (klatka JPEG zdekodowana do bitmapy).</summary>
    [ObservableProperty]
    private Bitmap? _cameraFrame;

    /// <summary>Obciążenie GPU serwera DGX (%) — duży gauge.</summary>
    [ObservableProperty]
    private double _gpuUtil;

    /// <summary>Pobór mocy GPU serwera DGX (W) — duży gauge.</summary>
    [ObservableProperty]
    private double _powerW;

    private readonly HomeAssistantClient? _ha;
    private readonly DgxClient? _dgx;
    private readonly WebcamService? _webcam;
    private readonly HomeAssistantOptions? _haOptions;

    public ObservableCollection<string> Transcript { get; } = new();

    /// <summary>Metryki systemu dla HUD (CPU/RAM).</summary>
    public SystemMetricsService Metrics { get; }

    // Konstruktor projektowy (podgląd w IDE).
    public MainWindowViewModel() : this(new DesignOrchestrator(), new SystemMetricsService(), null) { }

    public MainWindowViewModel(
        IAssistantOrchestrator orchestrator,
        SystemMetricsService metrics,
        IAudioCapture? capture,
        HomeAssistantClient? ha = null,
        DgxClient? dgx = null,
        WebcamService? webcam = null,
        IOptions<JarvisOptions>? options = null)
    {
        _orchestrator = orchestrator;
        Metrics = metrics;
        _ha = ha;
        _dgx = dgx;
        _webcam = webcam;
        _haOptions = options?.Value.HomeAssistant;

        _orchestrator.StateChanged += (_, state) =>
            Dispatcher.UIThread.Post(() =>
            {
                State = state;
                StateText = Describe(state);
            });
        _orchestrator.TranscriptAdded += (_, entry) =>
            Dispatcher.UIThread.Post(() => Transcript.Add($"{Prefix(entry.Role)} {entry.Text}"));
        _orchestrator.TurnCompleted += (_, ms) =>
            Dispatcher.UIThread.Post(() => LastTurnMs = ms);

        if (capture is not null)
            capture.FrameAvailable += OnAudioFrame;

        if (!Design.IsDesignMode)
            StartTelemetry();
    }

    // Uruchamia źródła telemetrii i timery odświeżania (kamera szybko, dane wolniej).
    private void StartTelemetry()
    {
        _webcam?.Start();
        _dgx?.Start();

        var cameraTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        cameraTimer.Tick += (_, _) => UpdateCamera();
        cameraTimer.Start();

        var pollSeconds = _haOptions?.PollSeconds is > 0 and var p ? p : 5;
        var dataTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(pollSeconds) };
        dataTimer.Tick += async (_, _) => await UpdateTelemetryAsync();
        dataTimer.Start();
        _ = UpdateTelemetryAsync(); // pierwszy odczyt od razu
    }

    private void UpdateCamera()
    {
        var jpeg = _webcam?.LatestJpeg;
        if (jpeg is null)
            return;

        try
        {
            using var ms = new MemoryStream(jpeg);
            // Nie zwalniamy poprzedniej bitmapy ręcznie — wątek renderujący Avalonii może jeszcze
            // jej używać (race → AccessViolation). Mała miniatura, GC sobie poradzi.
            CameraFrame = new Bitmap(ms);
        }
        catch { /* uszkodzona klatka — pomijamy */ }
    }

    private async Task UpdateTelemetryAsync()
    {
        var list = new List<HudReadout>();

        if (_ha is { Enabled: true } && _haOptions is not null)
        {
            foreach (var s in _haOptions.Sensors)
            {
                var state = await _ha.GetStateAsync(s.EntityId);
                list.Add(new HudReadout(s.Label, state is null ? "—" : $"{state}{s.Unit}", s.Right));
            }
        }

        if (_dgx is not null)
        {
            GpuUtil = _dgx.GpuUtil ?? 0;
            PowerW = _dgx.PowerW ?? 0;
        }

        Telemetry = list;
    }

    // Liczy szczyt ramki i aktualizuje wskaźnik z płynnym opadaniem (event leci z wątku audio).
    private void OnAudioFrame(object? sender, AudioFrame frame)
    {
        var peak = 0f;
        foreach (var sample in frame.Samples)
        {
            var abs = sample < 0 ? -sample : sample;
            if (abs > peak)
                peak = abs;
        }

        // Lekkie wzmocnienie (mowa rzadko zbliża się do 1.0) + przycięcie do zakresu paska.
        var level = Math.Min(1.0, peak * 3.0);
        Dispatcher.UIThread.Post(() => MicLevel = Math.Max(level, MicLevel * 0.8));
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (text.Length == 0)
            return;

        InputText = string.Empty;
        await _orchestrator.SubmitTextAsync(text);
    }

    private static string Describe(AssistantState state) => state switch
    {
        AssistantState.Idle => "Bezczynny — powiedz „Jarvis”",
        AssistantState.Listening => "Słucham…",
        AssistantState.Transcribing => "Rozpoznaję mowę…",
        AssistantState.Thinking => "Myślę…",
        AssistantState.Speaking => "Mówię…",
        _ => state.ToString(),
    };

    private static string Prefix(TranscriptRole role) => role switch
    {
        TranscriptRole.User => "🧑",
        TranscriptRole.Assistant => "🤖",
        _ => "⚙️",
    };

    /// <summary>Atrapa orchestratora dla podglądu w projektancie XAML.</summary>
    private sealed class DesignOrchestrator : IAssistantOrchestrator
    {
        public AssistantState State => AssistantState.Idle;
        public event EventHandler<AssistantState>? StateChanged { add { } remove { } }
        public event EventHandler<TranscriptEntry>? TranscriptAdded { add { } remove { } }
        public event EventHandler<double>? TurnCompleted { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public Task SubmitTextAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
        public void BeginPushToTalk() { }
        public void EndPushToTalk() { }
    }
}
