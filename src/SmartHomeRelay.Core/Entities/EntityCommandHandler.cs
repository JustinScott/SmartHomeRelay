namespace SmartHomeRelay.Core.Entities;

/// <summary>
/// Handles a command HA sent to an entity's command topic. Runs on a background task, never inside the
/// MQTT receive loop; exceptions are caught and logged by the host.
/// </summary>
/// <param name="payload">The raw payload, e.g. <c>PRESS</c>, <c>ON</c>, or <c>42</c>.</param>
/// <param name="cancellationToken">Cancelled when the app stops.</param>
public delegate Task EntityCommandHandler(string payload, CancellationToken cancellationToken);
