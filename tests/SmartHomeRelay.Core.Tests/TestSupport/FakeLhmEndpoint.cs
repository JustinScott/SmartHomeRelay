using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>A LibreHardwareMonitor that answers from a fixture, trusted by default.</summary>
internal sealed class FakeLhmEndpoint : ILibreHardwareMonitorEndpoint
{
    public static readonly Version Tested = new(0, 9, 6, 0);

    public LhmEndpointSnapshot Snapshot { get; set; } = Trusted(Tested);

    public LhmHttpResult Response { get; set; } = new(LhmHttpOutcome.Ok, LhmFixtures.Box1);

    public int? SystemDisk { get; set; } = 0;

    public List<LhmCredentials?> Requests { get; } = [];

    public static LhmEndpointSnapshot Trusted(Version version) =>
        new(PortOwnerKind.HttpSys, null, false, null, [new LhmProcessInfo(@"C:\Program Files\LibreHardwareMonitor\LibreHardwareMonitor.exe", version, true)]);

    public LhmEndpointSnapshot Inspect(int port) => Snapshot;

    public Task<LhmHttpResult> GetDataAsync(int port, LhmCredentials? credentials, CancellationToken cancellationToken)
    {
        Requests.Add(credentials);
        return Task.FromResult(Response);
    }

    public int? GetSystemDiskNumber() => SystemDisk;

    /// <summary>Changes one sensor's RawValue in the fixture.</summary>
    public void SetRawValue(string sensorId, string rawValue) =>
        Response = new(LhmHttpOutcome.Ok, LhmFixtures.WithRawValue(Response.Body!, sensorId, rawValue));
}
