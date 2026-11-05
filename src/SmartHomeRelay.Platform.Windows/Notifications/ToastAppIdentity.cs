namespace SmartHomeRelay.Platform.Windows.Notifications;

/// <summary>How the app appears on its toasts (spec 5.6).</summary>
/// <param name="AppUserModelId">The stable App User Model ID. Never change it: Windows keeps the user's notification settings under it.</param>
/// <param name="DisplayName">Name shown on toasts and in Windows' notification settings.</param>
/// <param name="IconPath">A PNG shown on toasts, or null.</param>
public sealed record ToastAppIdentity(string AppUserModelId, string DisplayName, string? IconPath)
{
    /// <summary>Smart Home Relay's App User Model ID.</summary>
    public const string DefaultAppUserModelId = "SmartHomeRelay.App";
}
