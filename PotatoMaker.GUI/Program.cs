using Avalonia;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;
using System;
using System.Runtime.Versioning;
using Velopack;

namespace PotatoMaker.GUI
{
    internal sealed class Program
    {
        internal static WindowsSingleInstanceManager? SingleInstanceManager { get; private set; }

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        [SupportedOSPlatform("windows")]
        public static void Main(string[] args)
        {
            CrashReportService.Shared.InstallGlobalHandlers();
            using IDisposable startupOperation = CrashReportService.Shared.BeginOperation("Starting application");
            try
            {
                var velopackApp = VelopackApp.Build();
                if (CachingVelopackLocator.CreateForCurrentProcess() is { } locator)
                    velopackApp = velopackApp.SetLocator(locator);

                velopackApp = velopackApp
                    .SetAutoApplyOnStartup(false)
                    .OnAfterInstallFastCallback(_ => WindowsFileContextMenuRegistration.RegisterForInstalledApp())
                    .OnAfterUpdateFastCallback(_ => WindowsFileContextMenuRegistration.RegisterForInstalledApp())
                    .OnBeforeUninstallFastCallback(_ =>
                    {
                        WindowsFileContextMenuRegistration.RemoveForInstalledApp();
                        MediaRuntimePaths.TryRemoveAll();
                    });
                velopackApp.Run();

                SingleInstanceManager = WindowsSingleInstanceManager.Create(args);
                if (SingleInstanceManager is { IsPrimaryInstance: false })
                {
                    SingleInstanceManager.Dispose();
                    SingleInstanceManager = null;
                    return;
                }

                using IDisposable runOperation = CrashReportService.Shared.BeginOperation("Running desktop UI");
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                SingleInstanceManager?.Dispose();
                SingleInstanceManager = null;
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
