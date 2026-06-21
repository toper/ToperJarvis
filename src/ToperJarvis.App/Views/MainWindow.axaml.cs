using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ToperJarvis.App.ViewModels;

namespace ToperJarvis.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        DataContextChanged += OnDataContextChanged;
    }

    // Po dodaniu wpisu do transkryptu przewijamy na dół (najnowsze widoczne).
    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Transcript.CollectionChanged += OnTranscriptChanged;
    }

    private void OnTranscriptChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => TranscriptScroll.ScrollToEnd(), DispatcherPriority.Background);

    /// <summary>F11 — przełącza pełny ekran/okno; Esc — wychodzi z pełnego ekranu.</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            e.Handled = true;
        }
    }

    /// <summary>Enter w polu komendy wysyła komendę.</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (DataContext is MainWindowViewModel vm && vm.SendCommand.CanExecute(null))
            vm.SendCommand.Execute(null);

        e.Handled = true;
    }
}
