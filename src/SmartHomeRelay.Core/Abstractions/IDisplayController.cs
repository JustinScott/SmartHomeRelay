namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Turns the displays off and on (spec 5.3).</summary>
public interface IDisplayController
{
    /// <summary>Puts the displays into power saving.</summary>
    Task TurnOffAsync(CancellationToken cancellationToken);

    /// <summary>Wakes the displays. Presence must not mistake this for the user coming back.</summary>
    Task TurnOnAsync(CancellationToken cancellationToken);
}
