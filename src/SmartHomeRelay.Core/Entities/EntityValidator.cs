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

            if (entity.CommandTopic is not null && !entity.AcceptsCommands)
            {
                errors.Add($"Entity '{entity.Key}' is a {entity.Component} and cannot have a command topic.");
            }

            if (entity.ReportsConnection && (entity.Component != EntityComponent.BinarySensor || entity.AvailableWhen is not null))
            {
                errors.Add($"Entity '{entity.Key}' reports the connection, so it must be a BinarySensor with no availability condition.");
            }

            if (entity.Component == EntityComponent.Number && entity.Range is null)
            {
                errors.Add($"Entity '{entity.Key}' is a Number but has no range.");
            }

            if (entity.Component == EntityComponent.Select && (entity.Options is not { Count: > 0 } options || options.Distinct(StringComparer.Ordinal).Count() != options.Count || options.Any(string.IsNullOrWhiteSpace)))
            {
                errors.Add($"Entity '{entity.Key}' is a Select but has no options, duplicate options, or a blank option.");
            }

            if (entity.Range is { } range && (range.Min >= range.Max || range.Step <= 0))
            {
                errors.Add($"Entity '{entity.Key}' has an invalid range.");
            }

            if (entity.AvailableWhen is { } availability)
            {
                var status = entities.FirstOrDefault(e => e.Key == availability.StatusKey);
                if (status is null || !status.HasState || status.Key == entity.Key || status.AvailableWhen is not null)
                {
                    errors.Add($"Entity '{entity.Key}' depends on '{availability.StatusKey}', which must be another entity of the same module with a state and no availability condition of its own.");
                }

                // The states end up inside a Jinja template in the discovery config: allow only plain words.
                if (availability.AvailableStates.Count == 0 || !availability.AvailableStates.All(IsValidKey))
                {
                    errors.Add($"Entity '{entity.Key}' has invalid available states; use lowercase letters, digits, and underscores.");
                }
            }
        }

        return errors;
    }

    /// <summary>True if <paramref name="key"/> is non-empty and only <c>a-z</c>, <c>0-9</c>, and <c>_</c>.</summary>
    public static bool IsValidKey(string key) =>
        !string.IsNullOrEmpty(key) && key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');
}
