using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Presents a recent video entry in the shell quick-load panel.
/// </summary>
public sealed class RecentVideoItemViewModel : ViewModelBase, IDisposable
{
    private Bitmap? _thumbnail;

    public RecentVideoItemViewModel(string fullPath, string fileName, DateTimeOffset lastModified, Action<string> onSelected)
        : this(fullPath, fileName, lastModified, isProcessed: false, onSelected)
    {
    }

    public RecentVideoItemViewModel(
        string fullPath,
        string fileName,
        DateTimeOffset lastModified,
        bool isProcessed,
        Action<string> onSelected)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(onSelected);

        FullPath = fullPath;
        FileName = fileName;
        ModifiedText = $"Modified {lastModified.LocalDateTime:g}";
        IsProcessed = isProcessed;
        SelectCommand = new RelayCommand(() => onSelected(FullPath));
    }

    public string FullPath { get; }

    public string FileName { get; }

    public string ModifiedText { get; }

    public bool IsProcessed { get; }

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (SetProperty(ref _thumbnail, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(IsThumbnailMissing));
            }
        }
    }

    public bool HasThumbnail => Thumbnail is not null;

    public bool IsThumbnailMissing => Thumbnail is null;

    public IRelayCommand SelectCommand { get; }

    public void SetThumbnail(Bitmap? thumbnail)
    {
        if (ReferenceEquals(_thumbnail, thumbnail))
            return;

        Bitmap? previous = _thumbnail;
        Thumbnail = thumbnail;
        previous?.Dispose();
    }

    public void Dispose() => SetThumbnail(null);
}
