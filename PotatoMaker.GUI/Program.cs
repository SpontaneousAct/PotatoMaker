using Avalonia;
using LibVLCSharp.Shared;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;
using System;
using System.IO;
using System.Runtime.Versioning;
using LibVlcCore = LibVLCSharp.Shared.Core;
using Velopack;

namespace PotatoMaker.GUI
{
    internal static class LibVlcRuntime
    {
        private static readonly object Sync = new();
        private static bool _initialized;
        private static readonly string ArchitectureFolder = Environment.Is64BitProcess ? "win-x64" : "win-x86";

        public static string? PackagedLibVlcDirectory
        {
            get
            {
                string path = Path.Combine(AppContext.BaseDirectory, "libvlc", ArchitectureFolder);
                return Directory.Exists(path) ? path : null;
            }
        }

        public static string? PackagedPluginsDirectory
        {
            get
            {
                string? baseDirectory = PackagedLibVlcDirectory;
                if (baseDirectory is null)
                    return null;

                string path = Path.Combine(baseDirectory, "plugins");
                return Directory.Exists(path) ? path : null;
            }
        }

        public static void EnsureInitialized()
        {
            lock (Sync)
            {
                if (_initialized)
                    return;

                if (PackagedLibVlcDirectory is { } packagedLibVlcDirectory)
                {
                    if (PackagedPluginsDirectory is { } pluginsDirectory)
                        Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginsDirectory);

                    LibVlcCore.Initialize(packagedLibVlcDirectory);
                    _initialized = true;
                    return;
                }

                LibVlcCore.Initialize();
                _initialized = true;
            }
        }
    }

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
            var velopackApp = VelopackApp.Build();
            if (CachingVelopackLocator.CreateForCurrentProcess() is { } locator)
                velopackApp = velopackApp.SetLocator(locator);

            velopackApp = velopackApp
                .SetAutoApplyOnStartup(false)
                .OnAfterInstallFastCallback(_ => WindowsFileContextMenuRegistration.RegisterForInstalledApp())
                .OnAfterUpdateFastCallback(_ => WindowsFileContextMenuRegistration.RegisterForInstalledApp())
                .OnBeforeUninstallFastCallback(_ => WindowsFileContextMenuRegistration.RemoveForInstalledApp());
            velopackApp.Run();

            SingleInstanceManager = WindowsSingleInstanceManager.Create(args);
            if (SingleInstanceManager is { IsPrimaryInstance: false })
            {
                SingleInstanceManager.Dispose();
                SingleInstanceManager = null;
                return;
            }

            FFmpegBinaries.EnsureConfigured();

            try
            {
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
