using SmartHomeRelay.Core.Configuration;

namespace SmartHomeRelay.Core.Tests.TestSupport;

internal sealed class FakeSettingsState(AppSettings settings) : ISettingsState
{
    private SettingsSnapshot _current = new(settings, []);

    public event EventHandler? Changed;

    public SettingsSnapshot Current => Volatile.Read(ref _current);

    public void Set(AppSettings settings, IReadOnlyList<string>? errors = null)
    {
        Volatile.Write(ref _current, new SettingsSnapshot(settings, errors ?? []));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Valid settings pointing at a broker.</summary>
    public static AppSettings Configured(string host = "broker.local")
    {
        var settings = TestSettings.Valid();
        settings.Mqtt.Host = host;
        settings.Device.Id = "DESKTOP-ABC123";
        settings.Device.Name = "Desk PC";
        return settings;
    }
}
