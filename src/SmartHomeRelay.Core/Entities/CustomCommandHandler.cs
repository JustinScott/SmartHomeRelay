namespace SmartHomeRelay.Core.Entities;

/// <summary>
/// Handles a command sent to one of a custom component's command topics (spec 3.3). Runs on a background
/// task like <see cref="EntityCommandHandler"/>; exceptions are caught and logged by the host.
/// </summary>
/// <param name="command">The command name, the last topic level, e.g. <c>play</c>.</param>
/// <param name="payload">The raw payload.</param>
/// <param name="cancellationToken">Cancelled when the app stops.</param>
public delegate Task CustomCommandHandler(string command, string payload, CancellationToken cancellationToken);
