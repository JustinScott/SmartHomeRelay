using SmartHomeRelay.Platform.Windows.Presence;

namespace SmartHomeRelay.Platform.Tests.Presence;

/// <summary>The real Windows idle-time and session providers (spec 5.1).</summary>
[Trait("Category", "Windows")]
public sealed class WindowsPresenceProviderTests
{
    [Fact]
    public void GetIdleTime_ReturnsNonNegativeTime()
    {
        new WindowsIdleTimeProvider().GetIdleTime().ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void GetIdleTime_IsBelowTimeSinceBoot()
    {
        var sinceBoot = TimeSpan.FromMilliseconds(Environment.TickCount64);

        new WindowsIdleTimeProvider().GetIdleTime().ShouldBeLessThanOrEqualTo(sinceBoot);
    }

    [Theory]
    [InlineData(10_000u, 4_000u, 6_000)]
    [InlineData(5u, uint.MaxValue - 4, 10)] // GetTickCount wrapped around after ~49.7 days of uptime
    [InlineData(1_000u, 1_000u, 0)]
    public void Elapsed_HandlesTickCountWrapAround(uint now, uint lastInput, double expectedMs)
    {
        WindowsIdleTimeProvider.Elapsed(now, lastInput).ShouldBe(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Fact]
    public void IsLocked_ReturnsWithoutThrowing()
    {
        // Running tests means someone (or a CI agent) is at an unlocked or non-interactive session; the
        // value itself can't be asserted portably, but the WTS call and buffer handling must work.
        Should.NotThrow(() => new WindowsSessionStateProvider().IsLocked());
    }

    [Fact]
    public void GetUserName_ReturnsCurrentUser()
    {
        new WindowsSessionStateProvider().GetUserName().ShouldBe(Environment.UserName);
    }
}
