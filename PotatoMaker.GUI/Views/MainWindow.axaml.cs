using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Hosts the main desktop window.
/// </summary>
public partial class MainWindow : Window
{
    private readonly HashSet<Key> _pressedShortcutKeys = [];

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
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
            !_pressedShortcutKeys.Add(e.Key) &&
            !MainWindowViewModel.IsRepeatableGlobalShortcut(e.Key))
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
}
