using SmartHomeRelay.Core.Features.Hardware;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Hardware;

public sealed class LhmDataParserTests
{
    [Fact]
    public void Parse_Box1_FindsSensorsUnderTheirHardware()
    {
        var result = LhmDataParser.Parse(LhmFixtures.Synthetic);

        result.IsValid.ShouldBeTrue(result.Problem);
        var tctl = result.Sensors.Single(s => s.SensorId == "/amdcpu/0/temperature/2");
        tctl.HardwareId.ShouldBe("/amdcpu/0");
        tctl.Name.ShouldBe("Core (Tctl/Tdie)");
        tctl.Type.ShouldBe("Temperature");
        tctl.Value.ShouldBe(47.3);
        tctl.Unit.ShouldBe("°C");

        // Nested hardware: the Super I/O chip under the motherboard.
        result.Sensors.Single(s => s.SensorId == "/lpc/nct6798d/0/fan/1").HardwareId.ShouldBe("/lpc/nct6798d/0");
    }

    [Fact]
    public void Parse_SensorWithoutReading_HasNoValue()
    {
        var hotSpot = LhmDataParser.Parse(LhmFixtures.Synthetic).Sensors.Single(s => s.SensorId == "/gpu-amd/0/temperature/2");

        hotSpot.Value.ShouldBeNull();
        hotSpot.Unit.ShouldBe("°C");
    }

    [Theory]
    [InlineData("47.3 °C", 47.3, "°C")]
    [InlineData("47,3 °C", 47.3, "°C")] // LHM formats with the PC's culture
    [InlineData("-5.0 °C", -5.0, "°C")]
    [InlineData("\u22125,0 °C", -5.0, "°C")] // sv-SE minus sign
    [InlineData("1250 RPM", 1250.0, "RPM")]
    [InlineData("0.950", 0.95, "")] // Factor: no unit
    public void TryParseRawValue_ReadsNumberAndUnit(string raw, double expected, string unit)
    {
        LhmDataParser.TryParseRawValue(raw, out var value, out var parsedUnit).ShouldBeTrue();

        value.ShouldBe(expected);
        parsedUnit.ShouldBe(unit);
    }

    [Theory]
    [InlineData(" °C", "°C")]
    [InlineData("", "")]
    public void TryParseRawValue_NoReading_IsNullNotAnError(string raw, string unit)
    {
        LhmDataParser.TryParseRawValue(raw, out var value, out var parsedUnit).ShouldBeTrue();

        value.ShouldBeNull();
        parsedUnit.ShouldBe(unit);
    }

    [Theory]
    [InlineData("1.2.3 °C")]
    [InlineData("abc °C")]
    [InlineData("1e309 °C")]
    public void TryParseRawValue_Garbage_IsRejected(string raw)
    {
        LhmDataParser.TryParseRawValue(raw, out var value, out _).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[]", "no top-level Children")]
    [InlineData("""{ "Text": "Sensor", "Children": [] }""", "no sensors")]
    [InlineData("""{ "Text": "Sensor", "Children": [ { "Text": "x" } ] }""", "no Text or Children")]
    [InlineData("""{ "Text": "Sensor", "Children": [ { "Text": "t", "Children": [], "SensorId": 5 } ] }""", "SensorId")]
    public void Parse_UnexpectedShape_IsReportedNotGuessed(string json, string problem)
    {
        var result = LhmDataParser.Parse(json);

        result.IsValid.ShouldBeFalse();
        result.Problem!.ShouldContain(problem);
        result.Sensors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("RawValue")]
    [InlineData("Type")]
    public void Parse_SensorMissingAField_IsAFormatProblem(string property)
    {
        // E.g. LHM before 0.9.5 had no RawValue: never fall back to the display Value.
        var result = LhmDataParser.Parse(LhmFixtures.Without(LhmFixtures.Synthetic, "/amdcpu/0/load/0", property));

        result.IsValid.ShouldBeFalse();
        result.Problem!.ShouldContain(property);
    }

    [Fact]
    public void Parse_TooDeep_IsRejected()
    {
        var json = string.Concat(Enumerable.Repeat("""{ "Text": "n", "Children": [""", 40)) + string.Concat(Enumerable.Repeat("]}", 40));

        LhmDataParser.Parse(json).IsValid.ShouldBeFalse();
    }
}
