using Microsoft.Extensions.Time.Testing;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Core.Features.Metrics;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Metrics;

public sealed class MetricsModuleTests : IAsyncDisposable
{
    private readonly FakeMetricsSource _source = new();
    private readonly FakeSettingsState _settings = new(FakeSettingsState.Configured());
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingModuleContext _context = new();
    private MetricsModule _module;

    public MetricsModuleTests()
    {
        _module = new MetricsModule(_source, _settings, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _module.StopAsync(CancellationToken.None);
        _module.Dispose();
    }

    [Fact]
    public void DescribeEntities_OneUsageAndFreeEntityPerFixedDrive_PlusGpu()
    {
        _module.DescribeEntities().Select(e => e.Key).ShouldBe(
        [
            "cpu_usage", "memory_usage", "memory_used_gb",
            "disk_usage_c", "disk_free_c", "disk_usage_d", "disk_free_d",
            "network_down", "network_up", "gpu_usage",
        ]);
    }

    [Fact]
    public void DescribeEntities_NoGpuCounters_NoGpuEntity()
    {
        // Spec 5.5: gpu_usage is not created if the counters are unavailable.
        _source.IsGpuAvailable = false;
        _module = new MetricsModule(_source, _settings, _time);

        _module.DescribeEntities().Select(e => e.Key).ShouldNotContain("gpu_usage");
    }

    [Fact]
    public void DescribeEntities_DrivesFixedAtStartup()
    {
        var keys = _module.DescribeEntities().Select(e => e.Key).ToList();
        _source.Drives.Add(new DriveSpace('E', FakeMetricsSource.Gb, FakeMetricsSource.Gb));

        _module.DescribeEntities().Select(e => e.Key).ShouldBe(keys);
    }

    [Fact]
    public async Task Start_PublishesMemoryAndDiskRightAway_CpuAndNetworkAfterWarmUp()
    {
        await _module.StartAsync(_context, Ct);

        _context.Latest(MetricsKeys.MemoryUsage).ShouldBe("50");
        _context.Latest(MetricsKeys.MemoryUsedGb).ShouldBe("8.0");
        _context.Latest(MetricsKeys.DiskUsage('C')).ShouldBe("50");
        _context.Latest(MetricsKeys.DiskFree('C')).ShouldBe("250.0");
        _context.Latest(MetricsKeys.DiskFree('D')).ShouldBe("900.0");
        _context.Latest(MetricsKeys.CpuUsage).ShouldBeNull("CPU needs two samples");

        _source.RunCpu(MetricsModule.WarmUp, 25);
        _source.Transfer(bytesReceived: 2_500_000, bytesSent: 250_000); // 10 and 1 Mbit/s over 2 s
        await AdvanceUntilAsync(() => _context.Latest(MetricsKeys.CpuUsage) is not null, MetricsModule.WarmUp);

        _context.Latest(MetricsKeys.CpuUsage).ShouldBe("25");
        _context.Latest(MetricsKeys.NetworkDown).ShouldBe("10.0");
        _context.Latest(MetricsKeys.NetworkUp).ShouldBe("1.0");
        _context.Latest(MetricsKeys.GpuUsage).ShouldBe("10");
    }

    [Fact]
    public async Task Sample_SmallChanges_AreNotPublished()
    {
        await StartAndWarmUpAsync(cpuPercent: 25);
        var count = _context.Published.Count;

        // Next sample: CPU 26% (+1), memory +0.05 GB, network steady at ~10.4/1.0 Mbit/s (+4%), disk unchanged.
        _source.RunCpu(Interval, 26);
        _source.Transfer(bytesReceived: 39_000_000, bytesSent: 3_750_000);
        _source.Memory = _source.Memory with { AvailableBytes = _source.Memory.AvailableBytes - (FakeMetricsSource.Gb / 20) };
        await AdvanceOneIntervalAsync();

        _context.Published.Skip(count).ShouldBeEmpty("nothing moved past the deadband");
    }

    [Fact]
    public async Task Sample_LargeChange_IsPublished()
    {
        await StartAndWarmUpAsync(cpuPercent: 25);

        _source.RunCpu(Interval, 80);
        await AdvanceUntilAsync(() => _context.Latest(MetricsKeys.CpuUsage) == "80", Interval);

        _context.Count(MetricsKeys.CpuUsage).ShouldBe(2);
    }

    [Fact]
    public async Task Sample_UsesConfiguredInterval()
    {
        var settings = FakeSettingsState.Configured();
        settings.Features.Metrics.IntervalSeconds = 60;
        _settings.Set(settings);
        await StartAndWarmUpAsync(cpuPercent: 25);

        _source.RunCpu(TimeSpan.FromSeconds(30), 90);
        _time.Advance(TimeSpan.FromSeconds(30));
        await Task.Yield();
        _context.Count(MetricsKeys.CpuUsage).ShouldBe(1, "not sampled at 30 s when the interval is 60 s");

        await AdvanceUntilAsync(() => _context.Count(MetricsKeys.CpuUsage) == 2, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Suspend_StopsSampling_AndResumeStartsFreshBaselines()
    {
        await StartAndWarmUpAsync(cpuPercent: 25);
        await _module.OnSuspendingAsync(Ct);
        var count = _context.Published.Count;

        _source.RunCpu(TimeSpan.FromHours(1), 90);
        for (var i = 0; i < 5; i++)
        {
            _time.Advance(Interval);
            await Task.Yield();
        }

        _context.Published.Count.ShouldBe(count);

        // Adapter counters reset across sleep: must not produce a negative or huge rate.
        _source.Adapters[0] = _source.Adapters[0] with { BytesReceived = 0, BytesSent = 0 };
        await _module.OnResumedAsync(Ct);
        _source.RunCpu(MetricsModule.WarmUp, 60);
        await AdvanceUntilAsync(() => _context.Latest(MetricsKeys.CpuUsage) == "60", MetricsModule.WarmUp);
        // No traffic since wake: 0, not a negative or huge rate from counters that reset.
        _context.Latest(MetricsKeys.NetworkDown).ShouldBe("0.0");
    }

    [Fact]
    public async Task Sample_GpuReadFails_OtherMetricsStillPublished()
    {
        _source.ThrowOnGpu = new InvalidOperationException("counters broken");
        await _module.StartAsync(_context, Ct);

        _source.RunCpu(MetricsModule.WarmUp, 40);
        await AdvanceUntilAsync(() => _context.Latest(MetricsKeys.CpuUsage) == "40", MetricsModule.WarmUp);

        _context.Latest(MetricsKeys.GpuUsage).ShouldBeNull();
        _context.Latest(MetricsKeys.NetworkDown).ShouldNotBeNull();
    }

    [Fact]
    public async Task Sample_DriveDisappeared_IsSkipped()
    {
        await _module.StartAsync(_context, Ct);
        _source.Drives.RemoveAll(d => d.Letter == 'D');

        _source.RunCpu(MetricsModule.WarmUp, 40);
        await AdvanceUntilAsync(() => _context.Latest(MetricsKeys.CpuUsage) == "40", MetricsModule.WarmUp);

        _context.Count(MetricsKeys.DiskFree('D')).ShouldBe(1, "only the value from before it disappeared");
    }

    [Fact]
    public void IsEnabled_FollowsMetricsToggle()
    {
        var settings = FakeSettingsState.Configured();
        _module.IsEnabled(settings).ShouldBeTrue();

        settings.Features.Metrics.Enabled = false;
        _module.IsEnabled(settings).ShouldBeFalse();
    }

    private static TimeSpan Interval => TimeSpan.FromSeconds(30);

    private async Task StartAndWarmUpAsync(double cpuPercent)
    {
        await _module.StartAsync(_context, Ct);
        _source.RunCpu(MetricsModule.WarmUp, cpuPercent);
        _source.Transfer(2_500_000, 250_000);
        await AdvanceUntilAsync(() => _context.Latest(MetricsKeys.CpuUsage) is not null, MetricsModule.WarmUp);
    }

    private async Task AdvanceOneIntervalAsync()
    {
        var gpuReads = _source.GpuReads;
        await Eventually.TrueAsync(() => _source.GpuReads > gpuReads, "the next sample runs", () => _time.Advance(TimeSpan.FromSeconds(1)));
    }

    /// <summary>Advances the clock in small steps (at most <paramref name="max"/> beyond the first check) until the condition holds.</summary>
    private Task AdvanceUntilAsync(Func<bool> condition, TimeSpan max)
    {
        var advanced = TimeSpan.Zero;
        return Eventually.TrueAsync(condition, "the sample publishes", () =>
        {
            if (advanced < max)
            {
                _time.Advance(TimeSpan.FromSeconds(1));
                advanced += TimeSpan.FromSeconds(1);
            }
        });
    }
}
