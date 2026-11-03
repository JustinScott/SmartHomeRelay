namespace SmartHomeRelay.Core.Abstractions;

/// <summary>LibreHardwareMonitor web server login. Never log the password.</summary>
/// <param name="Username">User name.</param>
/// <param name="Password">Plain-text password, held only in memory.</param>
public sealed record LhmCredentials(string Username, string Password)
{
    /// <inheritdoc />
    public override string ToString() => $"LhmCredentials {{ Username = {Username}, Password = *** }}";
}
