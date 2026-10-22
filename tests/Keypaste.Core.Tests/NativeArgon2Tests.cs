using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Native;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// KeePassLib derives with the libargon2 keypaste carries beside its binary on Windows and macOS, or with the system's on
/// Linux, and that derivation is the managed one byte for byte and gives the reference's known answers (D-NEW-X81,
/// D-NEW-X82).
/// </summary>
/// <remarks>
/// <para>
/// The native side is KeePassLib's own native call, <c>Argon2Native</c>, alone. <c>Transform</c> falls back to the managed code
/// whenever that call gives nothing, so a native derivation through <c>Transform</c> would equal the managed one, and take its
/// time, even with no library working; here the call must give the key.
/// </para>
/// <para>
/// Alone in its collection, because a managed derivation turns KeePassLib's process-wide <see cref="NativeLib.AllowNative"/>
/// off while it runs. The production-cost case prints both derivations' time per pass, which verify.sh shows live, for the
/// record of each runner.
/// </para>
/// </remarks>
[Collection(nameof(NativeArgon2Alone))]
public sealed class NativeArgon2Tests(ITestOutputHelper output)
{
    private static readonly byte[] _message = SHA256.HashData("keypaste native argon2 message"u8);

    private static readonly byte[] _salt = SHA256.HashData("keypaste native argon2 salt"u8);

    // KeePassLib's Unix imports, in the order it tries them.
    private static readonly string[] _sonames = ["libargon2.so.0", "libargon2.so.1"];

    /// <summary>RFC 9106 §5: 32 bytes of 0x01, salt of 0x02, secret of 0x03, associated data of 0x04, 32 KiB, 3 passes, 4 lanes.</summary>
    /// <remarks>KeePassLib's native path refuses a secret and associated data, so these reach only the managed code.</remarks>
    [Theory]
    [InlineData(Argon2Type.D, "512b391b6f1162975371d30919734294f868e3be3984f3c1a13a4db9fabe4acb")]
    [InlineData(Argon2Type.ID, "0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659")]
    public void The_managed_derivation_gives_RFC_9106_s_tags(Argon2Type type, string tag)
    {
        var parameters = Parameters(type, Repeat(0x02, 16), lanes: 4, memory: 32 * 1024, passes: 3);
        parameters.SetByteArray(Argon2Kdf.ParamSecretKey, Repeat(0x03, 8));
        parameters.SetByteArray(Argon2Kdf.ParamAssocData, Repeat(0x04, 12));

        Assert.Equal(tag, Convert.ToHexStringLower(Managed(type, Repeat(0x01, 32), parameters)));
    }

    /// <summary>Upstream's <c>test.c</c> answers for Argon2id through <c>argon2_hash</c>, the call KeePassLib makes: "password", "somesalt", 2 passes over 256 KiB.</summary>
    [Theory]
    [InlineData(1u, "9dfeb910e80bad0311fee20f9c0e2b12c17987b4cac90c2ef54d5b3021c68bfe")]
    [InlineData(2u, "6d093c501fd5999645e0ea3bf620d7b8be7fd2db59c20d9fff9539da2bf57037")]
    public void The_native_derivation_gives_the_reference_s_Argon2id_answers(uint lanes, string hash)
    {
        var parameters = Parameters(Argon2Type.ID, Encoding.ASCII.GetBytes("somesalt"), lanes, memory: 256 * 1024, passes: 2);

        Assert.Equal(hash, Convert.ToHexStringLower(Native(Argon2Type.ID, Encoding.ASCII.GetBytes("password"), parameters)));
    }

    [Theory]
    [InlineData(Argon2Type.D, 0x13u, 1024 * 1024ul, 1ul, 2u)]
    [InlineData(Argon2Type.ID, 0x13u, 1024 * 1024ul, 1ul, 2u)]
    [InlineData(Argon2Type.D, 0x10u, 8 * 1024 * 1024ul, 3ul, 4u)]
    [InlineData(Argon2Type.ID, 0x13u, 8 * 1024 * 1024ul, 3ul, 4u)]
    // Three lanes leave 8 MiB short of a whole number of segments, which both round down, as a KeePassXC thread count can.
    [InlineData(Argon2Type.D, 0x13u, 8 * 1024 * 1024ul, 2ul, 3u)]
    [InlineData(Argon2Type.ID, 0x13u, 64 * 1024 * 1024ul, 2ul, 2u)]
    public void The_native_derivation_is_the_managed_one_byte_for_byte(Argon2Type type, uint version, ulong memory, ulong passes, uint lanes)
    {
        var parameters = Parameters(type, _salt, lanes, memory, passes, version);

        Assert.Equal(Managed(type, _message, parameters), Native(type, _message, parameters));
    }

    [Fact]
    public void At_the_production_cost_both_derive_one_key_and_each_one_s_time_per_pass_is_printed()
    {
        var parameters = Parameters(Argon2Type.D, _salt, KdbxFormat.Argon2Parallelism, KdbxFormat.Argon2Memory, KdbxFormat.Argon2Iterations);
        var native = TimeSpan.MaxValue;
        var managed = TimeSpan.MaxValue;

        for (var run = 0; run < 3; run++)
        {
            var (nativeKey, nativeTook) = Timed(() => Native(Argon2Type.D, _message, parameters));
            var (managedKey, managedTook) = Timed(() => Managed(Argon2Type.D, _message, parameters));
            Assert.Equal(managedKey, nativeKey);
            native = nativeTook < native ? nativeTook : native;
            managed = managedTook < managed ? managedTook : managed;
        }

        var passes = (double)KdbxFormat.Argon2Iterations;
        output.WriteLine(
            $"Argon2d at 64 MiB and 2 lanes on {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}: " +
            $"{CoreInfo.NativeArgon2} {native.TotalMilliseconds / passes:F1} ms per pass, " +
            $"managed {managed.TotalMilliseconds / passes:F1} ms per pass, managed/native {managed / native:F2}");
    }

    /// <summary>
    /// KeePassLib's native derivation alone, which must give the key, from the file keypaste carries on Windows and macOS or the
    /// system's libargon2 on Linux. A Linux without one skips, as its KeePassLib derives in managed code alone.
    /// </summary>
    private static byte[] Native(Argon2Type type, byte[] message, KdfParameters parameters)
    {
        var expected = NativeArgon2.BundledFileName is { } file ? Path.Combine(AppContext.BaseDirectory, file) : SystemLibrary();
        Assert.SkipWhen(expected is null, "this Linux has no libargon2, so KeePassLib derives in managed code alone");
        var argon2Native = typeof(Argon2Kdf).GetMethod("Argon2Native", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(argon2Native is not null, "KeePassLib's Argon2Kdf no longer has Argon2Native; call its native derivation another way");
        object?[] arguments =
        [
            message,
            parameters.GetByteArray(Argon2Kdf.ParamSalt),
            parameters.GetUInt32(Argon2Kdf.ParamParallelism, 0),
            parameters.GetUInt64(Argon2Kdf.ParamMemory, 0),
            parameters.GetUInt64(Argon2Kdf.ParamIterations, 0),
            32,
            parameters.GetUInt32(Argon2Kdf.ParamVersion, 0),
            null,
            null,
        ];

        var key = (byte[]?)argon2Native.Invoke(new Argon2Kdf(type), arguments);

        Assert.True(
            string.Equals(expected, NativeArgon2.Bound, StringComparison.Ordinal),
            $"KeePassLib's Argon2 is bound to {NativeArgon2.Bound ?? "nothing"}, not {expected}; " +
            "on Windows and macOS, scripts/build-argon2.sh builds the file before the tests are built");
        Assert.True(key is not null, $"{expected} gave KeePassLib no key, so Transform would have derived it in managed code");
        return key;
    }

    /// <summary>The soname KeePassLib binds first on Linux, or <see langword="null"/> where the system has no libargon2 and the managed code derives.</summary>
    private static string? SystemLibrary()
    {
        foreach (var name in _sonames)
        {
            if (NativeLibrary.TryLoad(name, typeof(PwDatabase).Assembly, null, out var handle))
            {
                NativeLibrary.Free(handle);
                return name;
            }
        }

        return null;
    }

    private static (byte[] Key, TimeSpan Took) Timed(Func<byte[]> derive)
    {
        var clock = Stopwatch.StartNew();
        var key = derive();
        return (key, clock.Elapsed);
    }

    private static byte[] Managed(Argon2Type type, byte[] message, KdfParameters parameters)
    {
        NativeLib.AllowNative = false;
        try
        {
            return new Argon2Kdf(type).Transform(message, parameters);
        }
        finally
        {
            NativeLib.AllowNative = true;
        }
    }

    private static KdfParameters Parameters(Argon2Type type, byte[] salt, uint lanes, ulong memory, ulong passes, uint version = 0x13)
    {
        var parameters = new Argon2Kdf(type).GetDefaultParameters();
        parameters.SetByteArray(Argon2Kdf.ParamSalt, salt);
        parameters.SetUInt32(Argon2Kdf.ParamParallelism, lanes);
        parameters.SetUInt64(Argon2Kdf.ParamMemory, memory);
        parameters.SetUInt64(Argon2Kdf.ParamIterations, passes);
        parameters.SetUInt32(Argon2Kdf.ParamVersion, version);
        return parameters;
    }

    private static byte[] Repeat(byte value, int count) => Enumerable.Repeat(value, count).ToArray();
}

[CollectionDefinition(nameof(NativeArgon2Alone), DisableParallelization = true)]
public sealed class NativeArgon2Alone;

/// <summary>
/// Registers keypaste's resolver before any test reaches KeePassLib other than through <c>KeePassInterop</c>, which nothing
/// shipped does, so no test binds KeePassLib's native Argon2 without it (D-NEW-X82).
/// </summary>
internal static class NativeArgon2First
{
    [ModuleInitializer]
    internal static void Register() => RuntimeHelpers.RunClassConstructor(typeof(KeePassInterop).TypeHandle);
}
