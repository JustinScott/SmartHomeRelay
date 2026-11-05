using Microsoft.Extensions.Time.Testing;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features.Notifications;
using SmartHomeRelay.Core.Mqtt;
using SmartHomeRelay.Core.Net;
using SmartHomeRelay.Core.Tests.TestSupport;

namespace SmartHomeRelay.Core.Tests.Features.Notifications;

/// <summary>Spec 5.6.</summary>
public sealed class NotificationsModuleTests : IAsyncDisposable
{
    private readonly FakeToastPresenter _toasts = new();
    private readonly ImageDownloader _downloader = new();
    private readonly TempAppPaths _paths = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly FakeSettingsState _settings = new(FakeSettingsState.Configured());
    private readonly RecordingModuleContext _context = new();
    private readonly NotificationsModule _module;

    public NotificationsModuleTests()
    {
        _module = new NotificationsModule(_settings, _toasts, _downloader, _paths, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _module.StopAsync(CancellationToken.None);
        _module.Dispose();
        _paths.Dispose();
    }

    [Fact]
    public void NotifyEntity_UsesTheContractTopic()
    {
        var entity = _module.DescribeEntities().ShouldHaveSingleItem();
        var settings = FakeSettingsState.Configured();
        var topics = new MqttTopics(settings.Mqtt, settings.Device);

        entity.Component.ShouldBe(EntityComponent.Notify);
        topics.CommandFor(entity).ShouldBe("shr/DESKTOP-ABC123/commands/notification");
        Snapshot.ShouldMatchApproved(DiscoveryPayloadBuilder.Build(entity, topics, settings.Device, "1.0.0"), "notifications_notification");
    }

    [Fact]
    public async Task Command_IsQueued_AndShownInTheBackground()
    {
        await _module.StartAsync(_context, Ct);

        await Handler("""{ "Title": "Doorbell", "Message": "Motion", "ActivationURL": "https://ha.local/cam" }""");

        await Eventually.TrueAsync(() => _toasts.Shown.Count == 1, "the toast is shown");
        var toast = _toasts.Shown.Single();
        toast.Title.ShouldBe("Doorbell");
        toast.LaunchUrl!.AbsoluteUri.ShouldBe("https://ha.local/cam");
        toast.Expiration.ShouldBe(TimeSpan.FromDays(1));
        toast.Silent.ShouldBeFalse();
    }

    [Fact]
    public async Task HeroImage_IsDownloadedWithTheSettingsLimits_AndSavedLocally()
    {
        await _module.StartAsync(_context, Ct);

        await _module.ShowAsync("""{ "Title": "T", "HeroImageURL": "https://ha.local/snap.jpg" }""", Ct);

        var request = _downloader.Requests.ShouldHaveSingleItem();
        request.Limits.MaxBytes.ShouldBe(NotificationsSettings.DefaultImageMaxBytes);
        request.Limits.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
        request.Limits.AcceptedTypePrefix.ShouldBe("image/");
        var path = _toasts.Shown.Single().HeroImagePath.ShouldNotBeNull();
        path.ShouldStartWith(_module.ImageDirectory);
        File.ReadAllBytes(path).Length.ShouldBe(3);
    }

    [Fact]
    public async Task HeroImage_DownloadFails_ToastStillShows()
    {
        _downloader.Reject = new DownloadRejectedException("The server answered 404.");
        await _module.StartAsync(_context, Ct);

        await _module.ShowAsync("""{ "Title": "T", "HeroImageURL": "https://ha.local/snap.jpg" }""", Ct);

        _toasts.Shown.ShouldHaveSingleItem().HeroImagePath.ShouldBeNull();
    }

    [Fact]
    public async Task HeroImage_UnsupportedType_ToastStillShows()
    {
        _downloader.ContentType = "image/webp";
        await _module.StartAsync(_context, Ct);

        await _module.ShowAsync("""{ "Title": "T", "HeroImageURL": "https://ha.local/snap.webp" }""", Ct);

        _toasts.Shown.ShouldHaveSingleItem().HeroImagePath.ShouldBeNull();
    }

    [Fact]
    public async Task LocalPath_IsNeverRead_AndNothingIsDownloaded()
    {
        await _module.StartAsync(_context, Ct);

        await _module.ShowAsync("""{ "Title": "T", "HeroImageURL": "C:\\Windows\\win.ini" }""", Ct);

        _downloader.Requests.ShouldBeEmpty();
        _toasts.Shown.ShouldHaveSingleItem().HeroImagePath.ShouldBeNull();
    }

    [Fact]
    public async Task BadPayload_ShowsNothing_AndDoesNotThrow()
    {
        await _module.StartAsync(_context, Ct);

        await Should.NotThrowAsync(() => _module.ShowAsync("{ broken", Ct));

        _toasts.Shown.ShouldBeEmpty();
    }

    [Fact]
    public async Task PresenterFailure_IsLogged_NotThrown()
    {
        _toasts.ThrowOnShow = new InvalidOperationException("Windows refused.");
        await _module.StartAsync(_context, Ct);

        await Should.NotThrowAsync(() => _module.ShowAsync("Hello", Ct));
    }

    [Fact]
    public async Task Buttons_OpenTheirUrls()
    {
        await _module.StartAsync(_context, Ct);

        await _module.ShowAsync("""{ "Title": "T", "Buttons": [ { "Label": "Live", "URL": "https://ha.local/live" } ] }""", Ct);

        var button = _toasts.Shown.Single().Buttons.ShouldHaveSingleItem();
        button.OpensUrl.ShouldBeTrue();
        button.Arguments.ShouldBe("https://ha.local/live");
    }

    [Fact]
    public async Task OldImages_AreSweptAtStart()
    {
        Directory.CreateDirectory(_module.ImageDirectory);
        var old = Path.Combine(_module.ImageDirectory, "old.jpg");
        var fresh = Path.Combine(_module.ImageDirectory, "fresh.jpg");
        File.WriteAllBytes(old, [1]);
        File.WriteAllBytes(fresh, [1]);
        File.SetLastWriteTimeUtc(old, _time.GetUtcNow().UtcDateTime - TimeSpan.FromDays(2));

        await _module.StartAsync(_context, Ct);

        File.Exists(old).ShouldBeFalse();
        File.Exists(fresh).ShouldBeTrue("its toast can still be opened");
    }

    [Fact]
    public void ImageStore_AcceptsOnlyWhatToastsShow()
    {
        HeroImageStore.IsSupported("image/jpeg").ShouldBeTrue();
        HeroImageStore.IsSupported("image/png").ShouldBeTrue();
        HeroImageStore.IsSupported("image/svg+xml").ShouldBeFalse();
    }

    private Task Handler(string payload) => _module.DescribeEntities().Single().CommandHandler!(payload, Ct);

    /// <summary>A downloader whose content type can be chosen.</summary>
    private sealed class ImageDownloader : SmartHomeRelay.Core.Abstractions.IHttpDownloader
    {
        public System.Collections.Concurrent.ConcurrentQueue<(Uri Url, DownloadLimits Limits)> Requests { get; } = new();

        public DownloadRejectedException? Reject { get; set; }

        public string ContentType { get; set; } = "image/jpeg";

        public Task<DownloadedFile> DownloadAsync(Uri url, DownloadLimits limits, CancellationToken cancellationToken)
        {
            Requests.Enqueue((url, limits));
            return Reject is { } ex ? Task.FromException<DownloadedFile>(ex) : Task.FromResult(new DownloadedFile(new byte[3], ContentType));
        }
    }
}
