using Microsoft.Extensions.Hosting;

namespace SmartHomeRelay.Core.Tests.TestSupport;

internal sealed class FakeApplicationLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public int StopRequests { get; private set; }

    public void StopApplication() => StopRequests++;
}
