using System.Reflection;
using System.Runtime.InteropServices;

namespace Keypaste.Core.Internal;

/// <summary>
/// Hands KeePassLib's native Argon2 import the libargon2 keypaste carries beside its binary on Windows and macOS, and on
/// Linux the system's, so vault keys are derived in native code rather than by KeePassLib's slower managed Argon2
/// (D-NEW-X81, D-NEW-X82).
/// </summary>
/// <remarks>
/// KeePassLib imports <c>argon2_hash</c> from <c>KeePassLibN.x64.dll</c> or <c>KeePassLibN.a64.dll</c> on Windows and from
/// the Debian and Fedora sonames elsewhere, so on its own it finds nothing on Windows or macOS. The carried file is loaded by
/// its full path, never searched for. A library that does not load or lacks <c>argon2_hash</c> is not handed over, and
/// KeePassLib then derives the same key in managed code.
/// </remarks>
internal static class NativeArgon2
{
    private const string _export = "argon2_hash";

    private static readonly string[] _imports = ["KeePassLibN.x64.dll", "KeePassLibN.a64.dll", "libargon2.so.0", "libargon2.so.1"];

    private static string? _bound;

    /// <summary>The file keypaste carries beside its binary here, or <see langword="null"/> on Linux, which uses the system's.</summary>
    internal static string? BundledFileName =>
        OperatingSystem.IsWindows() ? "argon2.dll" : OperatingSystem.IsMacOS() ? "libargon2.1.dylib" : null;

    /// <summary>The carried file's path or the system library's name KeePassLib's import was last bound to; <see langword="null"/> while none has been.</summary>
    internal static string? Bound => Volatile.Read(ref _bound);

    /// <summary>Registers the resolver for KeePassLib, once, before anything reaches its native Argon2.</summary>
    internal static void Register(Assembly keePassLib) => NativeLibrary.SetDllImportResolver(keePassLib, Resolve);

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!_imports.Contains(libraryName, StringComparer.Ordinal))
        {
            return IntPtr.Zero;
        }

        if (BundledFileName is { } file)
        {
            var path = Path.Combine(AppContext.BaseDirectory, file);
            return NativeLibrary.TryLoad(path, out var bundled) ? Bind(bundled, path) : IntPtr.Zero;
        }

        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var system) ? Bind(system, libraryName) : IntPtr.Zero;
    }

    private static IntPtr Bind(IntPtr handle, string library)
    {
        if (!NativeLibrary.TryGetExport(handle, _export, out _))
        {
            NativeLibrary.Free(handle);
            return IntPtr.Zero;
        }

        Volatile.Write(ref _bound, library);
        return handle;
    }
}
