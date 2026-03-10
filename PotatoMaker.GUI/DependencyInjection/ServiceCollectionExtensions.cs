using Microsoft.Extensions.DependencyInjection;
using PotatoMaker.GUI.Services;
using PotatoMaker.GUI.ViewModels;
using PotatoMaker.GUI.Views;

namespace PotatoMaker.GUI.DependencyInjection;

/// <summary>
/// Registers desktop app services and view models.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPotatoMakerGui(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAppSettingsService, JsonAppSettingsService>();
        services.AddSingleton<IAppSettingsCoordinator>(sp =>
        {
            var settingsService = sp.GetRequiredService<IAppSettingsService>();
            return new AppSettingsCoordinator(settingsService, settingsService.Load());
        });
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<IVideoAnalysisService, VideoAnalysisService>();
        services.AddSingleton<IVideoEncodingService, VideoEncodingService>();
        services.AddSingleton<IVideoFramePreviewService, VideoFramePreviewService>();
        services.AddSingleton<IEncoderCapabilityService, EncoderCapabilityService>();

        services.AddTransient<HelpModalViewModel>();
        services.AddTransient<EncodeWorkspaceViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
