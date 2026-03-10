using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PotatoMaker.GUI.DependencyInjection;
using PotatoMaker.GUI.Services;
using PotatoMaker.GUI.Views;

namespace PotatoMaker.GUI;

/// <summary>
/// Configures the Avalonia desktop application.
/// </summary>
public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            Services = new ServiceCollection()
                .AddPotatoMakerGui()
                .BuildServiceProvider();

            var settingsCoordinator = Services.GetRequiredService<IAppSettingsCoordinator>();
            var themeService = Services.GetRequiredService<IThemeService>();
            themeService.ApplyTheme(settingsCoordinator.Current.IsDarkMode);

            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var plugins = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in plugins)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
