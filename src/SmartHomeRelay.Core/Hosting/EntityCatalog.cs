using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features;

namespace SmartHomeRelay.Core.Hosting;

/// <summary>
/// Every module's entities, validated. A module whose entities break the rules (or whose
/// <see cref="IFeatureModule.DescribeEntities"/> throws) is rejected as a whole, so one bad module
/// cannot affect the others (spec 3.3).
/// </summary>
public sealed class EntityCatalog
{
    private readonly Lock _replaceGate = new();
    private readonly Dictionary<string, CustomCatalogEntry> _customByName;
    private Dictionary<string, CatalogEntry> _byKey;

    private EntityCatalog(
        IReadOnlyList<IFeatureModule> modules,
        Dictionary<string, CatalogEntry> byKey,
        Dictionary<string, CustomCatalogEntry> customByName,
        IReadOnlyList<string> rejections)
    {
        Modules = modules;
        _byKey = byKey;
        _customByName = customByName;
        Rejections = rejections;
    }

    /// <summary>Modules that passed validation, in registration order.</summary>
    public IReadOnlyList<IFeatureModule> Modules { get; }

    /// <summary>All accepted entities, in registration order.</summary>
    public IEnumerable<CatalogEntry> Entries => Volatile.Read(ref _byKey).Values;

    /// <summary>All accepted custom components (spec 3.3), in registration order.</summary>
    public IEnumerable<CustomCatalogEntry> CustomComponents => _customByName.Values;

    /// <summary>Why modules were rejected; empty when all are valid.</summary>
    public IReadOnlyList<string> Rejections { get; }

    /// <summary>Validates <paramref name="modules"/> and builds the catalog.</summary>
    public static EntityCatalog Build(IEnumerable<IFeatureModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var accepted = new List<IFeatureModule>();
        var byKey = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        var customByName = new Dictionary<string, CustomCatalogEntry>(StringComparer.Ordinal);
        var moduleIds = new HashSet<string>(StringComparer.Ordinal);
        var rejections = new List<string>();

        foreach (var module in modules)
        {
            if (!moduleIds.Add(module.Id))
            {
                rejections.Add($"Module '{module.Id}': another module already uses this ID.");
                continue;
            }

            IReadOnlyList<EntityDescriptor> entities;
            try
            {
                entities = module.DescribeEntities();
            }
            catch (Exception ex)
            {
                rejections.Add($"Module '{module.Id}': describing entities failed: {ex.Message}");
                continue;
            }

            IReadOnlyList<CustomComponent> customs;
            try
            {
                customs = module is IHasCustomComponents withCustoms ? withCustoms.DescribeCustomComponents() : [];
            }
            catch (Exception ex)
            {
                rejections.Add($"Module '{module.Id}': describing custom components failed: {ex.Message}");
                continue;
            }

            var errors = EntityValidator.Validate(entities).ToList();
            errors.AddRange(entities
                .Where(e => byKey.TryGetValue(e.Key, out var existing) && existing.Module != module)
                .Select(e => $"Entity key '{e.Key}' is already used by module '{byKey[e.Key].Module.Id}'."));
            errors.AddRange(entities
                .Where(e => customByName.ContainsKey(e.Key))
                .Select(e => $"Entity key '{e.Key}' is already a custom component's name."));
            errors.AddRange(ValidateCustoms(customs, entities, byKey, customByName));

            if (errors.Count > 0)
            {
                rejections.AddRange(errors.Select(e => $"Module '{module.Id}': {e}"));
                continue;
            }

            accepted.Add(module);
            foreach (var entity in entities)
            {
                byKey[entity.Key] = new CatalogEntry(module, entity);
            }

            foreach (var custom in customs)
            {
                customByName[custom.Name] = new CustomCatalogEntry(module, custom);
            }
        }

        return new EntityCatalog(accepted, byKey, customByName, rejections);
    }

    /// <summary>Finds the custom component named <paramref name="name"/>.</summary>
    public bool TryGetCustom(string name, out CustomCatalogEntry entry) => _customByName.TryGetValue(name, out entry!);

    /// <summary>Finds the entity with <paramref name="key"/>.</summary>
    public bool TryGet(string key, out CatalogEntry entry) => Volatile.Read(ref _byKey).TryGetValue(key, out entry!);

    /// <summary>
    /// Replaces one entity's description (spec 3.3: a select's options can change while the app runs).
    /// It must belong to <paramref name="module"/>, keep its key and component, and pass validation together
    /// with the module's other entities. Returns the problem, or null when replaced.
    /// </summary>
    internal string? TryReplace(IFeatureModule module, EntityDescriptor entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        lock (_replaceGate)
        {
            var current = Volatile.Read(ref _byKey);
            if (!current.TryGetValue(entity.Key, out var existing) || existing.Module != module)
            {
                return $"Entity '{entity.Key}' does not belong to module '{module.Id}'.";
            }

            if (existing.Entity.Component != entity.Component)
            {
                return $"Entity '{entity.Key}' cannot change from {existing.Entity.Component} to {entity.Component}.";
            }

            var siblings = current.Values.Where(e => e.Module == module).Select(e => e.Entity.Key == entity.Key ? entity : e.Entity).ToList();
            var errors = EntityValidator.Validate(siblings);
            if (errors.Count > 0)
            {
                return string.Join(" ", errors);
            }

            // Copy-on-write keeps readers lock-free and the order unchanged.
            var updated = new Dictionary<string, CatalogEntry>(current, StringComparer.Ordinal) { [entity.Key] = existing with { Entity = entity } };
            Volatile.Write(ref _byKey, updated);
            return null;
        }
    }

    /// <summary>
    /// A custom component's name is a topic level next to the entity keys, so it must not equal any of them
    /// (<c>{name}/state</c> would collide); its states and commands are topic levels too.
    /// </summary>
    private static List<string> ValidateCustoms(
        IReadOnlyList<CustomComponent> customs,
        IReadOnlyList<EntityDescriptor> entities,
        Dictionary<string, CatalogEntry> byKey,
        Dictionary<string, CustomCatalogEntry> customByName)
    {
        var errors = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var custom in customs)
        {
            if (!EntityValidator.IsValidKey(custom.Name))
            {
                errors.Add($"Custom component '{custom.Name}' must be lowercase letters, digits, and underscores.");
            }

            if (!names.Add(custom.Name) || customByName.ContainsKey(custom.Name))
            {
                errors.Add($"Custom component '{custom.Name}' is defined more than once.");
            }

            if (byKey.ContainsKey(custom.Name) || entities.Any(e => e.Key == custom.Name))
            {
                errors.Add($"Custom component '{custom.Name}' has the same name as an entity.");
            }

            var levels = custom.States.Concat(custom.Commands).Concat(custom.DisabledStates.Keys).ToList();
            if (!levels.All(EntityValidator.IsValidKey))
            {
                errors.Add($"Custom component '{custom.Name}' has a state or command that isn't lowercase letters, digits, and underscores.");
            }

            if (custom.States.Contains("command") || custom.States.Distinct(StringComparer.Ordinal).Count() != custom.States.Count)
            {
                errors.Add($"Custom component '{custom.Name}' has a duplicate state, or one named 'command'.");
            }

            if (!custom.DisabledStates.Keys.All(custom.States.Contains))
            {
                errors.Add($"Custom component '{custom.Name}' has a disabled state that isn't one of its states.");
            }

            if (custom.Commands.Count > 0 && custom.CommandHandler is null)
            {
                errors.Add($"Custom component '{custom.Name}' has commands but no handler.");
            }
        }

        return errors;
    }
}
