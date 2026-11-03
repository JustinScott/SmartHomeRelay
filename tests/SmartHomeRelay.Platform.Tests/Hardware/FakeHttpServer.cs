using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SmartHomeRelay.Platform.Tests.Hardware;

/// <summary>A minimal HTTP/1.1 server on 127.0.0.1 that answers from a callback (no HTTP.sys, no admin).</summary>
internal sealed class FakeHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Func<string, string> _respond;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    /// <param name="respond">Takes the request head (request line + headers), returns the full raw response.</param>
    public FakeHttpServer(Func<string, string> respond)
    {
        _respond = respond;
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> Requests { get; } = [];

    public static string Response(int status, string body, string contentType = "application/json", string extraHeaders = "") =>
        $"HTTP/1.1 {status} X\r\nContent-Type: {contentType}\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n{extraHeaders}Connection: close\r\n\r\n{body}";

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            var stream = client.GetStream();
            var head = await ReadHeadAsync(stream);
            lock (Requests)
            {
                Requests.Add(head);
            }

            var response = _respond(head);
            if (response.Length > 0)
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response), _stop.Token);
            }
            else
            {
                await Task.Delay(Timeout.Infinite, _stop.Token); // Never answer.
            }
        }
    }

    private async Task<string> ReadHeadAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var text = new StringBuilder();
        while (!text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, _stop.Token);
            if (read == 0)
            {
                break;
            }

            text.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        return text.ToString();
    }
}
