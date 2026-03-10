using Avalonia.Controls;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Hosts the main desktop window.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
}
