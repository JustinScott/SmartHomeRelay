# Smart Home Relay

[![CI](https://github.com/JustinScott/SmartHomeRelay/actions/workflows/ci.yml/badge.svg)](https://github.com/JustinScott/SmartHomeRelay/actions/workflows/ci.yml)

A Windows tray app that connects a PC to [Home Assistant](https://www.home-assistant.io/) over MQTT. The PC appears as one device in Home Assistant, with sensors, buttons and notifications, set up through MQTT discovery.

It runs as a standard user and never asks for administrator rights.

> **Status:** version 1.0.0, all v1 milestones built ([spec section 14](spec.md#14-milestones)). Installers are on the [Releases page](https://github.com/JustinScott/SmartHomeRelay/releases).

## What it does

| Feature | Home Assistant entities | Spec |
|---|---|---|
| Presence | Presence state, session locked, user active, idle time, signed-in user | [5.1](spec.md#51-presence) |
| Power | Shutdown, restart, cancel shutdown, sleep, hibernate, lock, sign out; last boot, uptime | [5.2](spec.md#52-power) |
| Metrics | CPU, memory, disk, network, GPU usage; CPU power; GPU temperature and power level | [5.5](spec.md#55-system-metrics) |
| Hardware sensors (optional) | CPU temperature, CPU fan, SSD temperature, from a [LibreHardwareMonitor you install](docs/hardware-sensors.md) | [5.8](spec.md#58-hardware-sensors-librehardwaremonitor--milestone-6) |
| Display | Monitor off, monitor on | [5.3](spec.md#53-display) |
| Audio | Volume (slider), mute, microphone mute, output device picker | [5.4](spec.md#54-audio) |
| Media player (optional) | A `media_player` that mirrors what Windows' media controls show, with play/pause, next, previous, seek, volume and mute, and plays text-to-speech and media from Home Assistant over your music (other apps are lowered meanwhile). Needs the [MQTT Media Player add-on](docs/media-player.md) from HACS | [5.9](spec.md#59-media-player-via-the-mqtt-media-player-add-on--milestone-8) |
| Notifications | Windows notifications from Home Assistant: a `notify` entity for `notify.send_message`, or JSON with a title, image, click link and up to 5 buttons ([how to send them](docs/notifications.md)) | [5.6](spec.md#56-notifications) |

Shutdown, restart, hibernate and sign out are off until you allow them in the settings window (**Features → Power**).

## Requirements

- Windows 10 1809 or later, or Windows 11 (x64)
- An MQTT broker that Home Assistant uses (for example the Mosquitto add-on), with a username and password
- For building: the .NET 10 SDK (see `global.json`)

## Setup

1. Download `SmartHomeRelay-<version>-setup.exe` from the [latest release](https://github.com/JustinScott/SmartHomeRelay/releases/latest) and run it. It installs for your Windows account only (to `%LOCALAPPDATA%\Programs\SmartHomeRelay`), so it never asks for administrator rights. The installer isn't code-signed, so Windows SmartScreen may warn the first time: choose **More info → Run anyway**.
2. On first start the settings window opens. On **Connection**, enter the broker's address, user name and password, click **Test connection**, then **Save**. The password is encrypted for your Windows account (DPAPI).
3. The PC appears in Home Assistant under **Settings → Devices & services → MQTT**.

The tray icon shows the connection state. Its menu has **Status**, **Settings** (or double-click the icon), **Reconnect**, **Open logs folder** and **Exit**. Smart Home Relay starts when you sign in; turn that off under **General** in the settings window. Rare options (MQTT topics, download limits, the hardware sensor list) are in `%APPDATA%\SmartHomeRelay\settings.json`; **General → Open settings.json** opens it.

**Upgrading:** run the newer installer; it closes the app, keeps your settings, and you start it again from the last page. **Uninstalling:** Settings → Apps in Windows. Your settings and logs are kept (delete `%APPDATA%\SmartHomeRelay` and `%LOCALAPPDATA%\SmartHomeRelay` to remove them too).

Optional: [hardware sensors with LibreHardwareMonitor](docs/hardware-sensors.md) (CPU temperature, fans) and the [media player](docs/media-player.md) (text-to-speech and media on the PC).

## Building and testing

```
dotnet build SmartHomeRelay.sln -warnaserror
dotnet test SmartHomeRelay.sln --filter "Category!=Windows&Category!=MqttBroker"
dotnet test SmartHomeRelay.sln --filter "Category!=MqttBroker"
dotnet test SmartHomeRelay.sln --filter "Category=MqttBroker"
```

1. Fast unit tests. They run on any OS.
2. Adds the Windows-only integration tests.
3. Needs a Mosquitto broker. `tests/brokers/start-test-brokers.sh` starts the two test brokers on Linux; CI runs these in a Linux job.

## Releasing

1. Set the new version in `Directory.Build.props` (it flows to the app, the installer and Home Assistant's software version) and commit.
2. Tag the commit and push the tag: `git tag v1.0.1` then `git push origin v1.0.1`.
3. CI builds and tests as usual, then publishes a GitHub Release with the installer. It refuses a tag that doesn't match the version.

Every CI run also uploads the installer as an artifact. To build it locally: install [Inno Setup 6](https://jrsoftware.org/isinfo.php), then

```
dotnet publish src/SmartHomeRelay.App/SmartHomeRelay.App.csproj -c Release -r win-x64 --self-contained -o publish
"%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 installer\SmartHomeRelay.iss
```

## Documentation

- [spec.md](spec.md): the source of truth for behavior, topics, settings and milestones
- [AGENTS.md](AGENTS.md): rules for contributors and AI coding agents
- [docs/hardware-sensors.md](docs/hardware-sensors.md): installing LibreHardwareMonitor for hardware sensors
- [docs/media-player.md](docs/media-player.md): installing the MQTT Media Player add-on for the media player
- [docs/notifications.md](docs/notifications.md): sending notifications to the PC from Home Assistant
- `tools/probe-sensors.ps1`: lists which temperature and power sources a PC exposes to a standard user

## License

[MIT](LICENSE). Use it, change it, and share it; fixes and additions are welcome as pull requests.
