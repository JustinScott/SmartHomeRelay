using SmartHomeRelay.Core.Features.Presence;

namespace SmartHomeRelay.Core.Tests.Features.Presence;

public sealed class PresenceCalculatorTests
{
    private static readonly TimeSpan s_threshold = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(false, 0, true, PresenceStates.Active)]
    [InlineData(false, 299, true, PresenceStates.Active)]
    [InlineData(false, 300, false, PresenceStates.Idle)] // exactly at the threshold counts as idle
    [InlineData(false, 7200, false, PresenceStates.Idle)]
    [InlineData(true, 0, false, PresenceStates.Locked)] // locked wins, even with recent input
    [InlineData(true, 7200, false, PresenceStates.Locked)]
    public void Calculate_DerivesUserActiveAndState(bool locked, int idleSeconds, bool expectedActive, string expectedState)
    {
        var snapshot = PresenceCalculator.Calculate(locked, "Justin", TimeSpan.FromSeconds(idleSeconds), s_threshold);

        snapshot.Locked.ShouldBe(locked);
        snapshot.UserActive.ShouldBe(expectedActive);
        snapshot.State.ShouldBe(expectedState);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4.9, 0)]
    [InlineData(5, 5)]
    [InlineData(12, 10)]
    [InlineData(299.9, 295)]
    [InlineData(300, 300)] // 5 minutes: switches to 1-minute steps
    [InlineData(359, 300)]
    [InlineData(360, 360)]
    [InlineData(28_830, 28_800)] // 8 hours overnight: one update a minute
    public void ReportIdleSeconds_UsesFineThenCoarseSteps(double idleSeconds, long expected)
    {
        PresenceCalculator.ReportIdleSeconds(TimeSpan.FromSeconds(idleSeconds)).ShouldBe(expected);
    }

    [Fact]
    public void ReportIdleSeconds_Negative_IsZero()
    {
        PresenceCalculator.ReportIdleSeconds(TimeSpan.FromSeconds(-3)).ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Calculate_NoUserName_PublishesNone(string userName)
    {
        PresenceCalculator.Calculate(false, userName, TimeSpan.Zero, s_threshold).UserName.ShouldBe("none");
    }

    [Fact]
    public void ToPayloads_UsesHomeAssistantFormats()
    {
        var payloads = PresenceCalculator.Calculate(true, "Justin", TimeSpan.FromSeconds(42), s_threshold).ToPayloads();

        payloads.ShouldBe(new Dictionary<string, string>
        {
            [PresenceKeys.SessionLocked] = "ON",
            [PresenceKeys.LoggedInUser] = "Justin",
            [PresenceKeys.IdleSeconds] = "40",
            [PresenceKeys.UserActive] = "OFF",
            [PresenceKeys.PresenceState] = "locked",
        }, ignoreOrder: true);
    }
}
