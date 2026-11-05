using System.Runtime.InteropServices;
using SmartHomeRelay.Platform.Windows.Interop;
using SmartHomeRelay.Platform.Windows.Interop.CoreAudio;

namespace SmartHomeRelay.Platform.Tests.Display;

/// <summary>Native layouts for SendInput and Core Audio (x64). No Windows APIs called.</summary>
public sealed class DisplayInteropLayoutTests
{
    [Fact]
    public void InputRecord_Is40Bytes_WithTheUnionAtByte8()
    {
        Marshal.SizeOf<MouseInput>().ShouldBe(32);
        Marshal.OffsetOf<InputRecord>(nameof(InputRecord.Mouse)).ToInt32().ShouldBe(8);
        Marshal.SizeOf<InputRecord>().ShouldBe(40);
    }

    [Fact]
    public void MouseInput_ExtraInfoIsPointerAligned()
    {
        Marshal.OffsetOf<MouseInput>(nameof(MouseInput.Flags)).ToInt32().ShouldBe(12);
        Marshal.OffsetOf<MouseInput>(nameof(MouseInput.ExtraInfo)).ToInt32().ShouldBe(24);
    }

    [Fact]
    public void PropVariant_Is24Bytes_WithTheValueAtByte8()
    {
        Marshal.SizeOf<PropVariant>().ShouldBe(24);
        Marshal.OffsetOf<PropVariant>(nameof(PropVariant.Pointer)).ToInt32().ShouldBe(8);
    }

    [Fact]
    public void PropertyKey_Is20Bytes()
    {
        Marshal.SizeOf<PropertyKey>().ShouldBe(20);
    }
}
