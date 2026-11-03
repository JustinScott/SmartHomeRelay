using SmartHomeRelay.Platform.Windows.Power;

namespace SmartHomeRelay.Platform.Tests.Power;

/// <summary>
/// Never performs a power action (AGENTS.md). Only the safe, read-only parts and the exact arguments
/// passed to shutdown.exe are tested.
/// </summary>
public sealed class WindowsPowerControllerTests
{
    [Fact]
    public void ShutdownArguments_ShutdownWithDelay()
    {
        WindowsPowerController.ShutdownArguments("/s", TimeSpan.FromSeconds(60))
            .ShouldBe(["/s", "/t", "60", "/c", WindowsPowerController.ShutdownComment]);
    }

    [Fact]
    public void ShutdownArguments_RestartImmediately()
    {
        WindowsPowerController.ShutdownArguments("/r", TimeSpan.Zero)
            .ShouldBe(["/r", "/t", "0", "/c", WindowsPowerController.ShutdownComment]);
    }

    [Theory]
    [InlineData(-5, "0")]
    [InlineData(600.9, "600")]
    public void ShutdownArguments_DelayIsClampedAndWholeSeconds(double seconds, string expected)
    {
        WindowsPowerController.ShutdownArguments("/s", TimeSpan.FromSeconds(seconds))[2].ShouldBe(expected);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void IsHibernateAvailable_ReturnsWithoutThrowing()
    {
        Should.NotThrow(() => new WindowsPowerController().IsHibernateAvailable);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void Uptime_IsPositiveAndMatchesTickCount()
    {
        var uptime = new WindowsUptimeProvider().GetUptime();

        uptime.ShouldBeGreaterThan(TimeSpan.Zero);
        uptime.ShouldBeLessThanOrEqualTo(TimeSpan.FromMilliseconds(Environment.TickCount64));
    }
}
