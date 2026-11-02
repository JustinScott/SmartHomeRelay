namespace SmartHomeRelay.Core.Abstractions;

/// <summary>
/// Encrypts secrets for storage in settings (spec 7: DPAPI, CurrentUser scope).
/// Plain-text secrets must never be logged or written to disk.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypts <paramref name="plainText"/> and returns a base64 string safe to store.</summary>
    string Protect(string plainText);

    /// <summary>Decrypts a value produced by <see cref="Protect"/>.</summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The value cannot be decrypted by this user.</exception>
    string Unprotect(string protectedValue);
}
