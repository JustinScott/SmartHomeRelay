namespace SmartHomeRelay.Core.Mqtt;

/// <summary>A message received from the broker.</summary>
/// <param name="Topic">The topic it was published to.</param>
/// <param name="Payload">The payload as UTF-8 text.</param>
/// <param name="Retained">True when the broker sent a stored (retained) message because of a new subscription.</param>
public sealed record MqttMessage(string Topic, string Payload, bool Retained = false);
