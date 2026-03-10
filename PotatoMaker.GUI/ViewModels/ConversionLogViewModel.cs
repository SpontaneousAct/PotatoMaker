using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Stores encode logs and progress for the UI.
/// </summary>
public partial class ConversionLogViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string? _progressLabel;

    public ObservableCollection<string> LogLines { get; } = [];

    public void AddLog(string message) => LogLines.Add(message);

    public void Clear()
    {
        LogLines.Clear();
        ProgressPercent = 0;
        ProgressLabel = null;
        IsProcessing = false;
    }
}
