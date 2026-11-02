using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

internal sealed class FakeUserNotifier : IUserNotifier
{
    public List<(string Title, string Message)> Notifications { get; } = [];

    public void Notify(string title, string message) => Notifications.Add((title, message));
}
