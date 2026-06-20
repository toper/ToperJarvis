using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ToperJarvis.App.ViewModels;

namespace ToperJarvis.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
    }

    /// <summary>F11 — pełny ekran / okno; F4 — (zarezerwowane) wyciszenie mikrofonu.</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
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
