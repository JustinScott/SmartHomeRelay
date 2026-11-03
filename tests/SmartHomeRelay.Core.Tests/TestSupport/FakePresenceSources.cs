using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>Settable idle time and session state for presence tests.</summary>
internal sealed class FakePresenceSources : IIdleTimeProvider, ISessionStateProvider
{
    public TimeSpan Idle { get; set; }

    public bool Locked { get; set; }

    public string UserName { get; set; } = "Justin";

    public Exception? ThrowOnNextIdleRead { get; set; }

    public int IsLockedCalls { get; private set; }

    public TimeSpan GetIdleTime()
    {
        if (ThrowOnNextIdleRead is { } ex)
        {
            ThrowOnNextIdleRead = null;
            throw ex;
        }

        return Idle;
    }

    public bool IsLocked()
    {
        IsLockedCalls++;
        return Locked;
    }

    public string GetUserName() => UserName;
}
