using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Threading;
using System.Runtime.InteropServices;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Notifies the user when an encode completes successfully.
/// </summary>
public interface IEncodeCompletionNotifier
{
    void NotifyEncodeSucceeded();
}

/// <summary>
/// Default no-op notifier used in tests and design-time scenarios.
/// </summary>
public sealed class NoOpEncodeCompletionNotifier : IEncodeCompletionNotifier
{
    public static NoOpEncodeCompletionNotifier Instance { get; } = new();

    private NoOpEncodeCompletionNotifier()
    {
    }

    public void NotifyEncodeSucceeded()
    {
    }
}

/// <summary>
/// Flashes the Windows taskbar button for the main window when an encode completes.
/// </summary>
public sealed class WindowsEncodeCompletionNotifier : IEncodeCompletionNotifier
{
    public void NotifyEncodeSucceeded()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
                return;

            WindowsTaskbarFlash.Flash(window);
        });
    }
}

internal static class WindowsTaskbarFlash
{
    private const uint FlashwTray = 0x00000002;
    private const uint FlashwTimerNoFg = 0x0000000C;

    public static void Flash(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!OperatingSystem.IsWindows() || !window.IsVisible || window.IsActive)
            return;

        nint handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;

        var flashInfo = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Handle = handle,
            Flags = FlashwTray | FlashwTimerNoFg,
            Count = uint.MaxValue,
            Timeout = 0
        };

        FlashWindowEx(ref flashInfo);
    }

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo pwfi);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public nint Handle;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }
}
