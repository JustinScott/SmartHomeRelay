namespace SmartHomeRelay.Core.Net;

/// <summary>A download that broke a rule or failed. The message never contains the URL's path or query.</summary>
public sealed class DownloadRejectedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public DownloadRejectedException()
    {
    }

    /// <summary>Creates the exception with a reason.</summary>
    public DownloadRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a reason and the cause.</summary>
    public DownloadRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
