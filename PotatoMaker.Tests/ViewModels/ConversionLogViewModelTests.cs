using PotatoMaker.Core;
using PotatoMaker.GUI.ViewModels;
using Xunit;

namespace PotatoMaker.Tests.ViewModels;

public sealed class ConversionLogViewModelTests
{
    [Fact]
    public void NewViewModel_UsesChooseAVideoText()
    {
        var viewModel = new ConversionLogViewModel();

        Assert.Equal(ConversionStatus.Idle, viewModel.Status);
        Assert.Equal("Choose a video", viewModel.StatusText);
    }

    [Fact]
    public void BeginAnalysis_SetsAnalysingStatusWithoutProgress()
    {
        var viewModel = new ConversionLogViewModel();

        viewModel.BeginAnalysis();

        Assert.Equal(ConversionStatus.Analysing, viewModel.Status);
        Assert.Equal("Analysing", viewModel.StatusText);
        Assert.False(viewModel.IsProcessing);
        Assert.False(viewModel.ShowProgress);
        Assert.Equal(0, viewModel.ProgressPercent);
    }

    [Fact]
    public void UpdateProgress_MapsAnalyzingLabelsToAnalysing()
    {
        var viewModel = new ConversionLogViewModel();

        viewModel.BeginEncoding();
        viewModel.UpdateProgress(new EncodeProgress("  [Pass 1/2] Analyzing", 42));

        Assert.Equal(ConversionStatus.Analysing, viewModel.Status);
        Assert.Equal("Analysing 1/2", viewModel.StatusText);
        Assert.True(viewModel.IsProcessing);
        Assert.True(viewModel.ShowProgress);
        Assert.Equal(42, viewModel.ProgressPercent);
        Assert.Equal("42%", viewModel.ProgressText);
    }

    [Fact]
    public void UpdateProgress_DefaultsToEncodingForEncodeLabels()
    {
        var viewModel = new ConversionLogViewModel();

        viewModel.BeginEncoding();
        viewModel.UpdateProgress(new EncodeProgress("  [Pass 2/2] Encoding", 67));

        Assert.Equal(ConversionStatus.Encoding, viewModel.Status);
        Assert.Equal("Compressing 2/2", viewModel.StatusText);
        Assert.True(viewModel.IsProcessing);
        Assert.Equal(67, viewModel.ProgressPercent);
    }

    [Fact]
    public void MarkDone_PreservesCompletedProgress()
    {
        var viewModel = new ConversionLogViewModel();

        viewModel.BeginEncoding();
        viewModel.MarkDone(TimeSpan.FromSeconds(83));

        Assert.Equal(ConversionStatus.Done, viewModel.Status);
        Assert.False(viewModel.IsProcessing);
        Assert.True(viewModel.ShowProgress);
        Assert.Equal(100, viewModel.ProgressPercent);
        Assert.Equal("Done in 1:23", viewModel.StatusText);
    }

    [Fact]
    public void UpdateProgress_DoesNotOverrideDoneState()
    {
        var viewModel = new ConversionLogViewModel();

        viewModel.BeginEncoding();
        viewModel.MarkDone(TimeSpan.FromSeconds(12));
        viewModel.UpdateProgress(new EncodeProgress("[NVENC]", 100));

        Assert.Equal(ConversionStatus.Done, viewModel.Status);
        Assert.False(viewModel.IsProcessing);
        Assert.Equal(100, viewModel.ProgressPercent);
        Assert.Equal("Done in 0:12", viewModel.StatusText);
    }

    [Fact]
    public void MarkCancelled_ClearsProgressAndStopsProcessing()
    {
        var viewModel = new ConversionLogViewModel();

        viewModel.BeginEncoding();
        viewModel.UpdateProgress(new EncodeProgress("[NVENC]", 19));
        viewModel.MarkCancelled();

        Assert.Equal(ConversionStatus.Cancelled, viewModel.Status);
        Assert.False(viewModel.IsProcessing);
        Assert.False(viewModel.ShowProgress);
        Assert.Equal(0, viewModel.ProgressPercent);
    }

    [Fact]
    public void MarkAnalysisError_DoesNotOverrideActiveEncodeState()
    {
        var viewModel = new ConversionLogViewModel();

        viewModel.BeginEncoding();
        viewModel.MarkAnalysisError();

        Assert.Equal(ConversionStatus.Analysing, viewModel.Status);
        Assert.True(viewModel.IsProcessing);
    }
}
