using PotatoMaker.GUI.ViewModels;
using PotatoMaker.Core;
using Xunit;

namespace PotatoMaker.Tests.ViewModels;

public sealed class OutputSettingsViewModelTests
{
    [Fact]
    public void CpuPresetOptions_AreLimitedToSimplifiedChoices()
    {
        var viewModel = new OutputSettingsViewModel();

        int[] presets = viewModel.CpuEncodePresetOptions.Select(option => option.Value).ToArray();

        Assert.Equal([6, 8, 10, EncodeSettings.MaxSvtAv1Preset], presets);
    }

    [Fact]
    public void SetCpuEncodePreset_MapsLegacyValueToNearestVisibleOption()
    {
        var viewModel = new OutputSettingsViewModel();

        viewModel.SetCpuEncodePreset(9);

        Assert.Equal(8, viewModel.CpuEncodePreset);
    }

    [Fact]
    public void NvencUnavailableToolTip_IsOnlyShownWhenNvencIsUnavailable()
    {
        var viewModel = new OutputSettingsViewModel();

        Assert.Null(viewModel.NvencUnavailableToolTip);

        viewModel.SetNvencSupport(false);

        Assert.Equal("NVENC AV1 is not available on this system.", viewModel.NvencUnavailableToolTip);

        viewModel.SetNvencSupport(true);

        Assert.Null(viewModel.NvencUnavailableToolTip);
    }

    [Fact]
    public void UseNvencEncoder_CannotBeEnabledWhenNvencIsKnownUnavailable()
    {
        var viewModel = new OutputSettingsViewModel();

        viewModel.SetNvencSupport(false);
        viewModel.UseNvencEncoder = true;

        Assert.False(viewModel.UseNvencEncoder);
    }

    [Fact]
    public void SetNvencSupport_False_DisablesCurrentNvencSelection()
    {
        var viewModel = new OutputSettingsViewModel
        {
            UseNvencEncoder = true
        };

        viewModel.SetNvencSupport(false);

        Assert.False(viewModel.UseNvencEncoder);
    }

    [Fact]
    public void FrameRateOptions_ExposeExpectedChoicesInRequestedOrder()
    {
        var viewModel = new OutputSettingsViewModel();

        EncodeFrameRateMode[] modes = viewModel.FrameRateOptions.Select(option => option.Value).ToArray();

        Assert.Equal(
        [
            EncodeFrameRateMode.Fps30,
            EncodeFrameRateMode.Fps60,
            EncodeFrameRateMode.Original
        ], modes);
    }

    [Fact]
    public void SetFrameRateMode_SelectsConfiguredMode()
    {
        var viewModel = new OutputSettingsViewModel();

        viewModel.SetFrameRateMode(EncodeFrameRateMode.Fps60);

        Assert.Equal(EncodeFrameRateMode.Fps60, viewModel.FrameRateMode);
    }

    [Fact]
    public void OutputNamePrefix_IsClampedToMaxLength()
    {
        var viewModel = new OutputSettingsViewModel();
        string value = new('p', EncodeSettings.MaxOutputNameAffixLength + 10);

        viewModel.OutputNamePrefix = value;

        Assert.Equal(EncodeSettings.MaxOutputNameAffixLength, viewModel.OutputNamePrefix.Length);
    }

    [Fact]
    public void OutputNameSuffix_TrimmedValue_IsClampedToMaxLength()
    {
        var viewModel = new OutputSettingsViewModel();
        string value = $"  {new string('s', EncodeSettings.MaxOutputNameAffixLength + 5)}  ";

        viewModel.OutputNameSuffix = value;

        Assert.Equal(new string('s', EncodeSettings.MaxOutputNameAffixLength), viewModel.OutputNameSuffix);
    }

    [Fact]
    public void SetCustomOutputFolder_PreservesExplicitFolderWhenItMatchesSourceFolder()
    {
        const string folder = "C:\\videos";
        var viewModel = new OutputSettingsViewModel();

        viewModel.SetSourceFolder(folder);
        viewModel.SetCustomOutputFolder(folder);

        Assert.Equal(folder, viewModel.CustomOutputFolder);
        Assert.Equal(folder, viewModel.OutputFolderPath);
        Assert.True(viewModel.CanResetOutputFolder);
    }

    [Fact]
    public void SetSourceFolder_DoesNotDiscardExistingExplicitCustomFolderWhenPathsMatch()
    {
        const string folder = "C:\\videos";
        var viewModel = new OutputSettingsViewModel
        {
            CustomOutputFolder = folder
        };

        viewModel.SetSourceFolder(folder);

        Assert.Equal(folder, viewModel.CustomOutputFolder);
        Assert.Equal(folder, viewModel.OutputFolderPath);
        Assert.True(viewModel.CanResetOutputFolder);
    }
}
