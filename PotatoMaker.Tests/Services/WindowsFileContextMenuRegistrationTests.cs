using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class WindowsFileContextMenuRegistrationTests
{
    [Fact]
    public void BuildCommand_QuotesExecutableAndSelectedFile()
    {
        string command = WindowsFileContextMenuRegistration.BuildCommand(@"C:\Apps\PotatoMaker.GUI.exe");

        Assert.Equal("\"C:\\Apps\\PotatoMaker.GUI.exe\" \"%1\"", command);
    }

    [Fact]
    public void GetRegistryKeyPaths_UsesPerUserVideoExtensionShellKeys()
    {
        IReadOnlyList<string> keyPaths = WindowsFileContextMenuRegistration.GetRegistryKeyPaths();

        Assert.Contains(@"Software\Classes\SystemFileAssociations\.mp4\shell\PotatoMaker.Compress", keyPaths);
        Assert.Contains(@"Software\Classes\SystemFileAssociations\.mkv\shell\PotatoMaker.Compress", keyPaths);
        Assert.All(keyPaths, path => Assert.Contains(@"\shell\PotatoMaker.Compress", path, StringComparison.Ordinal));
    }
}
