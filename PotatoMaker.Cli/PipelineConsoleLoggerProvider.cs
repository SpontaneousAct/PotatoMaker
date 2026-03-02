using Microsoft.Extensions.Logging;
using PotatoMaker.Core;

namespace PotatoMaker.Cli;

sealed class PipelineConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new PipelineConsoleLogger();
    public void Dispose() { }
}

sealed class PipelineConsoleLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);

        ConsoleColor? color = logLevel switch
        {
            LogLevel.Warning                                    => ConsoleColor.Yellow,
            LogLevel.Error or LogLevel.Critical                 => ConsoleColor.Red,
            _ when eventId == PipelineEvents.Success            => ConsoleColor.Green,
            _ when eventId == PipelineEvents.Emphasis           => ConsoleColor.Cyan,
            _                                                   => null
        };

        if (color is not null)
        {
            Console.ForegroundColor = color.Value;
            Console.WriteLine(message);
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine(message);
        }
    }
}
