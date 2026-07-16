using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PotatoMaker.GUI.DependencyInjection;
using PotatoMaker.GUI.Services;
using PotatoMaker.GUI.ViewModels;
using PotatoMaker.GUI.Views;

namespace PotatoMaker.GUI;

/// <summary>
/// Configures the Avalonia desktop application.
/// </summary>
public partial class App : Application
{
    private string[] _startupArgs = [];

    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            using IDisposable initializationOperation = CrashReportService.Shared.BeginOperation("Initializing application");
            DisableAvaloniaDataAnnotationValidation();
            ServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddPotatoMakerGui();
            Services = serviceCollection.BuildServiceProvider();

            var settingsCoordinator = Services.GetRequiredService<IAppSettingsCoordinator>();
            var themeService = Services.GetRequiredService<IThemeService>();
            themeService.ApplyTheme(settingsCoordinator.Current.Theme);

            var mainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            _startupArgs = desktop.Args ?? [];
            mainWindow.Opened += OnMainWindowOpened;

            if (mainWindow.DataContext is MainWindowViewModel viewModel)
            {
                if (Program.SingleInstanceManager is { IsPrimaryInstance: true } singleInstanceManager)
                {
                    singleInstanceManager.RegisterActivationHandler(args =>
                    {
                        Dispatcher.UIThread.Post(async () =>
                        {
                            if (mainWindow.DataContext is not MainWindowViewModel currentViewModel)
                                return;

                            WindowsWindowActivation.Activate(mainWindow);
                            try
                            {
                                var runtimePrompt = Services.GetRequiredService<IMediaToolsRuntimePromptService>();
                                if (await runtimePrompt.EnsureAvailableAsync())
                                {
                                    await currentViewModel.Workspace.InitializeRuntimeDependentStateAsync();
                                    currentViewModel.Workspace.VideoPlayer.EnsureInitializedAfterRuntimeSetup();
                                    currentViewModel.OpenExternalFiles(args);
                                }
                            }
                            catch
                            {
                                // The setup window contains the actionable error state.
                            }
                        });
                    });
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (sender is not MainWindow mainWindow)
            return;

        mainWindow.Opened -= OnMainWindowOpened;

        CrashReport? pendingReport = CrashReportService.Shared.TryGetLatestPendingReport();
        if (pendingReport is not null)
        {
            try
            {
                var dialog = new CrashReportWindow(pendingReport, CrashReportService.Shared);
                await dialog.ShowDialog(mainWindow);
                CrashReportService.Shared.MarkReportAsReviewed(pendingReport);
            }
            catch
            {
                // Avoid a follow-up startup crash if the crash prompt itself cannot be shown.
            }
        }

        if (mainWindow.DataContext is not MainWindowViewModel viewModel)
            return;

        _ = viewModel.InitializeAsync();

        try
        {
            var runtimePrompt = Services.GetRequiredService<IMediaToolsRuntimePromptService>();
            if (await runtimePrompt.EnsureAvailableAsync())
            {
                await viewModel.Workspace.InitializeRuntimeDependentStateAsync();
                viewModel.Workspace.VideoPlayer.EnsureInitializedAfterRuntimeSetup();
                viewModel.OpenExternalFiles(_startupArgs);
                return;
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch
        {
            // The media tools are required for the application to function correctly.
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
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
