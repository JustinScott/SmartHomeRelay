using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Interop;

namespace SmartHomeRelay.Platform.Windows.Hardware;

/// <summary>
/// Reads a user-managed LibreHardwareMonitor on this PC (spec 5.8). Standard user only; the address is
/// always <c>127.0.0.1</c>; only <c>GET /data.json</c> is ever requested.
/// </summary>
public sealed class WindowsLibreHardwareMonitorEndpoint : ILibreHardwareMonitorEndpoint, IDisposable
{
    /// <summary>The System process, which owns HTTP.sys listeners.</summary>
    internal const uint SystemProcessId = 4;

    private const string ProcessName = "LibreHardwareMonitor";

    private readonly HttpClient _http;
    private readonly Lazy<int?> _systemDisk = new(ReadSystemDiskNumber);

    /// <summary>Creates the endpoint.</summary>
    public WindowsLibreHardwareMonitorEndpoint()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false, // Loopback must never go through a proxy.
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None, // No Accept-Encoding: the size cap applies to what is read.
            ConnectTimeout = ILibreHardwareMonitorEndpoint.RequestTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <inheritdoc />
    public LhmEndpointSnapshot Inspect(int port)
    {
        var processes = FindLhmProcesses();
        var owners = ListenerOwners(port);

        if (owners.Count == 0)
        {
            return new(PortOwnerKind.None, null, false, null, processes);
        }

        var others = owners.Where(pid => pid != SystemProcessId).Distinct().ToList();
        if (others.Count == 0)
        {
            return new(PortOwnerKind.HttpSys, null, false, null, processes);
        }

        if (others.Count > 1)
        {
            return new(PortOwnerKind.Unknown, null, false, null, processes);
        }

        var path = TryGetImagePath(others[0]);
        return path is null
            ? new(PortOwnerKind.Unknown, null, false, null, processes)
            : new(PortOwnerKind.Process, path, ProtectedFolder.IsAdminOnly(path), TryGetFileVersion(path), processes);
    }

    /// <inheritdoc />
    public async Task<LhmHttpResult> GetDataAsync(int port, LhmCredentials? credentials, CancellationToken cancellationToken)
    {
        var uri = new Uri($"http://127.0.0.1:{port}/data.json");
        try
        {
            using var first = await SendAsync(uri, null, cancellationToken).ConfigureAwait(false);
            if (first.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (!first.Headers.WwwAuthenticate.Any(h => string.Equals(h.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)))
                {
                    return new(LhmHttpOutcome.UnexpectedResponse, Detail: "401 without a Basic login challenge");
                }

                if (credentials is null)
                {
                    return new(LhmHttpOutcome.LoginRequired);
                }

                var header = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}")));
                using var second = await SendAsync(uri, header, cancellationToken).ConfigureAwait(false);
                return second.StatusCode == HttpStatusCode.Unauthorized
                    ? new(LhmHttpOutcome.LoginFailed)
                    : await ReadAsync(second, cancellationToken).ConfigureAwait(false);
            }

            if (credentials is not null && first.StatusCode == HttpStatusCode.OK)
            {
                // LHM with authentication on always challenges first. Don't hand the password to whatever this is.
                return new(LhmHttpOutcome.LoginNotRequested);
            }

            return await ReadAsync(first, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(LhmHttpOutcome.Timeout);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused } || ex.HttpRequestError == HttpRequestError.ConnectionError)
        {
            return new(LhmHttpOutcome.NotListening);
        }
        catch (HttpRequestException ex)
        {
            return new(LhmHttpOutcome.UnexpectedResponse, Detail: ex.HttpRequestError.ToString());
        }
    }

    /// <inheritdoc />
    public int? GetSystemDiskNumber() => _systemDisk.Value;

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>Converts a port from <c>MIB_TCPROW_OWNER_PID</c> (network byte order in the low 16 bits).</summary>
    internal static int PortFromNetworkOrder(uint value) => (int)(((value & 0xFF) << 8) | ((value >> 8) & 0xFF));

    /// <summary>True for 0.0.0.0 and 127.x.x.x: the listeners a connection to 127.0.0.1 can reach.</summary>
    internal static bool ReachableFromLoopback(uint networkOrderAddress) =>
        networkOrderAddress == 0 || (networkOrderAddress & 0xFF) == 127;

    private async Task<HttpResponseMessage> SendAsync(Uri uri, AuthenticationHeaderValue? authorization, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ILibreHardwareMonitorEndpoint.RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri) { Version = HttpVersion.Version11 };
        request.Headers.Authorization = authorization;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
    }

    private static async Task<LhmHttpResult> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new(LhmHttpOutcome.UnexpectedResponse, Detail: $"HTTP {(int)response.StatusCode}");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return new(LhmHttpOutcome.UnexpectedResponse, Detail: $"content type '{mediaType}'");
        }

        if (response.Content.Headers.ContentLength > ILibreHardwareMonitorEndpoint.MaxBodyBytes)
        {
            return new(LhmHttpOutcome.TooLarge);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ILibreHardwareMonitorEndpoint.RequestTimeout);
        var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[ILibreHardwareMonitorEndpoint.MaxBodyBytes + 1];
            var total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(total), timeout.Token).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > ILibreHardwareMonitorEndpoint.MaxBodyBytes)
                {
                    return new(LhmHttpOutcome.TooLarge);
                }
            }

            return new(LhmHttpOutcome.Ok, Encoding.UTF8.GetString(buffer, 0, total));
        }
    }

    private static List<LhmProcessInfo> FindLhmProcesses()
    {
        var result = new List<LhmProcessInfo>();
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                var path = TryGetImagePath((uint)process.Id);
                result.Add(path is null
                    ? new LhmProcessInfo(null, null, false)
                    : new LhmProcessInfo(path, TryGetFileVersion(path), ProtectedFolder.IsAdminOnly(path)));
            }
        }

        return result;
    }

    private static unsafe List<uint> ListenerOwners(int port)
    {
        var size = 0;
        var owners = new List<uint>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = NativeMethods.GetExtendedTcpTable(0, ref size, false, NativeMethods.AfInet, NativeMethods.TcpTableOwnerPidListener, 0);
            if (result != NativeMethods.ErrorInsufficientBuffer && result != 0)
            {
                return owners;
            }

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = NativeMethods.GetExtendedTcpTable(buffer, ref size, false, NativeMethods.AfInet, NativeMethods.TcpTableOwnerPidListener, 0);
                if (result == NativeMethods.ErrorInsufficientBuffer)
                {
                    continue; // The table grew in between; try again.
                }

                if (result != 0)
                {
                    return owners;
                }

                var count = Marshal.ReadInt32(buffer);
                var rows = buffer + sizeof(uint);
                var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<TcpRowOwnerPid>(rows + (i * rowSize));
                    if (PortFromNetworkOrder(row.LocalPort) == port && ReachableFromLoopback(row.LocalAddr))
                    {
                        owners.Add(row.OwningPid);
                    }
                }

                return owners;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return owners;
    }

    private static unsafe string? TryGetImagePath(uint processId)
    {
        using var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = stackalloc char[1024];
        var size = 1024;
        return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
    }

    private static Version? TryGetFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0 && info.FilePrivatePart == 0
                ? null
                : new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static int? ReadSystemDiskNumber()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root) || root.Length < 2)
        {
            return null;
        }

        const uint shareReadWrite = 0x1 | 0x2;
        const uint openExisting = 3;
        using var volume = NativeMethods.CreateFile($@"\\.\{root[..2]}", 0, shareReadWrite, 0, openExisting, 0, 0);
        if (volume.IsInvalid)
        {
            return null;
        }

        // Fails with ERROR_MORE_DATA for a volume that spans disks: then there is no single system disk.
        return NativeMethods.DeviceIoControl(volume, NativeMethods.IoctlVolumeGetVolumeDiskExtents, 0, 0, out var extents, (uint)Marshal.SizeOf<VolumeDiskExtents>(), out _, 0)
            && extents.NumberOfDiskExtents == 1
            ? (int)extents.Extent.DiskNumber
            : null;
    }
}
