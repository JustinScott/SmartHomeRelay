namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Opens the settings window (spec 7.1). Safe from any thread; a request before the tray exists is kept until it does.</summary>
public interface ISettingsWindow
{
    /// <summary>Shows the settings window, or brings it to the front if it is open.</summary>
    void Open();
}
