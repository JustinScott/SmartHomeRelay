using System.Security.AccessControl;
using System.Security.Principal;

namespace SmartHomeRelay.Platform.Windows.Hardware;

/// <summary>
/// Decides whether a program file can only be changed by administrators (spec 5.8, anti-spoofing). The
/// threat is a program running as the signed-in user without elevation, so neither the file nor its folder
/// may be owned by, or give write, delete, or permission rights to, the current user, Everyone,
/// Authenticated Users, Users, or Interactive. The current user counts even when they are an administrator:
/// under UAC their unelevated programs still carry the user's own identity (but not the Administrators group).
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
            return IsProtected(file.GetAccessControl(), untrusted) && IsProtected(directory.GetAccessControl(), untrusted);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or PrivilegeNotHeldException or IdentityNotMappedException)
        {
            return false; // Can't tell: treat as not protected.
        }
    }

    /// <summary>
    /// True if <paramref name="security"/> neither belongs to nor grants a write-like right to any of
    /// <paramref name="untrusted"/>. An owner can always rewrite the permissions, so ownership counts too.
    /// </summary>
    internal static bool IsProtected(FileSystemSecurity security, IReadOnlySet<SecurityIdentifier> untrusted)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && untrusted.Contains(owner))
        {
            return false;
        }

        return !GrantsWrite(security.GetAccessRules(true, true, typeof(SecurityIdentifier)), untrusted);
    }

    /// <summary>True if any allow rule that applies to the object itself gives a write-like right to one of <paramref name="untrusted"/>.</summary>
    internal static bool GrantsWrite(AuthorizationRuleCollection rules, IReadOnlySet<SecurityIdentifier> untrusted)
    {
        foreach (FileSystemAccessRule rule in rules)
        {
            // Inherit-only rules (e.g. CREATOR OWNER on Program Files) only apply to new children.
            if (rule.AccessControlType == AccessControlType.Allow
                && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                && (rule.FileSystemRights & DangerousRights) != 0
                && rule.IdentityReference is SecurityIdentifier sid
                && untrusted.Contains(sid))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Identities an unelevated program running as the current user has.</summary>
    internal static HashSet<SecurityIdentifier> UntrustedSids()
    {
        var sids = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.WorldSid, null),
            new(WellKnownSidType.AuthenticatedUserSid, null),
            new(WellKnownSidType.BuiltinUsersSid, null),
            new(WellKnownSidType.InteractiveSid, null),
        };

        // Always, even for an administrator (regression: an elevated test run excluded the user, so a file in
        // the user's own Temp folder was judged admin-only).
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is { } user)
        {
            sids.Add(user);
        }

        return sids;
    }
}
