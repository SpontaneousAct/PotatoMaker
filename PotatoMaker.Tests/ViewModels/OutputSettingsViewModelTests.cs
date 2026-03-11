using PotatoMaker.GUI.ViewModels;
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
}
