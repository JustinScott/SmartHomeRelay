# Smart Home Relay: Specification

> Project name: **Smart Home Relay** (code namespace `SmartHomeRelay`).
> Evolves the existing prototype **SmartHomeRelay4** (WinUI 3). Section 3.5 describes what carries over and what changes.
> This document is the source of truth for architecture, scope, and conventions. Any LLM or human contributing code should read it fully before making changes, and should update it when a decision changes.

---

## 1. Overview

A Windows tray application that connects a Windows PC to Home Assistant (HA) over MQTT. It:

- **Reports** what the PC is doing (presence, system metrics, audio and display state).
- **Accepts commands** from HA (power actions, display, audio, notifications).
- **Survives** sleep, hibernate, shutdown, and network loss, and resumes working automatically.
- **Starts automatically** when the user logs in to Windows.

Single primary user (the author), but built cleanly enough to run on any Windows 10 (1809+) or Windows 11 machine.

### 1.1 Goals

1. Entities appear in HA automatically via **MQTT Discovery** (no manual YAML).
2. **Modular:** each feature lives in its own folder and files. Adding a feature never requires editing unrelated features.
3. **Testable:** business logic has no dependency on Windows APIs or UI, and is covered by automated tests so regressions are caught.
4. **Reliable:** correct behavior across sleep, hibernate, resume, lock/unlock, and broker or network outages.
5. Follows current C# and .NET best practices.

### 1.2 Non-goals (v1)

- Linux or macOS support.
- Running as a Windows Service (see 3.1 for how the design keeps this possible later).
- Arbitrary command or script execution from HA.
- A polished or themed UI. The UI is a tray icon and one settings window.
- Wake-from-sleep or wake-from-off. This is handled on the HA side (e.g., Wake-on-LAN integration).
- Anything that requires administrator privileges (the app never elevates). Hardware sensors that need a driver come from a LibreHardwareMonitor install the user manages (5.8); the app itself stays a standard-user process.
- MQTT over TLS (deferred; see section 12).
- Auto-update.

---

## 2. Decisions Summary

| Area | Decision |
|---|---|
| Language / runtime | C#, **.NET 10 LTS** (verify at project start; .NET 8 reaches end of support in Nov 2026), `net10.0-windows10.0.17763.0` |
| Process model | **Tray app in the user session**, starts at logon |
| UI framework | **WinForms** (`NotifyIcon` tray + one settings form) |
| Configuration | JSON file edited through the settings window |
| Transport | **MQTT** with HA **MQTT Discovery** |
| Installer | **Inno Setup**, per-user install (no admin required), handles startup registration and in-place upgrades |
| Notifications | Windows toast via `CommunityToolkit.WinUI.Notifications` (hero image supported) |
| MQTT topic contract | **Existing `shr/{host}/...` layout and notification payload fields are kept**, so current HA automations keep working (confirmed) |
| Frigate | **Removed.** HA sends image URLs directly |
| MQTT auth | Username + password, **plain TCP (no TLS in v1)** |
| Elevation | **Never.** The app runs as a standard user; metrics needing admin rights are excluded |
| Updates | **Manual**: download and run the newer installer |
| Hosting / CI | **GitHub** (repo, Actions CI, Releases for installers) |
| Prototype UI/packaging | WinUI 3 + MSIX is **replaced** by WinForms + Inno Setup |
| Tests | xUnit v3 + Shouldly (+ NSubstitute when fakes need it), run in CI on `windows-latest` |
| Scope v1 | Presence, Power, Display, Audio, System metrics, Notifications |

---

## 3. Architecture

### 3.1 Process model

The app runs **inside the interactive user session** as a tray application. This is required to detect lock state and input idle time, turn the monitor off, control audio, and show toasts. A Windows Service cannot do these reliably (session 0 isolation).

**Consequence:** the PC is only visible to HA while a user is logged in. Use Windows auto-login if boot-time availability matters.

**Future-proofing:** all logic lives in `Core` and `Platform.Windows` class libraries with no UI dependency. A service host could reuse them later without a rewrite.

### 3.2 Solution layout

```
SmartHomeRelay/
├── spec.md
├── Directory.Build.props          # shared settings: nullable, analyzers, warnings-as-errors, version
├── Directory.Packages.props       # central NuGet package version management
├── .editorconfig
├── src/
│   ├── SmartHomeRelay.Core/          # NO Windows or UI dependencies. Pure, testable logic.
│   │   ├── Abstractions/          # interfaces for everything platform-specific
│   │   ├── Configuration/         # settings models, validation, load/save
│   │   ├── Mqtt/                  # connection, reconnect, publish/subscribe, discovery builder
│   │   ├── Entities/              # entity descriptors (sensor, binary_sensor, button, switch, number)
│   │   ├── Hosting/               # module registry, lifecycle orchestration, power-event handling
│   │   └── Features/
│   │       ├── Presence/
│   │       ├── Power/
│   │       ├── Display/
│   │       ├── Audio/
│   │       ├── Metrics/
│   │       └── Notifications/
│   ├── SmartHomeRelay.Platform.Windows/   # ALL Windows API code (P/Invoke, WMI, WASAPI, SystemEvents)
│   │   ├── Presence/              # idle time, session state implementations
│   │   ├── Power/
│   │   ├── Display/
│   │   ├── Audio/
│   │   ├── Metrics/
│   │   ├── Notifications/
│   │   └── Interop/               # P/Invoke definitions (prefer CsWin32 source generator)
│   └── SmartHomeRelay.App/           # WinForms entry point
│       ├── Program.cs             # single-instance mutex, host bootstrap, DI composition
│       ├── Tray/                  # NotifyIcon, tray menu
│       └── Settings/              # SettingsForm + presenter/view-model
├── tests/
│   ├── SmartHomeRelay.Core.Tests/            # fast unit tests, no Windows required
│   └── SmartHomeRelay.Platform.Tests/        # Windows-only integration tests, tagged
└── installer/
    └── SmartHomeRelay.iss            # Inno Setup script
```

**Dependency rules (enforced by review and, ideally, an architecture test):**

- `Core` → depends on nothing in this solution.
- `Platform.Windows` → depends on `Core` (implements its interfaces).
- `App` → depends on both; it is the composition root.
- **No P/Invoke or `System.Windows.Forms` references anywhere in `Core`.**
- **No business logic in `App`.** Forms are thin views over testable presenters.

### 3.3 Feature module contract

Every capability is a **module**: a self-contained folder in `Core/Features/<Name>/` that implements one interface. Modules are registered in DI; the host discovers and runs all enabled modules. A module never references another module directly.

Illustrative sketch (exact signatures may evolve, but keep the shape):

```csharp
public interface IFeatureModule
{
    string Id { get; }                                   // e.g. "power"
    bool IsEnabled(AppSettings settings);
    IReadOnlyList<EntityDescriptor> DescribeEntities();  // what appears in HA
    Task StartAsync(IModuleContext context, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

// Optional: implement to react to system power state changes.
public interface IPowerAware
{
    Task OnSuspendingAsync(CancellationToken ct);
    Task OnResumedAsync(CancellationToken ct);
}

public interface IModuleContext
{
    Task PublishStateAsync(string entityKey, string payload, CancellationToken ct);
    ILogger Logger { get; }
    // Commands are routed via the CommandHandler on each EntityDescriptor.
}
```

Behavior of the host (`Core/Hosting/ModuleHost`), implemented in milestone 2:

- `StartAsync` is called once when a module becomes enabled (at startup or after a settings change), not on every reconnect. `StopAsync` is called when it is disabled or the app exits.
- `PublishStateAsync` can be called at any time, including while disconnected. The host keeps the latest value per entity and publishes all of them after every (re)connect and when HA restarts, so modules never track the connection.
- Commands run on background tasks, never inside the MQTT receive callback, so a slow handler never delays other commands. Commands for a disabled module's entities are ignored.
- A module whose entities break the rules (invalid or duplicate key, command entity without a handler, number without a range) is rejected as a whole and logged; the others run normally.

Rules for modules:

- One public type per file. Typical module = `XxxModule.cs` + small collaborators (`XxxStateReader.cs`, `XxxCommandHandler.cs`).
- Platform-specific work goes behind an interface in `Core/Abstractions` (e.g., `IIdleTimeProvider`) with the implementation in `Platform.Windows`.
- A failure in one module must be caught, logged, and must **not** stop other modules (the host isolates each module's start, update loop, and command handling).
- Adding a feature = add a folder + a module class + one DI registration line + tests. No other files change.

### 3.4 Key libraries

| Purpose | Library |
|---|---|
| Hosting, DI, config, logging | `Microsoft.Extensions.Hosting` (Generic Host) |
| MQTT | `MQTTnet` v5, using the plain `IMqttClient` with our own small reconnect supervisor (the prototype's managed client is not carried over; owning the loop makes suspend/resume explicit and testable) |
| Logging sink | `Serilog` (rolling file) via `Serilog.Extensions.Logging` |
| Toast notifications | `CommunityToolkit.WinUI.Notifications` |
| P/Invoke | `Microsoft.Windows.CsWin32` (source-generated) |
| Audio | `NAudio` (WASAPI / MMDevice) |
| CPU, RAM, disk, network | `System.Diagnostics` performance counters / `System.Management` |
| GPU usage | Windows `GPU Engine` performance counters (no admin required) |
| Tests | `xunit`, `NSubstitute`, `Shouldly` (or plain xUnit asserts) |

Package versions are pinned centrally in `Directory.Packages.props`.

### 3.5 Migrating from the prototype (SmartHomeRelay4)

| Prototype | New home |
|---|---|
| `MQTT.cs`: connection, reconnect | `Core/Mqtt` (connection + reconnect supervisor) |
| `MQTT.cs`: `PowerModeChanged` handling | `Core/Hosting` lifecycle orchestration, event source in `Platform.Windows` |
| `MQTT.cs`: `notification` command, toast, image download | `Core/Features/Notifications` (+ toast and image implementations in `Platform.Windows/Notifications`) |
| `MQTT.cs`: `HomeAssistantMQTTDiscovery` | `Core/Mqtt` discovery builder (always republishes) |
| `commands.cs` (`Notification`) | `Features/Notifications/NotificationRequest.cs`, same property names |
| `App.xaml.cs` (hardcoded broker, user, password) | `App/Program.cs` composition root; values come from settings |
| `MainWindow` (test buttons) | Dropped. Replaced by the tray menu and settings form |
| WinUI 3, Windows App SDK, MSIX manifest, H.NotifyIcon | Dropped (WinForms + Inno Setup) |

**Preserve:** the `shr/{host}/status` availability topic, the `commands/notification` command and its fields, hero image via temp file, the rebuild-client-after-resume approach, and the one-day toast expiration.

**Do not run the prototype and the new app at the same time on the same PC.** Both publish to `shr/{host}/status`, so they would overwrite each other's availability, and the prototype's broken discovery can confuse HA.

**Do not carry over (known defects in the prototype):**

1. Clean MQTT disconnect on suspend without publishing `offline`, which leaves HA showing the PC as online while asleep.
2. Discovery that is never called, builds an invalid `device` block, uses availability payloads (`ON`/`OFF`) that don't match the status topic (`online`/`offline`), and skips publishing when a config already exists.
3. `SpinWait.SpinUntil` blocking inside async methods.
4. Unhandled exceptions from malformed notification JSON, and passing a null image URI to the toast builder.
5. Temp image files that are never deleted, a new `HttpClient` per download, and no timeout or size limit.
6. A local-file fallback that concatenates payload text onto a hardcoded UNC path (path traversal risk).
7. Hardcoded broker address, credentials, and Frigate address in source.
8. One class doing connection, power handling, command routing, toasts, downloads, and discovery.

---

## 4. MQTT and Home Assistant Integration

### 4.1 Identity

- `deviceId`: the machine's host name by default (same as the prototype's `Dns.GetHostName()`, so existing topics keep working), configurable. Only letters, digits, `-`, and `_` are allowed, because it is also the `node_id` in HA discovery topics (Windows host names already fit). Immutable once entities exist in HA (changing it creates new entities). A sanitized form is used inside `unique_id` values.
- Entity `unique_id`: `{sanitizedDeviceId}_{entityKey}`, where `sanitizedDeviceId` is the device ID lowercased with every character other than `a-z`, `0-9`, and `_` replaced by `_` (host `DESKTOP-ABC123` gives `desktop_abc123_cpu_usage`). Topics use the device ID exactly as configured. **This rule is permanent**: changing it orphans every HA entity.
- All entities are grouped under one HA **device** (manufacturer `Smart Home Relay`, model `Windows PC`, `sw_version` from assembly version, `identifiers: [deviceId]`).
- MQTT client ID: `shr-{deviceId}`.

### 4.2 Topics

| Purpose | Topic |
|---|---|
| Discovery config | `{discoveryPrefix}/{component}/{deviceId}/{entityKey}/config` (retained) |
| State | `{baseTopic}/{deviceId}/{entityKey}/state` |
| Command | `{baseTopic}/{deviceId}/{entityKey}/set` |
| Availability | `{baseTopic}/{deviceId}/status` (`online` / `offline`, retained). Same topic the prototype uses |
| Notification command | `{baseTopic}/{deviceId}/commands/notification` (prototype-compatible, see 5.6) |

Defaults: `discoveryPrefix = homeassistant`, `baseTopic = shr`.

### 4.3 Behavior

- **Availability:** connect with an LWT of `offline` (retained) on the availability topic. Publish `online` (retained) after connecting. All entities reference this availability topic.
- **Discovery:** publish retained discovery configs for every enabled entity on every (re)connect. Disabled features have their discovery config **cleared** (empty retained payload) so they disappear from HA.
- **HA restart:** subscribe to `{discoveryPrefix}/status`. When HA publishes `online`, republish discovery and full current state.
- **Full state publish** after every (re)connect and every resume.
- **QoS 1** for commands and availability; state is retained where it makes sense (sensors yes, buttons no).
- **Reconnect:** automatic with exponential backoff (1s → 60s cap) and jitter. Never crash on broker unavailability. Log and keep retrying.
- **Protocol:** MQTT 3.1.1, for compatibility with any broker. 3.1.1 has no disconnect reasons, so the last will is only sent when the socket drops uncleanly; every clean disconnect therefore publishes `offline` first.
- **Refused connections:** a broker that refuses the login (wrong user name or password) puts the tray in the error state with a clear log message; the app keeps retrying with backoff. Note: MQTTnet v5 reports a refusal as a result code, not an exception, so the transport must check it.
- **Reconnect on settings change:** only when a connection-related setting changes (host, port, credentials, topics, device ID or name). Toggling a feature republishes discovery without reconnecting.
- **Payload conventions:** plain strings for simple values (`ON`/`OFF`, numbers); JSON only where structured data is needed (notifications).

---

## 5. Features (v1 scope)

Entity keys below are the `entityKey` values. Every module is individually toggleable in settings. Destructive commands are additionally individually toggleable.

### 5.1 Presence

Signals exposed: **session state** and **keyboard/mouse idle time** (per decision).

| Entity | Type | Notes |
|---|---|---|
| `session_locked` | binary_sensor | Locked / unlocked via session-switch events |
| `logged_in_user` | sensor | Current interactive user name; `none` if no one is logged in |
| `idle_seconds` | sensor | Seconds since last keyboard/mouse input (`GetLastInputInfo`), updated every few seconds |
| `user_active` | binary_sensor | `ON` when unlocked **and** idle < threshold |
| `presence_state` | sensor (enum) | `active`, `idle`, `locked` |

- Idle threshold is configurable (default 5 minutes).
- Publish on change only, with a periodic refresh for idle seconds.
- Lock/unlock/logon/logoff/remote-connect events come from `SystemEvents.SessionSwitch`, **not** polling.

Implementation details (milestone 4):

- **Sampling:** idle time is read every 5 seconds (`GetLastInputInfo`). Each value is published only when it changes.
- **`idle_seconds` is reported in steps:** 5-second steps under 5 minutes of idle, then 1-minute steps. It reads 0 while the user is active (so nothing is published), and long idle periods such as overnight produce at most one update a minute. Without steps, each PC would add about 17,000 history rows a day to HA. `user_active` and `presence_state` use the exact idle time, not the stepped value.
- **Lock and unlock publish immediately** (no waiting for the next sample). `Logon` counts as unlocked; other session events don't change the lock state.
- **Initial and post-wake lock state** is read from Windows (`WTSQuerySessionInformation`, `WTSSessionInfoEx`) for the app's session, because a lock can happen around sleep without an event reaching the app. If it can't be read, the app assumes unlocked; the next lock event corrects it.
- **Sleep:** sampling pauses on suspend (`IPowerAware`) and restarts on wake.
- **`session_locked` uses no HA device class.** HA's `lock` class means ON = *unlocked*, the opposite of this entity's name, so it uses an icon (`mdi:monitor-lock`) instead. `user_active` uses device class `occupancy`; `idle_seconds` uses `duration` (s) with state class `measurement`; `presence_state` is an `enum` sensor.
- **`logged_in_user`** is the Windows user name of the session the app runs in (`none` if unavailable).

### 5.2 Power

| Entity | Type | Notes |
|---|---|---|
| `shutdown` | button | Delayed by `shutdownDelaySeconds` (default 60s); Windows shows its standard shutdown countdown |
| `restart` | button | Same delay as `shutdown` |
| `cancel_shutdown` | button | Aborts a pending (delayed) shutdown or restart. Always available with Power enabled (added in milestone 5) |
| `sleep` | button | |
| `hibernate` | button | Only offered if hibernation is enabled on the machine |
| `lock` | button | `LockWorkStation` |
| `sign_out` | button | Suggested extra |
| `last_boot` | sensor (timestamp) | |
| `uptime` | sensor (duration) | |

- Every command is behind an interface (`IPowerController`) so tests never actually shut anything down.
- Shutdown, restart, hibernate, and sign-out are **disabled by default** and must be enabled in settings.
- Sleep and lock are not destructive: they are available whenever the Power feature is enabled and have no separate toggle.
- `shutdownDelaySeconds` (0 to 600, default 60) applies to both shutdown and restart. 0 means immediate.

Implementation details (milestone 5):

- **Disallowed buttons are shown, not hidden.** All buttons always appear in HA; a press of shutdown, restart, hibernate, or sign-out that isn't allowed is ignored and logged with the setting to change. The allow flag is read at press time, so turning it off takes effect immediately. Invalid settings never allow a destructive action.
- **Hibernate** is offered only if hibernation is enabled when the app starts (`IsPwrHibernateAllowed`). Enabling or disabling hibernation in Windows takes effect after an app restart.
- **Only a `PRESS` payload** triggers a button; anything else is ignored.
- **Shutdown, restart, sign-out, and cancel** run Windows' `shutdown.exe` with fixed arguments (`/s` or `/r` with `/t <delay>` and a comment, `/l`, `/a`). It shows the standard countdown notice and needs no elevation. Nothing from the MQTT payload reaches the command line.
- **Sleep and hibernate** call `SetSuspendState`; **lock** calls `LockWorkStation`.
- **`last_boot`** is a UTC timestamp published once at start. **`uptime`** is hours since boot (including sleep), unit `h`, rounded down to one decimal (e.g. `13.6`), published every 5 minutes (and right after wake). Hours rather than seconds so it reads naturally in HA without changing the display unit. Both are diagnostic entities.
- Known caveat: `SetSuspendState` can hibernate instead of sleep on some configurations. Document and test the behavior on the target machine. **Verified on BOX1 (milestone 5): Sleep sleeps (not hibernates).**

### 5.3 Display

| Entity | Type | Notes |
|---|---|---|
| `monitor_off` | button | `SC_MONITORPOWER` broadcast |
| `monitor_on` | button | Simulates tiny input to wake the display |

- Windows cannot reliably report whether the monitor is currently on, so **no monitor-state sensor** in v1.
- Brightness control is **out of scope for v1** (see 5.7).

### 5.4 Audio

| Entity | Type | Notes |
|---|---|---|
| `volume` | number (0–100) | Default output device master volume |
| `muted` | switch | Default output device mute |
| `mic_muted` | switch | Default capture device mute |
| `output_device` | sensor | Name of the current default output device |

- Must handle default-device changes at runtime (headphones plugged in, etc.) via MMDevice notifications.

### 5.5 System Metrics

| Entity | Type | Notes |
|---|---|---|
| `cpu_usage` | sensor (%) | |
| `memory_usage` | sensor (%) | Plus `memory_used_gb` |
| `disk_usage_{drive}` | sensor (%) | One per fixed drive; plus free space in GiB as `disk_free_{drive}` (e.g. `disk_usage_c`, `disk_free_c`) |
| `network_down` / `network_up` | sensor (Mbps) | Aggregate of the active adapter(s) |
| `gpu_usage` | sensor (%) | From Windows GPU Engine performance counters. Not created if the counters are unavailable |
| `cpu_power` | sensor (W) | CPU package power from the Windows "Energy Meter" counters (the CPU's RAPL energy counters). Not created if no package instance exists |
| `gpu_temperature` | sensor (°C) | Hottest GPU, from the Windows adapter perf data Task Manager uses. Not created if no GPU reports it |
| `gpu_power_level` | sensor (%) | GPU power draw as a percentage of the card's maximum (Windows doesn't report watts vendor-neutrally). Highest GPU. Not created if no GPU reports it |

- Update interval configurable (default 30s). Publish only when the value changes meaningfully (small deadband) to limit MQTT chatter.
- Implementation details (milestone 6):
  - **Deadband ("moderate"):** a value is published when CPU/GPU/memory/disk % moves at least 2 points, a size moves at least 0.1 GiB, or a network rate moves at least 10% (and at least 0.1 Mbit/s). The first value is always published. Percentages are whole numbers; sizes and rates have one decimal.
  - **Sizes are GiB** (1024^3 bytes), matching Task Manager and Explorer, and are labeled `GiB` in HA because HA reads `GB` as 10^9 bytes. Keys keep their spec names (`memory_used_gb`).
  - **CPU %** is busy time over total time between samples (`GetSystemTimes`). Windows 11's Task Manager shows "% Processor Utility", which is scaled by clock speed, so the two can differ somewhat.
  - **GPU %** is computed like Task Manager: the "GPU Engine" utilization counter is summed per engine across processes, and the busiest engine of any GPU is reported.
  - **Network** counts only physical Ethernet and Wi-Fi adapters that are up. Virtual (Hyper-V, VMware, VirtualBox, WSL), VPN/tunnel, Bluetooth, and loopback adapters are left out, because their traffic also crosses a physical adapter. A counter that goes backwards (adapter reset) is skipped for that sample.
  - **CPU power** sums the `rapl_package*_pkg` Energy Meter instances (milliwatts → W). Per-core, DRAM, and `_total` instances are ignored. Published as whole watts when it moves at least 10% (and at least 2 W).
  - **GPU temperature and power level** come from `D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERPERFDATA)` per adapter (tenths of °C and tenths of a percent). One card can appear under several adapter IDs and software adapters report nothing, so the highest reported value is used. Temperature is published as whole °C when it moves at least 2 °C; power level uses the percent deadband.
  - **Drive entities** are the fixed drives present at startup; GPU usage, CPU power, GPU temperature, and GPU power level are offered only if Windows reports them at startup. Changes take effect after an app restart.
  - **Timing:** memory and disk are published at start, CPU/CPU power/network/GPU 2 seconds later (they need two samples), then everything every `intervalSeconds` (re-read each time, so a change applies on the next sample). Sampling pauses during sleep and rate baselines restart on wake.
- **No elevation (v1):** the app runs as a standard user and never requests administrator rights. The app never reads anything that needs admin access or a kernel driver itself: notably **CPU temperature** (Windows only exposes ACPI thermal zones, which are fake on many desktops, e.g. 16.9 °C on BOX1), fan speeds, and motherboard sensors. Those come from a user-managed LibreHardwareMonitor through the Hardware feature (5.8). Only metrics available to a standard user are implemented; `tools/probe-sensors.ps1` shows what a given PC exposes.

### 5.6 Notifications

Show Windows toast notifications sent from HA. **This keeps the prototype's contract so existing HA automations keep working.**

- Topic: `{baseTopic}/{deviceId}/commands/notification` (default `shr/{hostname}/commands/notification`).
- JSON payload. Property names are matched **case-insensitively** (`HeroImageURL`, `heroImageUrl`). Note: the prototype's comment claiming snake_case keys work is incorrect, because only letter case is ignored.

```json
{
  "Title": "Doorbell",
  "Message": "Motion detected",
  "HeroImageURL": "https://example/snapshot.jpg",
  "ActivationURL": "https://example/camera-dashboard",
  "Audio": "",
  "Buttons": [
    { "Label": "Live feed", "URL": "https://example/cameras/front_door" },
    { "Label": "Clip", "URL": "https://example/clips/abc123.mp4" }
  ]
}
```

| Field | Behavior |
|---|---|
| `Title`, `Message` | Toast text. If both are empty, the payload is ignored and a warning is logged |
| `HeroImageURL` | `http(s)` URL downloaded to a temp file and shown as the hero image. If the download fails, the toast is still shown without an image |
| `ActivationURL` | Opened when the toast is clicked (protocol activation). Must be an absolute `http` or `https` URL; any other scheme is rejected and logged |
| `Audio` | Reserved, currently ignored |
| `Buttons` | **New, optional.** Up to 5 buttons, each with a `Label` and a `URL`. Clicking a button opens its URL (same URL rules as `ActivationURL`) with no round trip to HA. Buttons with an empty label or invalid URL are dropped; extras beyond 5 are ignored, and both are logged |

**Compatibility with existing automations:** every new field is optional and additive, so any payload that was valid for the prototype is still valid and produces the same toast. The only differences from the prototype:

1. `EventID` (Frigate) is no longer processed. Automations that send it must send `HeroImageURL` / `ActivationURL` instead.
2. `ActivationURL` and button URLs must be `http` or `https`. Any other scheme (e.g., `vlc://`) is rejected and logged. This is intentional.
3. Unknown properties are ignored rather than causing errors.

Reserved for a later version: a button may carry an `ActionID` instead of a `URL` to publish a click event back to HA (see 5.7). Until then, buttons with an `ActionID` are ignored and logged.
| `EventID` | **Removed** (Frigate support dropped). Ignored if present, with a debug log |

Behavior rules:

- Malformed JSON or a bad field must **never throw out of the message handler**. Log and continue.
- Image downloads use a shared `HttpClient` (via `IHttpClientFactory`), a timeout (default 10s), and a size cap (default 10 MB). Temp files are deleted after the toast is dismissed or expires, and a startup sweep removes leftovers.
- `HeroImageURL` must be a full `http` or `https` URL. Anything else (bare filenames, file paths, UNC paths) is ignored and logged, and the toast shows without an image. The prototype's local-file fallback is **removed**; the app never reads files named by an MQTT payload.
- Toast expiration defaults to 1 day (as in the prototype).
- Incoming notifications are placed on a small bounded queue and processed on a background worker, not inside the MQTT message handler. A slow image download must never delay other commands. If the queue is full, the oldest pending notification is dropped and the drop is logged.
- Needs a stable App User Model ID registered by the installer (Start Menu shortcut) so toasts and activation work reliably for this unpackaged app.

### 5.7 Candidate features for later (not v1)

Automatic discovery of all LibreHardwareMonitor sensors, enabled in HA as needed (5.8), Display brightness (internal panels via WMI, external monitors via DDC/CI), toast action buttons that publish click events back to HA, active window/app name, webcam in use, mic in use, media play/pause/next, battery status (laptops), pending Windows updates, screenshot camera entity, launch allowlisted apps, Bluetooth device state, editing `discoveryPrefix` and `baseTopic` in the settings window (an Advanced section with a warning that changing them breaks existing automations).

### 5.8 Hardware sensors (LibreHardwareMonitor) — milestone 6

Reads a **LibreHardwareMonitor (LHM) that the user installs and runs** (elevated, with its PawnIO driver) through LHM's local web server, and publishes selected sensors on the app's own HA device. The app never elevates, ships no driver, and adds no NuGet packages for this (HTTP and JSON come from the BCL). Setup steps for users: `docs/hardware-sensors.md`. Code: `Core/Features/Hardware` (module `hardware`), `Platform.Windows/Hardware`.

- **Off by default** (`features.hardware.enabled`). Use either this feature **or** HA's Libre Hardware Monitor integration, never both (every sensor would appear twice). Turning it off clears its discovery like any feature toggle.
- **Source:** only `GET http://127.0.0.1:{port}/data.json`. The host is fixed to loopback and is not a setting; the request never uses a proxy, follows no redirects, and sends no `Accept-Encoding`. The app never calls LHM's other endpoints (notably `/Sensor?action=Set`, which changes hardware controls). LHM can't be made to listen on loopback only (an address it can't find among the PC's interfaces falls back to all of them), which is why the setup guide blocks the port in the firewall.
- **Entities** (`features.hardware.sensors`, read at startup; a change applies after a restart):

| Key | Unit | Source in LHM (rule) |
|---|---|---|
| `cpu_temperature` | °C | Temperature under `/amdcpu/*` or `/intelcpu/*`, by name: `Core (Tctl/Tdie)`, `CPU Package`, `Core (Tctl)`, `Package`; the copy with a reading wins |
| `cpu_fan` | RPM | Fan under a Super I/O chip (`/lpc/*`) whose name contains `CPU`. No guess if none does: set an override |
| `ssd_temperature` | °C | Temperature of the physical disk holding Windows (`/nvme/{n}`, `/ssd/{n}`, `/hdd/{n}`, with `n` from `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`), `Composite Temperature` first |
| `hardware_status` | — | Diagnostic enum: `ok`, `newer_than_tested`, `not_running`, `login_failed`, `untrusted`, `incompatible` |
| `lhm_version` | — | Diagnostic: the LHM version being read, e.g. `0.9.6` |

- **Overrides:** `features.hardware.sensorOverrides` maps a key to an LHM `SensorId` (e.g. `"cpu_fan": "/lpc/nct6798d/0/fan/1"`) when the rule picks the wrong sensor or none. An override of the wrong sensor type is ignored. Keys stay ours; LHM IDs only appear in settings.
- **Availability:** each sensor's discovery has an `availability` list with `availability_mode: all`: the device `status` topic, and `hardware_status`'s state topic mapped to online for `ok`/`newer_than_tested` (a `value_template`; the states are validated as plain words). So sensors go *unavailable* whenever LHM is unusable while the rest of the device stays online. A sensor with no match or no reading publishes `None` (HA shows *unknown*) and a one-time log hint.
- **Values:** `RawValue` is a string formatted with the PC's culture (`"47,3 °C"`, `"1250 RPM"`, `" °C"` when there is no reading). The number is read with `,` or `.` as the decimal separator and the unit is checked against the expected one (`°C`, `RPM`). Published as whole numbers; deadband 2 °C for temperatures, 5 % (at least 50 RPM) for fans. Sampled every `features.hardware.intervalSeconds` (default 30, 5–3600); paused during sleep.
- **Compatibility:** `data.json` has no version field, so the version is read from the running `LibreHardwareMonitor.exe` (file version, readable by a standard user). `LhmCompatibility` in code: below 0.9.5 → `incompatible` (no stable sensor IDs or `RawValue`; HA's integration has the same cutoff); 0.9.5–0.9.6 → supported; newer → read, with `newer_than_tested`. One saved `data.json` per supported version is a test fixture (`tests/…/Fixtures/Lhm`); supporting a new release = add its capture, run the tests, and move the tested version (code and setup guide) in the same commit.
- **Validation on every read:** HTTP 200, a JSON content type, ≤ 4 MB, ≤ 3 s; tree shape (every node has `Text` and `Children`; every sensor has a `SensorId` starting with `/`, a `Type`, and a `RawValue`; ≤ 32 levels, ≤ 50,000 nodes; at least one sensor); a mapped sensor's unit must be the expected one. Any of these failing sets `incompatible` and publishes nothing. Values outside physical limits (−40 to 150 °C, 0 to 20,000 RPM) are dropped and logged.
- **User notice:** one tray balloon per new problem, never one per read; a problem that clears and comes back is news again. `not_running` is only announced after 2 minutes, because LHM and this app start at the same sign-in. Messages name the detected version and the tested one, e.g. "LibreHardwareMonitor 0.9.8 isn't compatible: its data format changed: … Hardware sensors are paused. Version 0.9.6 is known to work." Details go to the log.
- **Anti-spoofing** (a local program pretending to be LHM), checked before every read:
  1. A running `LibreHardwareMonitor.exe` is required, and every running copy must be in a folder only administrators can change (no write, delete, or permission rights for Everyone, Authenticated Users, Users, Interactive, or the current standard user, on the file or its folder). Otherwise `untrusted`.
  2. The IPv4 listeners on the port that `127.0.0.1` can reach (`GetExtendedTcpTable`, no admin needed): only the System process (PID 4, i.e. HTTP.sys, which needs admin rights or an admin-made URL reservation to register) is trusted; an ordinary process is trusted only if it is `LibreHardwareMonitor.exe` in such a folder; anything else, or more than one other owner, is `untrusted`. Nothing listening → `not_running`. (To confirm on BOX1: LHM's port shows PID 4.)
  3. The version must be readable and at least 0.9.5.
  4. The first request carries no credentials. If a login is configured and the server answers without asking for it (no `401` with a Basic challenge), the source is `untrusted` and the password is never sent.
- **Credentials:** LHM's web server login (`username`, `password`), with the password DPAPI-encrypted like the MQTT password (section 7). Basic auth over loopback only.
- **Later (possible change): automatic discovery of all LHM sensors.** Instead of the curated list, publish every useful LHM sensor (temperatures, fans, power, clocks, loads, voltages; not network adapters, data counters, or fixed thresholds) on the same HA device, with readable names ("RX 6800 XT GPU Hot Spot") and keys derived from LHM's sensor IDs. Most start disabled (`enabled_by_default: false`); a few start on (CPU temperature, GPU hot spot, GPU watts, drive temperatures, spinning fans). Users enable and rename the rest in HA, with no settings to edit (`sensors` and `sensorOverrides` would go away). Needs: entities that can be added while the app runs (today a module's entities are fixed at startup), a cached sensor list for when LHM isn't running at startup, vanished sensors kept as unavailable rather than deleted (so HA customizations survive), and a tray item to remove missing ones. Cost: more MQTT traffic (disabled entities still get states, limited by the deadbands). Deferred by decision; the curated list stays for now.
- **Later (not planned):** a provider interface so other read-only sources (NVIDIA NVML, AMD ADLX) plug in with one class and one registration line, compiled in, never loaded from a folder; and an optional SYSTEM sensor service hosting the LHM library, which would need pinned dependencies, a lock file, and signature, vulnerability and license checks first. Hardware control (fans, power limits) stays out of scope.

---|---|---|
| `cpu_temperature` | °C | CPU package temperature (AMD Tctl/Tdie, Intel CPU Package) |
| `cpu_fan` | RPM | The motherboard fan the user maps as the CPU fan |
| `ssd_temperature` | °C | System drive temperature |
| `hardware_status` | — | Diagnostic: `ok`, `not_running`, `login_failed`, `untrusted`, `newer_than_tested`, `incompatible`; attributes give the detected LHM version and the newest tested version |

- **Availability:** hardware entities use two availability topics with `availability_mode: all` (the device's `status`, and `{baseTopic}/{deviceId}/hardware/status`), so they go *unavailable* when LHM is unusable while the rest of the device stays online. `hardware_status` itself uses only the device topic.
- **Compatibility:** `data.json` has no version field, so the version is read from the running `LibreHardwareMonitor.exe` (file version, readable by a standard user). A table in code: below 0.9.5 → `incompatible` (no stable sensor IDs; HA's integration has the same cutoff); tested range (0.9.5–0.9.6 to start) → supported; newer → tried with `newer_than_tested`. One saved `data.json` per supported version is a test fixture; supporting a new release = add its sample and move the tested version in the same commit.
- **Validation on every read:** HTTP 200, JSON, ≤ 4 MB, ≤ 3 s; tree shape (`Text`, `Children`; sensors with `SensorId`, `Type`, numeric `RawValue`); each mapped sensor found by its rule (a missing one goes unavailable alone); values within physical limits (−40 to 150 °C, 0 to 20,000 RPM, 0 to 100 %). A failed structural check sets `incompatible` and makes all hardware entities unavailable. Wrong values are never published.
- **User notice:** one tray balloon per new problem (not per read), e.g. "LibreHardwareMonitor 0.9.8 changed its data format. Hardware sensors are paused. Version 0.9.6 is known to work." The tray status shows the same text; details go to the log.
- **Anti-spoofing** (a local program pretending to be LHM): before reading, the app finds the process listening on the port (no admin needed). If it is the System process, the listener is HTTP.sys, which needs admin rights or an admin-made URL reservation to register, so it is trusted (to be verified on BOX1 before relying on it). If it is an ordinary process, its image must be `LibreHardwareMonitor.exe` in an admin-only-writable folder. A running `LibreHardwareMonitor.exe` in such a folder, at a supported version, is required either way. If credentials are configured and the server does not ask for them, the source is `untrusted`. Anything failing these checks is `untrusted` and not read.
- **Credentials:** LHM's web server login (`username`, `password`), with the password DPAPI-encrypted like the MQTT password (section 7). Basic auth over loopback only.
- **Later (not planned):** a provider interface so other read-only sources (NVIDIA NVML, AMD ADLX) plug in with one class and one registration line, compiled in, never loaded from a folder; and an optional SYSTEM sensor service hosting the LHM library, which would need pinned dependencies, a lock file, and signature, vulnerability and license checks first. Hardware control (fans, power limits) stays out of scope.

---

## 6. Lifecycle and Power Handling

This is the most failure-prone area. Behavior is defined explicitly:

| Event | Source | Required behavior |
|---|---|---|
| App start | Process launch | Enforce single instance (named mutex). Load config. Start modules. Connect MQTT. Publish discovery, `online`, full state. |
| Lock / unlock / logon / logoff | `SystemEvents.SessionSwitch` | Presence module updates immediately. |
| **Suspend** (sleep or hibernate) | `SystemEvents.PowerModeChanged` (Suspend) | Best-effort, short timeout (~2s): publish `offline`, pause polling loops, disconnect cleanly. LWT covers the case where this fails. Publishing `offline` explicitly is **required**: a clean MQTT disconnect does not trigger the last-will message, so HA would otherwise show the PC as online while asleep (a prototype defect). |
| **Resume** | `PowerModeChanged` (Resume) | Wait for network (`NetworkChange` plus a broker reachability check), reconnect with backoff, republish `online`, discovery, and full state. Restart polling. |
| Network lost / restored | `NetworkChange` | Rely on MQTT reconnect logic. On restore, republish full state. |
| Shutdown / sign-out | `SystemEvents.SessionEnding` | Publish `offline`, disconnect, dispose. |
| Settings changed | Settings window | Apply without restart: restart MQTT connection and add/remove modules as needed. |
| Unhandled exception | Global handlers | Log. Keep the app alive where safe. Never leave a ghost tray icon (dispose `NotifyIcon` on exit). |

How it is built (milestone 3):

- `ISystemEventSource` (Core/Abstractions) delivers suspend, resume, session ending, session switch, and network availability. `WindowsSystemEventSource` (Platform.Windows) implements it over `SystemEvents` and `NetworkChange`; `LifecycleCoordinator` (Core/Hosting) holds all the decisions.
- **Suspend and session ending use a deferral:** handlers register their work and the Windows event handler waits for it, up to 2 seconds, before letting Windows continue. This is the only place the app blocks a thread, and only because Windows proceeds as soon as the handler returns.
- **Suspend:** publish `offline`, disconnect, then notify `IPowerAware` modules (in parallel), all within the 2-second budget. The connection then stays down (tray: *Paused*) until resume; no reconnect attempts while asleep. A connection attempt in progress is cancelled and never announces `online`.
- **If `offline` cannot be published in time** (e.g. a stalled network), the connection is dropped without a goodbye, so the broker sees the socket close and publishes the last will immediately. It is never left half-open.
- **Resume:** notify `IPowerAware` modules first, then reconnect immediately (no backoff delay), republishing `online`, discovery, and full state. A resume without a preceding suspend (e.g. a missed Modern Standby event) also forces a fresh connection.
- **Network available:** if a reconnect is waiting out its backoff, retry now. Ignored while connected or suspended. Losing the network needs no action: the MQTT keep-alive detects it and the normal reconnect takes over.
- **Shutdown / sign-out:** publish `offline`, disconnect, then exit the app. If the user cancels the shutdown, the app has already exited and must be started again (an accepted trade-off; the next sign-in starts it automatically).
- **Lock / unlock:** delivered to the coordinator and logged; the Presence module (milestone 4) consumes them.

Notes:

- On Modern Standby (S0 low-power) systems, the process may keep running while the display is off. Timers may be throttled. State publishing must tolerate gaps and not assume a continuous tick.
- All time-based polling uses `PeriodicTimer` / `async` loops with `CancellationToken`, not `Thread.Sleep` or blocking waits.
- Power/session event sources are wrapped behind an interface (`ISystemEventSource`) so lifecycle logic can be unit-tested by raising fake events.

---

## 7. Configuration

- File: `%APPDATA%\SmartHomeRelay\settings.json`. Created with defaults on first run.
- Bound via the Options pattern; validated on load and on save (invalid values rejected with clear messages in the UI).
- **MQTT password** is stored encrypted using DPAPI (`ProtectedData`, `CurrentUser` scope). Never logged. The app never writes it in plain text.
- **Entering the password by hand:** the file always contains an empty `"password": ""` field right after `username`, so the user can see where to type (milestone 2 finding: without it, the password was typed into `passwordProtected`, which only holds encrypted values). The user types the password there. When the app loads the file it encrypts the value into `passwordProtected` and rewrites the file without the plain-text field, so plain text exists only between the user saving and the app picking up the change. This is how the password is set until the settings window exists (milestone 9).
- **First run:** when the app creates `settings.json`, it opens the file in the user's default editor for `.json` files (Notepad if there is none) and shows a tray balloon explaining what to fill in. Saved changes are applied without a restart.

```json
{
  "mqtt": {
    "host": "192.168.1.10",
    "port": 1883,
    "useTls": false,
    "username": "winbridge",
    "passwordProtected": "<dpapi blob>",
    "discoveryPrefix": "homeassistant",
    "baseTopic": "shr"
  },
  "device": { "id": "desktop_main", "name": "Main Desktop" },
  "features": {
    "presence": { "enabled": true, "idleThresholdSeconds": 300 },
    "power":    { "enabled": true, "allowShutdown": false, "allowRestart": false, "allowHibernate": false, "allowSignOut": false, "shutdownDelaySeconds": 60 },
    "display":  { "enabled": true },
    "audio":    { "enabled": true },
    "metrics":  { "enabled": true, "intervalSeconds": 30 },
    "notifications": { "enabled": true, "imageDownloadTimeoutSeconds": 10, "imageMaxBytes": 10485760 },
    "hardware": { "enabled": false, "port": 8085, "username": "", "passwordProtected": "", "intervalSeconds": 30, "sensors": ["cpu_temperature", "cpu_fan", "ssd_temperature"], "sensorOverrides": {} }
  },
  "startWithWindows": true,
  "logLevel": "Information"
}
```

- `device.id` and `device.name` default to the host name when empty. On first run the file is written with the current host name, which pins the ID so a later host name change does not create new HA entities.
- An empty `mqtt.host` means "not configured": the app runs and the tray shows *Not configured*.
- A settings file that is not valid JSON, has a value of the wrong type, or fails validation never crashes the app. The problems are logged, the tray shows *Settings error*, and the app runs on defaults (for parse or type errors) until the file is fixed. The user's file is never overwritten in that case.
- **New settings after an upgrade:** at startup, settings that this version has but an existing `settings.json` lacks (e.g. `features.hardware`) are added to the file with their current values (the defaults), so users can see and edit them. Only missing keys are added: existing values, key order, and unknown keys are kept, and keys match case-insensitively like configuration binding. A missing key goes after the key that precedes it in the default layout. The file is only rewritten when it parses and validates; a file with comments is never rewritten (it would lose them), and the missing settings are listed in the log instead. A section the user deletes comes back with its defaults on the next start.
- `features.hardware` (5.8) is off by default. Its `password` is entered and encrypted the same way as the MQTT password. It has no host setting: LHM is always read on `127.0.0.1`.
- `logLevel` accepts `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, and is applied live (section 11).

### 7.1 Settings window (WinForms)

- Tabs or sections: **Connection** (host, port, username, password, **Test connection** button), **Device** (name, ID), **Features** (toggles and per-feature options), **General** (start with Windows, log level).
- The form is a thin view. A `SettingsPresenter` (in `App/Settings/`, depending only on abstractions) holds validation and save logic and is unit-testable.
- Tray menu: status (connected / disconnected), Settings, Reconnect, Pause/Resume reporting, Open logs folder, Exit. Items are added in the milestone that implements them; the menu never shows placeholder items. Until the settings window exists (milestone 9), **Settings** opens `settings.json` in the default editor. **Reconnect** publishes `offline`, disconnects, and connects again immediately.
- Launching the app while it is already running: the running copy shows a tray balloon ("Smart Home Relay is already running") and the new process exits.
- "Start with Windows" toggles the per-user `Run` registry value, the same one the installer creates.

---

## 8. Installation, Startup, and Updates

- **Installer:** Inno Setup script in `installer/`. Per-user install to `%LOCALAPPDATA%\Programs\SmartHomeRelay` (no admin prompt).
- Installer creates a Start Menu shortcut (with the App User Model ID for toast support) and, optionally, the `HKCU\...\Run` startup entry.
- **Publish mode:** self-contained `win-x64` only (no ARM64 or x86 builds; no .NET runtime required on the target PC). Single-file publishing is optional.
- **Upgrades (v1):** running a newer installer upgrades in place; the app is closed first (installer uses a `CloseApplications` mechanism). Settings in `%APPDATA%` are preserved.
- Version is defined once in `Directory.Build.props` and flows to the assembly, installer, and HA `sw_version`.
- **Manual updates only.** CI attaches the installer to a GitHub Release; updating means downloading and running the newer installer. Auto-update is out of scope (if wanted later, evaluate Velopack).

---

## 9. Testing Strategy

**Principle:** every module ships with tests. A bug fix ships with a test that fails without the fix.

| Layer | Project | What is tested | Runs |
|---|---|---|---|
| Unit | `Core.Tests` | Module logic, state mapping, command handling, presence derivation, discovery payload generation, config validation, backoff logic, lifecycle orchestration using fake event sources | Every build, any OS |
| Contract | `Core.Tests` | All registered modules: entity keys unique, discovery payloads valid, every command entity has a handler, disabled features clear their discovery | Every build |
| Snapshot | `Core.Tests` | Discovery JSON for each entity compared against approved JSON files checked into the repo, so accidental changes are caught | Every build |
| Integration (MQTT) | `Core.Tests` (tagged `MqttBroker`) | Real Mosquitto broker: connect, LWT, discovery, command round trip, reconnect. Runs in a separate `ubuntu-latest` CI job with Mosquitto installed on the runner, because Windows runners cannot run Linux containers. `tests/brokers/start-test-brokers.sh` starts two brokers: anonymous on 1883 and password-protected on 1884 (like the HA Mosquitto add-on), so refused logins are tested too. Locally they use `SHR_TEST_MQTT_HOST`/`SHR_TEST_MQTT_PORT`/`SHR_TEST_MQTT_AUTH_PORT` (default `localhost`, 1883, 1884) and skip if no broker answers; `SHR_REQUIRE_MQTT_BROKER=1` (set in CI) turns a missing broker into a failure. They use `shrtest` topic prefixes so they can never create entities in a real HA, but should still be pointed at a throwaway broker | CI (Linux job), optional locally |
| Integration (Windows) | `Platform.Tests` (tagged `Windows`) | Real implementations of idle time, audio, metrics against the actual OS. Never run destructive actions (shutdown, sleep) | CI on `windows-latest` |
| Architecture | `Core.Tests` | `Core` has no references to Windows or WinForms assemblies | Every build |

Regression tests required from the prototype review (3.5), each written as a test that fails against the prototype's behavior:

- Suspend publishes `offline` **before** disconnecting.
- Discovery payloads are valid JSON with a correct `device` block, availability payloads match the availability topic, and discovery is republished when a config already exists.
- Malformed notification JSON does not throw; a failed image download still produces a toast without an image.
- A non-URL `HeroImageURL` (bare filename, drive path, UNC path) is ignored without reading any file, and the toast still shows.
- Temp images are cleaned up.
- **Compatibility:** a payload containing only prototype fields (`Title`, `Message`, `HeroImageURL`, `ActivationURL`, `Audio`) produces the same toast content as before.
- Buttons: more than 5 are truncated, entries with an empty label or invalid or disallowed URL are dropped, and a payload with no valid buttons still shows a normal toast.
- A slow image download does not delay handling of a subsequent command (queue behavior).

Conventions:

- Test naming: `Method_Scenario_ExpectedResult`.
- Time and delays are injected (`TimeProvider`) so tests never use real sleeps.
- Destructive platform actions are only ever exercised through fakes.
- **CI** (GitHub Actions): a `windows-latest` job restores, builds with warnings-as-errors, runs all tests except `MqttBroker`, and publishes the artifact; an `ubuntu-latest` job runs the `MqttBroker` tests against Mosquitto. A failing test blocks merge. On a version tag, CI also builds the Inno Setup installer and attaches it to a GitHub Release.

---

## 10. Coding Standards

- C# with **nullable reference types enabled**, `ImplicitUsings` as appropriate, **file-scoped namespaces**, latest language version.
- `TreatWarningsAsErrors` on, with .NET analyzers enabled. Style enforced via `.editorconfig`.
- **One top-level type per file**, and the filename matches the type name.
- **Dependency injection** everywhere. No static mutable state, no service locator, no singletons by hand.
- `async`/`await` end to end, `CancellationToken` on every long-running or I/O operation, no `.Result` / `.Wait()`.
- `ILogger<T>` for logging with structured message templates. Never log secrets.
- `IOptions` / `IOptionsMonitor` for configuration.
- Prefer small interfaces and composition over inheritance. Prefer `record` for immutable data.
- Public APIs in `Core` have XML doc comments. Comments explain *why*, not *what*.
- P/Invoke only in `Platform.Windows/Interop`, via CsWin32 or `LibraryImport`. Never `DllImport` scattered in feature code.
- Dispose properly: `IDisposable` / `IAsyncDisposable` for timers, MQTT clients, audio devices, and the `NotifyIcon`.
- WinForms UI work happens on the UI thread only. Background work never touches controls directly.

---

## 11. Logging and Diagnostics

- Serilog rolling file in `%LOCALAPPDATA%\SmartHomeRelay\logs\` (daily, 14 days retained, size capped).
- Default level `Information`; `Debug` selectable in settings for troubleshooting.
- Tray menu "Open logs folder". Tray tooltip and icon reflect connection state: normal icon when connected, amber badge while not configured, connecting, or reconnecting, red badge for a settings or connection error.

---

## 12. Security

- v1 connects to the broker over **plain TCP with username/password authentication** (no TLS, matching the author's LAN setup). TLS support is deferred. The settings model keeps a `useTls` field (always `false` in v1) so TLS can be added later without breaking existing config files.
- Credentials protected with DPAPI. No secrets in logs, in discovery payloads, or in the repo. A password typed into `settings.json` by hand is encrypted and removed from the file as soon as the app reads it (section 7).
- The app only talks to the configured broker (plus HTTP(S) image downloads for notifications, with size and time limits, and, when the Hardware feature is on, `GET /data.json` from LibreHardwareMonitor on `127.0.0.1` with the checks in 5.8).
- **No credentials, IP addresses, or host names in source code** (the prototype hardcoded them). They live only in settings, with the password DPAPI-encrypted. Rotate any credential that was ever committed to source control.
- Repository hygiene: enable GitHub secret scanning and push protection. `settings.json` lives in `%APPDATA%`, outside the repo, so it cannot be committed by accident.
- Notification image paths and activation URLs are validated as described in 5.6 (`http`/`https` URLs only; no local file access).
- No arbitrary command or script execution from MQTT in v1. Only the fixed, enumerated commands above.
- Destructive power commands are opt-in.
- Consider broker ACLs scoped to `{baseTopic}/{deviceId}/#` and `{discoveryPrefix}/#/{deviceId}/#`.

---

## 13. How to Add a New Feature (checklist for contributors and LLMs)

1. Read this spec. Confirm the feature isn't already covered or explicitly out of scope.
2. Create `Core/Features/<Name>/` with a `<Name>Module : IFeatureModule` and any small collaborator classes (one type per file).
3. If the feature needs Windows APIs, define an interface in `Core/Abstractions/` and implement it in `Platform.Windows/<Name>/`.
4. Add a settings section (model + validation + defaults) and a toggle in the settings window.
5. Register the module and its platform implementation in the composition root (`App/Program.cs`). This is the only shared file touched.
6. Define entities with stable `entityKey`s. **Never rename a shipped key** (it orphans the HA entity).
7. Write unit tests, ensure the contract and snapshot tests pass, and add a Windows integration test if platform code was added.
8. Update section 5 of this spec with the new entities.

### Rules for LLM-generated changes

- Do not put logic in forms, `Program.cs`, or `Platform.Windows` beyond thin OS wrappers.
- Do not add a dependency from `Core` to Windows or UI assemblies.
- Do not modify unrelated modules to make a feature work. If that seems necessary, stop and propose a change to the shared abstractions instead.
- Do not add new NuGet packages without noting why. Versions go in `Directory.Packages.props`.
- Every PR or change includes tests.
- Keep changes small and focused; match the existing structure and naming.

---

## 14. Milestones

1. **Skeleton:** solution layout, Generic Host, DI, config, logging, tray icon, single-instance, CI pipeline.
2. **MQTT core:** connection, LWT/availability, reconnect, discovery builder, entity abstractions, contract and snapshot tests, Linux CI job with Mosquitto. Also: first-run opening of `settings.json` and encryption of a hand-entered password (section 7).
3. **Lifecycle:** suspend/resume/session/network handling with fake event source tests, then real implementation.
4. **Presence** module.
5. **Power** module.
6. **Metrics** module, plus hardware sensors from a user-managed LibreHardwareMonitor (5.8).
7. **Display** and **Audio** modules.
8. **Notifications** module.
9. **Settings window** polish and installer.

---

## 15. Decisions Log and Open Items

**Confirmed**

- Project name: Smart Home Relay.
- Tray app, WinForms settings window, Inno Setup installer, manual updates.
- Existing MQTT topic contract and notification fields are kept; existing automations must keep working.
- Notification URLs are `http`/`https` only. Frigate `EventID` support is removed.
- MQTT: username/password, no TLS.
- No elevation; no hardware metrics that require admin rights.
- GitHub for hosting, CI, and releases.
- Display: monitor off/on only; brightness deferred.
- Notification images: full `http`/`https` URLs only; no local file access.
- Architecture: x64 only.
- Second launch shows a balloon in the running copy, then exits (milestone 1).
- Tray menu grows per milestone; no disabled placeholder items (milestone 1).
- `logLevel` added to `settings.json` (section 7) to back the General > log level option (milestone 1).
- Tests use xUnit v3 and Shouldly.
- HA device manufacturer is `Smart Home Relay`.
- `unique_id` uses the lowercased, underscore-sanitized device ID (section 4.1).
- Shutdown and restart wait `shutdownDelaySeconds` (default 60s). Sleep and lock have no toggle.
- Until milestone 9, settings are edited in `settings.json` directly: it opens on first run, and a plain `password` field is encrypted automatically.
- The settings window stays in milestone 9. Editable topic settings are a later enhancement (5.7).
- MQTT broker integration tests run in a Linux CI job.
- Verified on the real broker and HA (milestone 4): sleep/wake, lock/unlock, and a Home Assistant restart (the app republishes status, discovery, and state).
- Verified on BOX1 (milestone 5): Lock, Sleep (true sleep), Shutdown with the 60s Windows countdown, Cancel shutdown, and the allowShutdown setting taking effect without a restart.
- Verified on BOX1 (milestone 6): CPU, memory, disk, and GPU match Task Manager. Network is an average over the sample interval, so it is smoother than Task Manager's 1-second view; accepted as is.
- Added after milestone 6, verified with `tools/probe-sensors.ps1` on BOX1 (Ryzen 7 5800X3D, AMD GPU) as a standard user: `cpu_power` (Energy Meter), `gpu_temperature` and `gpu_power_level` (D3DKMT adapter perf data). CPU temperature is not available without a driver.
- Hardware sensors (CPU temperature, fans) come from a LibreHardwareMonitor the user installs and manages, read through its local web server (5.8). Chosen over a bundled elevated sensor service: no elevation, driver, or new packages in the app, and all sensors stay on one HA device. Use this feature or HA's LHM integration, not both. Moved into milestone 6 so all metrics are settled before milestone 7.
- Verified on BOX1 (hardware sensors, LHM 0.9.6 with PawnIO): `hardware_status` is `ok`, so the anti-spoofing checks pass against the real LHM; CPU and SSD temperatures map by rule. BOX1's Gigabyte board (ITE IT8688E) names its fans "Fan #1".."Fan #5", so `cpu_fan` needs a `sensorOverrides` entry, as designed (the rules never guess). Its real `data.json` is a test fixture.
- An existing `settings.json` gains settings added in newer versions automatically at startup (section 7), instead of users pasting them in.
- `uptime` is published in hours (`h`, one decimal) instead of seconds, for readability (after milestone 6).
- MQTT 3.1.1; device ID limited to letters, digits, `-`, `_` (milestone 2).
- Until milestone 9 the tray's Settings item opens `settings.json` (milestone 2).

**Open**

- [ ] Tray item "Pause/Resume reporting" (7.1) is not assigned to a milestone yet. It can reuse the suspend/resume path from milestone 3.
- [ ] Rotate the MQTT password if the prototype source (with its hardcoded credentials) was ever committed or shared.
