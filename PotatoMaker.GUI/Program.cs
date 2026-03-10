using Avalonia;
using LibVLCSharp.Shared;
using PotatoMaker.Core;
using System;
using System.IO;
using LibVlcCore = LibVLCSharp.Shared.Core;

namespace PotatoMaker.GUI
{
    internal static class LibVlcRuntime
    {
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
    }

    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            FFmpegBinaries.EnsureConfigured();
            InitializeLibVlc();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        private static void InitializeLibVlc()
        {
            if (LibVlcRuntime.PackagedLibVlcDirectory is { } packagedLibVlcDirectory)
            {
                if (LibVlcRuntime.PackagedPluginsDirectory is { } pluginsDirectory)
                    Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginsDirectory);

                LibVlcCore.Initialize(packagedLibVlcDirectory);
                return;
            }

            LibVlcCore.Initialize();
        }
    }
}
