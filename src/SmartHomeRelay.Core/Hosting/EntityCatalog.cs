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
    private readonly Dictionary<string, CatalogEntry> _byKey;

    private EntityCatalog(IReadOnlyList<IFeatureModule> modules, Dictionary<string, CatalogEntry> byKey, IReadOnlyList<string> rejections)
    {
        Modules = modules;
        _byKey = byKey;
        Rejections = rejections;
    }

    /// <summary>Modules that passed validation, in registration order.</summary>
    public IReadOnlyList<IFeatureModule> Modules { get; }

    /// <summary>All accepted entities.</summary>
    public IEnumerable<CatalogEntry> Entries => _byKey.Values;

    /// <summary>Why modules were rejected; empty when all are valid.</summary>
    public IReadOnlyList<string> Rejections { get; }

    /// <summary>Validates <paramref name="modules"/> and builds the catalog.</summary>
    public static EntityCatalog Build(IEnumerable<IFeatureModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var accepted = new List<IFeatureModule>();
        var byKey = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
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

            var errors = EntityValidator.Validate(entities).ToList();
            errors.AddRange(entities
                .Where(e => byKey.TryGetValue(e.Key, out var existing) && existing.Module != module)
                .Select(e => $"Entity key '{e.Key}' is already used by module '{byKey[e.Key].Module.Id}'."));

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
        }

        return new EntityCatalog(accepted, byKey, rejections);
    }

    /// <summary>Finds the entity with <paramref name="key"/>.</summary>
    public bool TryGet(string key, out CatalogEntry entry) => _byKey.TryGetValue(key, out entry!);
}
