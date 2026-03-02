using System;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PotatoMaker.Core;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Routes <see cref="ILogger"/> calls from <see cref="ProcessingPipeline"/> into the
/// <see cref="ConversionLogViewModel"/> so every pipeline message appears in the UI log.
/// </summary>
sealed class ViewModelLogger : ILogger<ProcessingPipeline>
{
    private readonly ConversionLogViewModel _log;

    public ViewModelLogger(ConversionLogViewModel log) => _log = log;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        string message = formatter(state, exception);

        // Ensure collection mutation happens on the UI thread
        Dispatcher.UIThread.Post(() => _log.AddLog(message));
    }
}
