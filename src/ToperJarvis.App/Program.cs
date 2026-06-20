using System;
using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.App;

sealed class Program
{
    /// <summary>Kontener DI aplikacji, budowany przed startem Avalonii.</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    private static IHost? _host;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        _host = BuildHost(args);
        Services = _host.Services;
        _host.Start();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }
    }

    private static IHost BuildHost(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddOptions<JarvisOptions>()
                    .Bind(context.Configuration.GetSection(JarvisOptions.SectionName));

                services.AddJarvisApp();
            })
            .Build();

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
