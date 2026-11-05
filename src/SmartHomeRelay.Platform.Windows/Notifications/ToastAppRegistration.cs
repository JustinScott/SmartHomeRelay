using Microsoft.Win32;

namespace SmartHomeRelay.Platform.Windows.Notifications;

/// <summary>
/// Registers the app's identity for toasts under the current user (spec 5.6), the way unpackaged apps without a
/// Start Menu shortcut do: <c>HKCU\Software\Classes\AppUserModelId\{id}</c> with <c>DisplayName</c> and <c>IconUri</c>.
/// No admin rights. Rewritten at every start so a moved app or icon is picked up.
/// </summary>
internal static class ToastAppRegistration
{
    public const string KeyPath = @"Software\Classes\AppUserModelId\";

    public static void Register(ToastAppIdentity identity)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath + identity.AppUserModelId, writable: true);
        key.SetValue("DisplayName", identity.DisplayName, RegistryValueKind.String);
        if (identity.IconPath is { } icon)
        {
            key.SetValue("IconUri", icon, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue("IconUri", throwOnMissingValue: false);
        }
    }
}
