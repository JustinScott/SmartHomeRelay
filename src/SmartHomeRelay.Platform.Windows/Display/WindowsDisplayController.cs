using System.Runtime.InteropServices;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Input;
using SmartHomeRelay.Platform.Windows.Interop;
using SmartHomeRelay.Platform.Windows.Presence;

namespace SmartHomeRelay.Platform.Windows.Display;

/// <summary>
/// Monitor off/on (spec 5.3). Off broadcasts <c>SC_MONITORPOWER</c>; on nudges the mouse one pixel and back
/// (the dependable way to wake a display on current Windows) and records the nudge so Presence ignores it.
/// </summary>
public sealed class WindowsDisplayController(SyntheticInputTracker syntheticInput) : IDisplayController
{
    private const nint MonitorOff = 2;
    private const uint BroadcastTimeoutMs = 2_000;

    /// <inheritdoc />
    public Task TurnOffAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                // A broadcast waits for every top-level window; skip hung ones and cap the wait.
                NativeMethods.SendMessageTimeout(
                    NativeMethods.HwndBroadcast,
                    NativeMethods.WmSysCommand,
                    NativeMethods.ScMonitorPower,
                    MonitorOff,
                    NativeMethods.SmtoAbortIfHung,
                    BroadcastTimeoutMs,
                    out _);
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task TurnOnAsync(CancellationToken cancellationToken)
    {
        var before = WindowsIdleTimeProvider.ReadLastInputTick();
        var after = before;
        syntheticInput.Begin(before);
        try
        {
            InputRecord[] nudge = [Move(1), Move(-1)];
            if (NativeMethods.SendInput((uint)nudge.Length, nudge, Marshal.SizeOf<InputRecord>()) != nudge.Length)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            }

            // Windows updates the last-input time when it processes the input, which can lag SendInput slightly.
            for (var attempt = 0; attempt < 20 && after == before; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
                after = WindowsIdleTimeProvider.ReadLastInputTick();
            }
        }
        finally
        {
            syntheticInput.Complete(after);
        }

        // Also tell Windows a display is wanted, for drivers that ignore the nudge.
        NativeMethods.SetThreadExecutionState(NativeMethods.EsDisplayRequired);
    }

    private static InputRecord Move(int dx) => new()
    {
        Type = InputRecord.TypeMouse,
        Mouse = new MouseInput { Dx = dx, Flags = NativeMethods.MouseEventMove },
    };
}
