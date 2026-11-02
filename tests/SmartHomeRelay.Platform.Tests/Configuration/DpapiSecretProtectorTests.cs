using System.Security.Cryptography;
using SmartHomeRelay.Platform.Windows.Configuration;

namespace SmartHomeRelay.Platform.Tests.Configuration;

[Trait("Category", "Windows")]
public sealed class DpapiSecretProtectorTests
{
    private readonly DpapiSecretProtector _protector = new();

    [Fact]
    public void Protect_ThenUnprotect_ReturnsOriginal()
    {
        const string secret = "correct horse battery staple ✓";

        var protectedValue = _protector.Protect(secret);

        _protector.Unprotect(protectedValue).ShouldBe(secret);
    }

    [Fact]
    public void Protect_DoesNotContainPlainText()
    {
        const string secret = "plaintext-password";

        var protectedValue = _protector.Protect(secret);

        protectedValue.ShouldNotContain(secret);
        Convert.FromBase64String(protectedValue).Length.ShouldBeGreaterThan(secret.Length);
    }

    [Fact]
    public void Unprotect_NotBase64_ThrowsCryptographicException()
    {
        Should.Throw<CryptographicException>(() => _protector.Unprotect("not base64!"));
    }

    [Fact]
    public void Unprotect_TamperedValue_ThrowsCryptographicException()
    {
        var bytes = Convert.FromBase64String(_protector.Protect("secret"));
        bytes[^1] ^= 0xFF;

        Should.Throw<CryptographicException>(() => _protector.Unprotect(Convert.ToBase64String(bytes)));
    }
}
