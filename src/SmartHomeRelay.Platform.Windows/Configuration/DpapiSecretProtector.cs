using System.Security.Cryptography;
using System.Text;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Platform.Windows.Configuration;

/// <summary>DPAPI with <see cref="DataProtectionScope.CurrentUser"/>: only this Windows user can decrypt (spec 7).</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    // Ties blobs to this app, so another app using DPAPI for the same user can't trivially reuse them.
    private static readonly byte[] s_entropy = Encoding.UTF8.GetBytes("SmartHomeRelay.Settings.v1");

    /// <inheritdoc />
    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(bytes, s_entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);

        byte[] protectedBytes;
        try
        {
            protectedBytes = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The protected value is not valid base64.", ex);
        }

        var bytes = ProtectedData.Unprotect(protectedBytes, s_entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
