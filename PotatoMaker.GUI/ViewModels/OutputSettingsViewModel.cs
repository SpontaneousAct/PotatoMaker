using CommunityToolkit.Mvvm.ComponentModel;

namespace PotatoMaker.GUI.ViewModels;

public partial class OutputSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _useCpuEncoder;
}
