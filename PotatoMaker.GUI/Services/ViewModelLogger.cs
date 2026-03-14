using Microsoft.Extensions.Logging;
using PotatoMaker.Core;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Consumes <see cref="ILogger"/> calls from <see cref="ProcessingPipeline"/> without
/// surfacing the raw console stream in the main UI.
/// </summary>
sealed class ViewModelLogger : ILogger<ProcessingPipeline>
{
    public ViewModelLogger(ConversionLogViewModel log)
    {
        _ = log;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // The status card intentionally avoids exposing raw pipeline output.
    }
}
