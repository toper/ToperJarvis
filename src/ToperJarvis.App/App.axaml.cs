using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ToperJarvis.Abstractions;
using ToperJarvis.App.ViewModels;
using ToperJarvis.App.Views;

namespace ToperJarvis.App;

public partial class App : Application
{
    private Window? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow
            {
                DataContext = Program.Services.GetRequiredService<MainWindowViewModel>(),
            };
            desktop.MainWindow = _mainWindow;

            // Start pętli głosowej (przechwytywanie audio + nasłuch słowa-klucza).
            Program.Services.GetRequiredService<IAssistantOrchestrator>().Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayShow(object? sender, System.EventArgs e)
    {
        if (_mainWindow is null)
            return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void OnTrayFullscreen(object? sender, System.EventArgs e)
    {
        if (_mainWindow is null)
            return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.FullScreen;
    }

    private void OnTrayExit(object? sender, System.EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
