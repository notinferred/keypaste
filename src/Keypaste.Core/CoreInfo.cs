using System.Reflection;

namespace Keypaste.Core;

/// <summary>
/// Identity and liveness information for keypaste-core. Contains no vault logic —
/// this type exists to prove the core-to-frontend wiring and will not grow.
/// </summary>
public static class CoreInfo
{
    /// <summary>Gets the version of the loaded keypaste-core assembly.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Returns a greeting identifying the loaded keypaste-core assembly.</summary>
    /// <returns>A human-readable greeting including <see cref="Version"/>.</returns>
    public static string Hello() => $"keypaste-core {Version} — no vault logic yet.";

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
