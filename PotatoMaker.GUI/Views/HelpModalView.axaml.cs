using Avalonia.Controls;
using Avalonia.Input;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

public partial class HelpModalView : UserControl
{
    public HelpModalView()
    {
        InitializeComponent();
    }

    private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not HelpModalViewModel viewModel || !viewModel.IsOpen)
            return;

        var point = e.GetPosition(HelpCard);
        bool clickedInsideCard = point.X >= 0 &&
                                 point.Y >= 0 &&
                                 point.X <= HelpCard.Bounds.Width &&
                                 point.Y <= HelpCard.Bounds.Height;
        if (clickedInsideCard)
            return;

        viewModel.CloseCommand.Execute(null);
        e.Handled = true;
    }
}
