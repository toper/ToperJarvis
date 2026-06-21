using System;
using System.Collections.Generic;
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
            BuildAudioMenus();

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

    /// <summary>Buduje podmenu wyboru mikrofonu i wyjścia audio w trayu.</summary>
    private void BuildAudioMenus()
    {
        var tray = TrayIcon.GetIcons(this)?.FirstOrDefault();
        if (tray?.Menu is not { } menu)
            return;

        _microphoneMenu = InsertSubmenu(menu, "Mikrofon");
        _outputMenu = InsertSubmenu(menu, "Głośnik (wyjście)");
        RefreshMicrophoneMenu();
        RefreshOutputMenu();
    }

    // Wstawia puste podmenu nad separatorem przed „Wyjście" (lub na koniec).
    private static NativeMenu InsertSubmenu(NativeMenu menu, string header)
    {
        var sub = new NativeMenu();
        var separatorIndex = menu.Items.ToList().FindIndex(i => i is NativeMenuItemSeparator);
        if (separatorIndex < 0)
            separatorIndex = menu.Items.Count;
        menu.Items.Insert(separatorIndex, new NativeMenuItem { Header = header, Menu = sub });
        return sub;
    }

    private void RefreshMicrophoneMenu()
    {
        var capture = Program.Services.GetRequiredService<IAudioCapture>();
        PopulateDeviceMenu(_microphoneMenu, "Domyślny (systemowy)", capture.SelectedDeviceName,
            capture.GetInputDevices().Select(d => d.Name), OnMicrophoneSelected);
    }

    private void RefreshOutputMenu()
    {
        var output = Program.Services.GetRequiredService<IAudioOutput>();
        PopulateDeviceMenu(_outputMenu, "Domyślne (systemowe)", output.SelectedDeviceName,
            output.GetOutputDevices().Select(d => d.Name), OnOutputSelected);
    }

    // Wypełnia podmenu pozycją „domyślny" + listą urządzeń (radio), z zaznaczeniem aktualnego wyboru.
    private static void PopulateDeviceMenu(
        NativeMenu? sub, string defaultHeader, string? selected, IEnumerable<string> devices, Action<string> onSelect)
    {
        if (sub is null)
            return;

        sub.Items.Clear();
        var defaultItem = new NativeMenuItem
        {
            Header = defaultHeader,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = selected is null,
        };
        defaultItem.Click += (_, _) => onSelect("");
        sub.Items.Add(defaultItem);

        var names = devices.ToList();
        if (names.Count == 0)
        {
            sub.Items.Add(new NativeMenuItem { Header = "(brak urządzeń)", IsEnabled = false });
            return;
        }

        sub.Items.Add(new NativeMenuItemSeparator());
        foreach (var name in names)
        {
            var item = new NativeMenuItem
            {
                Header = name,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = string.Equals(name, selected, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => onSelect(name);
            sub.Items.Add(item);
        }
    }

    private void OnMicrophoneSelected(string deviceName)
    {
        Program.Services.GetRequiredService<IAudioCapture>().SelectDevice(deviceName);
        Program.Services.GetRequiredService<LocalSettingsWriter>()
            .Set(deviceName, "Jarvis", "Audio", "InputDeviceName");
        RefreshMicrophoneMenu();
    }

    private void OnOutputSelected(string deviceName)
    {
        Program.Services.GetRequiredService<IAudioOutput>().SelectDevice(deviceName);
        Program.Services.GetRequiredService<LocalSettingsWriter>()
            .Set(deviceName, "Jarvis", "Audio", "OutputDeviceName");
        RefreshOutputMenu();
    }
}
