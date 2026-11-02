using SmartHomeRelay.Platform.Windows.Hosting;

namespace SmartHomeRelay.Platform.Tests.Hosting;

[Trait("Category", "Windows")]
public sealed class SingleInstanceGuardTests
{
    // Unique per test so parallel tests and a running copy of the app never interfere.
    private readonly string _name = "SmartHomeRelay.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void TryAcquire_FirstInstance_ReturnsTrue()
    {
        using var guard = new SingleInstanceGuard(_name);

        guard.TryAcquire().ShouldBeTrue();
    }

    [Fact]
    public void TryAcquire_WhileAnotherInstanceHoldsIt_ReturnsFalse()
    {
        using var primary = new SingleInstanceGuard(_name);
        using var secondary = new SingleInstanceGuard(_name);
        primary.TryAcquire().ShouldBeTrue();

        secondary.TryAcquire().ShouldBeFalse();
    }

    [Fact]
    public void TryAcquire_AfterPrimaryDisposed_ReturnsTrue()
    {
        using (var primary = new SingleInstanceGuard(_name))
        {
            primary.TryAcquire().ShouldBeTrue();
        }

        using var next = new SingleInstanceGuard(_name);
        next.TryAcquire().ShouldBeTrue();
    }

    [Fact]
    public async Task SignalPrimaryInstance_RaisesSecondInstanceStartedInPrimary()
    {
        using var primary = new SingleInstanceGuard(_name);
        using var secondary = new SingleInstanceGuard(_name);
        primary.TryAcquire().ShouldBeTrue();
        var raised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.SecondInstanceStarted += (_, _) => raised.TrySetResult();

        secondary.TryAcquire().ShouldBeFalse();
        secondary.SignalPrimaryInstance();

        // Real OS signal, so this waits on the event rather than a fake clock. Completes in milliseconds.
        await raised.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var guard = new SingleInstanceGuard(_name);
        guard.TryAcquire();

        guard.Dispose();
        Should.NotThrow(guard.Dispose);
    }
}
