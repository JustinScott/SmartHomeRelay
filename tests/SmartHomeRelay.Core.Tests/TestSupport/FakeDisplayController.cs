using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

internal sealed class FakeDisplayController : IDisplayController
{
    public List<string> Calls { get; } = [];

    public Task TurnOffAsync(CancellationToken cancellationToken)
    {
        Calls.Add("off");
        return Task.CompletedTask;
    }

    public Task TurnOnAsync(CancellationToken cancellationToken)
    {
        Calls.Add("on");
        return Task.CompletedTask;
    }
}
