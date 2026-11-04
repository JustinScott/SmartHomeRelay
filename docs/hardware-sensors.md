# Hardware sensors (CPU temperature, fans) with LibreHardwareMonitor

> This is the Hardware feature in [spec section 5.8](../spec.md), part of milestone 6 (Metrics).

Windows doesn't let a standard user read CPU temperature or motherboard fan speeds; that takes a kernel driver. Smart Home Relay never runs as admin, so it gets those readings from **LibreHardwareMonitor (LHM)**, which you install and manage. Smart Home Relay reads LHM's data on your own PC and publishes it on the same Home Assistant device as everything else.

```
LibreHardwareMonitor (elevated, PawnIO driver)
        │  GET http://127.0.0.1:8085/data.json  (read-only, login required)
        ▼
Smart Home Relay tray app (standard user) ──MQTT──▶ Home Assistant
```

Setup takes about 10 minutes. Steps 2 to 5 and 7 need an administrator account.

## Before you start: choose one

Use **either** Smart Home Relay's Hardware feature **or** Home Assistant's [Libre Hardware Monitor integration](https://www.home-assistant.io/integrations/libre_hardware_monitor/), not both. Running both creates every sensor twice.

| | Smart Home Relay Hardware feature | Home Assistant LHM integration |
|---|---|---|
| Where sensors appear | On your Smart Home Relay device, with all other PC sensors | On a separate LHM device |
| Which sensors | A curated list you choose | Everything LHM finds (you can disable entities) |
| LHM web server | Loopback only; blocked from the network | Must be reachable from Home Assistant over the network |
| Turned on with | `features.hardware.enabled: true` | Settings → Devices & services in Home Assistant |

If you choose the Home Assistant integration, leave `features.hardware.enabled` set to `false` (the default) and follow its documentation instead. Steps 1 to 4 below still apply, but not the firewall rule in step 5.

## Tested versions

| Component | Tested version | Notes |
|---|---|---|
| LibreHardwareMonitor | 0.9.6 | 0.9.5 and later are accepted. Older versions are refused. Newer versions are tried, with a warning, until they're tested. |
| PawnIO | 2.2.0 | LHM's hardware-access driver. Needed for CPU temperature. |

Before updating LHM, check this table. If you update to an untested version and its data format changed, Smart Home Relay pauses the hardware sensors and tells you which version is known to work. Nothing breaks; you can roll back.

## 1. Install LibreHardwareMonitor

1. Download the release zip **only** from the official releases page: <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases>. Check the address in your browser. Lookalike download sites exist.
2. Extract it to `C:\Program Files\LibreHardwareMonitor`.
   Use Program Files, not Downloads or your Desktop. Only administrators can change files there, so other programs can't replace LHM. Smart Home Relay also refuses to trust a `LibreHardwareMonitor.exe` that runs from a folder standard users can write to.
3. In an **admin** PowerShell, clear the "downloaded from the internet" flag:

   ```powershell
   Get-ChildItem 'C:\Program Files\LibreHardwareMonitor' -Recurse | Unblock-File
   ```

## 2. Install PawnIO

LHM uses PawnIO to read CPU sensors. Install it from <https://pawnio.eu/>, or with winget:

```powershell
winget install --id namazso.PawnIO --exact
```

PawnIO is a kernel driver. By default only administrators can use it. Don't change that setting.

## 3. Start LHM elevated at sign-in

1. Run `LibreHardwareMonitor.exe` once as administrator and confirm you see CPU temperatures.
2. In LHM's **Options** menu, turn on **Start Minimized** and **Minimize To Tray**.
3. Start it at sign-in with admin rights. LHM's own **Run On Windows Startup** option is the simplest. If LHM doesn't start elevated after you sign in again, create a scheduled task instead (admin PowerShell):

   ```powershell
   $exe       = 'C:\Program Files\LibreHardwareMonitor\LibreHardwareMonitor.exe'
   $action    = New-ScheduledTaskAction -Execute $exe
   $trigger   = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
   $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
   Register-ScheduledTask -TaskName 'LibreHardwareMonitor' -Action $action -Trigger $trigger -Principal $principal
   ```

## 4. Turn on LHM's web server, with a login

LHM's web server has no login by default, and it can also **change** hardware settings such as fan speeds. Always set a login.

1. Make a long random password just for this (don't reuse one):

   ```powershell
   $b = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
   ```

2. In LHM: **Options → Remote Web Server**. Set the port (`8085` is the default), turn on authentication with a username and that password, then turn on **Run**. Restart LHM if it asks you to.
3. Check it on the same PC: open <http://127.0.0.1:8085/> in a browser and sign in. You should see the sensor tree.

Don't create a URL reservation (`netsh http add urlacl`) for this port. LHM doesn't need one, and Smart Home Relay relies on the port being registerable only by an administrator.

## 5. Block the web server from your network

Smart Home Relay only needs the web server on this PC. LHM can't be set to listen on this PC only (it listens on every network interface), so block the port from everything else (admin PowerShell; use your port if you changed it):

```powershell
New-NetFirewallRule -DisplayName 'LibreHardwareMonitor web server (block network)' `
  -Direction Inbound -Protocol TCP -LocalPort 8085 -Action Block -Profile Any
```

Windows Firewall doesn't filter traffic within the same PC (loopback), so Smart Home Relay still connects. A block rule also wins over any allow rule LHM may have created.

## 6. Turn on the Hardware feature in Smart Home Relay

Open `settings.json` (tray menu → **Settings**) and set:

```json
"hardware": {
  "enabled": true,
  "port": 8085,
  "username": "the LHM username",
  "password": "the LHM password",
  "intervalSeconds": 30,
  "sensors": ["cpu_temperature", "cpu_fan", "ssd_temperature"],
  "sensorOverrides": {}
}
```

When you save, Smart Home Relay encrypts the password (Windows DPAPI) and removes the plain text from the file, the same as the MQTT password. There is no host setting: LHM is always read on `127.0.0.1`. Changes to `sensors` apply after you restart Smart Home Relay.

| Sensor | What it reads |
|---|---|
| `cpu_temperature` | The CPU's own package temperature (AMD "Core (Tctl/Tdie)", Intel "CPU Package") |
| `cpu_fan` | The motherboard fan LHM names "CPU Fan" (or anything with "CPU" in its name) |
| `ssd_temperature` | The drive Windows is installed on |

### If a sensor shows "Unknown"

The built-in rule didn't find a match, for example because your motherboard's fans are named "Fan #1", "Fan #2" in LHM (many Gigabyte boards do). Smart Home Relay doesn't guess; point the sensor at the right one:

1. List the fans with their IDs and current speeds (Windows PowerShell; in PowerShell 7 add `-AllowUnencryptedAuthentication`, which is safe here because the traffic never leaves the PC). Enter LHM's web server login when asked:

   ```powershell
   $d = Invoke-RestMethod http://127.0.0.1:8085/data.json -Credential (Get-Credential) -UseBasicParsing
   function Walk($n) { if ($n.Type -eq 'Fan') { '{0,-30} {1,-10} {2}' -f $n.SensorId, $n.RawValue, $n.Text }; foreach ($c in $n.Children) { Walk $c } }
   Walk $d
   ```

2. Pick the fan whose RPM matches your CPU cooler (your BIOS fan page shows which header it's on) and copy its ID, such as `/lpc/it8688e/0/fan/0`.
3. Add it to `settings.json` and save. It applies on the next reading, no restart needed:

   ```json
   "sensorOverrides": { "cpu_fan": "/lpc/it8688e/0/fan/0" }
   ```

Don't hide the sensors you use in LHM's window; hidden sensors may drop out of its web data.

## 7. Check it

- The tray status shows no hardware warning.
- In Home Assistant, the Smart Home Relay device has the new sensors. Under **Diagnostic**, **Hardware status** reads `ok` and **LibreHardwareMonitor version** shows the version being read.
- If LHM isn't running yet when Smart Home Relay starts, you'll only be told after about two minutes. Both usually start at the same sign-in.

## Troubleshooting by Hardware status

| Status | Meaning | What to do |
|---|---|---|
| `ok` | Working | — |
| `not_running` | Nothing is answering on the port, or LHM isn't running | Start LHM (as admin) and check step 3 |
| `login_failed` | LHM refused the username or password | Re-enter them in `settings.json` |
| `untrusted` | Something other than a trusted LHM owns the port, LHM runs from a folder standard users can write to, or the server didn't ask for the login you configured | Check that LHM is in Program Files (step 1) and that no other program uses the port. Then restart LHM. |
| `newer_than_tested` | LHM is newer than the tested version, but its data still checks out | Nothing, unless values look wrong. Watch for a Smart Home Relay update. |
| `incompatible` | LHM is older than 0.9.5, or its data format changed | Install the tested version from the table above |

While the status is anything but `ok` or `newer_than_tested`, the hardware sensors show **Unavailable** in Home Assistant, and Smart Home Relay shows one tray message per new problem.

The Smart Home Relay log (tray menu → **Open logs folder**) names the exact check that failed.

## Switching to Home Assistant's integration later

1. Set `features.hardware.enabled` to `false`. Smart Home Relay removes its hardware entities from Home Assistant.
2. Delete the firewall rule from step 5 (Home Assistant has to reach LHM over the network):

   ```powershell
   Remove-NetFirewallRule -DisplayName 'LibreHardwareMonitor web server (block network)'
   ```

3. Add the Libre Hardware Monitor integration in Home Assistant. Keep LHM's login on, and enter it there.

## Uninstalling

1. Set `features.hardware.enabled` to `false`.
2. Remove the scheduled task if you made one: `Unregister-ScheduledTask -TaskName 'LibreHardwareMonitor'`.
3. Remove the firewall rule (see above), delete `C:\Program Files\LibreHardwareMonitor`, and uninstall PawnIO from **Settings → Apps** if nothing else uses it.

## Security notes

- Smart Home Relay never runs as admin, and it only ever **reads** `data.json`. It never calls LHM's endpoints that change settings.
- The checks Smart Home Relay runs before it trusts the data (loopback only, port ownership, LHM's install location and version, login, size and value limits) are described in [spec section 5.8](../spec.md).
- LHM and PawnIO run with full rights. Install them only from the official sources above, and keep them updated within the tested versions.
