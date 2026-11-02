namespace SmartHomeRelay.Core.Entities;

/// <summary>Allowed values of a <see cref="EntityComponent.Number"/> entity.</summary>
/// <param name="Min">Smallest value.</param>
/// <param name="Max">Largest value.</param>
/// <param name="Step">Increment between values.</param>
public sealed record NumberRange(double Min, double Max, double Step);
