using Keypaste.Core.Totp;

namespace Keypaste.Core.AutoType;

/// <summary>
/// What one Auto-Type of one item into one window types, key by key, resolved whole before the first key, or why
/// it types nothing (D-0604 to D-0607).
/// </summary>
/// <remarks>
/// <para>
/// The sequence is chosen for the window's title (D-0606) and read with KeePassXC 2.8's grammar
/// (<see cref="AutoTypeSequence"/>). Each field it names is filled in as D-0465 fills a value, so it types what a
/// copy copies, and each <see cref="AutoTypeCode"/> computes the item's TOTP code only when typing reaches it. A
/// refusal anywhere refuses the whole sequence, naming the token as written and never a value, and leaves no step.
/// </para>
/// <para>
/// A plan holds the filled text and the TOTP seed, so whoever types it disposes it when typing ends, however it
/// ends: the seed is zeroed and the steps dropped. <see cref="ToString"/> and every step's <c>ToString</c> show no
/// value.
/// </para>
/// </remarks>
public sealed class AutoTypePlan : IDisposable
{
    /// <summary>KeePassXC's default wait between keys, until a <c>{DELAY=n}</c> sets another.</summary>
    public static readonly TimeSpan DefaultKeyDelay = TimeSpan.FromMilliseconds(25);

    private const string _cannot = "keypaste can't type this item: ";
    private const string _edit = " Edit the item so keypaste can fill it in.";
    private const string _holds = "holds ";
    private const string _sequenceHolds = "its Auto-Type sequence holds ";

    private List<AutoTypeStep> _steps = [];
    private TotpSpec? _totp;

    private AutoTypePlan(string? sequence, AutoTypeChoice? choice, bool expired, string? refusal)
    {
        Sequence = sequence;
        Source = choice?.Source;
        Association = choice?.Association;
        UnmatchedPatterns = choice?.Unmatched ?? [];
        Expired = expired;
        Refusal = refusal;
    }

    /// <summary>The sequence chosen for the window, as the vault holds it, or null when Auto-Type is off for the item.</summary>
    public string? Sequence { get; }

    /// <summary>Where <see cref="Sequence"/> comes from, or null when Auto-Type is off for the item.</summary>
    public AutoTypeSource? Source { get; }

    /// <summary>The window association that chose <see cref="Sequence"/>, counting from 1, or null when none did.</summary>
    public int? Association { get; }

    /// <summary>
    /// Why each association whose pattern could not be filled in, read or matched in time matched nothing, such as
    /// <c>association 2's window pattern is a regular expression keypaste cannot read</c>.
    /// </summary>
    public IReadOnlyList<string> UnmatchedPatterns { get; }

    /// <summary>Whether the item had expired when the plan was made; an expired item still types.</summary>
    public bool Expired { get; }

    /// <summary>Why nothing is typed, as a sentence that names a token or a field and never a value; null when <see cref="Steps"/> type.</summary>
    public string? Refusal { get; private set; }

    /// <summary>Each step in order; empty when refused and once the plan is disposed.</summary>
    public IReadOnlyList<AutoTypeStep> Steps => _steps;

    /// <summary>Whether the plan's steps type.</summary>
    public bool CanType => Refusal is null;

    /// <summary>Whether it types, never what.</summary>
    /// <returns>Such as <c>Auto-Type plan of 4 steps</c>.</returns>
    public override string ToString() => Refusal is null ? $"Auto-Type plan of {_steps.Count} steps" : "Auto-Type plan refused";

    /// <summary>Zeroes the TOTP seed and drops every step.</summary>
    public void Dispose()
    {
        _totp?.Dispose();
        _totp = null;
        _steps = [];
    }

    /// <summary>Resolves one item's Auto-Type for one window's title, reading the item through <paramref name="item"/>.</summary>
    /// <param name="settings">The item's and its groups' settings.</param>
    /// <param name="title">The window's title.</param>
    /// <param name="now">When, for whether the item has expired.</param>
    /// <param name="item">Reads the item's fields, fills a value in and reads its TOTP.</param>
    /// <param name="timeout">How long one window pattern may take to match.</param>
    /// <returns>The plan, which the caller disposes.</returns>
    internal static AutoTypePlan Resolve(AutoTypeSettings settings, string title, DateTimeOffset now, AutoTypeItem item, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(item);

        var expired = settings.Expires is { } expires && expires <= now;

        if (settings.Off is { } off)
        {
            return new AutoTypePlan(null, null, expired, off);
        }

        var choice = settings.Choose(title, item.Fill, timeout);
        var reading = AutoTypeSequence.Read(choice.Sequence);
        var refusal = settings.Obfuscated ? Refused("its Auto-Type asks for KeePass's two-channel obfuscation, which keypaste does not use")
            : reading.Refusal is { } why ? Refused("its Auto-Type sequence " + why)
            : null;
        var plan = new AutoTypePlan(choice.Sequence, choice, expired, refusal);

        if (refusal is null)
        {
            plan.Build(reading.Tokens, item);
        }

        return plan;
    }

    /// <summary>Turns each token into its steps, or refuses the plan at the first token that cannot be typed, dropping every step.</summary>
    private void Build(IReadOnlyList<SequenceToken> tokens, AutoTypeItem item)
    {
        foreach (var token in tokens)
        {
            Refusal = token switch
            {
                KeyToken key => Add(new AutoTypePress(key.Key, key.Modifiers), key.Count),
                CharacterToken character => Add(new AutoTypeCharacter(character.Character, character.Modifiers), character.Count),
                FieldToken field => AddField(field, item),
                TotpToken code => AddCode(code, item),
                PauseToken pause => Add(new AutoTypePause(TimeSpan.FromMilliseconds(pause.Milliseconds)), 1),
                KeyDelayToken delay => Add(new AutoTypeKeyDelay(TimeSpan.FromMilliseconds(delay.Milliseconds)), 1),
                ClearFieldToken => Add(new AutoTypeClearField(), 1),
                _ => null,
            };

            if (Refusal is not null)
            {
                Dispose();
                return;
            }
        }
    }

    private string? Add(AutoTypeStep step, int times)
    {
        _steps.AddRange(Enumerable.Repeat(step, times));
        return null;
    }

    /// <summary>A field's stored value filled in as D-0465 fills it, typed when it holds anything; or why the sequence is refused.</summary>
    /// <remarks>
    /// A <c>{S:…}</c> reaches a standard field or any custom field, as a copy does (D-0547), but neither a name
    /// holding a brace (D-0531) nor a field a TOTP is read from, whose code only <c>{TOTP}</c> types.
    /// </remarks>
    private string? AddField(FieldToken token, AutoTypeItem item)
    {
        var named = token.Field.AsSpan().IndexOfAny('{', '}') >= 0 || TotpSpec.IsSourceField(token.Field);

        if (named || item.Read(token.Field) is not { } stored)
        {
            return Refused(_sequenceHolds + AutoTypeSequence.Shown(token.Written) + (named && item.Has(token.Field)
                ? ", which names a field keypaste never types"
                : ", which names no field of its item"));
        }

        var filled = item.Fill(stored);

        if (filled.Value is not { } value)
        {
            var refusal = filled.Refusal!;
            return Refused($"{Subject(token.Field)} {(refusal.StartsWith(_holds, StringComparison.Ordinal) ? refusal[_holds.Length..] : refusal)}", _edit);
        }

        if (value.Any(char.IsControl))
        {
            return Refused($"{Subject(token.Field)} a control character, such as a line break or a tab, and keypaste types those keys only from the sequence");
        }

        return value.Length > 0 ? Add(new AutoTypeText(value), 1) : null;
    }

    /// <summary>A step that types the current TOTP code, the item's TOTP read once; or why the sequence is refused.</summary>
    private string? AddCode(TotpToken token, AutoTypeItem item)
    {
        if (_totp is null)
        {
            _totp = item.ReadTotp(out var why);

            if (_totp is null)
            {
                return why is null
                    ? Refused(_sequenceHolds + AutoTypeSequence.Shown(token.Written) + ", and the item has no TOTP")
                    : Refused(_sequenceHolds + AutoTypeSequence.Shown(token.Written) + ", and keypaste can't compute the item's TOTP code", " " + why);
            }
        }

        return Add(new AutoTypeCode(_totp), 1);
    }

    /// <summary>What a refusal inside a field says the field is, with the verb that follows it.</summary>
    private static string Subject(string field) => field switch
    {
        "Title" => "its title holds",
        "UserName" => "its username holds",
        "Password" => "its password holds",
        "URL" => "its web address holds",
        "Notes" => "its notes hold",
        _ => $"its field {EntryNameSanitizer.Sanitize(field).Text} holds",
    };

    /// <summary>The refusal as one sentence; every part of it the vault wrote was drawn safely where it was named.</summary>
    private static string Refused(string reason, string ending = "") => _cannot + reason + "." + ending;
}

/// <summary>Reads one item for its Auto-Type plan, under whatever holds the vault still.</summary>
/// <param name="has">Whether the item has a field of this name, standard or custom, matched exactly.</param>
/// <param name="read">A field's stored value, standard or custom, matched exactly; null when the item has none of that name.</param>
/// <param name="fill">Fills a value of the item in as D-0465 fills one.</param>
/// <param name="totp">Reads the item's TOTP.</param>
internal sealed class AutoTypeItem(
    Func<string, bool> has,
    Func<string, string?> read,
    Func<string, PlaceholderFill> fill,
    TotpReader totp)
{
    internal bool Has(string field) => has(field);

    internal string? Read(string field) => read(field);

    internal PlaceholderFill Fill(string value) => fill(value);

    internal TotpSpec? ReadTotp(out string? refusal) => totp(out refusal);
}

/// <summary>Reads an item's TOTP as <see cref="Vault.ReadTotp"/> does.</summary>
/// <param name="refusal">Why it cannot be computed, naming a field and never a value; null otherwise.</param>
/// <returns>The spec, which the caller disposes; null when the item has none or it is refused.</returns>
internal delegate TotpSpec? TotpReader(out string? refusal);

/// <summary>The modifiers a sequence's <c>+</c>, <c>^</c> and <c>%</c> hold down for the next key only.</summary>
[Flags]
public enum AutoTypeModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Shift, from <c>+</c>.</summary>
    Shift = 1,

    /// <summary>Control, from <c>^</c>.</summary>
    Control = 2,

    /// <summary>Alt, from <c>%</c>, which is Option on macOS.</summary>
    Alt = 4,
}

/// <summary>A named key a sequence presses, as KeePassXC 2.8 maps its names (<c>g_placeholderToKey</c>).</summary>
public enum AutoTypeKey
{
    /// <summary><c>{TAB}</c>.</summary>
    Tab,

    /// <summary><c>{ENTER}</c>.</summary>
    Enter,

    /// <summary><c>{SPACE}</c>.</summary>
    Space,

    /// <summary><c>{UP}</c>.</summary>
    Up,

    /// <summary><c>{DOWN}</c>.</summary>
    Down,

    /// <summary><c>{LEFT}</c>.</summary>
    Left,

    /// <summary><c>{RIGHT}</c>.</summary>
    Right,

    /// <summary><c>{INSERT}</c> or <c>{INS}</c>.</summary>
    Insert,

    /// <summary><c>{DELETE}</c> or <c>{DEL}</c>.</summary>
    Delete,

    /// <summary><c>{HOME}</c>.</summary>
    Home,

    /// <summary><c>{END}</c>.</summary>
    End,

    /// <summary><c>{PGUP}</c>.</summary>
    PageUp,

    /// <summary><c>{PGDN}</c> or <c>{PGDOWN}</c>.</summary>
    PageDown,

    /// <summary><c>{BACKSPACE}</c>, <c>{BS}</c> or <c>{BKSP}</c>.</summary>
    Backspace,

    /// <summary><c>{BREAK}</c>, which KeePassXC presses as Pause.</summary>
    Break,

    /// <summary><c>{ESC}</c>.</summary>
    Escape,

    /// <summary><c>{HELP}</c>.</summary>
    Help,

    /// <summary><c>{ADD}</c>, the keypad's plus.</summary>
    Add,

    /// <summary><c>{SUBTRACT}</c>, the keypad's minus.</summary>
    Subtract,

    /// <summary><c>{MULTIPLY}</c>, the keypad's asterisk.</summary>
    Multiply,

    /// <summary><c>{DIVIDE}</c>, the keypad's slash.</summary>
    Divide,

    /// <summary><c>{F1}</c>.</summary>
    F1,

    /// <summary><c>{F2}</c>.</summary>
    F2,

    /// <summary><c>{F3}</c>.</summary>
    F3,

    /// <summary><c>{F4}</c>.</summary>
    F4,

    /// <summary><c>{F5}</c>.</summary>
    F5,

    /// <summary><c>{F6}</c>.</summary>
    F6,

    /// <summary><c>{F7}</c>.</summary>
    F7,

    /// <summary><c>{F8}</c>.</summary>
    F8,

    /// <summary><c>{F9}</c>.</summary>
    F9,

    /// <summary><c>{F10}</c>.</summary>
    F10,

    /// <summary><c>{F11}</c>.</summary>
    F11,

    /// <summary><c>{F12}</c>.</summary>
    F12,

    /// <summary><c>{F13}</c>.</summary>
    F13,

    /// <summary><c>{F14}</c>.</summary>
    F14,

    /// <summary><c>{F15}</c>.</summary>
    F15,

    /// <summary><c>{F16}</c>.</summary>
    F16,
}

/// <summary>One step of an <see cref="AutoTypePlan"/>.</summary>
public abstract class AutoTypeStep
{
    private protected AutoTypeStep()
    {
    }
}

/// <summary>Presses and releases a named key, with the modifiers held around it.</summary>
public sealed class AutoTypePress : AutoTypeStep
{
    internal AutoTypePress(AutoTypeKey key, AutoTypeModifiers modifiers)
    {
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>The key.</summary>
    public AutoTypeKey Key { get; }

    /// <summary>The modifiers held while it is pressed.</summary>
    public AutoTypeModifiers Modifiers { get; }

    /// <summary>The key and its modifiers.</summary>
    /// <returns>Such as <c>Shift+Tab</c>.</returns>
    public override string ToString() =>
        Modifiers == AutoTypeModifiers.None ? Key.ToString() : $"{Modifiers.ToString().Replace(", ", "+", StringComparison.Ordinal)}+{Key}";
}

/// <summary>Types one character the sequence itself holds, with the modifiers held around it.</summary>
public sealed class AutoTypeCharacter : AutoTypeStep
{
    internal AutoTypeCharacter(string character, AutoTypeModifiers modifiers)
    {
        Character = character;
        Modifiers = modifiers;
    }

    /// <summary>One Unicode character: one UTF-16 code unit, or both halves of a surrogate pair.</summary>
    public string Character { get; }

    /// <summary>The modifiers held while it is typed.</summary>
    public AutoTypeModifiers Modifiers { get; }

    /// <summary>Its kind only, since a sequence may spell out text.</summary>
    /// <returns><c>a character</c>.</returns>
    public override string ToString() => "a character";
}

/// <summary>Types a field's text as D-0465 fills it in, character by character, with no modifier.</summary>
/// <remarks>The text holds no control character: Tab and Enter come only from the sequence.</remarks>
public sealed class AutoTypeText : AutoTypeStep
{
    internal AutoTypeText(string text) => Text = text;

    /// <summary>The filled text, never empty.</summary>
    public string Text { get; }

    /// <summary>Its kind only, never the text.</summary>
    /// <returns><c>a field's text</c>.</returns>
    public override string ToString() => "a field's text";
}

/// <summary>Types the item's TOTP code as it is when typing reaches this step (D-0477).</summary>
public sealed class AutoTypeCode : AutoTypeStep
{
    private readonly TotpSpec _totp;

    internal AutoTypeCode(TotpSpec totp) => _totp = totp;

    /// <summary>The code current now.</summary>
    /// <param name="clock">The clock typing reads now from.</param>
    /// <returns>The code's digits, or Steam's characters.</returns>
    /// <exception cref="ObjectDisposedException">The plan was disposed and the seed zeroed.</exception>
    public string Code(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return _totp.Code(clock).Value;
    }

    /// <summary>Its kind only, never the code.</summary>
    /// <returns><c>the TOTP code</c>.</returns>
    public override string ToString() => "the TOTP code";
}

/// <summary>Pauses before the next step, from <c>{DELAY n}</c>.</summary>
public sealed class AutoTypePause : AutoTypeStep
{
    internal AutoTypePause(TimeSpan duration) => Duration = duration;

    /// <summary>How long.</summary>
    public TimeSpan Duration { get; }

    /// <summary>The pause.</summary>
    /// <returns>Such as <c>pause 500 ms</c>.</returns>
    public override string ToString() => $"pause {Duration.TotalMilliseconds} ms";
}

/// <summary>Sets the wait between keys from here on, from <c>{DELAY=n}</c>.</summary>
public sealed class AutoTypeKeyDelay : AutoTypeStep
{
    internal AutoTypeKeyDelay(TimeSpan delay) => Delay = delay;

    /// <summary>The wait between keys.</summary>
    public TimeSpan Delay { get; }

    /// <summary>The wait.</summary>
    /// <returns>Such as <c>50 ms between keys</c>.</returns>
    public override string ToString() => $"{Delay.TotalMilliseconds} ms between keys";
}

/// <summary>
/// Clears the focused field with KeePassXC 2.8's keys for the platform, from <c>{CLEARFIELD}</c>: Ctrl+Home,
/// Ctrl+Shift+End and Backspace on Windows; Command+Left, Command+Shift+Right and Backspace on macOS; Home,
/// Shift+End and Backspace on X11.
/// </summary>
public sealed class AutoTypeClearField : AutoTypeStep
{
    internal AutoTypeClearField()
    {
    }

    /// <summary>The step.</summary>
    /// <returns><c>clear the field</c>.</returns>
    public override string ToString() => "clear the field";
}
