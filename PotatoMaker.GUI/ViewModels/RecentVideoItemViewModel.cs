using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Presents a recent video entry in the shell quick-load panel.
/// </summary>
public sealed class RecentVideoItemViewModel : ViewModelBase, IDisposable
{
    private Bitmap? _thumbnail;
    private bool _isProcessed;
    private bool _isQueued;

    public RecentVideoItemViewModel(string fullPath, string fileName, DateTimeOffset lastModified, Action<string> onSelected)
        : this(fullPath, fileName, lastModified, isProcessed: false, isQueued: false, onSelected)
    {
    }

    public RecentVideoItemViewModel(
        string fullPath,
        string fileName,
        DateTimeOffset lastModified,
        bool isProcessed,
        Action<string> onSelected)
        : this(fullPath, fileName, lastModified, isProcessed, isQueued: false, onSelected)
    {
    }

    public RecentVideoItemViewModel(
        string fullPath,
        string fileName,
        DateTimeOffset lastModified,
        bool isProcessed,
        bool isQueued,
        Action<string> onSelected)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(onSelected);

        FullPath = fullPath;
        FileName = fileName;
        LastModified = lastModified;
        ModifiedText = $"Modified {lastModified.LocalDateTime:g}";
        _isProcessed = isProcessed;
        _isQueued = isQueued;
        SelectCommand = new RelayCommand(() => onSelected(FullPath));
    }

    public string FullPath { get; }

    public string FileName { get; }

    public DateTimeOffset LastModified { get; }

    public string ModifiedText { get; }

    public bool IsProcessed
    {
        get => _isProcessed;
        private set => SetProperty(ref _isProcessed, value);
    }

    public bool IsQueued
    {
        get => _isQueued;
        private set => SetProperty(ref _isQueued, value);
    }

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

    public void SetProcessed(bool isProcessed) => IsProcessed = isProcessed;

    public void SetQueued(bool isQueued) => IsQueued = isQueued;

    public void Dispose() => SetThumbnail(null);
}
