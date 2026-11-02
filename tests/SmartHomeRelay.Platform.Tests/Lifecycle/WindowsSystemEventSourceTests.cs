using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Lifecycle;

namespace SmartHomeRelay.Platform.Tests.Lifecycle;

/// <summary>
/// The real Windows event source. Never actually suspends or shuts down the machine: transitions are
/// raised through the same code path the OS handler uses.
/// </summary>
[Trait("Category", "Windows")]
public sealed class WindowsSystemEventSourceTests
{
    [Fact]
    public void Construct_SubscribesAndDisposesWithoutError()
    {
        var source = new WindowsSystemEventSource(NullLogger<WindowsSystemEventSource>.Instance);

        Should.NotThrow(source.Dispose);
        Should.NotThrow(source.Dispose); // twice is fine
    }

    [Fact]
    public void IsNetworkAvailable_ReturnsWithoutThrowing()
    {
        using var source = new WindowsSystemEventSource(NullLogger<WindowsSystemEventSource>.Instance);

        Should.NotThrow(() => source.IsNetworkAvailable);
    }

    [Fact]
    public void RaiseSuspending_WaitsForDeferredWork()
    {
        using var source = new WindowsSystemEventSource(NullLogger<WindowsSystemEventSource>.Instance, TimeSpan.FromSeconds(5));
        var finished = false;
        source.Suspending += (_, e) => e.Defer(Task.Run(async () =>
        {
            await Task.Delay(100);
            finished = true;
        }));

        var completed = source.RaiseSuspending();

        completed.ShouldBeTrue();
        finished.ShouldBeTrue("Windows must not continue before the deferred work (publishing offline) is done");
    }

    [Fact]
    public void RaiseSuspending_StuckWork_GivesUpAfterBudget()
    {
        var budget = TimeSpan.FromMilliseconds(300);
        using var source = new WindowsSystemEventSource(NullLogger<WindowsSystemEventSource>.Instance, budget);
        source.Suspending += (_, e) => e.Defer(new TaskCompletionSource().Task); // never completes

        var stopwatch = Stopwatch.StartNew();
        var completed = source.RaiseSuspending();

        completed.ShouldBeFalse();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "it must not hold up the suspend");
    }

    [Fact]
    public void RaiseSuspending_FailedWork_StillCompletes()
    {
        using var source = new WindowsSystemEventSource(NullLogger<WindowsSystemEventSource>.Instance, TimeSpan.FromSeconds(5));
        source.Suspending += (_, e) => e.Defer(Task.FromException(new InvalidOperationException("boom")));

        source.RaiseSuspending().ShouldBeTrue();
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionLock, SessionSwitchKind.Lock)]
    [InlineData(SessionSwitchReason.SessionUnlock, SessionSwitchKind.Unlock)]
    [InlineData(SessionSwitchReason.SessionLogon, SessionSwitchKind.Logon)]
    [InlineData(SessionSwitchReason.SessionLogoff, SessionSwitchKind.Logoff)]
    [InlineData(SessionSwitchReason.ConsoleConnect, SessionSwitchKind.ConsoleConnect)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect, SessionSwitchKind.ConsoleDisconnect)]
    [InlineData(SessionSwitchReason.RemoteConnect, SessionSwitchKind.RemoteConnect)]
    [InlineData(SessionSwitchReason.RemoteDisconnect, SessionSwitchKind.RemoteDisconnect)]
    [InlineData(SessionSwitchReason.SessionRemoteControl, SessionSwitchKind.Other)]
    public void Map_TranslatesEveryReason(SessionSwitchReason reason, SessionSwitchKind expected)
    {
        WindowsSystemEventSource.Map(reason).ShouldBe(expected);
    }
}
