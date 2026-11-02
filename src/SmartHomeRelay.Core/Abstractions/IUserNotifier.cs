namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Shows the user a short informational message (a tray balloon on Windows).</summary>
public interface IUserNotifier
{
    /// <summary>
    /// Shows <paramref name="message"/>. Safe to call from any thread and before the tray exists;
    /// messages are shown once it does.
    /// </summary>
    void Notify(string title, string message);
}
