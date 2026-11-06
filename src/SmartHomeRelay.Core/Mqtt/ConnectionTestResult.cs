namespace SmartHomeRelay.Core.Mqtt;

/// <summary>The outcome of the settings window's Test connection (spec 7.1).</summary>
/// <param name="Succeeded">The broker accepted the login.</param>
/// <param name="Message">A sentence for the user. Never contains the password.</param>
public sealed record ConnectionTestResult(bool Succeeded, string Message);
