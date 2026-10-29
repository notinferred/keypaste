using System.Reflection;
using System.Runtime.InteropServices;

namespace Keypaste.Browser;

/// <summary>
/// The libsodium functions keypaste calls, and no others (D-0611): its NaCl box, and <c>sodium_init</c> before it.
/// </summary>
/// <remarks>
/// The NativeAOT <c>keypaste</c> links libsodium into itself, so these calls are bound when it is linked and load no library.
/// A build that runs in the .NET runtime, such as the tests', loads the shared library <c>scripts/build-libsodium.sh</c> builds
/// from the same release, by its full path beside the binary: it is never searched for, so no other libsodium answers in its
/// place (D-0612).
/// </remarks>
internal static class Sodium
{
    private const string _library = "libsodium";

    // Where an empty message points: libsodium copies even zero bytes from the pointer it is given.
    private static readonly byte[] _empty = new byte[1];

    static Sodium()
    {
        NativeLibrary.SetDllImportResolver(typeof(Sodium).Assembly, Resolve);
    }

    /// <summary>The shared library a build in the .NET runtime loads from beside it.</summary>
    internal static string SharedFileName =>
        OperatingSystem.IsWindows() ? "libsodium.dll" : OperatingSystem.IsMacOS() ? "libsodium.dylib" : "libsodium.so";

    /// <summary><c>crypto_box_keypair</c> into two 32-byte buffers.</summary>
    internal static int BoxKeyPair(Span<byte> publicKey, Span<byte> secretKey)
    {
        Initialize();
        return CryptoBoxKeyPair(ref MemoryMarshal.GetReference(publicKey), ref MemoryMarshal.GetReference(secretKey));
    }

    /// <summary><c>crypto_box_easy</c>: the tag and then the ciphertext of <paramref name="message"/> into <paramref name="box"/>.</summary>
    internal static int BoxEasy(Span<byte> box, ReadOnlySpan<byte> message, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> secretKey)
    {
        Initialize();
        return CryptoBoxEasy(
            ref MemoryMarshal.GetReference(box),
            in Start(message),
            (ulong)message.Length,
            in MemoryMarshal.GetReference(nonce),
            in MemoryMarshal.GetReference(publicKey),
            in MemoryMarshal.GetReference(secretKey));
    }

    /// <summary><c>crypto_box_open_easy</c>: 0 with <paramref name="box"/>'s message in <paramref name="message"/>, or -1 when its tag does not verify.</summary>
    internal static int BoxOpenEasy(Span<byte> message, ReadOnlySpan<byte> box, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> secretKey)
    {
        Initialize();
        return CryptoBoxOpenEasy(
            ref Start(message),
            in MemoryMarshal.GetReference(box),
            (ulong)box.Length,
            in MemoryMarshal.GetReference(nonce),
            in MemoryMarshal.GetReference(publicKey),
            in MemoryMarshal.GetReference(secretKey));
    }

    // sodium_init may be called again, from any thread, and answers 1 once it has run.
    private static void Initialize()
    {
        if (SodiumInit() < 0)
        {
            throw new CryptographicException("libsodium did not initialize");
        }
    }

    private static ref byte Start(Span<byte> span) => ref span.IsEmpty ? ref _empty[0] : ref MemoryMarshal.GetReference(span);

    private static ref readonly byte Start(ReadOnlySpan<byte> span) => ref span.IsEmpty ? ref _empty[0] : ref MemoryMarshal.GetReference(span);

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, _library, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var path = Path.Combine(AppContext.BaseDirectory, SharedFileName);

        return NativeLibrary.TryLoad(path, out var handle)
            ? handle
            : throw new DllNotFoundException($"{path} did not load; scripts/build-libsodium.sh builds it");
    }

    [DllImport(_library, EntryPoint = "sodium_init", ExactSpelling = true)]
    private static extern int SodiumInit();

    [DllImport(_library, EntryPoint = "crypto_box_keypair", ExactSpelling = true)]
    private static extern int CryptoBoxKeyPair(ref byte publicKey, ref byte secretKey);

    [DllImport(_library, EntryPoint = "crypto_box_easy", ExactSpelling = true)]
    private static extern int CryptoBoxEasy(ref byte box, in byte message, ulong messageLength, in byte nonce, in byte publicKey, in byte secretKey);

    [DllImport(_library, EntryPoint = "crypto_box_open_easy", ExactSpelling = true)]
    private static extern int CryptoBoxOpenEasy(ref byte message, in byte box, ulong boxLength, in byte nonce, in byte publicKey, in byte secretKey);
}
