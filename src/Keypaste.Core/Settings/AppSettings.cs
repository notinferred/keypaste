using Keypaste.Core.Audit;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Recent;

namespace Keypaste.Core.Settings;

/// <summary>Which palette the desktop app paints itself in.</summary>
public enum AppTheme
{
    /// <summary>Whatever the operating system is set to, followed as it changes.</summary>
    System = 0,

    /// <summary>Light, whatever the operating system says.</summary>
    Light = 1,

    /// <summary>Dark, whatever the operating system says.</summary>
    Dark = 2,
}

/// <summary>
/// The desktop app's preferences and the chosen vault, as they sit in <c>app.toml</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>An unreadable file costs a preference and never costs a lock.</b> Every failure here — no
/// file, no permission, a half-finished hand edit — produces <see cref="Default"/>, which locks
/// after five minutes. That is the same direction <c>clients.toml</c> fails in (docs/PRODUCT.md law 3.7): the
/// fallback has to be the state that protects the vault, never the state the file happened to ask
/// for.
/// </para>
/// <para>
/// <b>There is no "never".</b> Not an omission and not a feature waiting to be added. A vault
/// unlocked on a screen somebody walks away from is the threat the idle timeout exists for
/// (THREATS.md T-3), and a setting that turns it off would be the setting every hurried person
/// picks once and forgets. The range is a range, so the loosest a file can ask for is eight hours
/// and the tightest is a minute.
/// </para>
/// <para>
/// <b>A number out of range is clamped, never rejected.</b> The clamp lives in
/// <see cref="IdleTimeoutSeconds"/> itself rather than in <see cref="Load"/>, so there is no way to
/// hold an <see cref="AppSettings"/> whose timeout does not lock — not from a file, not from a
/// <c>with</c> expression, not from a settings screen that forgot to validate.
/// </para>
/// <para>
/// <b>The boolean is written as 1 and 0.</b> <see cref="Toml"/> refuses <c>true</c> and <c>false</c>
/// outright, deliberately, so that a hand-written file cannot say yes in a shape keypaste had to guess at.
/// Rather than widen the reader for a window preference, this file writes a number and says so in a
/// comment beside it — the refusal is worth more than the ergonomics of one line.
/// </para>
/// <para>
/// <b>Fail closed, and do not clobber.</b> A file that will not parse is left exactly as it is
/// (D-0028). Rewriting it with the defaults would destroy a hand edit at the moment its author was
/// most likely to be part-way through making it.
/// </para>
/// </remarks>
public sealed record AppSettings
{
    /// <summary>The shortest idle timeout a file may ask for, in seconds.</summary>
    public const int MinimumIdleTimeoutSeconds = 60;

    /// <summary>The longest idle timeout a file may ask for, in seconds.</summary>
    public const int MaximumIdleTimeoutSeconds = 8 * 60 * 60;

    /// <summary>The section header the preferences are written under.</summary>
    public const string SectionName = "settings";

    /// <summary>
    /// The three keys the section carries.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> for the reason <see cref="AuditText"/> gives: the
    /// naming rule in <c>.editorconfig</c> applies <c>_camelCase</c> to every private field,
    /// constants included, and this repository has no <c>private const</c> anywhere.
    /// </remarks>
    internal const string IdleTimeoutKey = "idle_timeout_seconds";

    /// <inheritdoc cref="IdleTimeoutKey"/>
    internal const string ThemeKey = "theme";

    /// <inheritdoc cref="IdleTimeoutKey"/>
    internal const string LockWhenMinimizedKey = "lock_when_minimized";

    /// <inheritdoc cref="IdleTimeoutKey"/>
    internal const string StayInTrayKey = "stay_in_tray";
    internal const string VaultKey = "vault";
    internal const string CrashReportsKey = "crash_reports";
    internal const string TermsKey = "terms";

    private const string _vaultComment = "the vault agents and the CLI use without --vault";

    private static readonly string[] _knownKeys = [IdleTimeoutKey, ThemeKey, LockWhenMinimizedKey, StayInTrayKey, VaultKey, CrashReportsKey, TermsKey];

    private static readonly string[] _header =
    [
        "keypaste keeps the desktop app's preferences here, and the vault agents and the CLI use.",
        "Delete this file to go back to the defaults.",
    ];

    private readonly int _idleTimeoutSeconds;

    /// <summary>What the app runs as when there is nothing usable to read.</summary>
    /// <remarks>
    /// Five minutes because it is the number a person who has never opened this file lives with,
    /// and it has to be short enough to matter on an unattended screen and long enough that reading
    /// a page of documentation does not cost a re-unlock.
    /// </remarks>
    public static AppSettings Default { get; } = new()
    {
        IdleTimeoutSeconds = 300,
        Theme = AppTheme.System,
        LockWhenMinimized = false,
    };

    /// <summary>How long the vault stays unlocked with nobody touching it.</summary>
    /// <value>
    /// Always between <see cref="MinimumIdleTimeoutSeconds"/> and
    /// <see cref="MaximumIdleTimeoutSeconds"/>; anything else is clamped on the way in.
    /// </value>
    public required int IdleTimeoutSeconds
    {
        get => _idleTimeoutSeconds;
        init => _idleTimeoutSeconds = Math.Clamp(value, MinimumIdleTimeoutSeconds, MaximumIdleTimeoutSeconds);
    }

    /// <summary>Which palette to paint in.</summary>
    public required AppTheme Theme { get; init; }

    /// <summary>Whether minimising the window locks the vault.</summary>
    /// <remarks>
    /// Off by default. It is a stricter setting than the idle timeout, not a looser one — the
    /// timeout still applies to a minimised window — so it is safe to leave to the person who wants
    /// it.
    /// </remarks>
    public required bool LockWhenMinimized { get; init; }

    /// <summary>Whether closing the window leaves the app locked in the menu bar or tray; null until chosen, when the platform decides.</summary>
    public bool? StayInTray { get; init; }
    /// <summary>The vault agents and the CLI use when no <c>--vault</c> or <c>KEYPASTE_VAULT</c> names one, or null.</summary>
    /// <remarks>A full path and never anything read from the vault (D-0389).</remarks>
    public string? Vault { get; init; }

    /// <summary>Whether the person chose to send the desktop app's crash reports; null until they answer.</summary>
    /// <remarks>Only <c>1</c> in a file that reads whole turns them on, since every failure to read gives <see cref="Default"/> (D-0594).</remarks>
    public bool? SendCrashReports { get; init; }

    /// <summary>The version of the terms of use the person was shown when they chose, or null.</summary>
    public string? TermsShown { get; init; }

    /// <summary>Reads the preferences.</summary>
    /// <param name="path">The file, from <see cref="KeypasteHome.SettingsPath"/>.</param>
    /// <returns>What the file asked for, or <see cref="Default"/> when it asked for nothing usable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This never throws and never writes. A key the file omits takes its default on its own,
    /// because a person who set only the theme meant to set only the theme; a file the parser
    /// cannot read whole takes the defaults whole, because past the first syntax error nothing in
    /// it can be trusted to mean what it looks like.
    /// </para>
    /// </remarks>
    public static AppSettings Load(string path)
    {
        TryLoad(path, out var settings);
        return settings;
    }

    /// <summary>Reads the preferences, saying whether a file that exists could not be read.</summary>
    /// <param name="path">The file, from <see cref="KeypasteHome.SettingsPath"/>.</param>
    /// <param name="settings">What <see cref="Load"/> returns.</param>
    /// <returns><see langword="false"/> when the file exists and could not be read or parsed, so a writer knows it would clobber it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static bool TryLoad(string path, out AppSettings settings) => TryLoad(path, out settings, out _);

    /// <summary>Reads the preferences, saying why a file that exists could not be read.</summary>
    /// <param name="path">The file, from <see cref="KeypasteHome.SettingsPath"/>.</param>
    /// <param name="settings">What <see cref="Load"/> returns.</param>
    /// <param name="problem">Why the file could not be read or parsed, such as <c>line 3: …</c>; empty otherwise.</param>
    /// <returns><see langword="false"/> when the file exists and could not be read or parsed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static bool TryLoad(string path, out AppSettings settings, out string problem)
    {
        ArgumentNullException.ThrowIfNull(path);

        settings = Default;
        problem = string.Empty;
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return false;
        }

        if (!Toml.TryDecode(bytes, TomlLimits.Paths, out var text, out problem)
            || !Toml.TryParse(text, TomlLimits.Paths, out var document, out problem))
        {
            return false;
        }

        foreach (var table in document.Tables)
        {
            if (string.Equals(table.Name, SectionName, StringComparison.Ordinal))
            {
                settings = Read(table);
                break;
            }
        }

        return true;
    }

    /// <summary>Writes the preferences, keeping every other key and section the file holds.</summary>
    /// <param name="path">The file, from <see cref="KeypasteHome.SettingsPath"/>.</param>
    /// <param name="settings">What to write.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="settings"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// A key of <c>[[settings]]</c> or a section this version does not read is written back as it was read,
    /// after the preferences, so a version older than the one that wrote it does not drop it (D-0520). A
    /// file that does not parse is replaced, as before. When what is kept cannot be written back as itself, or
    /// the file would no longer read whole, nothing is written and this returns <see langword="false"/>.
    /// </para>
    /// <para>
    /// Owner-only on Unix. On Windows the file inherits the profile's ACL, which is the protection
    /// <c>audit.jsonl</c> and <c>recent.toml</c> already rely on and is stated rather than implied.
    /// A failure to write is swallowed and reported in the return value: losing a preference is not
    /// worth interrupting anybody.
    /// </para>
    /// </remarks>
    public static bool Save(string path, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(settings);

        var writer = new TomlWriter(TomlLimits.Paths);

        foreach (var line in _header)
        {
            writer.Comment(line);
        }

        writer
            .BlankLine()
            .Table(SectionName)
            .Number(IdleTimeoutKey, settings.IdleTimeoutSeconds)
            .Text(ThemeKey, Written(settings.Theme))
            .Number(LockWhenMinimizedKey, settings.LockWhenMinimized ? 1 : 0, "1 or 0; this file has no booleans");

        if (settings.StayInTray is { } tray)
        {
            writer.Number(StayInTrayKey, tray ? 1 : 0);
        }

        if (settings.Vault is { } vault && CanRecord(vault))
        {
            writer.Text(VaultKey, RecentVaults.Portable(vault), _vaultComment);
        }

        if (settings.SendCrashReports is { } crashReports)
        {
            writer.Number(CrashReportsKey, crashReports ? 1 : 0, "1 sends the app's crash reports to keypaste.com");
        }

        if (settings.TermsShown is { } terms && writer.CanWrite(TermsKey, terms))
        {
            writer.Text(TermsKey, terms, "the terms of use shown when that was chosen");
        }

        if (!TryKeep(writer, Existing(path)))
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(writer.ToString());

        if (!Toml.TryDecode(bytes, TomlLimits.Paths, out var text, out _) || !Toml.TryParse(text, TomlLimits.Paths, out _, out _))
        {
            return false;
        }

        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            AtomicFile.Write(path, bytes);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The sections the file at <paramref name="path"/> holds, or none when it is absent or does not read whole.</summary>
    private static TomlDocument Existing(string path)
    {
        try
        {
            return Toml.TryDecode(File.ReadAllBytes(path), TomlLimits.Paths, out var text, out _)
                && Toml.TryParse(text, TomlLimits.Paths, out var document, out _)
                    ? document
                    : TomlDocument.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TomlDocument.Empty;
        }
    }

    /// <summary>Writes back what <paramref name="existing"/> holds beyond the preferences: the other keys of its first <c>[[settings]]</c>, then every other section.</summary>
    /// <returns><see langword="false"/> when something kept cannot be written so that it reads back as itself.</returns>
    private static bool TryKeep(TomlWriter writer, TomlDocument existing)
    {
        var section = existing.Tables.FirstOrDefault(table => string.Equals(table.Name, SectionName, StringComparison.Ordinal));

        try
        {
            if (section is not null && !TryWrite(writer, section.Pairs.Where(pair => !_knownKeys.Contains(pair.Key, StringComparer.Ordinal))))
            {
                return false;
            }

            foreach (var table in existing.Tables.Where(other => !ReferenceEquals(other, section)))
            {
                writer.BlankLine().Table(table.Name);

                if (!TryWrite(writer, table.Pairs))
                {
                    return false;
                }
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    private static bool TryWrite(TomlWriter writer, IEnumerable<TomlPair> pairs)
    {
        foreach (var pair in pairs)
        {
            switch (pair.Value.Kind)
            {
                case TomlValueKind.Text when writer.CanWrite(pair.Key, pair.Value.Text):
                    writer.Text(pair.Key, pair.Value.Text);
                    break;
                case TomlValueKind.Number:
                    writer.Number(pair.Key, pair.Value.Number);
                    break;
                case TomlValueKind.Array when writer.CanWriteStrings(pair.Key, pair.Value.Items):
                    writer.Strings(pair.Key, pair.Value.Items);
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="vaultPath"/> can be written as <see cref="Vault"/> and read back as itself.</summary>
    /// <remarks>A path holding a quote, a control character or, on Unix, a backslash cannot; nor can one longer than the file's strings.</remarks>
    internal static bool CanRecord(string vaultPath) =>
        new TomlWriter(TomlLimits.Paths).CanWrite(VaultKey, RecentVaults.Portable(vaultPath), _vaultComment);

    private static AppSettings Read(TomlTable table) => new()
    {
        IdleTimeoutSeconds =
            table.TryGet(IdleTimeoutKey, out var idle) && idle.Value.Kind == TomlValueKind.Number
                ? idle.Value.Number
                : Default.IdleTimeoutSeconds,
        Theme = table.TryGet(ThemeKey, out var theme) && theme.Value.Kind == TomlValueKind.Text
            ? Parsed(theme.Value.Text)
            : Default.Theme,
        LockWhenMinimized =
            table.TryGet(LockWhenMinimizedKey, out var minimized)
            && minimized.Value.Kind == TomlValueKind.Number
                ? minimized.Value.Number != 0
                : Default.LockWhenMinimized,
        StayInTray =
            table.TryGet(StayInTrayKey, out var tray) && tray.Value.Kind == TomlValueKind.Number
                ? tray.Value.Number != 0
                : Default.StayInTray,
        Vault = table.TryGet(VaultKey, out var vault) && vault.Value.Kind == TomlValueKind.Text
            ? ChosenVault.FullPath(vault.Value.Text)
            : null,
        SendCrashReports = table.TryGet(CrashReportsKey, out var crashReports) && crashReports.Value.Kind == TomlValueKind.Number
            ? crashReports.Value.Number == 1
            : null,
        TermsShown = table.TryGet(TermsKey, out var terms) && terms.Value.Kind == TomlValueKind.Text ? terms.Value.Text : null,
    };

    /// <summary>
    /// Reads a theme name, falling back to the one that needs no decision.
    /// </summary>
    /// <remarks>
    /// Case-insensitive, because a file a human types <c>"Dark"</c> into meant dark, and because
    /// the cost of being wrong about it is a palette rather than a permission.
    /// </remarks>
    private static AppTheme Parsed(string text) => text switch
    {
        _ when string.Equals(text, "light", StringComparison.OrdinalIgnoreCase) => AppTheme.Light,
        _ when string.Equals(text, "dark", StringComparison.OrdinalIgnoreCase) => AppTheme.Dark,
        _ => AppTheme.System,
    };

    private static string Written(AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => "system",
    };
}
