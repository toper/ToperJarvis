using Microsoft.Extensions.DependencyInjection;
using ToperJarvis.Abstractions.Vision;

namespace ToperJarvis.Platform.Windows;

/// <summary>Rejestracja implementacji zależnych od Windows.</summary>
public static class WindowsServiceCollectionExtensions
{
    public static IServiceCollection AddJarvisWindows(this IServiceCollection services)
    {
        services.AddSingleton<IScreenCapture, WindowsScreenCapture>();
        return services;
    }
}
