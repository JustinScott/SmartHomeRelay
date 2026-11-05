# Smart Home Relay

[![CI](https://github.com/JustinScott/SmartHomeRelay/actions/workflows/ci.yml/badge.svg)](https://github.com/JustinScott/SmartHomeRelay/actions/workflows/ci.yml)

A Windows tray app that connects a PC to [Home Assistant](https://www.home-assistant.io/) over MQTT. The PC appears as one device in Home Assistant, with sensors, buttons and notifications, set up through MQTT discovery.

It runs as a standard user and never asks for administrator rights.

> **Status:** in development, built milestone by milestone ([spec section 14](spec.md#14-milestones)). No release yet.

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
| Notifications | Planned (milestone 9) | [5.6](spec.md#56-notifications) |

Shutdown, restart, hibernate and sign out are off until you allow them in `settings.json`.

## Requirements

- Windows 10 1809 or later, or Windows 11 (x64)
- An MQTT broker that Home Assistant uses (for example the Mosquitto add-on), with a username and password
- For building: the .NET 10 SDK (see `global.json`)

## Setup

1. Run `SmartHomeRelay.App.exe`. On first run it creates `%APPDATA%\SmartHomeRelay\settings.json` and opens it.
2. Fill in `mqtt.host`, `mqtt.username` and `mqtt.password`, then save. The password is encrypted for your Windows account (DPAPI) and removed from the file as soon as the app reads it.
3. The PC appears in Home Assistant under **Settings → Devices & services → MQTT**.

The tray icon shows the connection state. Its menu has **Status**, **Settings** (opens `settings.json`), **Reconnect**, **Open logs folder** and **Exit**. A settings window and an installer come in milestone 10.

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

## Documentation

- [spec.md](spec.md): the source of truth for behavior, topics, settings and milestones
- [AGENTS.md](AGENTS.md): rules for contributors and AI coding agents
- [docs/hardware-sensors.md](docs/hardware-sensors.md): installing LibreHardwareMonitor for hardware sensors
- [docs/media-player.md](docs/media-player.md): installing the MQTT Media Player add-on for the media player
- `tools/probe-sensors.ps1`: lists which temperature and power sources a PC exposes to a standard user
