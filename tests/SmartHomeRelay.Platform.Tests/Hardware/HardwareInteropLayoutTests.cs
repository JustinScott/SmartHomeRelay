using System.Runtime.InteropServices;
using SmartHomeRelay.Platform.Windows.Hardware;
using SmartHomeRelay.Platform.Windows.Interop;

namespace SmartHomeRelay.Platform.Tests.Hardware;

/// <summary>Native layouts and byte-order helpers for the LHM endpoint. No Windows APIs called.</summary>
public sealed class HardwareInteropLayoutTests
{
    [Fact]
    public void TcpRowOwnerPid_Is24Bytes_WithPidLast()
    {
        Marshal.SizeOf<TcpRowOwnerPid>().ShouldBe(24);
        Marshal.OffsetOf<TcpRowOwnerPid>(nameof(TcpRowOwnerPid.OwningPid)).ToInt32().ShouldBe(20);
    }

    [Fact]
    public void VolumeDiskExtents_FirstExtentAtByte8()
    {
        Marshal.SizeOf<DiskExtent>().ShouldBe(24);
        Marshal.OffsetOf<VolumeDiskExtents>(nameof(VolumeDiskExtents.Extent)).ToInt32().ShouldBe(8);
        Marshal.SizeOf<VolumeDiskExtents>().ShouldBe(32);
    }

    [Theory]
    [InlineData(0x951Fu, 8085)] // 8085 = 0x1F95, stored big-endian
    [InlineData(0x5000u, 80)]
    public void PortFromNetworkOrder_SwapsBytes(uint raw, int port)
    {
        WindowsLibreHardwareMonitorEndpoint.PortFromNetworkOrder(raw).ShouldBe(port);
    }

    [Theory]
    [InlineData(0x00000000u, true)] // 0.0.0.0
    [InlineData(0x0100007Fu, true)] // 127.0.0.1 in network order
    [InlineData(0x0101A8C0u, false)] // 192.168.1.1
    public void ReachableFromLoopback(uint address, bool expected)
    {
        WindowsLibreHardwareMonitorEndpoint.ReachableFromLoopback(address).ShouldBe(expected);
    }
}
