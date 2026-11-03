using System.ComponentModel;
using System.Diagnostics;

namespace SmartHomeRelay.Platform.Windows.Metrics;

/// <summary>
/// Reads one counter for every instance of a performance counter category, computing each value from the
/// previous call's sample (needed for time-based counters; harmless for plain values). The first call
/// returns nothing. One <c>ReadCategory</c> per call is far cheaper than a counter per instance, and
/// instances may come and go between calls.
/// </summary>
internal sealed class PerformanceCounterDeltaReader(string category, string counter)
{
    private readonly Lock _gate = new();
    private Dictionary<string, CounterSample> _last = new(StringComparer.Ordinal);

    /// <summary>True if the category and counter exist on this PC.</summary>
    public bool Exists
    {
        get
        {
            try
            {
                return PerformanceCounterCategory.Exists(category)
                    && PerformanceCounterCategory.CounterExists(counter, category);
            }
            catch (Exception ex) when (IsCounterError(ex))
            {
                return false;
            }
        }
    }

    /// <summary>The category's current instance names; empty if it doesn't exist.</summary>
    public IReadOnlyList<string> GetInstanceNames()
    {
        try
        {
            return Exists ? new PerformanceCounterCategory(category).GetInstanceNames() : [];
        }
        catch (Exception ex) when (IsCounterError(ex))
        {
            return [];
        }
    }

    /// <summary>Value per instance since the previous call. Empty on the first call or if unavailable.</summary>
    public IReadOnlyDictionary<string, double> Read()
    {
        lock (_gate)
        {
            InstanceDataCollection? instances;
            try
            {
                instances = new PerformanceCounterCategory(category).ReadCategory()[counter];
            }
            catch (Exception ex) when (IsCounterError(ex))
            {
                return new Dictionary<string, double>();
            }

            var current = new Dictionary<string, CounterSample>(StringComparer.Ordinal);
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            if (instances is not null)
            {
                foreach (InstanceData instance in instances.Values)
                {
                    current[instance.InstanceName] = instance.Sample;
                    if (_last.TryGetValue(instance.InstanceName, out var previous))
                    {
                        values[instance.InstanceName] = CounterSample.Calculate(previous, instance.Sample);
                    }
                }
            }

            _last = current;
            return values;
        }
    }

    private static bool IsCounterError(Exception ex) =>
        ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException;
}
