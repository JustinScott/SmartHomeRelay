namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Time since the user's last keyboard or mouse input in this session (spec 5.1).</summary>
public interface IIdleTimeProvider
{
    /// <summary>Time since the last input. Never negative.</summary>
    TimeSpan GetIdleTime();
}
