using SmartHomeRelay.Core.Entities;

namespace SmartHomeRelay.Core.Tests.Entities;

public sealed class EntityValidatorTests
{
    private static readonly EntityCommandHandler s_noop = (_, _) => Task.CompletedTask;

    [Fact]
    public void Validate_WellFormedEntities_ReturnsNoErrors()
    {
        EntityDescriptor[] entities =
        [
            new("cpu_usage", "CPU usage", EntityComponent.Sensor),
            new("session_locked", "Session locked", EntityComponent.BinarySensor),
            new("sleep", "Sleep", EntityComponent.Button) { CommandHandler = s_noop },
            new("muted", "Muted", EntityComponent.Switch) { CommandHandler = s_noop },
            new("volume", "Volume", EntityComponent.Number) { CommandHandler = s_noop, Range = new NumberRange(0, 100, 1) },
        ];

        EntityValidator.Validate(entities).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("CPU")]
    [InlineData("cpu-usage")]
    [InlineData("cpu usage")]
    [InlineData("cpu/usage")]
    public void Validate_BadKey_ReturnsError(string key)
    {
        EntityValidator.Validate([new(key, "Name", EntityComponent.Sensor)]).ShouldHaveSingleItem();
    }

    [Fact]
    public void Validate_DuplicateKey_ReturnsError()
    {
        var errors = EntityValidator.Validate(
        [
            new("uptime", "Uptime", EntityComponent.Sensor),
            new("uptime", "Uptime again", EntityComponent.Sensor),
        ]);

        errors.ShouldHaveSingleItem().ShouldContain("more than once");
    }

    [Theory]
    [InlineData(EntityComponent.Button)]
    [InlineData(EntityComponent.Switch)]
    public void Validate_CommandEntityWithoutHandler_ReturnsError(EntityComponent component)
    {
        EntityValidator.Validate([new("thing", "Thing", component)]).ShouldHaveSingleItem().ShouldContain("no command handler");
    }

    [Fact]
    public void Validate_SensorWithHandler_ReturnsError()
    {
        EntityValidator.Validate([new("thing", "Thing", EntityComponent.Sensor) { CommandHandler = s_noop }])
            .ShouldHaveSingleItem().ShouldContain("cannot have a command handler");
    }

    [Fact]
    public void Validate_NumberWithoutRange_ReturnsError()
    {
        EntityValidator.Validate([new("volume", "Volume", EntityComponent.Number) { CommandHandler = s_noop }])
            .ShouldHaveSingleItem().ShouldContain("no range");
    }

    [Theory]
    [InlineData(100, 0, 1)]
    [InlineData(0, 100, 0)]
    public void Validate_InvalidRange_ReturnsError(double min, double max, double step)
    {
        EntityValidator.Validate([new("volume", "Volume", EntityComponent.Number) { CommandHandler = s_noop, Range = new NumberRange(min, max, step) }])
            .ShouldHaveSingleItem().ShouldContain("invalid range");
    }

    [Fact]
    public void Validate_EmptyName_ReturnsError()
    {
        EntityValidator.Validate([new("uptime", " ", EntityComponent.Sensor)]).ShouldHaveSingleItem().ShouldContain("no name");
    }

    [Fact]
    public void Validate_AvailableWhen_SiblingStatus_IsValid()
    {
        EntityDescriptor[] entities =
        [
            new("cpu_temperature", "CPU temperature", EntityComponent.Sensor) { AvailableWhen = new EntityAvailability("hardware_status", ["ok"]) },
            new("hardware_status", "Hardware status", EntityComponent.Sensor),
        ];

        EntityValidator.Validate(entities).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("missing", "ok")]
    [InlineData("press", "ok")] // a button has no state
    [InlineData("hardware_status", "OK'; drop")] // ends up in a Jinja template
    public void Validate_AvailableWhen_Invalid(string statusKey, string state)
    {
        EntityDescriptor[] entities =
        [
            new("cpu_temperature", "CPU temperature", EntityComponent.Sensor) { AvailableWhen = new EntityAvailability(statusKey, [state]) },
            new("hardware_status", "Hardware status", EntityComponent.Sensor),
            new("press", "Press", EntityComponent.Button) { CommandHandler = s_noop },
        ];

        EntityValidator.Validate(entities).ShouldNotBeEmpty();
    }
}
