using Microsoft.Extensions.DependencyInjection;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Configuration;
using SmartHomeRelay.Platform.Windows.Hosting;

namespace SmartHomeRelay.Platform.Windows;

/// <summary>Registers the Windows implementations of shared (non-feature) abstractions.</summary>
public static class WindowsPlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers platform services. <paramref name="paths"/> and <paramref name="singleInstanceGuard"/>
    /// are created before the host (logging and the mutex are needed first), so they are passed in and
    /// owned by the caller.
    /// </summary>
    public static IServiceCollection AddWindowsPlatform(
        this IServiceCollection services,
        IAppPaths paths,
        ISingleInstanceGuard singleInstanceGuard)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(paths);
        services.AddSingleton(singleInstanceGuard);
        services.AddSingleton<IHostNameProvider, DnsHostNameProvider>();
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<IFolderOpener, ShellFolderOpener>();
        return services;
    }
}
