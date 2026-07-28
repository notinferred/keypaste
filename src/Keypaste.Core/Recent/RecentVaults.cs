using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Keypaste.Core.Audit;
using Keypaste.Core.Policy;

namespace Keypaste.Core.Recent;

/// <summary>One vault this machine has opened.</summary>
/// <param name="Path">The absolute path, in this platform's own form.</param>
/// <param name="OpenedAt">When it was last opened successfully.</param>
public sealed record RecentVault(string Path, DateTimeOffset OpenedAt);

/// <summary>
/// The vaults the desktop app has opened on this machine, most recent first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a successful unlock is recorded.</b> Someone who drags in a file they cannot open — a
/// colleague's vault, a file they were sent — leaves nothing behind. The list is a record of what
/// this person has actually used, not of what has been pointed at them.
/// </para>
/// <para>
/// <b>It holds paths and nothing else.</b> No entry names, no counts, no fingerprints of the
/// contents. CORE.md law 3.5 is about telemetry and this file never leaves the machine, but a vault
/// path is still information about a person — <c>~/work/acme-prod.kdbx</c> says something — which is
/// why it is capped, written owner-only, and forgettable from the UI in one click.
/// </para>
/// <para>
/// <b>Paths are stored with forward slashes, including on Windows.</b> Not a style choice:
/// <see cref="Toml"/> refuses a backslash inside a value, deliberately, so that a glob in a policy
/// file cannot be written one way and mean another. Rather than weaken that rule for every file the
/// reader parses, this one writes <c>C:/Users/…</c>, which .NET accepts everywhere a path is taken
/// and which <see cref="System.IO.Path.GetFullPath(string)"/> turns back into the native form on the
/// way in. The file stays readable and the parser keeps its guarantee.
/// </para>
/// </remarks>
public static class RecentVaults
{
    /// <summary>How many vaults are remembered.</summary>
    /// <remarks>
    /// Ten is enough to cover every vault a person actually switches between and short enough that
    /// the list stays scannable without a scrollbar on the unlock screen.
    /// </remarks>
    public const int Capacity = 10;

    /// <summary>The section header each entry is written under.</summary>
    public const string SectionName = "vault";

    /// <summary>
    /// The two keys a section carries.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> for the reason <see cref="Audit.AuditText"/>
    /// gives: <c>.editorconfig</c> applies <c>_camelCase</c> to every private field, constants
    /// included, and this repository has no <c>private const</c> anywhere.
    /// </remarks>
    internal const string PathKey = "path";

    /// <inheritdoc cref="PathKey"/>
    internal const string OpenedAtKey = "opened_at";

    private static readonly string[] _header =
    [
        "# keypaste remembers which vault files you have opened on this machine.",
        "# It holds no entry names and no secrets. Delete this file to forget them.",
        "",
    ];

    /// <summary>Reads the list.</summary>
    /// <param name="path">The file, from <see cref="KeypasteHome.RecentPath"/>.</param>
    /// <returns>The vaults, most recent first. Empty when there is nothing usable to read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Anything wrong with the file means an empty list, and the file is left alone.</b> The same
    /// rule the policy loader follows, for a weaker version of the same reason: this is a
    /// convenience, and a convenience that overwrites a file a human may be part-way through editing
    /// has cost more than it saved. An empty list costs one trip through the file picker.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<RecentVault> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (!Toml.TryDecode(bytes, TomlLimits.Paths, out var text, out _)
            || !Toml.TryParse(text, TomlLimits.Paths, out var document, out _))
        {
            return [];
        }

        var vaults = new List<RecentVault>(Capacity);

        foreach (var table in document.Tables)
        {
            if (!string.Equals(table.Name, SectionName, StringComparison.Ordinal)
                || !TryRead(table, out var vault))
            {
                // One malformed section does not discard the rest. Unlike a policy file, where a
                // rule nobody can read must void the whole document because it may have been the
                // rule that said no, a forgotten shortcut is only a forgotten shortcut.
                continue;
            }

            vaults.Add(vault);
        }

        return Trim(vaults);
    }

    /// <summary>Puts a vault at the front of the list.</summary>
    /// <param name="existing">The list as it stands.</param>
    /// <param name="path">The vault that was opened.</param>
    /// <param name="openedAt">When.</param>
    /// <returns>The new list, most recent first, at most <see cref="Capacity"/> long.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="existing"/> or <paramref name="path"/> is null.</exception>
    /// <remarks>
    /// Comparison is by full path. Opening the same vault twice moves it to the front rather than
    /// appearing twice, which is what stops the list filling with one file.
    /// </remarks>
    public static IReadOnlyList<RecentVault> Remember(
        IReadOnlyList<RecentVault> existing,
        string path,
        DateTimeOffset openedAt)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(path);

        var full = System.IO.Path.GetFullPath(path);
        var vaults = new List<RecentVault> { new(full, openedAt) };

        foreach (var vault in existing)
        {
            if (!Same(vault.Path, full))
            {
                vaults.Add(vault);
            }
        }

        return Trim(vaults);
    }

    /// <summary>Removes a vault from the list.</summary>
    /// <param name="existing">The list as it stands.</param>
    /// <param name="path">The vault to forget.</param>
    /// <returns>The list without it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="existing"/> or <paramref name="path"/> is null.</exception>
    public static IReadOnlyList<RecentVault> Forget(IReadOnlyList<RecentVault> existing, string path)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(path);

        var full = System.IO.Path.GetFullPath(path);
        return [.. existing.Where(vault => !Same(vault.Path, full))];
    }

    /// <summary>Writes the list, replacing whatever was there.</summary>
    /// <param name="path">The file, from <see cref="KeypasteHome.RecentPath"/>.</param>
    /// <param name="vaults">The vaults, most recent first.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="vaults"/> is null.</exception>
    /// <remarks>
    /// Owner-only on Unix. On Windows the file inherits the profile's ACL, which is the same
    /// protection <c>audit.jsonl</c> already relies on and is stated rather than implied.
    /// A failure to write is swallowed: losing a shortcut is not worth interrupting anybody.
    /// </remarks>
    public static bool Save(string path, IReadOnlyList<RecentVault> vaults)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(vaults);

        var lines = new List<string>(_header);

        foreach (var vault in Trim(vaults))
        {
            lines.Add($"[[{SectionName}]]");
            lines.Add($"{PathKey} = \"{Portable(vault.Path)}\"");
            lines.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{OpenedAtKey} = \"{vault.OpenedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}\""));
            lines.Add(string.Empty);
        }

        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(path, lines);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// A path written the way this file writes it: absolute, and with no backslash in it.
    /// </summary>
    /// <param name="path">Any path.</param>
    /// <returns>The path with forward slashes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static string Portable(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return System.IO.Path.GetFullPath(path).Replace('\\', '/');
    }

    private static bool TryRead(TomlTable table, [NotNullWhen(true)] out RecentVault? vault)
    {
        vault = null;

        if (!table.TryGet(PathKey, out var pair) || pair.Value.Kind != TomlValueKind.Text)
        {
            return false;
        }

        string full;

        try
        {
            full = System.IO.Path.GetFullPath(pair.Value.Text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var openedAt = table.TryGet(OpenedAtKey, out var when)
            && when.Value.Kind == TomlValueKind.Text
            && DateTimeOffset.TryParse(
                when.Value.Text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;

        vault = new RecentVault(full, openedAt);
        return true;
    }

    private static List<RecentVault> Trim(IReadOnlyList<RecentVault> vaults) =>
        vaults.Count <= Capacity ? [.. vaults] : [.. vaults.Take(Capacity)];

    /// <summary>
    /// Whether two paths name the same file, by the rules of this platform.
    /// </summary>
    /// <remarks>
    /// Case-insensitive on Windows and on macOS, where the default file systems are; case-sensitive
    /// on Linux, where it is not. Getting this wrong shows up as the same vault appearing twice in
    /// the list, which is cosmetic — but it is the same question <c>env set</c> had to answer about
    /// colliding names, and answering it the same way here costs one line.
    /// </remarks>
    private static bool Same(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
