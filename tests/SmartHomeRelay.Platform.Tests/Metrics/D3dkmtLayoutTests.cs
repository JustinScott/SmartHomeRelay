using System.Runtime.InteropServices;
using SmartHomeRelay.Platform.Windows.Interop;
using SmartHomeRelay.Platform.Windows.Metrics;

namespace SmartHomeRelay.Platform.Tests.Metrics;

/// <summary>
/// Pins the native layouts of the D3DKMT structs (d3dkmthk.h, x64) used for GPU temperature and power,
/// verified against the sensor probe on BOX1. No Windows APIs are called, so these run on any OS.
/// </summary>
public sealed class D3dkmtLayoutTests
{
    [Fact]
    public void AdapterInfo_Is20Bytes()
    {
        Marshal.SizeOf<D3dkmtAdapterInfo>().ShouldBe(20);
        Marshal.OffsetOf<D3dkmtAdapterInfo>(nameof(D3dkmtAdapterInfo.LuidLowPart)).ToInt32().ShouldBe(4);
    }

    [Fact]
    public void EnumAdapters2_PointerIsAtByte8()
    {
        Marshal.OffsetOf<D3dkmtEnumAdapters2>(nameof(D3dkmtEnumAdapters2.Adapters)).ToInt32().ShouldBe(8);
        Marshal.SizeOf<D3dkmtEnumAdapters2>().ShouldBe(16);
    }

    [Fact]
    public void QueryAdapterInfo_DataPointerIsAtByte8()
    {
        Marshal.OffsetOf<D3dkmtQueryAdapterInfo>(nameof(D3dkmtQueryAdapterInfo.PrivateDriverData)).ToInt32().ShouldBe(8);
        Marshal.SizeOf<D3dkmtQueryAdapterInfo>().ShouldBe(24);
    }

    [Fact]
    public void AdapterPerfData_SensorFieldsAfterTheEightByteFields()
    {
        // 4 (index) + 4 padding + 5 x 8 (frequencies, bandwidths) = 48.
        Marshal.OffsetOf<D3dkmtAdapterPerfData>(nameof(D3dkmtAdapterPerfData.FanRpm)).ToInt32().ShouldBe(48);
        Marshal.OffsetOf<D3dkmtAdapterPerfData>(nameof(D3dkmtAdapterPerfData.Power)).ToInt32().ShouldBe(52);
        Marshal.OffsetOf<D3dkmtAdapterPerfData>(nameof(D3dkmtAdapterPerfData.Temperature)).ToInt32().ShouldBe(56);
        Marshal.SizeOf<D3dkmtAdapterPerfData>().ShouldBe(64);
    }

    [Fact]
    public void ToSensors_ConvertsTenths()
    {
        var info = new D3dkmtAdapterInfo { LuidLowPart = 0x4160BFB, LuidHighPart = 1 };
        var perf = new D3dkmtAdapterPerfData { Temperature = 470, Power = 32, FanRpm = 1095 };

        var sensors = WindowsSystemMetricsSource.ToSensors(info, perf);

        sensors.AdapterId.ShouldBe(0x1_0416_0BFBL);
        sensors.TemperatureCelsius.ShouldBe(47.0);
        sensors.PowerPercent.ShouldBe(3.2, tolerance: 1e-9);
        sensors.FanRpm.ShouldBe(1095u);
    }
}
