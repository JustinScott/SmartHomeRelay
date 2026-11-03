using SmartHomeRelay.Core.Features.Hardware;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Hardware;

/// <summary>Every saved data.json sample (one per supported LHM version) must parse and map (spec 5.8).</summary>
public sealed class LhmFixtureTests
{
    public static TheoryData<string> Fixtures => [.. LhmFixtures.All.Select(f => Path.GetFileName(f))];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_ParsesAndEveryCuratedSensorIsFound(string file)
    {
        var parsed = LhmDataParser.Parse(File.ReadAllText(Path.Combine(LhmFixtures.Directory, file)));

        parsed.IsValid.ShouldBeTrue(parsed.Problem);
        foreach (var definition in HardwareKeys.Sensors)
        {
            var sensor = LhmSensorMapper.Find(definition.Key, parsed.Sensors, new Dictionary<string, string>(), systemDiskNumber: 0);
            sensor.ShouldNotBeNull(definition.Key);
            sensor.Unit.ShouldBe(definition.LhmUnit, definition.Key);
            sensor.Value.ShouldNotBeNull(definition.Key);
        }
    }

    [Fact]
    public void TestedVersion_HasAFixture()
    {
        var tested = LhmCompatibility.Format(LhmCompatibility.TestedVersion);

        LhmFixtures.All.ShouldContain(f => Path.GetFileName(f).Contains($"-{tested}", StringComparison.Ordinal), $"add a data.json sample for LHM {tested}");
    }
}
