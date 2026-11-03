using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Interop;

namespace SmartHomeRelay.Platform.Windows.Power;

/// <summary>
/// Power actions for a standard (non-elevated) user (spec 5.2).
/// Shutdown, restart, sign-out, and cancel use Windows' own <c>shutdown.exe</c> with fixed arguments: it
/// shows the standard countdown notice and handles the shutdown privilege itself. Lock, sleep, and
/// hibernate call the Win32 APIs directly.
/// </summary>
public sealed class WindowsPowerController : IPowerController
{
    /// <summary>Shown in Windows' countdown notice.</summary>
    internal const string ShutdownComment = "Requested by Home Assistant (Smart Home Relay)";

    // shutdown /a with nothing pending: "Unable to abort the system shutdown because no shutdown was in progress."
    private const int ErrorNoShutdownInProgress = 1116;

    private static readonly TimeSpan s_processTimeout = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    public bool IsHibernateAvailable => NativeMethods.IsPwrHibernateAllowed();

    /// <inheritdoc />
    public Task ShutdownAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        RunShutdownExeAsync(ShutdownArguments("/s", delay), cancellationToken);

    /// <inheritdoc />
    public Task RestartAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        RunShutdownExeAsync(ShutdownArguments("/r", delay), cancellationToken);

    /// <inheritdoc />
    public Task CancelShutdownAsync(CancellationToken cancellationToken) =>
        RunShutdownExeAsync(["/a"], cancellationToken, ErrorNoShutdownInProgress);

    /// <inheritdoc />
    public Task SignOutAsync(CancellationToken cancellationToken) =>
        RunShutdownExeAsync(["/l"], cancellationToken);

    /// <inheritdoc />
    public Task SleepAsync(CancellationToken cancellationToken) => SuspendAsync(hibernate: false);

    /// <inheritdoc />
    public Task HibernateAsync(CancellationToken cancellationToken) => SuspendAsync(hibernate: true);

    /// <inheritdoc />
    public Task LockAsync(CancellationToken cancellationToken)
    {
        if (!NativeMethods.LockWorkStation())
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return Task.CompletedTask;
    }

    /// <summary>Arguments for a delayed shutdown or restart. Fixed values only; nothing from MQTT.</summary>
    internal static IReadOnlyList<string> ShutdownArguments(string action, TimeSpan delay)
    {
        var seconds = (int)Math.Clamp(delay.TotalSeconds, 0, 315_360_000); // shutdown.exe's maximum /t
        return [action, "/t", seconds.ToString(CultureInfo.InvariantCulture), "/c", ShutdownComment];
    }

    private static Task SuspendAsync(bool hibernate) =>
        // SetSuspendState blocks until the PC wakes up, so keep it off the caller's thread.
        Task.Run(() =>
        {
            if (!NativeMethods.SetSuspendState(hibernate, force: false, wakeupEventsDisabled: false))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        });

    private static async Task RunShutdownExeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken, int? acceptableExitCode = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("shutdown.exe did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(s_processTimeout);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

        if (process.ExitCode != 0 && process.ExitCode != acceptableExitCode)
        {
            throw new InvalidOperationException(
                $"shutdown.exe {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.");
        }
    }
}
