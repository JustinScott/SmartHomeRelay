using System.Net;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Platform.Windows.Configuration;

/// <summary>Uses <see cref="Dns.GetHostName"/>, exactly as the prototype did, so topic names match.</summary>
public sealed class DnsHostNameProvider : IHostNameProvider
{
    /// <inheritdoc />
    public string GetHostName() => Dns.GetHostName();
}
