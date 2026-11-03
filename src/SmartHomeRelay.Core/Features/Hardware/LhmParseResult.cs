namespace SmartHomeRelay.Core.Features.Hardware;

/// <summary>The outcome of parsing <c>data.json</c>: sensors, or why the format isn't understood.</summary>
/// <param name="Sensors">Every sensor found (empty when <see cref="Problem"/> is set).</param>
/// <param name="Problem">Null when the format is understood; otherwise what was wrong.</param>
public sealed record LhmParseResult(IReadOnlyList<LhmSensor> Sensors, string? Problem)
{
    /// <summary>True when the format is understood.</summary>
    public bool IsValid => Problem is null;

    /// <summary>A failed parse.</summary>
    public static LhmParseResult Invalid(string problem) => new([], problem);
}
