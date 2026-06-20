using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToperJarvis.Abstractions;
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

    public ObservableCollection<string> Transcript { get; } = new();

    /// <summary>Metryki systemu dla HUD (CPU/RAM).</summary>
    public SystemMetricsService Metrics { get; }

    // Konstruktor projektowy (podgląd w IDE).
    public MainWindowViewModel() : this(new DesignOrchestrator(), new SystemMetricsService()) { }

    public MainWindowViewModel(IAssistantOrchestrator orchestrator, SystemMetricsService metrics)
    {
        _orchestrator = orchestrator;
        Metrics = metrics;
        _orchestrator.StateChanged += (_, state) =>
            Dispatcher.UIThread.Post(() =>
            {
                State = state;
                StateText = Describe(state);
            });
        _orchestrator.TranscriptAdded += (_, entry) =>
            Dispatcher.UIThread.Post(() => Transcript.Add($"{Prefix(entry.Role)} {entry.Text}"));
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
        public void Start() { }
        public void Stop() { }
        public Task SubmitTextAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
    }
}
