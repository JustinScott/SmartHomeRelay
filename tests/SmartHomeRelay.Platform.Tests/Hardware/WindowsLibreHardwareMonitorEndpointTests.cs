using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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
