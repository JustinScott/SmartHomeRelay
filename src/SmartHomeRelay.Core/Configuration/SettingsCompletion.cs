namespace SmartHomeRelay.Core.Configuration;

/// <summary>What <see cref="ISettingsStore.AddMissingAsync"/> did.</summary>
/// <param name="Added">JSON paths of the settings that were missing, e.g. <c>features.hardware</c>.</param>
/// <param name="Written">True if the file was rewritten with them.</param>
/// <param name="HasComments">True if the file has comments, in which case it is left untouched.</param>
public sealed record SettingsCompletion(IReadOnlyList<string> Added, bool Written, bool HasComments);
