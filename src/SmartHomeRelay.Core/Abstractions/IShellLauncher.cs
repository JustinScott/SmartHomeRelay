namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Opens files and folders with the user's default apps (tray menu, first-run settings).</summary>
public interface IShellLauncher
{
    /// <summary>Creates <paramref name="path"/> if needed and opens it in the file browser.</summary>
    void OpenFolder(string path);

    /// <summary>Opens <paramref name="path"/> in its default app, or a plain text editor if there is none.</summary>
    void OpenFile(string path);
}
