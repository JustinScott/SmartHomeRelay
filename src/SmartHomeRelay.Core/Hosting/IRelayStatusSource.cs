namespace SmartHomeRelay.Core.Hosting;

/// <summary>The overall connection status shown in the tray (spec 11).</summary>
public interface IRelayStatusSource
{
    /// <summary>Raised when <see cref="Status"/> changes. May be raised on any thread.</summary>
    event EventHandler? StatusChanged;

    /// <summary>The current status.</summary>
    RelayStatus Status { get; }
}
