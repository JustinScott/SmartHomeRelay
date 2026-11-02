namespace SmartHomeRelay.Core.Entities;

/// <summary>Checks the rules every module's entities must follow (spec 9, contract tests).</summary>
public static class EntityValidator
{
    /// <summary>Returns every problem found in one module's entities; empty when valid.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<EntityDescriptor> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            if (!IsValidKey(entity.Key))
            {
                errors.Add($"Entity key '{entity.Key}' must be lowercase letters, digits, and underscores.");
            }

            if (!seen.Add(entity.Key))
            {
                errors.Add($"Entity key '{entity.Key}' is used more than once.");
            }

            if (string.IsNullOrWhiteSpace(entity.Name))
            {
                errors.Add($"Entity '{entity.Key}' has no name.");
            }

            if (entity.AcceptsCommands && entity.CommandHandler is null)
            {
                errors.Add($"Entity '{entity.Key}' is a {entity.Component} but has no command handler.");
            }

            if (!entity.AcceptsCommands && entity.CommandHandler is not null)
            {
                errors.Add($"Entity '{entity.Key}' is a {entity.Component} and cannot have a command handler.");
            }

            if (entity.Component == EntityComponent.Number && entity.Range is null)
            {
                errors.Add($"Entity '{entity.Key}' is a Number but has no range.");
            }

            if (entity.Range is { } range && (range.Min >= range.Max || range.Step <= 0))
            {
                errors.Add($"Entity '{entity.Key}' has an invalid range.");
            }
        }

        return errors;
    }

    /// <summary>True if <paramref name="key"/> is non-empty and only <c>a-z</c>, <c>0-9</c>, and <c>_</c>.</summary>
    public static bool IsValidKey(string key) =>
        !string.IsNullOrEmpty(key) && key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');
}
