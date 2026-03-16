using CommunityToolkit.Mvvm.Input;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Presents a recent video entry in the shell quick-load panel.
/// </summary>
public sealed class RecentVideoItemViewModel
{
    public RecentVideoItemViewModel(string fullPath, string fileName, DateTimeOffset lastModified, Action<string> onSelected)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(onSelected);

        FullPath = fullPath;
        FileName = fileName;
        ModifiedText = $"Modified {lastModified.LocalDateTime:g}";
        SelectCommand = new RelayCommand(() => onSelected(FullPath));
    }

    public string FullPath { get; }

    public string FileName { get; }

    public string ModifiedText { get; }

    public IRelayCommand SelectCommand { get; }
}
