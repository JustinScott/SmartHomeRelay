using Microsoft.Extensions.Options;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Configuration;

/// <summary>
/// Fills an empty device ID and name with the host name, matching the prototype's
/// <c>Dns.GetHostName()</c> topics (spec 4.1). Explicit values are never changed.
/// </summary>
public sealed class DeviceDefaultsPostConfigure(IHostNameProvider hostNameProvider) : IPostConfigureOptions<AppSettings>
{
    /// <inheritdoc />
    public void PostConfigure(string? name, AppSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Device.Id))
        {
            options.Device.Id = hostNameProvider.GetHostName();
        }

        if (string.IsNullOrWhiteSpace(options.Device.Name))
        {
            options.Device.Name = hostNameProvider.GetHostName();
        }
    }
}
