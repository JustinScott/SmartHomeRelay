using System.Security.Cryptography;
using System.Text;
using SmartHomeRelay.Core.Abstractions;

namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>Reversible stand-in for DPAPI: <c>enc:</c> + base64. Anything else fails like a foreign DPAPI blob.</summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    private const string Prefix = "enc:";

    public string Protect(string plainText) => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));

    public string Unprotect(string protectedValue) =>
        protectedValue.StartsWith(Prefix, StringComparison.Ordinal)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue[Prefix.Length..]))
            : throw new CryptographicException("Not protected for this user.");
}
