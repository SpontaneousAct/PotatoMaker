using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PotatoMaker.GUI.Views;

namespace PotatoMaker.GUI.Services;

public interface IMediaToolsRuntimePromptService
{
    Task<bool> EnsureAvailableAsync(CancellationToken ct = default);
}

/// <summary>
/// Shows one required first-run setup dialog when either native media runtime is missing or invalid.
/// </summary>
public sealed class MediaToolsRuntimePromptService : IMediaToolsRuntimePromptService
{
    private readonly IMediaToolsRuntimeService _runtimeService;
    private readonly SemaphoreSlim _promptSync = new(1, 1);

    public MediaToolsRuntimePromptService(IMediaToolsRuntimeService runtimeService)
    {
        _runtimeService = runtimeService;
    }

    public async Task<bool> EnsureAvailableAsync(CancellationToken ct = default)
    {
        await _promptSync.WaitAsync(ct);
        try
        {
            MediaToolsRuntimeStatus status = await _runtimeService.DetectAsync(ct);
            if (status.IsReady)
                return true;

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is null)
            {
                return false;
            }

            var dialog = new MediaToolsSetupWindow(_runtimeService, status);
            using CancellationTokenRegistration registration = ct.Register(
                () => Dispatcher.UIThread.Post(() => dialog.Close(false)));
            return await dialog.ShowDialog<bool>(desktop.MainWindow);
        }
        finally
        {
            _promptSync.Release();
        }
    }
}
