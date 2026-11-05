using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHomeRelay.Core.Configuration;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Features;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Hosting;

/// <summary>
/// Runs the feature modules and connects them to MQTT (spec 3.3, 4.3):
/// starts/stops modules as settings change, publishes discovery (clearing it for disabled modules),
/// keeps the latest state per entity and republishes it after every (re)connect, republishes everything
/// when HA restarts, and routes commands to entity handlers on background tasks.
/// Every module call is isolated: a failure is logged and never reaches other modules.
/// </summary>
public sealed partial class ModuleHost : IHostedService, IMqttSessionHandler, IDisposable
{
    private readonly IReadOnlyList<IFeatureModule> _modules;
    private readonly IMqttTransport _transport;
    private readonly ISettingsState _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ModuleHost> _logger;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Component, string State), string> _customStates = new();
    private readonly ConcurrentDictionary<long, Task> _background = new();
    private readonly CancellationTokenSource _stopping = new();
    private EntityCatalog _catalog = EntityCatalog.Build([]);
    private Session? _session;
    private long _nextBackgroundId;

    /// <summary>Creates the host for all registered <paramref name="modules"/>.</summary>
    public ModuleHost(
        IEnumerable<IFeatureModule> modules,
        IMqttTransport transport,
        ISettingsState settings,
        ILoggerFactory loggerFactory)
    {
        _modules = [.. modules];
        _transport = transport;
        _settings = settings;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ModuleHost>();
    }

    /// <summary>The validated entities of all modules.</summary>
    public EntityCatalog Catalog => _catalog;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _catalog = EntityCatalog.Build(_modules);
        foreach (var rejection in _catalog.Rejections)
        {
            LogModuleRejected(_logger, rejection);
        }

        _settings.Changed += OnSettingsChanged;
        _transport.MessageReceived += OnMessageReceived;
        _transport.Disconnected += OnDisconnected;

        await ReconcileModulesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _settings.Changed -= OnSettingsChanged;
        _transport.MessageReceived -= OnMessageReceived;
        _transport.Disconnected -= OnDisconnected;
        await _stopping.CancelAsync().ConfigureAwait(false);

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var module in _catalog.Modules.Where(m => _running.Contains(m.Id)))
            {
                await StopModuleAsync(module, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Dispose();
        _lifecycle.Dispose();
    }

    /// <inheritdoc />
    public async Task OnConnectedAsync(MqttTopics topics, AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(settings);

        var session = new Session(topics, settings);
        Volatile.Write(ref _session, session);

        // Subscribing to every command topic (even of disabled modules) means toggling a feature never
        // needs a resubscribe; commands for disabled entities are ignored at dispatch.
        var subscriptions = new List<string> { topics.HomeAssistantStatus };
        subscriptions.AddRange(_catalog.Entries.Where(e => e.Entity.AcceptsCommands).Select(e => topics.Command(e.Entity.Key)));
        subscriptions.AddRange(_catalog.CustomComponents.SelectMany(c => c.Component.Commands.Select(command => topics.CustomCommand(c.Component.Name, command))));
        await _transport.SubscribeAsync(subscriptions, cancellationToken).ConfigureAwait(false);

        await PublishDiscoveryAndStateAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tells running <see cref="IPowerAware"/> modules the system is about to sleep (spec 3.3, 6).</summary>
    public Task NotifySuspendingAsync(CancellationToken cancellationToken) =>
        NotifyPowerAwareAsync((m, ct) => m.OnSuspendingAsync(ct), "suspend", cancellationToken);

    /// <summary>Tells running <see cref="IPowerAware"/> modules the system woke up.</summary>
    public Task NotifyResumedAsync(CancellationToken cancellationToken) =>
        NotifyPowerAwareAsync((m, ct) => m.OnResumedAsync(ct), "resume", cancellationToken);

    /// <summary>Waits for outstanding background work (commands, republishes). For tests.</summary>
    internal Task WhenBackgroundIdleAsync() => Task.WhenAll(_background.Values);

    internal async Task PublishStateAsync(IFeatureModule module, string entityKey, string payload, CancellationToken cancellationToken)
    {
        if (!_catalog.TryGet(entityKey, out var entry) || entry.Module != module || !entry.Entity.HasState)
        {
            LogUnknownStateKey(_logger, module.Id, entityKey);
            return;
        }

        _states[entityKey] = payload;

        var session = Volatile.Read(ref _session);
        if (session is null || !_transport.IsConnected)
        {
            return; // Published on the next connect.
        }

        try
        {
            await PublishStateAsync(session, entry, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogStatePublishFailed(_logger, ex, entityKey);
        }
    }

    internal async Task UpdateEntityAsync(IFeatureModule module, EntityDescriptor entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (_catalog.TryReplace(module, entity) is { } problem)
        {
            LogEntityUpdateRejected(_logger, module.Id, problem);
            return;
        }

        var session = Volatile.Read(ref _session);
        if (session is null || !_transport.IsConnected || !IsEnabled(module, CurrentValidSettings() ?? session.Settings))
        {
            return; // Published with the rest on the next connect.
        }

        try
        {
            var payload = DiscoveryPayloadBuilder.Build(entity, session.Topics, session.Settings.Device, RelayVersion.Current);
            await _transport.PublishAsync(session.Topics.Discovery(entity.Component, entity.Key), payload, MqttQos.AtLeastOnce, retain: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogStatePublishFailed(_logger, ex, entity.Key);
        }
    }

    internal async Task PublishCustomStateAsync(IFeatureModule module, string component, string state, string payload, CancellationToken cancellationToken)
    {
        if (!_catalog.TryGetCustom(component, out var entry) || entry.Module != module || !entry.Component.States.Contains(state))
        {
            LogUnknownStateKey(_logger, module.Id, $"{component}/{state}");
            return;
        }

        _customStates[(component, state)] = payload;

        var session = Volatile.Read(ref _session);
        if (session is null || !_transport.IsConnected)
        {
            return;
        }

        try
        {
            await PublishCustomStateAsync(session, entry.Component, state, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogStatePublishFailed(_logger, ex, $"{component}/{state}");
        }
    }

    private Task PublishCustomStateAsync(Session session, CustomComponent component, string state, string payload, CancellationToken cancellationToken) =>
        _transport.PublishAsync(session.Topics.CustomState(component.Name, state), payload, MqttQos.AtMostOnce, component.RetainStates, cancellationToken);

    /// <summary>
    /// Custom components (spec 3.3): the config when enabled, else an empty retained config plus the
    /// component's disabled states; then the kept states of enabled ones.
    /// </summary>
    private async Task PublishCustomComponentsAsync(Session session, AppSettings enabledSettings, CancellationToken cancellationToken)
    {
        foreach (var entry in _catalog.CustomComponents)
        {
            var component = entry.Component;
            var enabled = IsEnabled(entry.Module, enabledSettings);
            string config;
            try
            {
                config = enabled
                    ? component.BuildConfig(new CustomComponentTopics(session.Topics, component.Name, session.Settings.Device, RelayVersion.Current))
                    : string.Empty;
            }
            catch (Exception ex)
            {
                LogCustomConfigFailed(_logger, ex, component.Name);
                continue;
            }

            await _transport.PublishAsync(component.DiscoveryTopic(session.Topics.DeviceId), config, MqttQos.AtLeastOnce, retain: true, cancellationToken).ConfigureAwait(false);

            if (!enabled)
            {
                foreach (var (state, payload) in component.DisabledStates)
                {
                    await PublishCustomStateAsync(session, component, state, payload, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            foreach (var ((name, state), payload) in _customStates)
            {
                if (name == component.Name)
                {
                    await PublishCustomStateAsync(session, component, state, payload, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task PublishDiscoveryAndStateAsync(Session session, CancellationToken cancellationToken)
    {
        var enabledSettings = CurrentValidSettings() ?? session.Settings;
        var device = session.Settings.Device;

        foreach (var entry in _catalog.Entries)
        {
            var topic = session.Topics.Discovery(entry.Entity.Component, entry.Entity.Key);

            // Disabled features get an empty retained config so HA removes their entities (spec 4.3).
            var payload = IsEnabled(entry.Module, enabledSettings)
                ? DiscoveryPayloadBuilder.Build(entry.Entity, session.Topics, device, RelayVersion.Current)
                : string.Empty;

            await _transport.PublishAsync(topic, payload, MqttQos.AtLeastOnce, retain: true, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (key, payload) in _states)
        {
            if (_catalog.TryGet(key, out var entry) && IsEnabled(entry.Module, enabledSettings))
            {
                await PublishStateAsync(session, entry, payload, cancellationToken).ConfigureAwait(false);
            }
        }

        await PublishCustomComponentsAsync(session, enabledSettings, cancellationToken).ConfigureAwait(false);

        LogDiscoveryPublished(_logger, _catalog.Entries.Count(), _states.Count);
    }

    private Task PublishStateAsync(Session session, CatalogEntry entry, string payload, CancellationToken cancellationToken) =>
        _transport.PublishAsync(session.Topics.State(entry.Entity.Key), payload, MqttQos.AtMostOnce, entry.Entity.RetainState, cancellationToken);

    private void OnMessageReceived(object? sender, MqttMessage message)
    {
        var session = Volatile.Read(ref _session);
        if (session is null)
        {
            return;
        }

        if (message.Topic == session.Topics.HomeAssistantStatus)
        {
            if (string.Equals(message.Payload, MqttTopics.Online, StringComparison.OrdinalIgnoreCase))
            {
                LogHomeAssistantRestarted(_logger);
                RunInBackground(ct => PublishDiscoveryAndStateAsync(session, ct));
            }

            return;
        }

        if (session.Topics.TryGetCustomCommand(message.Topic, out var componentName, out var command))
        {
            OnCustomCommand(session, componentName, command, message.Payload);
            return;
        }

        if (!session.Topics.TryGetCommandKey(message.Topic, out var key)
            || !_catalog.TryGet(key, out var entry)
            || entry.Entity.CommandHandler is not { } handler)
        {
            return;
        }

        if (!IsEnabled(entry.Module, CurrentValidSettings() ?? session.Settings))
        {
            LogCommandForDisabledEntity(_logger, key);
            return;
        }

        LogCommandReceived(_logger, key);

        // Never run handlers inside the MQTT receive callback: a slow one would block other commands.
        RunInBackground(ct => handler(message.Payload, ct), key);
    }

    private void OnCustomCommand(Session session, string componentName, string command, string payload)
    {
        if (!_catalog.TryGetCustom(componentName, out var entry)
            || entry.Component.CommandHandler is not { } handler
            || !entry.Component.Commands.Contains(command))
        {
            return;
        }

        var label = $"{componentName}/{command}";
        if (!IsEnabled(entry.Module, CurrentValidSettings() ?? session.Settings))
        {
            LogCommandForDisabledEntity(_logger, label);
            return;
        }

        LogCommandReceived(_logger, label);
        RunInBackground(ct => handler(command, payload, ct), label);
    }

    private void OnDisconnected(object? sender, EventArgs e) => Volatile.Write(ref _session, null);

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (!_settings.Current.IsValid)
        {
            return;
        }

        RunInBackground(async ct =>
        {
            await ReconcileModulesAsync(ct).ConfigureAwait(false);
            if (Volatile.Read(ref _session) is { } session && _transport.IsConnected)
            {
                await PublishDiscoveryAndStateAsync(session, ct).ConfigureAwait(false);
            }
        });
    }

    /// <summary>Starts modules that should run and stops those that should not (spec 6, settings changed).</summary>
    private async Task ReconcileModulesAsync(CancellationToken cancellationToken)
    {
        var settings = CurrentValidSettings();
        if (settings is null)
        {
            return;
        }

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var module in _catalog.Modules)
            {
                var shouldRun = IsEnabled(module, settings);
                var isRunning = _running.Contains(module.Id);

                if (shouldRun && !isRunning)
                {
                    await StartModuleAsync(module, cancellationToken).ConfigureAwait(false);
                }
                else if (!shouldRun && isRunning)
                {
                    await StopModuleAsync(module, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StartModuleAsync(IFeatureModule module, CancellationToken cancellationToken)
    {
        try
        {
            var logger = _loggerFactory.CreateLogger($"SmartHomeRelay.Features.{module.Id}");
            await module.StartAsync(new ModuleContext(this, module, logger), cancellationToken).ConfigureAwait(false);
            _running.Add(module.Id);
            LogModuleStarted(_logger, module.Id);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogModuleStartFailed(_logger, ex, module.Id);
        }
    }

    private async Task StopModuleAsync(IFeatureModule module, CancellationToken cancellationToken)
    {
        _running.Remove(module.Id);
        foreach (var entry in _catalog.Entries.Where(e => e.Module == module))
        {
            _states.TryRemove(entry.Entity.Key, out _);
        }

        foreach (var key in _customStates.Keys.Where(k => _catalog.TryGetCustom(k.Component, out var c) && c.Module == module))
        {
            _customStates.TryRemove(key, out _);
        }

        try
        {
            await module.StopAsync(cancellationToken).ConfigureAwait(false);
            LogModuleStopped(_logger, module.Id);
        }
        catch (Exception ex)
        {
            LogModuleStopFailed(_logger, ex, module.Id);
        }
    }

    private async Task NotifyPowerAwareAsync(Func<IPowerAware, CancellationToken, Task> call, string transition, CancellationToken cancellationToken)
    {
        List<IFeatureModule> running;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            running = [.. _catalog.Modules.Where(m => _running.Contains(m.Id))];
        }
        finally
        {
            _lifecycle.Release();
        }

        // In parallel: the suspend budget is shared, so one slow module must not use it all up.
        await Task.WhenAll(running.OfType<IPowerAware>().Select(async module =>
        {
            try
            {
                await call(module, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                LogPowerTransitionFailed(_logger, ex, ((IFeatureModule)module).Id, transition);
            }
            catch (OperationCanceledException)
            {
                // Out of time; the transition continues without this module.
            }
        })).ConfigureAwait(false);
    }

    private bool IsEnabled(IFeatureModule module, AppSettings settings)
    {
        try
        {
            return module.IsEnabled(settings);
        }
        catch (Exception ex)
        {
            LogModuleIsEnabledFailed(_logger, ex, module.Id);
            return false;
        }
    }

    private AppSettings? CurrentValidSettings()
    {
        var snapshot = _settings.Current;
        return snapshot.IsValid ? snapshot.Settings : null;
    }

    private void RunInBackground(Func<CancellationToken, Task> work, string? commandKey = null)
    {
        var id = Interlocked.Increment(ref _nextBackgroundId);
        var token = _stopping.Token;
        var task = Task.Run(
            async () =>
            {
                try
                {
                    await work(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    if (commandKey is null)
                    {
                        LogBackgroundWorkFailed(_logger, ex);
                    }
                    else
                    {
                        LogCommandFailed(_logger, ex, commandKey);
                    }
                }
                finally
                {
                    _background.TryRemove(id, out _);
                }
            },
            CancellationToken.None);
        _background.TryAdd(id, task);
        if (task.IsCompleted)
        {
            // Finished before it was added; don't leave it behind.
            _background.TryRemove(id, out _);
        }
    }

    private sealed record Session(MqttTopics Topics, AppSettings Settings);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module rejected: {Reason}")]
    private static partial void LogModuleRejected(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Module {ModuleId} started")]
    private static partial void LogModuleStarted(ILogger logger, string moduleId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Module {ModuleId} stopped")]
    private static partial void LogModuleStopped(ILogger logger, string moduleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module {ModuleId} failed to start; other modules are unaffected")]
    private static partial void LogModuleStartFailed(ILogger logger, Exception exception, string moduleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module {ModuleId} failed to stop cleanly")]
    private static partial void LogModuleStopFailed(ILogger logger, Exception exception, string moduleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module {ModuleId} threw from IsEnabled; treating it as disabled")]
    private static partial void LogModuleIsEnabledFailed(ILogger logger, Exception exception, string moduleId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Module {ModuleId} published state for unknown entity {EntityKey}; ignored")]
    private static partial void LogUnknownStateKey(ILogger logger, string moduleId, string entityKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not publish state for {EntityKey}; it will be republished on reconnect")]
    private static partial void LogStatePublishFailed(ILogger logger, Exception exception, string entityKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published discovery for {EntityCount} entities and {StateCount} states")]
    private static partial void LogDiscoveryPublished(ILogger logger, int entityCount, int stateCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Home Assistant came online; republishing discovery and state")]
    private static partial void LogHomeAssistantRestarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Command received for {EntityKey}")]
    private static partial void LogCommandReceived(ILogger logger, string entityKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored command for {EntityKey}: its feature is disabled")]
    private static partial void LogCommandForDisabledEntity(ILogger logger, string entityKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Command for {EntityKey} failed")]
    private static partial void LogCommandFailed(ILogger logger, Exception exception, string entityKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module {ModuleId} failed to handle {Transition}; other modules are unaffected")]
    private static partial void LogPowerTransitionFailed(ILogger logger, Exception exception, string moduleId, string transition);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module {ModuleId} tried an invalid entity update; kept the previous one: {Problem}")]
    private static partial void LogEntityUpdateRejected(ILogger logger, string moduleId, string problem);

    [LoggerMessage(Level = LogLevel.Error, Message = "Building the discovery config of custom component {Component} failed")]
    private static partial void LogCustomConfigFailed(ILogger logger, Exception exception, string component);

    [LoggerMessage(Level = LogLevel.Error, Message = "Background work failed")]
    private static partial void LogBackgroundWorkFailed(ILogger logger, Exception exception);
}
