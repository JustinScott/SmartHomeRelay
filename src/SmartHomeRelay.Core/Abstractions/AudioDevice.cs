namespace SmartHomeRelay.Core.Abstractions;

/// <summary>An active audio endpoint.</summary>
/// <param name="Id">Windows' endpoint ID (stable for the device).</param>
/// <param name="Name">The name Windows shows, e.g. <c>Speakers (Realtek(R) Audio)</c>.</param>
public sealed record AudioDevice(string Id, string Name);
