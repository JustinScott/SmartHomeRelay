using System.Runtime.InteropServices;
using SmartHomeRelay.Platform.Windows.Interop;
using SmartHomeRelay.Platform.Windows.Presence;

namespace SmartHomeRelay.Platform.Tests.Presence;

/// <summary>
/// Regression: SessionFlags was read at byte 12, which is SessionState (0 = active), so an active,
/// unlocked session was reported as locked. These pin the native layout of WTSINFOEXW (x64).
/// No Windows APIs are called, so these run on any OS.
/// </summary>
public sealed class WtsInfoExLayoutTests
{
    [Fact]
    public void Level1Data_StartsAtByte8_BecauseOfItsEightByteFields()
    {
        Marshal.OffsetOf<WtsInfoEx>(nameof(WtsInfoEx.Data)).ToInt32().ShouldBe(8);
    }

    [Fact]
    public void SessionFlags_IsAtByte16_NotByte12()
    {
        var offset = Marshal.OffsetOf<WtsInfoEx>(nameof(WtsInfoEx.Data)).ToInt32()
            + Marshal.OffsetOf<WtsInfoExLevel1>(nameof(WtsInfoExLevel1.SessionFlags)).ToInt32();

        offset.ShouldBe(16);
    }

    [Fact]
    public void Size_MatchesNativeWtsInfoExW()
    {
        // 4 (Level) + 4 padding + 224 (WTSINFOEX_LEVEL1_W: 3 ints, 72 WCHARs, 4 padding, 5 LARGE_INTEGERs, 6 DWORDs).
        Marshal.SizeOf<WtsInfoEx>().ShouldBe(232);
    }

    [Theory]
    [InlineData(0, true)]   // WTS_SESSIONSTATE_LOCK
    [InlineData(1, false)]  // WTS_SESSIONSTATE_UNLOCK
    [InlineData(-1, false)] // WTS_SESSIONSTATE_UNKNOWN
    public void IsLockedFlag_InterpretsSessionFlags(int flags, bool expected)
    {
        WindowsSessionStateProvider.IsLockedFlag(flags).ShouldBe(expected);
    }

    [Fact]
    public void Parse_BufferForActiveUnlockedSession_IsNotLocked()
    {
        // The exact bytes Windows returns for an active (SessionState 0), unlocked (SessionFlags 1) session.
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<WtsInfoEx>());
        try
        {
            Marshal.StructureToPtr(new WtsInfoEx { Level = 1, Data = new WtsInfoExLevel1 { SessionId = 1, SessionState = 0, SessionFlags = 1, WinStationName = "Console", UserName = "Justin", DomainName = "BOX1" } }, buffer, false);

            Marshal.ReadInt32(buffer, 12).ShouldBe(0, "byte 12 is SessionState: reading it as SessionFlags was the bug");
            var parsed = Marshal.PtrToStructure<WtsInfoEx>(buffer);
            WindowsSessionStateProvider.IsLockedFlag(parsed.Data.SessionFlags).ShouldBeFalse();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
