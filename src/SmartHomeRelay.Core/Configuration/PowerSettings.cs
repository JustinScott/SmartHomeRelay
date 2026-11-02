namespace SmartHomeRelay.Core.Configuration;

/// <summary>Power feature settings (spec 5.2). Destructive commands are disabled by default.</summary>
public sealed class PowerSettings : FeatureToggle
{
    /// <summary>Allow the <c>shutdown</c> command.</summary>
    public bool AllowShutdown { get; set; }

    /// <summary>Allow the <c>restart</c> command.</summary>
    public bool AllowRestart { get; set; }

    /// <summary>Allow the <c>hibernate</c> command.</summary>
    public bool AllowHibernate { get; set; }

    /// <summary>Allow the <c>sign_out</c> command.</summary>
    public bool AllowSignOut { get; set; }

    /// <summary>
    /// Delay before shutdown and restart, during which Windows shows its countdown. 0 means immediate.
    /// </summary>
    public int ShutdownDelaySeconds { get; set; } = 60;
}
