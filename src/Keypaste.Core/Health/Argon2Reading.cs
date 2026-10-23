using Keypaste.Core.Internal;

namespace Keypaste.Core.Health;

/// <summary>
/// Which Argon2 derives this keypaste's vault keys: the library it carries beside it on Windows and macOS, the system's on
/// Linux, or KeePassLib's slower managed code (D-0522, D-0525).
/// </summary>
internal static class Argon2Reading
{
    private const string _subject = "argon2";

    private const string _slower = "so unlocking and saving take longer";

    /// <summary>Derives one small key in this process, from constant bytes and no vault, and says what derived it.</summary>
    internal static HealthFinding Read()
    {
        var bound = KeePassInterop.DeriveArgon2Once();
        var carried = NativeArgon2.BundledFileName is { } file ? Path.Combine(AppContext.BaseDirectory, file) : null;
        return Finding(bound, carried, carried is not null && File.Exists(carried));
    }

    /// <summary>What doctor says of the library KeePassLib's import was bound to, or of none.</summary>
    /// <param name="bound">The carried file's path or the system library's name, or null when managed code derived the key.</param>
    /// <param name="carried">The file this keypaste carries, or null on Linux.</param>
    /// <param name="present">Whether <paramref name="carried"/> is there.</param>
    internal static HealthFinding Finding(string? bound, string? carried, bool present)
    {
        if (bound is not null)
        {
            return carried is null
                ? Said(HealthStatus.Ok, $"this keypaste derives vault keys with the system's {bound}", where: null, fix: null)
                : Said(HealthStatus.Ok, $"this keypaste derives vault keys with {Path.GetFileName(bound)}", bound, fix: null);
        }

        if (carried is null)
        {
            return Said(HealthStatus.Note, $"no libargon2 is installed, {_slower}", where: null, "install your distribution's libargon2 package");
        }

        var name = Path.GetFileName(carried);

        return present
            ? Said(HealthStatus.Fix, $"{name} beside this keypaste did not work, {_slower}", carried, "reinstall keypaste")
            : Said(HealthStatus.Fix, $"{name} is not beside this keypaste, {_slower}", carried, $"keep {name} beside keypaste");
    }

    private static HealthFinding Said(HealthStatus status, string says, string? where, string? fix) =>
        new(HealthArea.Argon2, _subject, status, says) { Where = where, Fix = fix };
}
