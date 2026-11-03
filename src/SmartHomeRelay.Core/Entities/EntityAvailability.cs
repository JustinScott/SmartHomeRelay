namespace SmartHomeRelay.Core.Entities;

/// <summary>
/// Makes an entity available only while another entity of the same module reports one of
/// <see cref="AvailableStates"/>, in addition to the device being online. HA sees both through an
/// <c>availability</c> list with <c>availability_mode: all</c>, e.g. hardware sensors go unavailable while
/// <c>hardware_status</c> is not <c>ok</c>, but the rest of the device stays online.
/// </summary>
/// <param name="StatusKey">The key of the entity whose state decides availability.</param>
/// <param name="AvailableStates">States of that entity in which this one is available (lowercase, digits, underscores).</param>
public sealed record EntityAvailability(string StatusKey, IReadOnlyList<string> AvailableStates);
