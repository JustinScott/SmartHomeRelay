using System.Security.AccessControl;
using System.Security.Principal;

namespace SmartHomeRelay.Platform.Windows.Hardware;

/// <summary>
/// Decides whether a program file can only be changed by administrators (spec 5.8, anti-spoofing): neither
/// the file nor its folder may give write, delete, or permission rights to everyone, signed-in users,
/// the Users group, or the current (standard) user.
/// </summary>
internal static class ProtectedFolder
{
    private const FileSystemRights DangerousRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>True if only administrators (or SYSTEM, TrustedInstaller) can change <paramref name="filePath"/>.</summary>
    public static bool IsAdminOnly(string filePath)
    {
        try
        {
            var file = new FileInfo(filePath);
            if (!file.Exists || file.Directory is not { } directory)
            {
                return false;
            }

            var untrusted = UntrustedSids();
            return !GrantsWrite(file.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)), untrusted)
                && !GrantsWrite(directory.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)), untrusted);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or PrivilegeNotHeldException or IdentityNotMappedException)
        {
            return false; // Can't tell: treat as not protected.
        }
    }

    /// <summary>True if any allow rule gives a write-like right to one of <paramref name="untrusted"/>.</summary>
    internal static bool GrantsWrite(AuthorizationRuleCollection rules, IReadOnlySet<SecurityIdentifier> untrusted)
    {
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & DangerousRights) != 0
                && rule.IdentityReference is SecurityIdentifier sid
                && untrusted.Contains(sid))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<SecurityIdentifier> UntrustedSids()
    {
        var sids = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.WorldSid, null),
            new(WellKnownSidType.AuthenticatedUserSid, null),
            new(WellKnownSidType.BuiltinUsersSid, null),
            new(WellKnownSidType.InteractiveSid, null),
        };

        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is { } user && !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            sids.Add(user);
        }

        return sids;
    }
}
