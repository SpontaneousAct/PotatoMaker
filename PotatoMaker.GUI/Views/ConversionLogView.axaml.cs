using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

public partial class ConversionLogView : UserControl
{
    private const double ScrollTolerance = 10;
    private bool _logAutoScroll = true;
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
        _logScroller?.ScrollChanged += OnLogScrollChanged;

        _subscribedVm = Vm;
        _subscribedVm.LogLines.CollectionChanged += OnLogLinesChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_logScroller is not null)
            _logScroller.ScrollChanged -= OnLogScrollChanged;
        _logScroller = null;

        if (_subscribedVm is not null)
            _subscribedVm.LogLines.CollectionChanged -= OnLogLinesChanged;
        _subscribedVm = null;

        base.OnUnloaded(e);
    }

    private void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        _logAutoScroll = sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - ScrollTolerance;
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_logAutoScroll) return;
        _logScroller?.ScrollToEnd();
    }
}
