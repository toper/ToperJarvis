using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ToperJarvis.App.Services;

/// <summary>
/// Minimalny provider logów do pliku (bez zewnętrznych zależności). Aplikacja jest WinExe — logi
/// konsolowe są niewidoczne, więc pełny log trafia do pliku obok exe (<c>logs/jarvis.log</c>).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileLoggerProvider(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Write($"===== Start sesji ToperJarvis ====={Environment.NewLine}");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    internal void Write(string text)
    {
        lock (_gate)
            File.AppendAllText(_path, text);
    }

    public void Dispose() { }

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        // Skraca długą nazwę kategorii do samej klasy dla czytelności.
        private readonly string _shortCategory = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var sb = new StringBuilder();
            sb.Append(DateTimeOffset.Now.ToString("HH:mm:ss.fff"))
              .Append(" [").Append(Level(logLevel)).Append("] ")
              .Append(_shortCategory).Append(": ")
              .Append(formatter(state, exception));
            if (exception is not null)
                sb.Append(Environment.NewLine).Append(exception);
            sb.Append(Environment.NewLine);

            provider.Write(sb.ToString());
        }

        private static string Level(LogLevel l) => l switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
