using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

internal sealed class FakeHostNameProvider(string hostName) : IHostNameProvider
{
    public string GetHostName() => hostName;
}
