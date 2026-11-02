using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Tests.Mqtt;

public sealed class ReconnectBackoffTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(5, 32)]
    [InlineData(6, 60)]
    [InlineData(50, 60)]
    public void GetDelay_NoJitter_DoublesUpToSixtySeconds(int attempt, double expectedSeconds)
    {
        var backoff = new ReconnectBackoff(new FixedRandom(0.5)); // 0.5 means no jitter

        backoff.GetDelay(attempt).TotalSeconds.ShouldBe(expectedSeconds, tolerance: 0.0001);
    }

    [Fact]
    public void GetDelay_JitterExtremes_StayWithinTwentyPercent()
    {
        new ReconnectBackoff(new FixedRandom(0.0)).GetDelay(2).TotalSeconds.ShouldBe(3.2, tolerance: 0.0001);
        new ReconnectBackoff(new FixedRandom(0.999999)).GetDelay(2).TotalSeconds.ShouldBe(4.8, tolerance: 0.001);
    }

    [Fact]
    public void GetDelay_JitterNeverExceedsCap()
    {
        new ReconnectBackoff(new FixedRandom(0.999999)).GetDelay(10).ShouldBe(ReconnectBackoff.Maximum);
    }

    [Fact]
    public void GetDelay_RealRandom_AlwaysWithinBounds()
    {
        var backoff = new ReconnectBackoff(new Random(1234));
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var delay = backoff.GetDelay(attempt);
            delay.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.8));
            delay.ShouldBeLessThanOrEqualTo(ReconnectBackoff.Maximum);
        }
    }

    [Fact]
    public void GetDelay_NegativeAttempt_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ReconnectBackoff(new Random()).GetDelay(-1));
    }

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }
}
