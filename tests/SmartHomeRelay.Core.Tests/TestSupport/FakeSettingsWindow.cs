using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

internal sealed class FakeSettingsWindow : ISettingsWindow
{
    public int Opened { get; private set; }

    public void Open() => Opened++;
}
