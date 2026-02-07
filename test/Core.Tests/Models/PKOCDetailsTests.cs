using CredBench.Core.Models.TechnologyDetails;
using NUnit.Framework;

namespace CredBench.Core.Tests.Models;

[TestFixture]
public class PKOCDetailsTests
{
    // Known ECC P-256 uncompressed public key (04 + 32-byte X + 32-byte Y = 65 bytes = 130 hex chars)
    // X = A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F90
    // Y = 1122334455667788990011223344556677889900112233445566778899001122
    private const string TestPublicKeyHex =
        "04" +
        "A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F90" +
        "1122334455667788990011223344556677889900112233445566778899001122";

    private const string ExpectedX = "A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F90";

    // --- PublicKeyX extraction ---

    [Test]
    public void PublicKeyX_ExtractsXComponent_FromUncompressedKey()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        Assert.That(details.PublicKeyX, Is.EqualTo(ExpectedX));
    }

    [Test]
    public void PublicKeyX_ReturnsNull_WhenPublicKeyHexTooShort()
    {
        var details = new PKOCDetails { PublicKeyHex = "04A1B2C3" };

        Assert.That(details.PublicKeyX, Is.Null);
    }

    [Test]
    public void PublicKeyX_ReturnsNull_WhenPublicKeyHexIsNull()
    {
        var details = new PKOCDetails { PublicKeyHex = null };

        Assert.That(details.PublicKeyX, Is.Null);
    }

    // --- 256-bit credential ---

    [Test]
    public void Credential256Hex_IsFullXComponent()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        Assert.That(details.Credential256Hex, Is.EqualTo(ExpectedX));
    }

    [Test]
    public void Credential256Bits_Has256Characters()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        Assert.That(details.Credential256Bits, Has.Length.EqualTo(256));
    }

    [Test]
    public void Credential256Bits_ContainsOnlyBinaryDigits()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        Assert.That(details.Credential256Bits, Does.Match(@"^[01]+$"));
    }

    [Test]
    public void Credential256Bits_MatchesHexValue()
    {
        // Use a known small X for easy manual verification
        // X starts with A1 = 10100001, B2 = 10110010
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        Assert.That(details.Credential256Bits, Does.StartWith("10100001" + "10110010"));
    }

    // --- 75-bit credential ---

    [Test]
    public void Credential75Hex_ExtractsLower75Bits()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        Assert.That(details.Credential75Hex, Is.Not.Null);
        // 75 bits = 9 full bytes + 3 remainder bits = 10 bytes = 20 hex chars
        Assert.That(details.Credential75Hex, Has.Length.EqualTo(20));
    }

    [Test]
    public void Credential75Bits_Has75Characters()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        Assert.That(details.Credential75Bits, Has.Length.EqualTo(75));
    }

    [Test]
    public void Credential75Bits_IsLower75BitsOf256()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        // 75-bit should be the last 75 characters of the 256-bit string
        var expected = details.Credential256Bits![181..];
        Assert.That(details.Credential75Bits, Is.EqualTo(expected));
    }

    [Test]
    public void Credential75Hex_MatchesCredential75Bits()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        // Convert 75-bit hex back to binary and verify it matches Credential75Bits
        var hexBits = HexToBinary(details.Credential75Hex!);
        // Hex is 10 bytes = 80 bits, but only lower 75 are meaningful
        // The top 5 bits of the first byte should be zero (masked)
        Assert.That(hexBits[5..], Is.EqualTo(details.Credential75Bits));
    }

    // --- 64-bit credential ---

    [Test]
    public void Credential64Hex_ExtractsLower64Bits()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        Assert.That(details.Credential64Hex, Is.Not.Null);
        // 64 bits = 8 bytes = 16 hex chars
        Assert.That(details.Credential64Hex, Has.Length.EqualTo(16));
    }

    [Test]
    public void Credential64Hex_IsLast16CharsOfX()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        // Lower 64 bits = last 16 hex chars of X component
        Assert.That(details.Credential64Hex, Is.EqualTo(ExpectedX[48..]));
    }

    [Test]
    public void Credential64Bits_Has64Characters()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        Assert.That(details.Credential64Bits, Has.Length.EqualTo(64));
    }

    [Test]
    public void Credential64Bits_IsLower64BitsOf256()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        var expected = details.Credential256Bits![192..];
        Assert.That(details.Credential64Bits, Is.EqualTo(expected));
    }

    [Test]
    public void Credential64Bits_MatchesCredential64Hex()
    {
        var details = new PKOCDetails { PublicKeyHex = TestPublicKeyHex };

        var hexBits = HexToBinary(details.Credential64Hex!);
        Assert.That(details.Credential64Bits, Is.EqualTo(hexBits));
    }

    // --- Null propagation ---

    [Test]
    public void AllCredentials_ReturnNull_WhenPublicKeyHexIsNull()
    {
        var details = new PKOCDetails { PublicKeyHex = null };

        Assert.That(details.Credential256Hex, Is.Null);
        Assert.That(details.Credential256Bits, Is.Null);
        Assert.That(details.Credential75Hex, Is.Null);
        Assert.That(details.Credential75Bits, Is.Null);
        Assert.That(details.Credential64Hex, Is.Null);
        Assert.That(details.Credential64Bits, Is.Null);
    }

    [Test]
    public void AllCredentials_ReturnNull_WhenKeyTooShort()
    {
        var details = new PKOCDetails { PublicKeyHex = "04AABB" };

        Assert.That(details.PublicKeyX, Is.Null);
        Assert.That(details.Credential256Hex, Is.Null);
        Assert.That(details.Credential75Hex, Is.Null);
        Assert.That(details.Credential64Hex, Is.Null);
    }

    // --- Known value verification ---

    [Test]
    public void CredentialParsing_WithKnownKey_ProducesCorrectValues()
    {
        // Use a simple key where X is all zeros except last byte = 0xFF
        var x = new string('0', 62) + "FF";
        var y = new string('0', 64);
        var publicKey = "04" + x + y;

        var details = new PKOCDetails { PublicKeyHex = publicKey };

        // 256-bit hex = full X
        Assert.That(details.Credential256Hex, Is.EqualTo(x));

        // 64-bit hex = last 16 hex chars of X = "00000000000000FF"
        Assert.That(details.Credential64Hex, Is.EqualTo("00000000000000FF"));

        // 64-bit binary = last 64 bits, only last 8 are 1s
        Assert.That(details.Credential64Bits, Is.EqualTo(
            "00000000000000000000000000000000000000000000000000000000" + "11111111"));

        // 256-bit binary = 248 zeros + 11111111
        Assert.That(details.Credential256Bits, Does.EndWith("11111111"));
        Assert.That(details.Credential256Bits!.Replace("0", "").Replace("1", ""), Is.Empty);
    }

    [Test]
    public void Credential75Hex_MasksUpperBitsCorrectly()
    {
        // X with all FF bytes — lower 75 bits should have top 5 bits of first byte masked to 0
        var x = new string('F', 64);
        var y = new string('0', 64);
        var publicKey = "04" + x + y;

        var details = new PKOCDetails { PublicKeyHex = publicKey };

        // 75 bits: 9 full bytes (all FF) + 3 remainder bits from the byte before
        // Byte at srcOffset-1 = FF, mask = (1<<3)-1 = 0x07, so first byte = 07
        // Remaining 9 bytes = all FF
        Assert.That(details.Credential75Hex, Does.StartWith("07"));
        Assert.That(details.Credential75Hex, Does.EndWith("FFFFFFFFFFFFFFFFFF"));

        // 75-bit Wiegand = all 1s for lower 75 bits
        Assert.That(details.Credential75Bits, Has.Length.EqualTo(75));
        Assert.That(details.Credential75Bits, Is.EqualTo(new string('1', 75)));
    }

    // Helper to convert hex to binary string (mirrors the model's logic)
    private static string HexToBinary(string hex)
    {
        var bits = new char[hex.Length * 4];
        var pos = 0;
        for (var i = 0; i < hex.Length; i += 2)
        {
            var value = Convert.ToByte(hex.Substring(i, 2), 16);
            for (var bit = 7; bit >= 0; bit--)
                bits[pos++] = (value & (1 << bit)) != 0 ? '1' : '0';
        }
        return new string(bits, 0, pos);
    }
}
