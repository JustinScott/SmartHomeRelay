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
- Anything that requires administrator privileges (the app never elevates).
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

### 5.2 Power

| Entity | Type | Notes |
|---|---|---|
| `shutdown` | button | Delayed by `shutdownDelaySeconds` (default 60s); Windows shows its standard shutdown countdown |
| `restart` | button | Same delay as `shutdown` |
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
- Known caveat: `SetSuspendState` can hibernate instead of sleep on some configurations. Document and test the behavior on the target machine.

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
| `disk_usage_{drive}` | sensor (%) | One per fixed drive; plus free space in GB |
| `network_down` / `network_up` | sensor (Mbps) | Aggregate of the active adapter(s) |
| `gpu_usage` | sensor (%) | From Windows GPU Engine performance counters. Not created if the counters are unavailable |

- Update interval configurable (default 30s). Publish only when the value changes meaningfully (small deadband) to limit MQTT chatter.
- **No elevation (v1):** the app runs as a standard user and never requests administrator rights. Any metric that needs admin access (CPU/GPU temperatures, fan speeds, power draw through low-level hardware access) is **out of scope**. Only metrics available to a standard user are implemented.

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

Display brightness (internal panels via WMI, external monitors via DDC/CI), toast action buttons that publish click events back to HA, active window/app name, webcam in use, mic in use, media play/pause/next, battery status (laptops), pending Windows updates, screenshot camera entity, launch allowlisted apps, Bluetooth device state, editing `discoveryPrefix` and `baseTopic` in the settings window (an Advanced section with a warning that changing them breaks existing automations).

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

Notes:

- On Modern Standby (S0 low-power) systems, the process may keep running while the display is off. Timers may be throttled. State publishing must tolerate gaps and not assume a continuous tick.
- All time-based polling uses `PeriodicTimer` / `async` loops with `CancellationToken`, not `Thread.Sleep` or blocking waits.
- Power/session event sources are wrapped behind an interface (`ISystemEventSource`) so lifecycle logic can be unit-tested by raising fake events.

---

## 7. Configuration

- File: `%APPDATA%\SmartHomeRelay\settings.json`. Created with defaults on first run.
- Bound via the Options pattern; validated on load and on save (invalid values rejected with clear messages in the UI).
- **MQTT password** is stored encrypted using DPAPI (`ProtectedData`, `CurrentUser` scope). Never logged. The app never writes it in plain text.
- **Entering the password by hand:** the user may add `"password": "<plain text>"` under `mqtt`. When the app loads the file it encrypts the value into `passwordProtected` and rewrites the file without the plain-text field, so plain text exists only between the user saving and the app picking up the change. This is how the password is set until the settings window exists (milestone 9).
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
    "notifications": { "enabled": true, "imageDownloadTimeoutSeconds": 10, "imageMaxBytes": 10485760 }
  },
  "startWithWindows": true,
  "logLevel": "Information"
}
```

- `device.id` and `device.name` default to the host name when empty. On first run the file is written with the current host name, which pins the ID so a later host name change does not create new HA entities.
- An empty `mqtt.host` means "not configured": the app runs and the tray shows *Not configured*.
- A settings file that is not valid JSON, has a value of the wrong type, or fails validation never crashes the app. The problems are logged, the tray shows *Settings error*, and the app runs on defaults (for parse or type errors) until the file is fixed. The user's file is never overwritten in that case.
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
| Integration (MQTT) | `Core.Tests` (tagged `MqttBroker`) | Real Mosquitto broker: connect, LWT, discovery, command round trip, reconnect. Runs in a separate `ubuntu-latest` CI job with Mosquitto installed on the runner, because Windows runners cannot run Linux containers. Locally they use `SHR_TEST_MQTT_HOST`/`SHR_TEST_MQTT_PORT` (default `localhost:1883`) and skip if no broker answers; `SHR_REQUIRE_MQTT_BROKER=1` (set in CI) turns a missing broker into a failure. They use `shrtest` topic prefixes so they can never create entities in a real HA, but should still be pointed at a throwaway broker | CI (Linux job), optional locally |
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
- The app only talks to the configured broker (plus HTTP(S) image downloads for notifications, with size and time limits).
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
6. **Metrics** module.
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
- MQTT 3.1.1; device ID limited to letters, digits, `-`, `_` (milestone 2).
- Until milestone 9 the tray's Settings item opens `settings.json` (milestone 2).

**Open**

- [ ] Rotate the MQTT password if the prototype source (with its hardcoded credentials) was ever committed or shared.
