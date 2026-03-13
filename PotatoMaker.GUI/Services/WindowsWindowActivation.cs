using Avalonia.Controls;
using Avalonia.Controls.Platform;
using System.Runtime.InteropServices;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Restores and foregrounds the main window on Windows when a second launch is redirected.
/// </summary>
internal static class WindowsWindowActivation
{
    private const int SwRestore = 9;

    public static void Activate(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        if (!window.IsVisible)
            window.Show();

        window.Activate();

        if (!OperatingSystem.IsWindows())
            return;

        nint handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;

        ShowWindow(handle, SwRestore);
        SetForegroundWindow(handle);
    }

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
