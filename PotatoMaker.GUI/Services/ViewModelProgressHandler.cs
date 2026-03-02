using System;
using Avalonia.Threading;
using PotatoMaker.Core;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Bridges <see cref="IProgress{EncodeProgress}"/> from the pipeline to the
/// <see cref="ConversionLogViewModel"/> progress bar and label.
/// </summary>
sealed class ViewModelProgressHandler : IProgress<EncodeProgress>
{
    private readonly ConversionLogViewModel _log;

    public ViewModelProgressHandler(ConversionLogViewModel log) => _log = log;

    public void Report(EncodeProgress value)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _log.ProgressPercent = Math.Clamp(value.Percent, 0, 100);
            _log.ProgressLabel = $"{value.Label}  {value.Percent}%";
        });
    }
}
