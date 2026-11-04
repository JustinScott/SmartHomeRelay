using System.Text.Json;
using SmartHomeRelay.Core.Features.Hardware;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Hardware;

/// <summary>
/// Every saved data.json sample (one per supported LHM version and board) must parse, and each curated
/// sensor must map to the sensor listed in the sample's <c>.expected.json</c> (spec 5.8).
/// </summary>
public sealed class LhmFixtureTests
{
    public static TheoryData<string> Fixtures => [.. LhmFixtures.All.Select(f => Path.GetFileName(f))];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_ParsesAndMapsAsExpected(string file)
    {
        var expected = Expected(file);
        var parsed = LhmDataParser.Parse(File.ReadAllText(Path.Combine(LhmFixtures.Directory, file)));

        parsed.IsValid.ShouldBeTrue(parsed.Problem);
        foreach (var definition in HardwareKeys.Sensors)
        {
            var sensor = LhmSensorMapper.Find(definition.Key, parsed.Sensors, expected.Overrides, expected.SystemDisk);
            sensor.ShouldNotBeNull(definition.Key);
            sensor.SensorId.ShouldBe(expected.Sensors[definition.Key], definition.Key);
            sensor.Unit.ShouldBe(definition.LhmUnit, definition.Key);
            sensor.Value.ShouldNotBeNull(definition.Key);
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_WithoutOverrides_LeavesOnlyTheKnownGapsUnmatched(string file)
    {
        // The rules never guess: e.g. a board whose fans are only "Fan #1".."Fan #5" gets no cpu_fan.
        var expected = Expected(file);
        var parsed = LhmDataParser.Parse(File.ReadAllText(Path.Combine(LhmFixtures.Directory, file)));

        var unmatched = HardwareKeys.SensorKeys
            .Where(key => LhmSensorMapper.Find(key, parsed.Sensors, new Dictionary<string, string>(), expected.SystemDisk) is null)
            .ToList();

        unmatched.ShouldBe(expected.UnmatchedWithoutOverrides, ignoreOrder: true);
    }

    [Fact]
    public void TestedVersion_HasARealCapture()
    {
        var tested = LhmCompatibility.Format(LhmCompatibility.TestedVersion);

        LhmFixtures.All.ShouldContain(
            f => Path.GetFileName(f).EndsWith($"-{tested}.json", StringComparison.Ordinal) && !Path.GetFileName(f).StartsWith("synthetic", StringComparison.Ordinal),
            $"add a real data.json capture for LHM {tested}");
    }

    [Fact]
    public void Box1_ControlsWithoutAReading_ParseAsNoValue()
    {
        // Real LHM output: fan controls the board doesn't report read " %".
        var sensors = LhmDataParser.Parse(LhmFixtures.Box1).Sensors;

        var control = sensors.Single(s => s.SensorId == "/lpc/it8688e/0/control/0");
        control.Value.ShouldBeNull();
        control.Unit.ShouldBe("%");
    }

    private static FixtureExpectation Expected(string file)
    {
        var path = Path.Combine(LhmFixtures.Directory, Path.ChangeExtension(file, ".expected.json"));
        File.Exists(path).ShouldBeTrue($"add {Path.GetFileName(path)} beside {file}");
        return JsonSerializer.Deserialize<FixtureExpectation>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private sealed record FixtureExpectation(
        int SystemDisk,
        Dictionary<string, string> Overrides,
        Dictionary<string, string> Sensors,
        List<string> UnmatchedWithoutOverrides);
}
