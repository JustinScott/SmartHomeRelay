using Microsoft.Win32;
using SmartHomeRelay.Platform.Windows.Hosting;

namespace SmartHomeRelay.Platform.Tests.Hosting;

/// <summary>Spec 7.1: start with Windows. Uses a test key under HKCU, never the real Run key.</summary>
[Trait("Category", "Windows")]
public sealed class WindowsStartupRegistrationTests : IDisposable
{
    private readonly string _keyPath = $@"Software\SmartHomeRelay.Tests\{Guid.NewGuid():N}";
    private readonly string _installed = Path.Combine(Path.GetTempPath(), "shr-installed");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);

    [Fact]
    public void Installed_CreatesAndRemovesTheValue_OnlyWhenNeeded()
    {
        var exe = Path.Combine(_installed, "SmartHomeRelay.exe");
        var registration = new WindowsStartupRegistration(exe, _installed, _keyPath);

        registration.IsInstalled.ShouldBeTrue();
        registration.Apply(true).ShouldBeTrue();
        Read().ShouldBe($"\"{exe}\"");
        registration.Apply(true).ShouldBeFalse("already set");
        registration.Apply(false).ShouldBeTrue();
        Read().ShouldBeNull();
        registration.Apply(false).ShouldBeFalse("already gone");
    }

    [Fact]
    public void NotInstalled_NeverTouchesTheKey()
    {
        var registration = new WindowsStartupRegistration(@"C:\src\SmartHomeRelay\bin\Debug\SmartHomeRelay.exe", _installed, _keyPath);

        registration.IsInstalled.ShouldBeFalse();
        registration.Apply(true).ShouldBeFalse();
        Registry.CurrentUser.OpenSubKey(_keyPath).ShouldBeNull();
    }

    private string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue(WindowsStartupRegistration.ValueName) as string;
    }
}
