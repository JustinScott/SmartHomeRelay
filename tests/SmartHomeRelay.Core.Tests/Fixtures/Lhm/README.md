# LibreHardwareMonitor `data.json` samples

One file per supported LHM version (spec 5.8), named `<pc>-<lhm version>.json`. `LhmFixtureTests` parses every file here and checks that each curated sensor is found.

`box1-0.9.6-synthetic.json` is built from LHM 0.9.6's source (`HttpServer.GenerateJsonForNode`) with BOX1's hardware. Replace it with a real capture: with LHM running and its web server on, save `http://127.0.0.1:8085/data.json` (signed in) as `box1-0.9.6.json`, then delete the synthetic file.

To support a new LHM release: add its capture here, run the tests, and move `LhmCompatibility.TestedVersion` and the table in `docs/hardware-sensors.md` in the same commit.
