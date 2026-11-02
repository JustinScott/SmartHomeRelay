namespace SmartHomeRelay.Core.Abstractions;

/// <summary>Supplies the machine's host name, the default device ID (spec 4.1).</summary>
public interface IHostNameProvider
{
    /// <summary>The host name exactly as the prototype read it, so existing topics keep working.</summary>
    string GetHostName();
}
