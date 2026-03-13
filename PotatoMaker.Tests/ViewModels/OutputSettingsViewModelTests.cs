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

        Assert.Equal([6, 8, 10], presets);
    }

    [Fact]
    public void SetCpuEncodePreset_MapsLegacyValueToNearestVisibleOption()
    {
        var viewModel = new OutputSettingsViewModel();

        viewModel.SetCpuEncodePreset(9);

        Assert.Equal(8, viewModel.CpuEncodePreset);
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
}
