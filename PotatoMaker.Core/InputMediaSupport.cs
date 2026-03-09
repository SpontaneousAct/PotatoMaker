namespace PotatoMaker.Core;

/// <summary>
/// Shared allowlist and validation helpers for input video files.
/// </summary>
public static class InputMediaSupport
{
    private static readonly string[] SupportedExtensionsInternal =
    [
        ".mp4",
        ".mkv",
        ".avi",
        ".mov",
        ".webm",
        ".wmv",
        ".flv"
    ];

    private static readonly HashSet<string> SupportedExtensionsSet = new(SupportedExtensionsInternal, StringComparer.OrdinalIgnoreCase);
    private static readonly string[] FileDialogPatternsInternal = SupportedExtensionsInternal
        .Select(extension => $"*{extension}")
        .ToArray();

    public static IReadOnlyList<string> SupportedExtensions => SupportedExtensionsInternal;
    public static IReadOnlyList<string> FileDialogPatterns => FileDialogPatternsInternal;
    public static string SupportedExtensionsDisplay { get; } = string.Join(", ", SupportedExtensionsInternal);

    public static bool IsSupportedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && SupportedExtensionsSet.Contains(extension);
    }

    public static bool TryValidatePath(string? path, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "No input file specified.";
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            errorMessage = $"File not found: {fullPath}";
            return false;
        }

        string extension = Path.GetExtension(fullPath);
        if (string.IsNullOrWhiteSpace(extension) || !SupportedExtensionsSet.Contains(extension))
        {
            string extensionLabel = string.IsNullOrWhiteSpace(extension) ? "with no extension" : $"'{extension}'";
            errorMessage = $"Unsupported input file type {extensionLabel}. Supported formats: {SupportedExtensionsDisplay}.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public static void ThrowIfInvalidPath(string? path)
    {
        if (TryValidatePath(path, out string errorMessage))
            return;

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException(errorMessage, nameof(path));

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(errorMessage, fullPath);

        throw new NotSupportedException(errorMessage);
    }
}
