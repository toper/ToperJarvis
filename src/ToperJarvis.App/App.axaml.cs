using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ToperJarvis.Abstractions;
using ToperJarvis.Abstractions.Speech;
using ToperJarvis.App.Services;
using ToperJarvis.App.ViewModels;
using ToperJarvis.App.Views;

namespace ToperJarvis.App;

public partial class App : Application
{
    private Window? _mainWindow;
    private NativeMenu? _microphoneMenu;
    private NativeMenu? _outputMenu;

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

            // Podmenu wyboru urządzeń audio w trayu (mikrofon + wyjście).
            BuildMicrophoneMenu();
            BuildOutputMenu();

            // Start pętli głosowej (przechwytywanie audio + nasłuch słowa-klucza).
            var orchestrator = Program.Services.GetRequiredService<IAssistantOrchestrator>();
            orchestrator.Start();

            // Push-to-talk (hold-to-talk) — globalny hotkey jako pewniejsza alternatywa wake-worda.
            var ptt = Program.Services.GetRequiredService<IPushToTalkHotkey>();
            ptt.Pressed += (_, _) => orchestrator.BeginPushToTalk();
            ptt.Released += (_, _) => orchestrator.EndPushToTalk();
            ptt.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayShow(object? sender, EventArgs e)
    {
        if (_mainWindow is null)
            return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void OnTrayFullscreen(object? sender, EventArgs e)
    {
        if (_mainWindow is null)
            return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.FullScreen;
    }

    private void OnTrayExit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>Wstawia do menu traya podmenu „Mikrofon" z listą urządzeń wejściowych.</summary>
    private void BuildMicrophoneMenu()
    {
        var tray = TrayIcon.GetIcons(this)?.FirstOrDefault();
        if (tray?.Menu is not { } menu)
            return;

        _microphoneMenu = new NativeMenu();
        var micItem = new NativeMenuItem { Header = "Mikrofon", Menu = _microphoneMenu };

        // Wstaw nad separatorem przed pozycją „Wyjście"; gdy brak — na koniec.
        var separatorIndex = menu.Items.ToList().FindIndex(i => i is NativeMenuItemSeparator);
        if (separatorIndex < 0)
            separatorIndex = menu.Items.Count;
        menu.Items.Insert(separatorIndex, micItem);

        RefreshMicrophoneMenu();
    }

    /// <summary>Odbudowuje pozycje podmenu z aktualną listą urządzeń i zaznaczeniem wyboru.</summary>
    private void RefreshMicrophoneMenu()
    {
        if (_microphoneMenu is null)
            return;

        var capture = Program.Services.GetRequiredService<IAudioCapture>();
        var selected = capture.SelectedDeviceName;
        _microphoneMenu.Items.Clear();

        var defaultItem = new NativeMenuItem
        {
            Header = "Domyślny (systemowy)",
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = selected is null,
        };
        defaultItem.Click += (_, _) => OnMicrophoneSelected("");
        _microphoneMenu.Items.Add(defaultItem);

        var devices = capture.GetInputDevices();
        if (devices.Count == 0)
        {
            _microphoneMenu.Items.Add(new NativeMenuItem { Header = "(brak urządzeń)", IsEnabled = false });
            return;
        }

        _microphoneMenu.Items.Add(new NativeMenuItemSeparator());
        foreach (var device in devices)
        {
            var item = new NativeMenuItem
            {
                Header = device.Name,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = string.Equals(device.Name, selected, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => OnMicrophoneSelected(device.Name);
            _microphoneMenu.Items.Add(item);
        }
    }

    /// <summary>Przełącza mikrofon w locie i zapisuje wybór do appsettings.Local.json.</summary>
    private void OnMicrophoneSelected(string deviceName)
    {
        Program.Services.GetRequiredService<IAudioCapture>().SelectDevice(deviceName);
        Program.Services.GetRequiredService<LocalSettingsWriter>()
            .Set(deviceName, "Jarvis", "Audio", "InputDeviceName");
        RefreshMicrophoneMenu();
    }

    /// <summary>Wstawia do menu traya podmenu „Głośnik" z listą urządzeń wyjściowych.</summary>
    private void BuildOutputMenu()
    {
        var tray = TrayIcon.GetIcons(this)?.FirstOrDefault();
        if (tray?.Menu is not { } menu)
            return;

        _outputMenu = new NativeMenu();
        var outItem = new NativeMenuItem { Header = "Głośnik (wyjście)", Menu = _outputMenu };

        var separatorIndex = menu.Items.ToList().FindIndex(i => i is NativeMenuItemSeparator);
        if (separatorIndex < 0)
            separatorIndex = menu.Items.Count;
        menu.Items.Insert(separatorIndex, outItem);

        RefreshOutputMenu();
    }

    /// <summary>Odbudowuje pozycje podmenu wyjścia z aktualną listą urządzeń i zaznaczeniem.</summary>
    private void RefreshOutputMenu()
    {
        if (_outputMenu is null)
            return;

        var output = Program.Services.GetRequiredService<IAudioOutput>();
        var selected = output.SelectedDeviceName;
        _outputMenu.Items.Clear();

        var defaultItem = new NativeMenuItem
        {
            Header = "Domyślne (systemowe)",
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = selected is null,
        };
        defaultItem.Click += (_, _) => OnOutputSelected("");
        _outputMenu.Items.Add(defaultItem);

        var devices = output.GetOutputDevices();
        if (devices.Count == 0)
        {
            _outputMenu.Items.Add(new NativeMenuItem { Header = "(brak urządzeń)", IsEnabled = false });
            return;
        }

        _outputMenu.Items.Add(new NativeMenuItemSeparator());
        foreach (var device in devices)
        {
            var item = new NativeMenuItem
            {
                Header = device.Name,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = string.Equals(device.Name, selected, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => OnOutputSelected(device.Name);
            _outputMenu.Items.Add(item);
        }
    }

    /// <summary>Ustawia wyjście audio i zapisuje wybór do appsettings.Local.json.</summary>
    private void OnOutputSelected(string deviceName)
    {
        Program.Services.GetRequiredService<IAudioOutput>().SelectDevice(deviceName);
        Program.Services.GetRequiredService<LocalSettingsWriter>()
            .Set(deviceName, "Jarvis", "Audio", "OutputDeviceName");
        RefreshOutputMenu();
    }
}
