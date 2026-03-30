using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Hosts the main desktop window.
/// </summary>
public partial class MainWindow : Window
{
    private const uint WmParentNotify = 0x0210;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmXButtonDown = 0x020B;

    private static readonly SubclassProc WindowSubclassProc = OnWindowSubclassMessage;
    private readonly HashSet<Key> _pressedShortcutKeys = [];
    private readonly Button _recentVideosButton;
    private readonly Popup _recentVideosPopup;
    private GCHandle? _windowSubclassHandle;
    private nint _windowHandle;

    public MainWindow()
    {
        InitializeComponent();
        _recentVideosButton = this.FindControl<Button>("RecentVideosButton")!;
        _recentVideosPopup = this.FindControl<Popup>("RecentVideosPopup")!;
        Opened += OnWindowOpened;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachNativeChildClickMonitor();
        IDisposable? disposable = DataContext as IDisposable;
        DataContext = null;
        disposable?.Dispose();
        base.OnClosed(e);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not MainWindowViewModel viewModel)
            return;

        if (MainWindowViewModel.IsGlobalShortcut(e.Key, e.KeyModifiers) &&
            !_pressedShortcutKeys.Add(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (viewModel.TryHandleGlobalShortcut(e.Key, e.KeyModifiers))
            e.Handled = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        _pressedShortcutKeys.Remove(e.Key);

        if (MainWindowViewModel.IsGlobalShortcut(e.Key, e.KeyModifiers))
            e.Handled = true;
    }

    private void OnRecentVideosPopupClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsRecentVideosPanelOpen)
            viewModel.IsRecentVideosPanelOpen = false;
    }

    private void OnWindowOpened(object? sender, EventArgs e) => AttachNativeChildClickMonitor();

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsRecentVideosPanelOpen)
            return;

        if (e.Source is StyledElement source &&
            (IsVisualOrDescendant(source, _recentVideosButton) ||
             (_recentVideosPopup.Child is StyledElement popupContent && IsVisualOrDescendant(source, popupContent))))
        {
            return;
        }

        viewModel.IsRecentVideosPanelOpen = false;
    }

    private void AttachNativeChildClickMonitor()
    {
        if (!OperatingSystem.IsWindows() || _windowSubclassHandle.HasValue)
            return;

        _windowHandle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_windowHandle == IntPtr.Zero)
            return;

        GCHandle handle = GCHandle.Alloc(this);
        if (!SetWindowSubclass(_windowHandle, WindowSubclassProc, 1, (nuint)GCHandle.ToIntPtr(handle)))
        {
            handle.Free();
            return;
        }

        _windowSubclassHandle = handle;
    }

    private void DetachNativeChildClickMonitor()
    {
        if (!_windowSubclassHandle.HasValue)
            return;

        if (_windowHandle != IntPtr.Zero)
            RemoveWindowSubclass(_windowHandle, WindowSubclassProc, 1);

        _windowSubclassHandle.Value.Free();
        _windowSubclassHandle = null;
        _windowHandle = IntPtr.Zero;
    }

    private static nint OnWindowSubclassMessage(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint idSubclass,
        nuint refData)
    {
        // Native child windows (like the LibVLC preview surface) bypass Avalonia pointer routing,
        // but Windows still notifies the parent HWND when they receive mouse clicks.
        if (message == WmParentNotify && IsMouseButtonDownMessage(wParam))
        {
            GCHandle handle = GCHandle.FromIntPtr((nint)refData);
            if (handle.Target is MainWindow window)
                Dispatcher.UIThread.Post(window.CloseRecentVideosPopupIfOpen, DispatcherPriority.Input);
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void CloseRecentVideosPopupIfOpen()
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsRecentVideosPanelOpen)
            viewModel.IsRecentVideosPanelOpen = false;
    }

    private static bool IsVisualOrDescendant(StyledElement element, StyledElement ancestor)
    {
        for (StyledElement? current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static bool IsMouseButtonDownMessage(nint wParam)
    {
        uint message = (uint)((ulong)wParam & 0xFFFF);
        return message is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        nuint uIdSubclass,
        nuint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        nuint uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = false)]
    private static extern nint DefSubclassProc(
        nint hWnd,
        uint uMsg,
        nint wParam,
        nint lParam);

    private delegate nint SubclassProc(
        nint hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nuint dwRefData);
}
