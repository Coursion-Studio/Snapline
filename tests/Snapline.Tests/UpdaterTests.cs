using System.IO;
using System.Security.Cryptography;
using System.Text;
using Snapline.Updates;

namespace Snapline.Tests;

public class UpdaterTests
{
    private static (string publicXml, Func<byte[], string> sign) TestKey()
    {
        var rsa = RSA.Create(2048);
        return (rsa.ToXmlString(false), data => Convert.ToBase64String(rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] Payload() => Encoding.UTF8.GetBytes(new string('x', 5000) + "installer");

    [Fact]
    public void Genuine_update_is_accepted()
    {
        var (pub, sign) = TestKey();
        var data = Payload();
        Updater.Verify(data, new UpdateInfo { Sha256 = Sha(data), Signature = sign(data) }, pub);
    }

    [Fact]
    public void Changed_byte_is_rejected_by_checksum()
    {
        var (pub, sign) = TestKey();
        var data = Payload();
        var info = new UpdateInfo { Sha256 = Sha(data), Signature = sign(data) };
        var changed = (byte[])data.Clone();
        changed[100] ^= 0xFF;
        var ex = Assert.ThrowsAny<CryptographicException>(() => Updater.Verify(changed, info, pub));
        Assert.Contains("checksum", ex.Message);
    }

    [Fact]
    public void Changed_file_with_matching_checksum_is_rejected_by_signature()
    {
        var (pub, sign) = TestKey();
        var data = Payload();
        var signature = sign(data);
        var changed = (byte[])data.Clone();
        changed[100] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => Updater.Verify(changed, new UpdateInfo { Sha256 = Sha(changed), Signature = signature }, pub));
    }

    [Fact]
    public void Signature_from_another_key_is_rejected()
    {
        var (pub, _) = TestKey();
        var (_, otherSign) = TestKey();
        var data = Payload();
        Assert.ThrowsAny<CryptographicException>(() => Updater.Verify(data, new UpdateInfo { Sha256 = Sha(data), Signature = otherSign(data) }, pub));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void Junk_signature_is_rejected(string signature)
    {
        var (pub, _) = TestKey();
        var data = Payload();
        Assert.ThrowsAny<CryptographicException>(() => Updater.Verify(data, new UpdateInfo { Sha256 = Sha(data), Signature = signature }, pub));
    }

    [Fact]
    public void Shipped_public_key_is_valid()
    {
        using var rsa = RSA.Create();
        rsa.FromXmlString(UpdateKey.PublicXml);
        Assert.True(rsa.KeySize >= 2048);
    }

    private const string Feed = """
        {
          "version": "1.2.3",
          "published": "2026-11-20",
          "url": "https://example.com/SnaplineSetup-1.2.3.exe",
          "sha256": "abc",
          "signature": "def",
          "notes": [ "First", "Second" ]
        }
        """;

    [Fact]
    public void Feed_is_parsed()
    {
        var info = Updater.ParseFeed(Encoding.UTF8.GetBytes(Feed));
        Assert.Equal(new Version(1, 2, 3), info.Version);
        Assert.Equal("https://example.com/SnaplineSetup-1.2.3.exe", info.Url);
        Assert.Equal(new[] { "First", "Second" }, info.Notes);
    }

    [Fact]
    public void Feed_with_byte_order_mark_is_parsed()
    {
        // Windows PowerShell's Set-Content -Encoding utf8 writes a BOM; this once broke the updater.
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Feed)).ToArray();
        Assert.Equal(new Version(1, 2, 3), Updater.ParseFeed(bytes).Version);
    }

    [Fact]
    public void Incomplete_feed_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => Updater.ParseFeed(Encoding.UTF8.GetBytes("""{ "version": "1.0.0" }""")));
    }

    [Theory]
    [InlineData(1, 2, 3, 4, "1.2.3")]
    [InlineData(1, 2, -1, -1, "1.2.0")]
    public void Versions_are_compared_on_three_parts(int a, int b, int c, int d, string expected)
    {
        var v = c < 0 ? new Version(a, b) : new Version(a, b, c, d);
        Assert.Equal(Version.Parse(expected), Updater.Norm(v));
    }
}
