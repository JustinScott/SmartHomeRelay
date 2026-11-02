namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Opens a folder in the OS file browser (tray menu "Open logs folder", spec 7.1).</summary>
public interface IFolderOpener
{
    /// <summary>Creates <paramref name="path"/> if needed and opens it.</summary>
    void Open(string path);
}
