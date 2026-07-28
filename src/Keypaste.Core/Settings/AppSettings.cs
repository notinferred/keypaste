using System.Globalization;
using Keypaste.Core.Audit;
using Keypaste.Core.Policy;

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
/// The desktop app's preferences, as they sit in <c>app.toml</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>An unreadable file costs a preference and never costs a lock.</b> Every failure here — no
/// file, no permission, a half-finished hand edit — produces <see cref="Default"/>, which locks
/// after five minutes. That is the same direction the policy loader fails in (CORE.md law 3.7): the
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
/// outright, deliberately, so that a policy file cannot say yes in a shape keypaste had to guess at.
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

    private static readonly string[] _header =
    [
        "# keypaste's desktop app keeps its preferences here.",
        "# Delete this file to go back to the defaults.",
        "",
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
    /// <para>
    /// Held to <see cref="TomlLimits.Policy"/> rather than <see cref="TomlLimits.Paths"/>: nothing
    /// here is a path, and the longest string this file can legitimately hold is <c>"system"</c>.
    /// </para>
    /// </remarks>
    public static AppSettings Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Default;
        }

        if (!Toml.TryDecode(bytes, TomlLimits.Policy, out var text, out _)
            || !Toml.TryParse(text, TomlLimits.Policy, out var document, out _))
        {
            return Default;
        }

        foreach (var table in document.Tables)
        {
            if (string.Equals(table.Name, SectionName, StringComparison.Ordinal))
            {
                return Read(table);
            }
        }

        return Default;
    }

    /// <summary>Writes the preferences, replacing whatever was there.</summary>
    /// <param name="path">The file, from <see cref="KeypasteHome.SettingsPath"/>.</param>
    /// <param name="settings">What to write.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="settings"/> is null.</exception>
    /// <remarks>
    /// Owner-only on Unix. On Windows the file inherits the profile's ACL, which is the protection
    /// <c>audit.jsonl</c> and <c>recent.toml</c> already rely on and is stated rather than implied.
    /// A failure to write is swallowed and reported in the return value: losing a preference is not
    /// worth interrupting anybody.
    /// </remarks>
    public static bool Save(string path, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(settings);

        var lines = new List<string>(_header)
        {
            $"[[{SectionName}]]",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{IdleTimeoutKey} = {settings.IdleTimeoutSeconds}"),
            $"{ThemeKey} = \"{Written(settings.Theme)}\"",
            $"{LockWhenMinimizedKey} = {(settings.LockWhenMinimized ? "1" : "0")}  # 1 or 0; this file has no booleans",
            string.Empty,
        };

        try
        {
            var directory = Path.GetDirectoryName(path);

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
