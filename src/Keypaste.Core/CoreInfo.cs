using System.Reflection;

namespace Keypaste.Core;

/// <summary>Identity of the loaded keypaste-core assembly, and the native Argon2 it derives vault keys with.</summary>
public static class CoreInfo
{
    /// <summary>Gets the version of the loaded keypaste-core assembly.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// Gets the native libargon2 KeePassLib derives vault keys with in this process: the path of the file keypaste carries
    /// beside its binary, or the system library's name. <see langword="null"/> before the first derivation, and while no
    /// library loads, when KeePassLib derives the same key in managed code (D-NEW-X82).
    /// </summary>
    public static string? NativeArgon2 => Internal.NativeArgon2.Bound;

    private static string ReadVersion()
    {
        var informational = typeof(CoreInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return "0.0.0-unknown";
        }

        // Deterministic CI builds append "+<commit sha>"; keep the user-facing version clean.
        var metadata = informational.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? informational : informational[..metadata];
    }
}
