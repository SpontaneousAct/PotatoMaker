using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Keeps the log view pinned to the latest output.
/// </summary>
public partial class ConversionLogView : UserControl
{
    private bool _scrollPending;
    private ScrollViewer? _logScroller;
    private ConversionLogViewModel? _subscribedVm;

    public ConversionLogView()
    {
        InitializeComponent();
    }

    private ConversionLogViewModel Vm => (ConversionLogViewModel)DataContext!;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _logScroller = this.FindControl<ScrollViewer>("LogScroller");

        _subscribedVm = Vm;
        _subscribedVm.LogLines.CollectionChanged += OnLogLinesChanged;
        RequestScrollToBottom();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _logScroller = null;

        if (_subscribedVm is not null)
            _subscribedVm.LogLines.CollectionChanged -= OnLogLinesChanged;
        _subscribedVm = null;

        base.OnUnloaded(e);
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestScrollToBottom();

    private void RequestScrollToBottom()
    {
        if (_scrollPending)
            return;

        _scrollPending = true;

        Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            _logScroller?.ScrollToEnd();

            Dispatcher.UIThread.Post(
                () => _logScroller?.ScrollToEnd(),
                DispatcherPriority.Render);
        }, DispatcherPriority.Background);
    }
}
