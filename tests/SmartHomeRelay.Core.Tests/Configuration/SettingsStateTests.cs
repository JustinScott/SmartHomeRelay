using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Configuration;

public sealed class SettingsStateTests : IDisposable
{
    private readonly TempAppPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public async Task StartAsync_ValidFile_HasNoErrors()
    {
        _paths.WriteSettingsFile("""{ "mqtt": { "host": "broker.local" } }""");
        using var context = new SettingsTestContext(_paths);

        await context.State.StartAsync(TestContext.Current.CancellationToken);

        context.State.Current.IsValid.ShouldBeTrue();
        context.State.Current.Settings.Mqtt.Host.ShouldBe("broker.local");
    }

    [Fact]
    public async Task StartAsync_NoFile_UsesDefaultsWithHostName()
    {
        using var context = new SettingsTestContext(_paths);

        await context.State.StartAsync(TestContext.Current.CancellationToken);

        context.State.Current.IsValid.ShouldBeTrue();
        context.State.Current.Settings.Device.Id.ShouldBe(SettingsTestContext.HostName);
        context.State.Current.Settings.Mqtt.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public async Task StartAsync_MalformedJson_ReportsSyntaxErrorAndFallsBackToDefaults()
    {
        _paths.WriteSettingsFile("""{ "mqtt": { "host": "broker.local" """);
        using var context = new SettingsTestContext(_paths);

        await context.State.StartAsync(TestContext.Current.CancellationToken);

        var snapshot = context.State.Current;
        snapshot.IsValid.ShouldBeFalse();
        snapshot.Errors.ShouldContain(e => e.StartsWith("settings.json is not valid JSON", StringComparison.Ordinal));
        snapshot.Settings.Mqtt.Host.ShouldBeEmpty();
        snapshot.Settings.Device.Id.ShouldBe(SettingsTestContext.HostName);
    }

    [Fact]
    public async Task StartAsync_ValueOfWrongType_ReportsErrorInsteadOfThrowing()
    {
        _paths.WriteSettingsFile("""{ "mqtt": { "host": "broker.local", "port": "not-a-number" } }""");
        using var context = new SettingsTestContext(_paths);

        await context.State.StartAsync(TestContext.Current.CancellationToken);

        var snapshot = context.State.Current;
        snapshot.IsValid.ShouldBeFalse();
        snapshot.Errors.ShouldContain(e => e.StartsWith("settings.json has a value of the wrong type", StringComparison.Ordinal));
        snapshot.Settings.Mqtt.Port.ShouldBe(MqttSettings.DefaultPort);
    }

    [Fact]
    public async Task StartAsync_OutOfRangeValue_ReportsValidationError()
    {
        _paths.WriteSettingsFile("""{ "mqtt": { "port": 0 } }""");
        using var context = new SettingsTestContext(_paths);

        await context.State.StartAsync(TestContext.Current.CancellationToken);

        context.State.Current.Errors.ShouldContain(e => e.StartsWith("mqtt.port", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refresh_RaisesChanged()
    {
        using var context = new SettingsTestContext(_paths);
        await context.State.StartAsync(TestContext.Current.CancellationToken);
        var raised = 0;
        context.State.Changed += (_, _) => raised++;

        context.State.Refresh();

        raised.ShouldBe(1);
    }

    [Fact]
    public void Current_BeforeStart_ReturnsValidDefaults()
    {
        using var context = new SettingsTestContext(_paths);

        context.State.Current.IsValid.ShouldBeTrue();
        context.State.Current.Settings.Device.Id.ShouldBe(SettingsTestContext.HostName);
    }
}
