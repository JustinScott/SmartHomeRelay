# AGENTS.md

Instructions for AI coding agents working on **Smart Home Relay**: a Windows tray app that connects a Windows PC to Home Assistant over MQTT.

## Read first

**`spec.md` is the source of truth.** Read it fully before changing anything. If code and spec disagree, stop and ask. Do not silently pick one. If a decision changes, update `spec.md` in the same change.

## How to work

- Work on **one milestone at a time** (spec section 14). Do not build ahead.
- Keep changes small and focused. Match the existing structure and naming.
- Every change includes tests. A bug fix includes a test that fails without the fix.
- Before finishing: build with warnings as errors and run the tests. Report what you ran and the result. Do not claim something works if you did not run it.
- If something is ambiguous or missing from the spec, ask. Do not guess.

## Architecture rules (do not break these)

- Projects: `Core` (pure logic, no Windows or UI references) ← `Platform.Windows` (all OS and P/Invoke code) ← `App` (WinForms, composition root). `Core` must never reference Windows or WinForms assemblies.
- Each feature is a self-contained module in `Core/Features/<Name>/`, one public type per file. A module never references another module.
- Windows-specific work goes behind an interface in `Core/Abstractions` and is implemented in `Platform.Windows`.
- No business logic in forms, `Program.cs`, or `Platform.Windows`. Forms are thin views over testable presenters.
- Adding a feature should touch its own folder plus one DI registration line. If it seems to need more, stop and propose a change to the shared abstractions.
- Dependency injection everywhere. No static mutable state, no hand-built singletons.

## Code rules

- Nullable enabled, file-scoped namespaces, warnings as errors.
- `async`/`await` end to end with `CancellationToken`. Never `.Result`, `.Wait()`, `Thread.Sleep`, or `SpinWait` in async code.
- Inject time (`TimeProvider`) so tests never use real delays.
- Use `ILogger<T>`. Never log secrets.
- P/Invoke only in `Platform.Windows/Interop`.
- Dispose everything disposable (timers, MQTT client, audio devices, `NotifyIcon`).
- No new NuGet packages without a stated reason. Versions go in `Directory.Packages.props`.

## Hard constraints

- **Never elevate.** The app runs as a standard user. Do not add anything that needs administrator rights.
- **Never hardcode** credentials, IPs, or host names. Settings only; the password is DPAPI-encrypted.
- **Never rename a shipped entity key or MQTT topic.** Existing Home Assistant automations depend on them. The `shr/{host}/status` and `shr/{host}/commands/notification` topics and the notification field names (`Title`, `Message`, `HeroImageURL`, `ActivationURL`, `Audio`, `Buttons`) are a compatibility contract.
- **URLs from MQTT payloads must be `http` or `https` only.** Never read local files or paths named in a payload.
- **Publish `offline` before any clean MQTT disconnect** (suspend, shutdown). A clean disconnect does not trigger the last-will message.
- Destructive power commands (shutdown, restart, hibernate, sign out) stay disabled by default.
- Never run destructive OS actions in tests. Use fakes.
- One module failing must never crash the app or other modules.

## Commands

Once the solution exists (adjust if paths differ):

```
dotnet build SmartHomeRelay.sln -warnaserror
dotnet test SmartHomeRelay.sln --filter "Category!=Windows"
dotnet test SmartHomeRelay.sln
```

The second command runs the fast unit tests. The third includes Windows-only integration tests.

## Out of scope for v1

Linux/macOS, Windows Service host, TLS, auto-update, brightness control, hardware temperatures or anything needing admin, Frigate support, arbitrary command execution from MQTT. See spec sections 1.2 and 5.7.
