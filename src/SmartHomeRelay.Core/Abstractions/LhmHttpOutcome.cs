namespace SmartHomeRelay.Core.Abstractions;

/// <summary>How a <c>data.json</c> request went.</summary>
public enum LhmHttpOutcome
{
    /// <summary>HTTP 200 with a body within the size limit.</summary>
    Ok,

    /// <summary>The connection was refused: nothing is listening.</summary>
    NotListening,

    /// <summary>No answer within the time limit.</summary>
    Timeout,

    /// <summary>The server asked for a login and none is configured.</summary>
    LoginRequired,

    /// <summary>The server rejected the configured login.</summary>
    LoginFailed,

    /// <summary>A login is configured but the server never asked for it (not how LHM behaves with authentication on).</summary>
    LoginNotRequested,

    /// <summary>The body was larger than allowed.</summary>
    TooLarge,

    /// <summary>Any other HTTP status, or a response that isn't JSON.</summary>
    UnexpectedResponse,
}
