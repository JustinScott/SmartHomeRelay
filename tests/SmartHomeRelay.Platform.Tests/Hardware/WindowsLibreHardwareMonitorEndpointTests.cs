using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Hardware;

namespace SmartHomeRelay.Platform.Tests.Hardware;

/// <summary>Port ownership, install-location and disk checks on real Windows (read-only).</summary>
[Trait("Category", "Windows")]
public sealed class WindowsLibreHardwareMonitorEndpointTests : IDisposable
{
    private readonly WindowsLibreHardwareMonitorEndpoint _endpoint = new();

    public void Dispose() => _endpoint.Dispose();

    [Fact]
    public void Inspect_FreePort_HasNoOwner()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        _endpoint.Inspect(port).Owner.ShouldBe(PortOwnerKind.None);
    }

    [Fact]
    public void Inspect_PortOwnedByThisProcess_NamesThisProgram()
    {
        // The anti-spoofing check must see through to the program behind an ordinary listener.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var snapshot = _endpoint.Inspect(((IPEndPoint)listener.LocalEndpoint).Port);

            snapshot.Owner.ShouldBe(PortOwnerKind.Process);
            snapshot.OwnerImagePath.ShouldBe(Environment.ProcessPath, StringCompareShould.IgnoreCase);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void ProtectedFolder_WindowsSystemProgram_IsAdminOnly()
    {
        ProtectedFolder.IsAdminOnly(Path.Combine(Environment.SystemDirectory, "notepad.exe")).ShouldBeTrue();
    }

    [Fact]
    public void ProtectedFolder_UserTempFile_IsNot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shr-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, [0]);
        try
        {
            ProtectedFolder.IsAdminOnly(path).ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProtectedFolder_CurrentUserIsAlwaysUntrusted_EvenWhenAdmin()
    {
        // Regression: when the tests ran elevated, the user's own SID was left out, so a file in their Temp
        // folder (where the user has full control) counted as admin-only.
        using var identity = WindowsIdentity.GetCurrent();

        ProtectedFolder.UntrustedSids().ShouldContain(identity.User!);
    }

    [Fact]
    public void ProtectedFolder_OwnedByAnUntrustedUser_IsNot()
    {
        // An owner can rewrite the permissions, so ownership alone disqualifies a file.
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var security = new FileSecurity();
        security.SetOwner(everyone);

        ProtectedFolder.IsProtected(security, new HashSet<SecurityIdentifier> { everyone }).ShouldBeFalse();
    }

    [Fact]
    public void ProtectedFolder_InheritOnlyRules_DoNotCount()
    {
        // Program Files gives CREATOR OWNER full control, but only for new children.
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new DirectorySecurity();
        security.SetOwner(admins);
        security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.InheritOnly, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, AccessControlType.Allow));

        ProtectedFolder.IsProtected(security, new HashSet<SecurityIdentifier> { users }).ShouldBeTrue();

        security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify, AccessControlType.Allow));
        ProtectedFolder.IsProtected(security, new HashSet<SecurityIdentifier> { users }).ShouldBeFalse();
    }

    [Fact]
    public void ProtectedFolder_MissingFile_IsNot()
    {
        ProtectedFolder.IsAdminOnly(@"C:\does-not-exist\LibreHardwareMonitor.exe").ShouldBeFalse();
    }

    [Fact]
    public void GetSystemDiskNumber_IsANonNegativeDiskOrUnknown()
    {
        var disk = _endpoint.GetSystemDiskNumber();

        if (disk is { } number)
        {
            number.ShouldBeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void Inspect_ListsRunningLhm_WithoutThrowing()
    {
        // On BOX1 with LHM running this lists it; on CI there is none.
        var snapshot = _endpoint.Inspect(8085);

        snapshot.Processes.Count.ShouldBe(Process.GetProcessesByName("LibreHardwareMonitor").Length);
    }
}
