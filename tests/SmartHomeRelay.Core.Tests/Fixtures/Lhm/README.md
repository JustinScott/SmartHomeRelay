# LibreHardwareMonitor `data.json` samples

One real capture per supported LHM version (spec 5.8), named `<pc>-<lhm version>.json`, each with a `.expected.json` beside it: the system disk number to assume, any `sensorOverrides`, the sensor each curated key must map to, and which keys the rules leave unmatched without overrides. `LhmFixtureTests` checks every sample against its expectations.

| File | What it is |
|---|---|
| `box1-0.9.6.json` | Real capture from BOX1 (Gigabyte X570 AORUS ELITE WIFI, ITE IT8688E, Ryzen 7 5800X3D, RX 6800 XT) on LHM 0.9.6. Network adapter IDs replaced with placeholders; nothing else changed. |
| `synthetic-nct6798d-0.9.6.json` | Built from LHM 0.9.6's source (`HttpServer.GenerateJsonForNode`) for a board whose fan is named "CPU Fan". Most unit tests use this one. |

To capture a sample (normal PowerShell, LHM web server on):

```powershell
Invoke-WebRequest http://127.0.0.1:8085/data.json -Credential (Get-Credential) -UseBasicParsing -OutFile data.json
```

To support a new LHM release: add its capture and `.expected.json` here, run the tests, and move `LhmCompatibility.TestedVersion` and the table in `docs/hardware-sensors.md` in the same commit.
