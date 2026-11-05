using SmartHomeRelay.Platform.Windows.Input;

namespace SmartHomeRelay.Platform.Tests.Input;

/// <summary>Monitor on's mouse nudge must not look like the user came back (spec 5.3). Pure logic; any OS.</summary>
public sealed class SyntheticInputTrackerTests
{
    private readonly SyntheticInputTracker _tracker = new();

    [Fact]
    public void NoInjection_LastInputIsUnchanged()
    {
        _tracker.EffectiveLastInput(5_000).ShouldBe(5_000u);
    }

    [Fact]
    public void AfterInjection_IdleIsMeasuredFromTheRealInput()
    {
        _tracker.Begin(1_000);
        _tracker.Complete(9_000);

        _tracker.EffectiveLastInput(9_000).ShouldBe(1_000u);
        _tracker.EffectiveLastInput(9_000).ShouldBe(1_000u, "still ours on later samples");
    }

    [Fact]
    public void DuringInjection_IdleIsMeasuredFromTheRealInput()
    {
        // A Presence sample taken between SendInput and Complete must not see "active".
        _tracker.Begin(1_000);

        _tracker.EffectiveLastInput(9_000).ShouldBe(1_000u);
    }

    [Fact]
    public void RealInputAfterInjection_CountsAndClearsTheInjection()
    {
        _tracker.Begin(1_000);
        _tracker.Complete(9_000);

        _tracker.EffectiveLastInput(9_500).ShouldBe(9_500u);
        _tracker.EffectiveLastInput(9_000).ShouldBe(9_000u, "the injection is forgotten once the user is back");
    }

    [Fact]
    public void InjectionThatDidNotRegister_HidesNothing()
    {
        // E.g. on the secure desktop the last-input time doesn't move.
        _tracker.Begin(1_000);
        _tracker.Complete(1_000);

        _tracker.EffectiveLastInput(4_000).ShouldBe(4_000u);
    }
}
