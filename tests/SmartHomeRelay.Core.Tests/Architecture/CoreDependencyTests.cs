using System.Reflection;
using System.Runtime.Versioning;
using SmartHomeRelay.Core.Configuration;

namespace SmartHomeRelay.Core.Tests.Architecture;

/// <summary>Core must stay free of Windows and UI dependencies (spec 3.2, 9).</summary>
public sealed class CoreDependencyTests
{
    private static readonly Assembly s_core = typeof(AppSettings).Assembly;

    private static readonly string[] s_forbiddenAssemblyPrefixes =
    [
        "System.Windows.Forms",
        "System.Drawing",
        "System.Management",
        "System.Security.Cryptography.ProtectedData",
        "Microsoft.Win32.SystemEvents",
        "Microsoft.Win32.Registry",
        "Microsoft.Windows",
        "Microsoft.WindowsDesktop",
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "NAudio",
        "CommunityToolkit.WinUI",
        "SmartHomeRelay.Platform",
        "SmartHomeRelay.App",
    ];

    [Fact]
    public void Core_DoesNotReferenceWindowsOrUiAssemblies()
    {
        var offending = s_core.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => s_forbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offending.ShouldBeEmpty();
    }

    [Fact]
    public void Core_TargetsPortableFramework()
    {
        var framework = s_core.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        framework.ShouldNotBeNull();
        framework.ShouldNotContain("windows", Case.Insensitive);
    }

    [Fact]
    public void Core_ContainsNoPInvoke()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var pinvokes = s_core.GetTypes()
            .SelectMany(t => t.GetMethods(all))
            .Where(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .ToList();

        pinvokes.ShouldBeEmpty();
    }
}
