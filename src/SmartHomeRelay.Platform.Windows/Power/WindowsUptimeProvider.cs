using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Platform.Windows.Power;

/// <summary>Uptime from <see cref="Environment.TickCount64"/> (GetTickCount64), which includes time asleep.</summary>
public sealed class WindowsUptimeProvider : IUptimeProvider
{
    /// <inheritdoc />
    public TimeSpan GetUptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);
}
