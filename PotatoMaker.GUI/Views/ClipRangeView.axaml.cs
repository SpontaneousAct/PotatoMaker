using Avalonia.Controls;
using Avalonia.Input;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Displays start and end trim controls for the loaded clip.
/// </summary>
public partial class ClipRangeView : UserControl
{
    public ClipRangeView()
    {
        InitializeComponent();

        this.FindControl<Slider>("StartSlider")!.AddHandler(PointerReleasedEvent, OnStartSliderReleased);
        this.FindControl<Slider>("EndSlider")!.AddHandler(PointerReleasedEvent, OnEndSliderReleased);
    }

    private ClipRangeViewModel Vm => (ClipRangeViewModel)DataContext!;

    private void OnStartSliderReleased(object? sender, PointerReleasedEventArgs e) =>
        Vm.RequestPreviewCommit(ClipPreviewTarget.Start);

    private void OnEndSliderReleased(object? sender, PointerReleasedEventArgs e) =>
        Vm.RequestPreviewCommit(ClipPreviewTarget.End);
}
