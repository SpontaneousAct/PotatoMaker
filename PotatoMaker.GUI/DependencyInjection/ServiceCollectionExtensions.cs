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
        services.AddSingleton<IUpdateSettingsProvider, JsonUpdateSettingsProvider>();
        services.AddSingleton(sp => sp.GetRequiredService<IUpdateSettingsProvider>().Load());
        services.AddSingleton<IVelopackUpdateManagerFactory, VelopackUpdateManagerFactory>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<IAppVersionService, AssemblyAppVersionService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<IRecentVideoDiscoveryService, RecentVideoDiscoveryService>();
        services.AddSingleton<IRecentVideoThumbnailService, RecentVideoThumbnailService>();
        services.AddSingleton<IProcessedVideoTracker, ProcessedVideoTracker>();
        services.AddSingleton<IVideoAnalysisService, VideoAnalysisService>();
        services.AddSingleton<IVideoEncodingService, VideoEncodingService>();
        services.AddSingleton<IEncoderCapabilityService, EncoderCapabilityService>();
        services.AddSingleton<IEncodeCompletionNotifier, WindowsEncodeCompletionNotifier>();
        services.AddSingleton<EncodeExecutionCoordinator>();
        services.AddSingleton<CompressionQueueViewModel>();

        services.AddTransient(_ => new VideoPlayerViewModel(initializePlayer: false));
        services.AddTransient(sp => new EncodeWorkspaceViewModel(
            sp.GetRequiredService<IVideoAnalysisService>(),
            sp.GetRequiredService<IVideoEncodingService>(),
            sp.GetRequiredService<VideoPlayerViewModel>(),
            sp.GetRequiredService<IEncoderCapabilityService>(),
            sp.GetRequiredService<IAppSettingsCoordinator>(),
            sp.GetRequiredService<CompressionQueueViewModel>(),
            sp.GetRequiredService<EncodeExecutionCoordinator>(),
            true,
            sp.GetRequiredService<IEncodeCompletionNotifier>(),
            null,
            sp.GetRequiredService<IProcessedVideoTracker>()));
        services.AddTransient(sp => new MainWindowViewModel(
            sp.GetRequiredService<EncodeWorkspaceViewModel>(),
            sp.GetRequiredService<IThemeService>(),
            sp.GetRequiredService<IAppSettingsCoordinator>(),
            sp.GetRequiredService<IRecentVideoDiscoveryService>(),
            sp.GetRequiredService<IRecentVideoThumbnailService>(),
            sp.GetRequiredService<IProcessedVideoTracker>(),
            sp.GetRequiredService<CompressionQueueViewModel>(),
            sp.GetRequiredService<IAppUpdateService>(),
            sp.GetRequiredService<IAppVersionService>()));
        services.AddTransient<MainWindow>();

        return services;
    }
}
