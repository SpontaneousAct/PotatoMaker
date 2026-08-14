namespace PotatoMaker.Core;

/// <summary>
/// Adds actionable context to file-system failures raised while installing a media runtime.
/// The original exception remains available for the setup diagnostic log.
/// </summary>
public sealed class MediaToolInstallationException : IOException
{
    public MediaToolInstallationException(
        string toolName,
        string operation,
        string destinationDirectory,
        Exception innerException)
        : base(BuildMessage(toolName, operation, destinationDirectory), innerException)
    {
        ToolName = toolName;
        Operation = operation;
        DestinationDirectory = destinationDirectory;
    }

    public string ToolName { get; }

    public string Operation { get; }

    public string DestinationDirectory { get; }

    private static string BuildMessage(string toolName, string operation, string destinationDirectory) =>
        $"Windows could not finish {operation} for {toolName}. The install folder is '{destinationDirectory}'. " +
        "A file may be locked or Windows Security/antivirus may be blocking the install. " +
        "Close apps using these media tools, check protection history, and try again. " +
        "You can also use the manual-download links in this window.";
}
