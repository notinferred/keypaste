using System.Security.Cryptography;
using KeePassLib.Cryptography.Cipher;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The Twofish engine every vault is opened and saved through, held to the answers its authors published for 256-bit
/// keys, and to the CBC chaining and PKCS#7 padding a Twofish KDBX file is written with (D-0292).
/// </summary>
/// <remarks>
/// Each known answer is the first block of a CBC encryption under a zero IV, which is the block cipher's own output.
/// The values are from the Twofish known-answer diskette at schneier.com: <c>ECB_IVAL.TXT</c>, <c>ECB_TBL.TXT</c>
/// and <c>CBC_E_M.TXT</c>, whose 256-bit vectors are also the paper's appendix.
/// </remarks>
public sealed class TwofishCipherTests
{
    private const string _key = "0123456789ABCDEFFEDCBA987654321000112233445566778899AABBCCDDEEFF";

    [Fact]
    public void Twofish_is_in_the_pool_every_vault_is_read_and_written_with_under_KeePassXCs_uuid()
    {
        Assert.IsType<TwofishCipherEngine>(Engine());
        Assert.Equal(Convert.FromHexString("AD68F29F576F4BB9A36AD47AF965346C"), TwofishCipherEngine.TwofishUuid.UuidBytes);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", "00000000000000000000000000000000", "57FF739D4DC92C1BD7FC01700CC8216F")]
    [InlineData(_key, "00000000000000000000000000000000", "37527BE0052334B89F0CFCCAE87CFA20")]
    [InlineData("248A7F3528B168ACFDD1386E3F51E30C2E2158BC3E5FC714C1EEECA0EA696D48", "431058F4DBC7F734DA4F02F04CC4F459", "37FE26FF1CF66175F5DDF4C33B97A205")]
    public void A_256_bit_key_gives_the_published_answer_and_decrypts_it_back(string key, string plaintext, string ciphertext)
    {
        var encrypted = Encrypt(Convert.FromHexString(key), new byte[16], Convert.FromHexString(plaintext));

        Assert.Equal(ciphertext, Convert.ToHexString(encrypted, 0, 16));
        Assert.Equal(plaintext, Convert.ToHexString(Decrypt(Convert.FromHexString(key), new byte[16], encrypted)));
    }

    /// <summary>The table test: 49 encryptions, each key the plaintext before it followed by the first half of the key before it.</summary>
    [Fact]
    public void The_256_bit_table_test_reaches_its_published_49th_key_and_answer()
    {
        var key = new byte[32];
        var plaintext = new byte[16];
        var ciphertext = Array.Empty<byte>();
        var keys = new List<string>();

        for (var i = 1; i <= 49; i++)
        {
            keys.Add(Convert.ToHexString(key));
            ciphertext = Encrypt(key, new byte[16], plaintext)[..16];
            key = [.. plaintext, .. key[..16]];
            plaintext = ciphertext;
        }

        Assert.Equal("D43BB7556EA32E46F2A282B7D45B4E0D57FF739D4DC92C1BD7FC01700CC8216F", keys[3]);
        Assert.Equal("248A7F3528B168ACFDD1386E3F51E30C2E2158BC3E5FC714C1EEECA0EA696D48", keys[48]);
        Assert.Equal("37FE26FF1CF66175F5DDF4C33B97A205", Convert.ToHexString(ciphertext));
    }

    /// <summary>
    /// The CBC encryption Monte Carlo test's first three rounds: each chains ten thousand blocks through one stream, each
    /// plaintext the ciphertext two blocks back, then takes its next key, IV and plaintext from the last two ciphertexts.
    /// </summary>
    [Fact]
    public void The_256_bit_cbc_monte_carlo_test_gives_its_first_published_rounds()
    {
        string[] published = ["EA7162E65490B03B1AE4871FB35EF23B", "549FF6C6274F034211C31FADF3F22571", "CF222616B0E4F8E48967D769456B916B"];
        var key = new byte[32];
        var iv = new byte[16];
        var plaintext = new byte[16];

        foreach (var expected in published)
        {
            var (last, beforeLast) = MonteCarloRound(key, iv, plaintext);
            Assert.Equal(expected, Convert.ToHexString(last));

            for (var b = 0; b < 16; b++)
            {
                key[b] ^= beforeLast[b];
                key[16 + b] ^= last[b];
            }

            iv = last;
            plaintext = beforeLast;
        }
    }

    [Fact]
    public void Every_length_up_to_three_blocks_round_trips_padded_to_the_next_whole_block()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(16);

        for (var length = 0; length <= 48; length++)
        {
            var plaintext = RandomNumberGenerator.GetBytes(length);
            var ciphertext = Encrypt(key, iv, plaintext);

            Assert.Equal(((length / 16) + 1) * 16, ciphertext.Length);
            Assert.Equal(plaintext, Decrypt(key, iv, ciphertext));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void A_short_last_block_is_padded_with_the_count_of_bytes_it_lacks(int length)
    {
        var data = Enumerable.Range(1, length).Select(i => (byte)i).ToArray();
        byte[] padded = [.. data, .. Enumerable.Repeat((byte)(16 - length), 16 - length)];

        Assert.Equal(Encrypt(Convert.FromHexString(_key), new byte[16], padded)[..16], Encrypt(Convert.FromHexString(_key), new byte[16], data));
    }

    [Fact]
    public void A_whole_last_block_gains_a_block_of_sixteen_sixteens_chained_to_it()
    {
        var key = Convert.FromHexString(_key);
        var ciphertext = Encrypt(key, new byte[16], Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));
        var padding = ciphertext[..16].Select(b => (byte)(b ^ 16)).ToArray();

        Assert.Equal(32, ciphertext.Length);
        Assert.Equal(Encrypt(key, new byte[16], padding)[..16], ciphertext[16..]);
    }

    /// <summary>Flipping a bit of the first block flips the same bit of the decrypted padding block.</summary>
    [Theory]
    [InlineData(15)]
    [InlineData(0)]
    public void Damaged_padding_is_refused(int index)
    {
        var key = Convert.FromHexString(_key);
        var ciphertext = Encrypt(key, new byte[16], Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));
        ciphertext[index] ^= 1;

        Assert.Throws<CryptographicException>(() => Decrypt(key, new byte[16], ciphertext));
    }

    [Fact]
    public void Ciphertext_that_is_not_whole_blocks_or_is_empty_is_refused()
    {
        var key = Convert.FromHexString(_key);
        var ciphertext = Encrypt(key, new byte[16], Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));

        Assert.Throws<CryptographicException>(() => Decrypt(key, new byte[16], ciphertext[..^1]));
        Assert.Throws<CryptographicException>(() => Decrypt(key, new byte[16], []));
    }

    private static ICipherEngine Engine()
    {
        var engine = CipherPool.GlobalPool.GetCipher(TwofishCipherEngine.TwofishUuid);
        Assert.NotNull(engine);
        return engine;
    }

    private static byte[] Encrypt(byte[] key, byte[] iv, byte[] plaintext)
    {
        using var sink = new MemoryStream();
        using (var stream = Engine().EncryptStream(sink, key, iv))
        {
            stream.Write(plaintext);
        }

        return sink.ToArray();
    }

    private static byte[] Decrypt(byte[] key, byte[] iv, byte[] ciphertext)
    {
        using var source = new MemoryStream(ciphertext, writable: false);
        using var stream = Engine().DecryptStream(source, key, iv);
        using var plaintext = new MemoryStream();
        stream.CopyTo(plaintext);
        return plaintext.ToArray();
    }

    /// <returns>The last ciphertext block and the one before it.</returns>
    private static (byte[] Last, byte[] BeforeLast) MonteCarloRound(byte[] key, byte[] iv, byte[] first)
    {
        using var sink = new MemoryStream();
        using var stream = Engine().EncryptStream(sink, key, iv);

        var plaintext = first;
        var last = Array.Empty<byte>();
        var beforeLast = Array.Empty<byte>();

        for (var j = 0; j < 10_000; j++)
        {
            stream.Write(plaintext);
            Assert.Equal(16L * (j + 1), sink.Length);

            beforeLast = last;
            last = sink.GetBuffer().AsSpan((int)sink.Length - 16, 16).ToArray();
            plaintext = j == 0 ? iv : beforeLast;
        }

        return (last, beforeLast);
    }
}
