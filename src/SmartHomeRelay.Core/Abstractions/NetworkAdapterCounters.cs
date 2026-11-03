namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Cumulative byte counters of one network adapter.</summary>
/// <param name="Id">Stable adapter ID, used to pair samples.</param>
/// <param name="Description">Driver description, e.g. <c>Intel(R) Ethernet Controller I225-V</c>.</param>
/// <param name="Kind">Adapter kind.</param>
/// <param name="IsUp">Whether the adapter is connected.</param>
/// <param name="BytesReceived">Bytes received since the adapter started.</param>
/// <param name="BytesSent">Bytes sent since the adapter started.</param>
public sealed record NetworkAdapterCounters(
    string Id, string Description, NetworkAdapterKind Kind, bool IsUp, long BytesReceived, long BytesSent);
