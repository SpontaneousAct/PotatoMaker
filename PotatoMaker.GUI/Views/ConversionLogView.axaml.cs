using System.Collections.Specialized;
using Avalonia.Controls;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

public partial class ConversionLogView : UserControl
{
    private const double ScrollTolerance = 10;
    private bool _logAutoScroll = true;

    public ConversionLogView()
    {
        InitializeComponent();
    }

    private ConversionLogViewModel Vm => (ConversionLogViewModel)DataContext!;

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var scroller = this.FindControl<ScrollViewer>("LogScroller");
        scroller?.ScrollChanged += OnLogScrollChanged;

        Vm.LogLines.CollectionChanged += OnLogLinesChanged;
    }

    private void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        _logAutoScroll = sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - ScrollTolerance;
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_logAutoScroll) return;
        var scroller = this.FindControl<ScrollViewer>("LogScroller");
        scroller?.ScrollToEnd();
    }
}
