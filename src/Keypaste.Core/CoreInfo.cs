using System.Reflection;

namespace Keypaste.Core;

/// <summary>Identity of the loaded keypaste-core assembly.</summary>
public static class CoreInfo
{
    /// <summary>Gets the version of the loaded keypaste-core assembly.</summary>
    public static string Version { get; } = ReadVersion();

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
