namespace SmartHomeRelay.Core.Abstractions;

/// <summary>The result of reading LibreHardwareMonitor's <c>data.json</c>.</summary>
/// <param name="Outcome">How the request went.</param>
/// <param name="Body">The JSON text when <see cref="Outcome"/> is <see cref="LhmHttpOutcome.Ok"/>.</param>
/// <param name="Detail">A short description for the log (an HTTP status, a content type); never secrets.</param>
public sealed record LhmHttpResult(LhmHttpOutcome Outcome, string? Body = null, string? Detail = null);
