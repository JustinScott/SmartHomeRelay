using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

internal sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> OpenedFiles { get; } = [];

    public List<string> OpenedFolders { get; } = [];

    public void OpenFile(string path) => OpenedFiles.Add(path);

    public void OpenFolder(string path) => OpenedFolders.Add(path);
}
