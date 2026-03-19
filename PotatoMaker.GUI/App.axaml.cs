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
            themeService.ApplyTheme(settingsCoordinator.Current.IsDarkMode);

            var mainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            mainWindow.Opened += OnMainWindowOpened;

            if (mainWindow.DataContext is MainWindowViewModel viewModel)
            {
                viewModel.OpenExternalFiles(desktop.Args ?? []);

                if (Program.SingleInstanceManager is { IsPrimaryInstance: true } singleInstanceManager)
                {
                    singleInstanceManager.RegisterActivationHandler(args =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (mainWindow.DataContext is not MainWindowViewModel currentViewModel)
                                return;

                            currentViewModel.OpenExternalFiles(args);
                            WindowsWindowActivation.Activate(mainWindow);
                        });
                    });
                }

                _ = viewModel.InitializeAsync();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (sender is not MainWindow mainWindow)
            return;

        mainWindow.Opened -= OnMainWindowOpened;

        CrashReport? pendingReport = CrashReportService.Shared.TryGetLatestPendingReport();
        if (pendingReport is null)
            return;

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

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var plugins = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in plugins)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
