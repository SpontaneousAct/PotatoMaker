using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Controls the help overlay state.
/// </summary>
public partial class HelpModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [RelayCommand]
    private void Open() => IsOpen = true;

    [RelayCommand]
    private void Close() => IsOpen = false;
}
