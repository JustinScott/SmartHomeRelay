using System.Net;
using System.Net.Sockets;
using System.Text;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Hardware;

namespace SmartHomeRelay.Platform.Tests.Hardware;

/// <summary>The data.json request and login flow against a fake server (spec 5.8). Runs on any OS.</summary>
public sealed class LhmHttpTests : IDisposable
{
    private const string Json = """{ "Text": "Sensor", "Children": [] }""";
    private const string Challenge = "WWW-Authenticate: Basic realm=\"Libre Hardware Monitor\"\r\n";
    private static readonly LhmCredentials Login = new("relay", "s3cret");
    private static readonly string ExpectedAuth = "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("relay:s3cret"));

    private readonly WindowsLibreHardwareMonitorEndpoint _endpoint = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _endpoint.Dispose();

    [Fact]
    public async Task NoLogin_Ok_ReturnsBody_AndOnlyAsksForDataJson()
    {
        await using var server = new FakeHttpServer(_ => FakeHttpServer.Response(200, Json));

        var result = await _endpoint.GetDataAsync(server.Port, null, Ct);

        result.Outcome.ShouldBe(LhmHttpOutcome.Ok);
        result.Body.ShouldBe(Json);
        server.Requests.Single().ShouldStartWith("GET /data.json HTTP/1.1");
    }

    [Fact]
    public async Task Login_ChallengeThenAccepted_SendsBasicAuthOnlyAfterTheChallenge()
    {
        await using var server = new FakeHttpServer(head => head.Contains(ExpectedAuth, StringComparison.Ordinal)
            ? FakeHttpServer.Response(200, Json)
            : FakeHttpServer.Response(401, "no", "text/html", Challenge));

        var result = await _endpoint.GetDataAsync(server.Port, Login, Ct);

        result.Outcome.ShouldBe(LhmHttpOutcome.Ok);
        server.Requests.Count.ShouldBe(2);
        server.Requests[0].ShouldNotContain("Authorization", Case.Insensitive);
    }

    [Fact]
    public async Task Login_Rejected_IsLoginFailed()
    {
        await using var server = new FakeHttpServer(_ => FakeHttpServer.Response(401, "no", "text/html", Challenge));

        (await _endpoint.GetDataAsync(server.Port, Login, Ct)).Outcome.ShouldBe(LhmHttpOutcome.LoginFailed);
    }

    [Fact]
    public async Task Challenge_WithoutConfiguredLogin_IsLoginRequired()
    {
        await using var server = new FakeHttpServer(_ => FakeHttpServer.Response(401, "no", "text/html", Challenge));

        (await _endpoint.GetDataAsync(server.Port, null, Ct)).Outcome.ShouldBe(LhmHttpOutcome.LoginRequired);
    }

    [Fact]
    public async Task ServerThatNeverAsksForTheLogin_IsNotGivenThePassword()
    {
        // Anti-spoofing: LHM with authentication on always challenges first.
        await using var server = new FakeHttpServer(_ => FakeHttpServer.Response(200, Json));

        var result = await _endpoint.GetDataAsync(server.Port, Login, Ct);

        result.Outcome.ShouldBe(LhmHttpOutcome.LoginNotRequested);
        server.Requests.ShouldAllBe(r => !r.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NotJson_IsUnexpected()
    {
        await using var server = new FakeHttpServer(_ => FakeHttpServer.Response(200, "<html/>", "text/html"));

        var result = await _endpoint.GetDataAsync(server.Port, null, Ct);

        result.Outcome.ShouldBe(LhmHttpOutcome.UnexpectedResponse);
        result.Detail.ShouldNotBeNull().ShouldContain("text/html");
    }

    [Fact]
    public async Task OversizedBody_IsRejected()
    {
        var huge = new string('x', ILibreHardwareMonitorEndpoint.MaxBodyBytes + 10);
        await using var server = new FakeHttpServer(_ => FakeHttpServer.Response(200, huge));

        (await _endpoint.GetDataAsync(server.Port, null, Ct)).Outcome.ShouldBe(LhmHttpOutcome.TooLarge);
    }

    [Fact]
    public async Task OversizedBody_WithoutContentLength_IsRejected()
    {
        var huge = new string('x', ILibreHardwareMonitorEndpoint.MaxBodyBytes + 10);
        await using var server = new FakeHttpServer(_ => $"HTTP/1.1 200 X\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n{huge}");

        (await _endpoint.GetDataAsync(server.Port, null, Ct)).Outcome.ShouldBe(LhmHttpOutcome.TooLarge);
    }

    [Fact]
    public async Task NothingListening_IsNotListening()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        (await _endpoint.GetDataAsync(port, null, Ct)).Outcome.ShouldBe(LhmHttpOutcome.NotListening);
    }

    [Fact]
    public async Task NoAnswer_TimesOut()
    {
        await using var server = new FakeHttpServer(_ => string.Empty);

        (await _endpoint.GetDataAsync(server.Port, null, Ct)).Outcome.ShouldBe(LhmHttpOutcome.Timeout);
    }
}
