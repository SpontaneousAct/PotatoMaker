using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Displays the shell help screen.
/// </summary>
public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
    }

    private void OnOpenLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell launch failures so the support page never crashes the app.
        }
    }
}
