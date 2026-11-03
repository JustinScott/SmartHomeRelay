using System.ComponentModel;
using System.Runtime.InteropServices;
using SmartHomeRelay.Core.Abstractions;
using SmartHomeRelay.Platform.Windows.Interop;

namespace SmartHomeRelay.Platform.Windows.Presence;

/// <summary>Idle time from <c>GetLastInputInfo</c>: input in this session only (spec 5.1).</summary>
public sealed class WindowsIdleTimeProvider : IIdleTimeProvider
{
    /// <inheritdoc />
    public TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return Elapsed(unchecked((uint)Environment.TickCount), info.Time);
    }

    /// <summary>
    /// Milliseconds between two 32-bit tick counts. Unsigned subtraction is correct across the ~49.7-day
    /// wrap-around of GetTickCount.
    /// </summary>
    internal static TimeSpan Elapsed(uint now, uint lastInput) => TimeSpan.FromMilliseconds(unchecked(now - lastInput));
}
