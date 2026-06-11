using System.Security.Cryptography;
using Meshmakers.Octo.Sdk.Common.Encryption;

namespace Sdk.Common.Tests.Encryption;

/// <summary>
/// Behaviour + wire-format + cross-replica round-trip tests for
/// <see cref="InstanceSecretCrypto" />. The cross-replica test proves the operational unification
/// claim of OctoMesh M1: two independent service instances holding the same key value can decrypt
/// each other's ciphertext byte-for-byte.
/// </summary>
public class InstanceSecretCryptoTests
{
    private static byte[] FreshKey() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Encrypt_Then_Decrypt_RoundTrip()
    {
        var crypto = new InstanceSecretCrypto();
        var key = FreshKey();
        const string plaintext = "secret-payload";

        var ciphertext = crypto.Encrypt(key, plaintext);

        Assert.StartsWith("enc:v1:", ciphertext);
        Assert.Equal(plaintext, crypto.Decrypt(key, ciphertext));
    }

    [Fact]
    public void Decrypt_PlaintextValue_ReturnsValueUnchanged()
    {
        var crypto = new InstanceSecretCrypto();
        var key = FreshKey();

        Assert.Equal("plain-value", crypto.Decrypt(key, "plain-value"));
    }

    [Fact]
    public void Decrypt_EmptyString_ReturnsEmpty()
    {
        var crypto = new InstanceSecretCrypto();
        var key = FreshKey();

        Assert.Equal(string.Empty, crypto.Decrypt(key, string.Empty));
    }

    [Theory]
    [InlineData("enc:v1:abc", true)]
    [InlineData("enc:v2:abc", true)]   // future sentinel: still detected as encrypted
    [InlineData("plain-value", false)]
    [InlineData("encoded-value", false)] // 'enc' prefix but not 'enc:'
    [InlineData("", false)]
    public void IsEncrypted_RecognisesAnyEncPrefix(string value, bool expected)
    {
        var crypto = new InstanceSecretCrypto();
        Assert.Equal(expected, crypto.IsEncrypted(value));
    }

    [Fact]
    public void Encrypt_TwoCalls_ProduceDifferentCiphertext()
    {
        // Random nonce per call -> ciphertext must vary even for identical plaintext + key.
        var crypto = new InstanceSecretCrypto();
        var key = FreshKey();
        const string plaintext = "same-input";

        Assert.NotEqual(crypto.Encrypt(key, plaintext), crypto.Encrypt(key, plaintext));
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        var crypto = new InstanceSecretCrypto();
        var keyA = FreshKey();
        var keyB = FreshKey();
        var ciphertext = crypto.Encrypt(keyA, "secret");

        Assert.Throws<AuthenticationTagMismatchException>(() => crypto.Decrypt(keyB, ciphertext));
    }

    [Fact]
    public void CrossReplicaRoundTrip_TwoServiceInstancesWithSameKey_DecryptEachOther()
    {
        // Simulates the operational unification: ai-services replica encrypts,
        // communication-controller replica decrypts (or vice versa). With the
        // Helm-provided shared key, this round-trip must succeed by design.
        var cryptoA = new InstanceSecretCrypto();
        var cryptoB = new InstanceSecretCrypto();
        var sharedKey = FreshKey();
        const string plaintext = "cross-service-payload";

        var producedByA = cryptoA.Encrypt(sharedKey, plaintext);
        Assert.Equal(plaintext, cryptoB.Decrypt(sharedKey, producedByA));

        var producedByB = cryptoB.Encrypt(sharedKey, plaintext);
        Assert.Equal(plaintext, cryptoA.Decrypt(sharedKey, producedByB));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(64)]
    public void Encrypt_WithInvalidKeyLength_ThrowsArgumentException(int wrongLength)
    {
        var crypto = new InstanceSecretCrypto();
        Assert.Throws<ArgumentException>(() => crypto.Encrypt(new byte[wrongLength], "secret"));
    }

    [Fact]
    public void Decrypt_UnsupportedSentinel_Throws()
    {
        var crypto = new InstanceSecretCrypto();
        var key = FreshKey();

        Assert.Throws<CryptographicException>(() => crypto.Decrypt(key, "enc:v2:abc"));
    }

    [Fact]
    public void Decrypt_NotBase64_Throws()
    {
        var crypto = new InstanceSecretCrypto();
        var key = FreshKey();

        Assert.Throws<CryptographicException>(() => crypto.Decrypt(key, "enc:v1:!!!not-base64!!!"));
    }

    [Fact]
    public void Decrypt_Truncated_Throws()
    {
        var crypto = new InstanceSecretCrypto();
        var key = FreshKey();

        // "enc:v1:AAAA" -> base64 decodes to 3 bytes, less than nonce(12) + tag(16) minimum.
        Assert.Throws<CryptographicException>(() => crypto.Decrypt(key, "enc:v1:AAAA"));
    }

    [Fact]
    public void Encrypt_EmptyPlaintext_RoundTrips()
    {
        var crypto = new InstanceSecretCrypto();
        var key = FreshKey();

        var ciphertext = crypto.Encrypt(key, string.Empty);
        Assert.StartsWith("enc:v1:", ciphertext);
        Assert.Equal(string.Empty, crypto.Decrypt(key, ciphertext));
    }

    [Fact]
    public void Encrypt_UnicodePlaintext_RoundTrips()
    {
        var crypto = new InstanceSecretCrypto();
        var key = FreshKey();
        const string plaintext = "secret with umlauts: äöüß and emoji 🔐";

        var ciphertext = crypto.Encrypt(key, plaintext);
        Assert.Equal(plaintext, crypto.Decrypt(key, ciphertext));
    }
}
