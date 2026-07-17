using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace PotatoMaker.GUI.Services;

public sealed record LibVlcRuntimeValidationResult(
    bool IsValid,
    string? RuntimeDirectory,
    string? Version,
    string Message)
{
    public static LibVlcRuntimeValidationResult Missing(string message) =>
        new(false, null, null, message);
}

/// <summary>
/// Validates the native LibVLC files, supported major version, and process architecture.
/// </summary>
public static class LibVlcRuntimeValidator
{
    private const int SupportedMajorVersion = 3;

    public static LibVlcRuntimeValidationResult ValidateDirectory(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return LibVlcRuntimeValidationResult.Missing("The PotatoMaker VLC runtime is not installed.");

        string? runtimeDirectory = ResolveRuntimeDirectory(candidate);
        if (runtimeDirectory is null)
        {
            return LibVlcRuntimeValidationResult.Missing(
                "The PotatoMaker VLC runtime is incomplete: libvlc.dll, libvlccore.dll, or required preview plugins are missing.");
        }

        string libVlcPath = Path.Combine(runtimeDirectory, "libvlc.dll");
        try
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(libVlcPath);
            int majorVersion = versionInfo.FileMajorPart;
            string detectedVersion = versionInfo.FileVersion ?? versionInfo.ProductVersion ?? "unknown";
            if (majorVersion != SupportedMajorVersion)
            {
                return new LibVlcRuntimeValidationResult(
                    false,
                    runtimeDirectory,
                    detectedVersion,
                    $"VLC {detectedVersion} is not compatible with this PotatoMaker release.");
            }

            if (!MatchesCurrentProcessArchitecture(libVlcPath))
            {
                return new LibVlcRuntimeValidationResult(
                    false,
                    runtimeDirectory,
                    detectedVersion,
                    $"The VLC runtime does not match PotatoMaker's {RuntimeInformation.ProcessArchitecture} architecture.");
            }

            return new LibVlcRuntimeValidationResult(
                true,
                runtimeDirectory,
                detectedVersion,
                $"Using VLC {detectedVersion} from PotatoMaker's media-tools folder.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return new LibVlcRuntimeValidationResult(
                false,
                runtimeDirectory,
                null,
                $"The PotatoMaker VLC runtime could not be checked: {ex.Message}");
        }
    }

    internal static string? ResolveRuntimeDirectory(string candidate)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidate.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (File.Exists(fullPath))
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;

        string architectureFolder = Environment.Is64BitProcess ? "win-x64" : "win-x86";
        string[] possibleDirectories =
        [
            fullPath,
            Path.Combine(fullPath, architectureFolder)
        ];

        return possibleDirectories.FirstOrDefault(IsRuntimeDirectory);
    }

    private static bool IsRuntimeDirectory(string path) =>
        File.Exists(Path.Combine(path, "libvlc.dll")) &&
        File.Exists(Path.Combine(path, "libvlccore.dll")) &&
        Directory.Exists(Path.Combine(path, "plugins"));

    private static bool MatchesCurrentProcessArchitecture(string libraryPath)
    {
        using FileStream stream = File.OpenRead(libraryPath);
        using var reader = new PEReader(stream);
        Machine machine = reader.PEHeaders.CoffHeader.Machine;
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => machine == Machine.Amd64,
            Architecture.X86 => machine == Machine.I386,
            Architecture.Arm64 => machine == Machine.Arm64,
            _ => false
        };
    }
}
