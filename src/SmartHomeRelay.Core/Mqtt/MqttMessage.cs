namespace SmartHomeRelay.Core.Mqtt;

/// <summary>A message received from the broker.</summary>
/// <param name="Topic">The topic it was published to.</param>
/// <param name="Payload">The payload as UTF-8 text.</param>
public sealed record MqttMessage(string Topic, string Payload);
