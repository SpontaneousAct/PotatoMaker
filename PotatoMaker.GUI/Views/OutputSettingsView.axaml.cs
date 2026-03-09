using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PotatoMaker.GUI.ViewModels;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PotatoMaker.GUI.Views;

public partial class OutputSettingsView : UserControl
{
    private static readonly char[] PathSeparators = ['\\', '/'];
    private OutputSettingsViewModel? _subscribedViewModel;

    public OutputSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        OutputFolderHost.SizeChanged += (_, _) => UpdateOutputFolderDisplay();
    }

    private OutputSettingsViewModel Vm => (OutputSettingsViewModel)DataContext!;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Vm.OutputFolderPickerRequested = OpenFolderPickerAsync;
        SubscribeToViewModel(Vm);
        UpdateOutputFolderDisplay();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        UnsubscribeFromViewModel();
    }

    private async void OpenFolderPickerAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        IStorageFolder? startLocation = null;
        var currentFolder = Vm.OutputFolderPath;
        if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
            startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(currentFolder);

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select output folder",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            Vm.SetCustomOutputFolder(path);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is OutputSettingsViewModel viewModel)
        {
            SubscribeToViewModel(viewModel);
            viewModel.OutputFolderPickerRequested = OpenFolderPickerAsync;
        }
        else
        {
            UnsubscribeFromViewModel();
        }

        UpdateOutputFolderDisplay();
    }

    private void SubscribeToViewModel(OutputSettingsViewModel viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
            return;

        UnsubscribeFromViewModel();
        _subscribedViewModel = viewModel;
        _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void UnsubscribeFromViewModel()
    {
        if (_subscribedViewModel is null)
            return;

        _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OutputSettingsViewModel.OutputFolderPath) or nameof(OutputSettingsViewModel.CustomOutputFolder) or nameof(OutputSettingsViewModel.SourceFolder))
            UpdateOutputFolderDisplay();
    }

    private void UpdateOutputFolderDisplay()
    {
        if (OutputFolderLine1 is null || OutputFolderLine2 is null || OutputFolderHost is null)
            return;

        var fullPath = _subscribedViewModel?.OutputFolderPath;
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            OutputFolderLine1.Text = "Source file folder";
            OutputFolderLine2.Text = string.Empty;
            OutputFolderLine2.IsVisible = false;
            return;
        }

        var horizontalPadding = OutputFolderHost.Padding.Left + OutputFolderHost.Padding.Right;
        var availableWidth = Math.Max(0, OutputFolderHost.Bounds.Width - horizontalPadding);
        if (availableWidth <= 0)
        {
            OutputFolderLine1.Text = fullPath;
            OutputFolderLine2.Text = string.Empty;
            OutputFolderLine2.IsVisible = false;
            return;
        }

        var (line1, line2) = FormatPathForTwoLines(fullPath, availableWidth);
        OutputFolderLine1.Text = line1;
        OutputFolderLine2.Text = line2 ?? string.Empty;
        OutputFolderLine2.IsVisible = !string.IsNullOrEmpty(line2);
    }

    private (string line1, string? line2) FormatPathForTwoLines(string fullPath, double availableWidth)
    {
        if (MeasureTextWidth(fullPath) <= availableWidth)
            return (fullPath, null);

        if (TryFormatPath(fullPath, availableWidth, out var line1, out var line2))
            return (line1, line2);

        return (TrimWithEllipsis(fullPath, availableWidth), null);
    }

    private bool TryFormatPath(string path, double availableWidth, out string line1, out string? line2)
    {
        if (TryFindSplit(path, availableWidth, out var splitIndex))
        {
            line1 = path[..splitIndex];
            var secondLine = path[splitIndex..];

            if (MeasureTextWidth(secondLine) <= availableWidth)
            {
                line2 = secondLine;
                return true;
            }

            line2 = TrimWithEllipsis(secondLine, availableWidth);
            return true;
        }

        line1 = string.Empty;
        line2 = null;
        return false;
    }

    private bool TryFindSplit(string path, double availableWidth, out int splitIndex)
    {
        splitIndex = -1;
        var bestTotalVisibleCharacters = -1;
        var bestSecondLineWidth = double.PositiveInfinity;

        for (var i = 1; i < path.Length; i++)
        {
            if (!IsPreferredBreak(path, i))
                continue;

            var firstLine = path[..i];
            var secondLine = path[i..];
            var firstWidth = MeasureTextWidth(firstLine);
            if (firstWidth > availableWidth)
                continue;

            var visibleCharacters = secondLine.Length;
            if (MeasureTextWidth(secondLine) > availableWidth)
                visibleCharacters = FindVisibleCharacterCount(secondLine, availableWidth);

            if (visibleCharacters < 0)
                continue;

            var secondPreview = secondLine[..Math.Min(visibleCharacters, secondLine.Length)];
            var secondWidth = MeasureTextWidth(secondPreview);
            var totalVisibleCharacters = i + visibleCharacters;

            if (totalVisibleCharacters < bestTotalVisibleCharacters)
                continue;

            if (totalVisibleCharacters == bestTotalVisibleCharacters && secondWidth >= bestSecondLineWidth)
                continue;

            bestTotalVisibleCharacters = totalVisibleCharacters;
            bestSecondLineWidth = secondWidth;
            splitIndex = i;
        }

        return splitIndex >= 0;
    }

    private static bool IsPreferredBreak(string path, int splitIndex)
    {
        var previousChar = path[splitIndex - 1];
        return Array.IndexOf(PathSeparators, previousChar) >= 0;
    }

    private double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            OutputFolderLine1.FlowDirection,
            new Typeface(OutputFolderLine1.FontFamily, OutputFolderLine1.FontStyle, OutputFolderLine1.FontWeight, OutputFolderLine1.FontStretch),
            OutputFolderLine1.FontSize,
            Brushes.Transparent);

        return formattedText.WidthIncludingTrailingWhitespace;
    }

    private string TrimWithEllipsis(string text, double availableWidth)
    {
        const string ellipsis = "...";
        if (string.IsNullOrEmpty(text))
            return text;

        if (MeasureTextWidth(text) <= availableWidth)
            return text;

        var visibleCount = FindVisibleCharacterCount(text, availableWidth);
        if (visibleCount <= 0)
            return ellipsis;

        return text[..visibleCount] + ellipsis;
    }

    private int FindVisibleCharacterCount(string text, double availableWidth)
    {
        const string ellipsis = "...";
        var ellipsisWidth = MeasureTextWidth(ellipsis);
        if (ellipsisWidth > availableWidth)
            return -1;

        var low = 0;
        var high = text.Length;
        var best = 0;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = text[..mid] + ellipsis;

            if (MeasureTextWidth(candidate) <= availableWidth)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }
}
