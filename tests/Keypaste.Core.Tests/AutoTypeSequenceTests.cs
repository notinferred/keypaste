using Keypaste.Core.AutoType;
using Keypaste.Core.Totp;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An Auto-Type sequence reads as KeePassXC 2.8's <c>parseSequence</c> reads it, against the cases and errors of its
/// <c>TestAutoType</c>; what keypaste does not type and every reading KeePass 2 gives differently refuse it whole,
/// naming the token and never a value; fields type what D-0465 fills in; <c>{TOTP}</c> types the code current when
/// typing reaches it; and a window pattern matches as KeePassXC matches it, under a timeout (P.4a1).
/// </summary>
public sealed class AutoTypeSequenceTests
{
    private const string _sequenceHolds = "keypaste can't type this item: its Auto-Type sequence holds ";
    private const string _notTyped = ", which keypaste does not type.";
    private const string _differs = ", which KeePass and KeePassXC type differently.";
    private const string _edit = " Edit the item so keypaste can fill it in.";
    private const string _rfcSeed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static readonly VaultEntry _entry = new()
    {
        Title = "Example",
        Username = "Username",
        Password = "Password@1",
        Url = "https://example.com",
        Notes = "one line",
    };

    private static readonly Dictionary<string, string> _custom = new(StringComparer.Ordinal)
    {
        ["attr1"] = "value1",
        ["API_KEY"] = "sk-live-key",
        ["Email"] = "alice@example.com",
        ["SPECIAL TOKEN"] = "space",
        ["SPECIAL:TOKEN"] = "colon",
        ["HELLO WORLD"] = "hello",
        ["otp"] = $"otpauth://totp/x?secret={_rfcSeed}",
    };

    // ------------------------------------------------------------ KeePassXC's cases

    [Theory]
    [InlineData("{USERNAME} {PASSWORD} {URL} {S:attr1}", "Username Password@1 https://example.com value1")]
    [InlineData("{USERNAME}{TAB}{C:Extra Tab}{TAB}{S:attr1}", "Username[Tab][Tab]value1")]
    [InlineData("{{}{}}{}}{{}", "{}}{")]
    [InlineData("{]}{[}{[}{]}", "][[]")]
    [InlineData("{)}{(}{(}{)}", ")(()")]
    [InlineData("{NUMPAD1 3}", "111")]
    [InlineData("{PGDN}", "[PageDown]")]
    [InlineData("{delay 50}", "[pause 50]")]
    [InlineData("{delay=50}", "[delay 50]")]
    [InlineData("{BEEP 3 3}{BEEP 3}", "")]
    [InlineData("{PaSSworD}", "Password@1")]
    [InlineData("{S:SPECIAL TOKEN}{S:SPECIAL:TOKEN}{S:HELLO WORLD}{ENTER}", "spacecolonhello[Enter]")]
    [InlineData("{tab}{Enter}{bksp}{BS}{BACKSPACE}{DEL}{INS}{PGDOWN}{f16}{ESC}{BREAK}{HELP}{ADD}", "[Tab][Enter][Backspace][Backspace][Backspace][Delete][Insert][PageDown][F16][Escape][Break][Help][Add]")]
    [InlineData("^a%{F4}+{TAB}+^%x", "[Control+a][Alt+F4][Shift+Tab][Shift+Control+Alt+x]")]
    [InlineData("+{{}", "[Shift+{]")]
    [InlineData("{LEFTBRACE}{RIGHTBRACE}{LEFTPAREN}{RIGHTPAREN}{#}{+}{%}{^}{~}", "{}()#+%^~")]
    [InlineData("{CLEARFIELD}{PASSWORD}{ENTER}", "[clear]Password@1[Enter]")]
    [InlineData("{S:Email}{TAB}{S:Password}", "alice@example.com[Tab]Password@1")]
    [InlineData("a😀b", "a😀b")]
    public void A_sequence_types_as_KeePassXC_reads_it(string sequence, string typed)
    {
        using var plan = Plan(sequence);

        Assert.Null(plan.Refusal);
        Assert.Equal(typed, Typed(plan.Steps));
    }

    [Fact]
    public void KeePassXCs_counted_keys_press_each_key_that_many_times()
    {
        using var plan = Plan("{SPACE 10}{TAB 3}{RIGHT 50}");

        Assert.Equal(
            string.Concat(Enumerable.Repeat("[Space]", 10).Concat(Enumerable.Repeat("[Tab]", 3)).Concat(Enumerable.Repeat("[Right]", 50))),
            Typed(plan.Steps));
    }

    [Fact]
    public void A_character_outside_the_basic_plane_is_one_character_typed_as_both_halves()
    {
        using var plan = Plan("a😀b");

        var astral = Assert.IsType<AutoTypeCharacter>(plan.Steps[1]);
        Assert.Equal(3, plan.Steps.Count);
        Assert.Equal("😀", astral.Character);
        Assert.Equal(2, astral.Character.Length);
    }

    [Fact]
    public void KeePassXCs_long_sequence_is_refused_for_its_bare_tilde_and_types_without_it()
    {
        const string kpxc = "{F1 23}{~ 23}{% 23}{^}{F12}{(}{) 23}{[}{[}{]}{Delay=23}{+}{SUBTRACT}~+%@fixedstring";

        using (var refused = Plan(kpxc))
        {
            Assert.Equal(_sequenceHolds + "~" + _differs, refused.Refusal);
        }

        using var plan = Plan(kpxc.Replace("}~+", "}+", StringComparison.Ordinal));

        Assert.Null(plan.Refusal);
        Assert.Equal(
            string.Concat(Enumerable.Repeat("[F1]", 23)) + new string('~', 23) + new string('%', 23) + "^[F12](" + new string(')', 23)
                + "[[][delay 23]+[Subtract][Shift+Alt+@]fixedstring",
            Typed(plan.Steps));
    }

    [Theory]
    [InlineData("{{{}}{}{}}{{}}", "has unbalanced braces.")]
    [InlineData("{S:SPECIAL_TOKEN{}}", "has unbalanced braces.")]
    [InlineData("{PASSWORD", "has unbalanced braces.")]
    [InlineData("a}b{", "has unbalanced braces.")]
    [InlineData("{}", "holds {}, which keypaste does not type.")]
    [InlineData("{S:a{}b}", "holds {S:a{}b}, which keypaste does not type.")]
    [InlineData("{DELAY 50000}", "holds {DELAY 50000}, which pauses longer than 10,000 ms.")]
    [InlineData("{DELAY 10001}", "holds {DELAY 10001}, which pauses longer than 10,000 ms.")]
    [InlineData("{delay 5000000000}", "holds {delay 5000000000}, which pauses longer than 10,000 ms.")]
    [InlineData("{DELAY=50000}", "holds {DELAY=50000}, which waits longer than 500 ms between keys.")]
    [InlineData("{DELAY=501}", "holds {DELAY=501}, which waits longer than 500 ms between keys.")]
    [InlineData("{LEFT 50000000}", "holds {LEFT 50000000}, which repeats a key more than 100 times.")]
    [InlineData("{LEFT 101}", "holds {LEFT 101}, which repeats a key more than 100 times.")]
    [InlineData("{T-CONV:/{USERNAME}/base64/}", "holds {T-CONV:/{USERNAME}/base64/}, which keypaste does not type.")]
    [InlineData("{T-REPLACE-RX:/{USERNAME}/a/b/}", "holds {T-REPLACE-RX:/{USERNAME}/a/b/}, which keypaste does not type.")]
    public void KeePassXCs_own_errors_and_what_it_converts_refuse_the_sequence(string sequence, string reason)
    {
        using var plan = Plan(sequence);

        Assert.Equal("keypaste can't type this item: its Auto-Type sequence " + reason, plan.Refusal);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void A_delay_and_a_count_at_their_limits_type()
    {
        using var plan = Plan("{DELAY 10000}{DELAY=500}{DELAY=0}{DELAY 0}{LEFT 100}{TAB 0}");

        Assert.Null(plan.Refusal);
        Assert.Equal("[pause 10000][delay 500][delay 0][pause 0]" + string.Concat(Enumerable.Repeat("[Left]", 100)), Typed(plan.Steps));
    }

    // ------------------------------------------------------------ what keypaste does not type

    [Theory]
    [InlineData("{USERNAME}{TAB}{PASSWORD}{VKEY 13}", "{VKEY 13}")]
    [InlineData("{VKEY-EX 0x01}", "{VKEY-EX 0x01}")]
    [InlineData("{VKEY VK_LBUTTON}", "{VKEY VK_LBUTTON}")]
    [InlineData("{APPACTIVATE Notepad}", "{APPACTIVATE Notepad}")]
    [InlineData("{MODE=VIRTUAL}", "{MODE=VIRTUAL}")]
    [InlineData("{PICKCHARS}", "{PICKCHARS}")]
    [InlineData("{PICKCHARS:Password:C=3}", "{PICKCHARS:Password:C=3}")]
    [InlineData("{CMD:/calc/}", "{CMD:/calc/}")]
    [InlineData("{CLIPBOARD-SET:/x/}", "{CLIPBOARD-SET:/x/}")]
    [InlineData("{CLIPBOARD}", "{CLIPBOARD}")]
    [InlineData("{WIN}", "{WIN}")]
    [InlineData("{LWIN}", "{LWIN}")]
    [InlineData("{rwin}", "{rwin}")]
    [InlineData("{APPS}", "{APPS}")]
    [InlineData("{CAPSLOCK}", "{CAPSLOCK}")]
    [InlineData("{NUMLOCK}", "{NUMLOCK}")]
    [InlineData("{SCROLLLOCK}", "{SCROLLLOCK}")]
    [InlineData("{PTRSC}", "{PTRSC}")]
    [InlineData("{PRTSC}", "{PRTSC}")]
    [InlineData("{win 2}", "{win 2}")]
    [InlineData("#", "#")]
    [InlineData("#r", "#")]
    [InlineData("a+#b", "#")]
    [InlineData("{REF:P@I:0123}", "{REF:P@I:0123}")]
    [InlineData("{URL:RMVSCM}", "{URL:RMVSCM}")]
    [InlineData("{UUID}", "{UUID}")]
    [InlineData("{DT_SIMPLE}", "{DT_SIMPLE}")]
    [InlineData("{DB_DIR}", "{DB_DIR}")]
    [InlineData("{x{PASSWORD}y}", "{x{PASSWORD}y}")]
    [InlineData("{s:attr1}", "{s:attr1}")]
    [InlineData("{USERNAME 2}", "{USERNAME 2}")]
    [InlineData("{F17}", "{F17}")]
    [InlineData("{ESCAPE}", "{ESCAPE}")]
    [InlineData("{CLEAR}", "{CLEAR}")]
    [InlineData("{a}", "{a}")]
    [InlineData("{ TAB}", "{ TAB}")]
    public void A_token_keypaste_does_not_type_refuses_the_whole_sequence_by_name(string sequence, string token)
    {
        using var plan = Plan(sequence);

        Assert.Equal(_sequenceHolds + token + _notTyped, plan.Refusal);
        Assert.Empty(plan.Steps);
    }

    [Theory]
    [InlineData("~", "holds ~")]
    [InlineData("a(b)c", "holds (")]
    [InlineData(")", "holds )")]
    [InlineData("+(ab)", "holds +(")]
    [InlineData("%TEMP%", "holds %TEMP%")]
    [InlineData("{USERNAME}%USERNAME%{ENTER}", "holds %USERNAME%")]
    [InlineData("{S:%HOME%}", "holds %HOME%")]
    [InlineData("a\nb", "holds a control character")]
    [InlineData("a\tb", "holds a control character")]
    [InlineData("{DELAY}", "holds {DELAY}")]
    [InlineData("{DELAY=}", "holds {DELAY=}")]
    [InlineData("{DELAY=x}", "holds {DELAY=x}")]
    [InlineData("{DELAY=50 2}", "holds {DELAY=50 2}")]
    [InlineData("{DELAY= 5}", "holds {DELAY= 5}")]
    [InlineData("+{USERNAME}", "holds +{USERNAME}")]
    [InlineData("^{DELAY 5}a", "holds ^{DELAY 5}")]
    [InlineData("%{C:note}a", "holds %{C:note}")]
    [InlineData("+{CLEARFIELD}", "holds +{CLEARFIELD}")]
    [InlineData("^{TOTP}", "holds ^{TOTP}")]
    [InlineData("{TOTP 2}", "holds {TOTP 2}")]
    [InlineData("{CLEARFIELD 2}", "holds {CLEARFIELD 2}")]
    [InlineData("abc+", "ends with +")]
    [InlineData("^", "ends with ^")]
    [InlineData("x^%", "ends with ^%")]
    public void A_reading_KeePass_2_gives_differently_refuses_the_whole_sequence(string sequence, string reason)
    {
        using var plan = Plan(sequence);

        Assert.Equal("keypaste can't type this item: its Auto-Type sequence " + reason + _differs, plan.Refusal);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void KeePass_2s_two_channel_obfuscation_refuses_the_item()
    {
        var settings = Settings("{USERNAME}{TAB}{PASSWORD}") with { Obfuscated = true };

        using var plan = Resolve(settings, Item(_entry, _custom));

        Assert.Equal("keypaste can't type this item: its Auto-Type asks for KeePass's two-channel obfuscation, which keypaste does not use.", plan.Refusal);
        Assert.Empty(plan.Steps);
    }

    // ------------------------------------------------------------ fields, as D-0465 fills them

    [Fact]
    public void A_field_types_what_D_0465_fills_in_with_its_nested_placeholders_and_braces_inside_it_as_text()
    {
        var entry = _entry with { Password = "{USERNAME}-{S:API_KEY}", Notes = "{TAB}{ENTER} stay text" };

        using var plan = Plan("{PASSWORD}{TAB}{NOTES}", entry);

        Assert.Null(plan.Refusal);
        Assert.Equal("Username-sk-live-key[Tab]{TAB}{ENTER} stay text", Typed(plan.Steps));
        Assert.IsType<AutoTypeText>(plan.Steps[0]);
        Assert.IsType<AutoTypeText>(plan.Steps[2]);
    }

    [Fact]
    public void An_empty_field_types_nothing()
    {
        using var plan = Plan("{USERNAME}{TAB}{PASSWORD}{ENTER}", _entry with { Username = string.Empty });

        Assert.Equal("[Tab]Password@1[Enter]", Typed(plan.Steps));
    }

    [Theory]
    [InlineData("{REF:P@I:0123}", "its password holds the KeePass placeholder {REF:…}, which keypaste does not fill in.")]
    [InlineData("{TOTP}", "its password holds the KeePass placeholder {TOTP}, which keypaste does not fill in.")]
    [InlineData("x{S:Email}", "its password holds the KeePass placeholder {S:Email}, which names a field keypaste never releases.")]
    [InlineData("{S:NO_SUCH_KEY}", "its password holds the KeePass placeholder {S:…}, which names no field of its item.")]
    [InlineData("{PASSWORD}", "its password holds the KeePass placeholder {PASSWORD}, which leads back to itself.")]
    [InlineData("{x{USERNAME}y}", "its password holds the KeePass placeholder {USERNAME}, which KeePassXC 2.7 and 2.8 fill in differently inside braces that name no placeholder.")]
    [InlineData("{S:a{PASSWORD}b}", "its password holds the KeePass placeholder {S:…}, which names no field of its item.")]
    public void A_field_D_0465_refuses_refuses_the_sequence_with_its_sentence_and_the_item_panes_ending(string password, string reason)
    {
        using var plan = Plan("{USERNAME}{TAB}{PASSWORD}{ENTER}", _entry with { Password = password });

        Assert.Equal("keypaste can't type this item: " + reason + _edit, plan.Refusal);
        Assert.Empty(plan.Steps);
    }

    [Theory]
    [InlineData("{S:Missing}", "which names no field of its item.")]
    [InlineData("{S:otp}", "which names a field keypaste never types.")]
    [InlineData("{S:TimeOtp-Secret}", "which names no field of its item.")]
    [InlineData("{S:a{PASSWORD}b}", "which names no field of its item.")]
    [InlineData("{S:CuStOm}", "which names no field of its item.")]
    public void A_field_token_the_item_lacks_or_that_names_a_TOTP_field_refuses_the_sequence(string sequence, string why)
    {
        using var plan = Plan("{USERNAME}" + sequence);

        Assert.Equal(_sequenceHolds + sequence + ", " + why, plan.Refusal);
    }

    [Fact]
    public void A_field_whose_name_holds_braces_is_refused_even_when_the_item_has_it()
    {
        var custom = new Dictionary<string, string>(_custom, StringComparer.Ordinal) { ["a{PASSWORD}b"] = "braced" };

        using var plan = Plan("{S:a{PASSWORD}b}", _entry, custom);

        Assert.Equal(_sequenceHolds + "{S:a{PASSWORD}b}, which names a field keypaste never types.", plan.Refusal);
    }

    [Theory]
    [InlineData("{NOTES}", "its notes hold")]
    [InlineData("{PASSWORD}", "its password holds")]
    [InlineData("{S:Email}", "its field Email holds")]
    public void A_field_whose_text_holds_a_control_character_refuses_the_sequence(string sequence, string subject)
    {
        var entry = _entry with { Notes = "line one\nline two", Password = "tab\there" };
        var custom = new Dictionary<string, string>(_custom, StringComparer.Ordinal) { ["Email"] = "alice@example.com\r\n" };

        using var plan = Plan(sequence, entry, custom);

        Assert.Equal($"keypaste can't type this item: {subject} a control character, such as a line break or a tab, and keypaste types those keys only from the sequence.", plan.Refusal);
        Assert.Empty(plan.Steps);
    }

    // ------------------------------------------------------------ TOTP

    [Fact]
    public void TOTP_types_the_code_current_when_typing_reaches_it_across_a_period_boundary()
    {
        var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(59));
        using var expected = TotpSpec.FromUri(_custom["otp"], out _)!;

        using var plan = Plan("{TOTP}{DELAY 1000}{TIMEOTP}");

        var first = Assert.IsType<AutoTypeCode>(plan.Steps[0]);
        Assert.IsType<AutoTypePause>(plan.Steps[1]);
        var second = Assert.IsType<AutoTypeCode>(plan.Steps[2]);

        var before = first.Code(clock);
        clock.Advance(TimeSpan.FromSeconds(1));
        var after = second.Code(clock);

        Assert.Equal("287082", before);
        Assert.Equal(expected.Code(DateTimeOffset.FromUnixTimeSeconds(60)).Value, after);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void TOTP_is_refused_when_the_item_has_none_or_its_TOTP_cannot_be_computed()
    {
        var none = new Dictionary<string, string>(StringComparer.Ordinal);
        var broken = new Dictionary<string, string>(StringComparer.Ordinal) { ["otp"] = $"otpauth://totp/x?secret={_rfcSeed}&digits=" };

        using var missing = Plan("{USERNAME}{TOTP}", _entry, none);
        using var refused = Plan("{USERNAME}{TOTP}", _entry, broken);

        Assert.Equal(_sequenceHolds + "{TOTP}, and the item has no TOTP.", missing.Refusal);
        Assert.StartsWith(_sequenceHolds + "{TOTP}, and keypaste can't compute the item's TOTP code. ", refused.Refusal, StringComparison.Ordinal);
        Assert.Empty(missing.Steps);
        Assert.Empty(refused.Steps);
    }

    [Fact]
    public void Disposing_the_plan_zeroes_the_TOTP_seed_and_drops_every_step()
    {
        var plan = Plan("{PASSWORD}{TOTP}");
        var code = Assert.IsType<AutoTypeCode>(plan.Steps[1]);

        plan.Dispose();

        Assert.Empty(plan.Steps);
        Assert.Throws<ObjectDisposedException>(() => code.Code(TimeProvider.System));
    }

    // ------------------------------------------------------------ what the plan shows

    [Fact]
    public void The_plan_and_its_steps_show_no_value()
    {
        var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(59));

        using var plan = Plan("{USERNAME}{TAB}{PASSWORD}{TAB}{TOTP}{TAB}{S:API_KEY}Q{ENTER}");
        var shown = string.Join('\n', [plan.ToString(), .. plan.Steps.Select(step => step.ToString())]);
        var code = Assert.IsType<AutoTypeCode>(plan.Steps[4]).Code(clock);

        foreach (var value in new[] { "Username", "Password@1", "sk-live-key", code, _rfcSeed, "Q" })
        {
            Assert.DoesNotContain(value, shown, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_refused_sequence_yields_no_key_even_after_tokens_that_typed()
    {
        using var plan = Plan("{USERNAME}{TAB}{PASSWORD}{TOTP}{ENTER}{S:Missing}");

        Assert.False(plan.CanType);
        Assert.Empty(plan.Steps);
        Assert.Equal(_sequenceHolds + "{S:Missing}, which names no field of its item.", plan.Refusal);
    }

    // ------------------------------------------------------------ window patterns

    [Theory]
    [InlineData("//REGEX1//", "lorem REGEX1 ipsum", "Fits")]
    [InlineData("//REGEX1//", "lorem regex1 ipsum", "Fits")]
    [InlineData("//^REGEX2$//", "REGEX2", "Fits")]
    [InlineData("//^REGEX2$//", "xREGEX2", "Differs")]
    [InlineData(@"//^REGEX3-([rd]\d){2}$//", "REGEX3-R2D2", "Fits")]
    [InlineData("*notepad*", "Windows Notepad", "Fits")]
    [InlineData("*notepad*", "Some Other Window", "Differs")]
    [InlineData("custom window", "Custom Window", "Fits")]
    [InlineData("custom window", "custom window 2", "Differs")]
    [InlineData("a.b", "axb", "Differs")]
    [InlineData("a?b", "axb", "Differs")]
    [InlineData("a?b*", "a?b and more", "Fits")]
    [InlineData("////", "any title", "Fits")]
    [InlineData("///", "///", "Fits")]
    [InlineData("//(//", "(", "Unreadable")]
    public void A_window_pattern_matches_as_KeePassXC_matches_it(string pattern, string title, string match) =>
        Assert.Equal(Enum.Parse<PatternMatch>(match), WindowPattern.Match(pattern, title, WindowPattern.Timeout));

    [Fact]
    public void A_regular_expression_that_runs_out_of_time_matches_nothing() =>
        Assert.Equal(
            PatternMatch.TimedOut,
            WindowPattern.Match(@"//^(\w+\s?)*$//", new string('a', 64) + "!", TimeSpan.FromMilliseconds(10)));

    [Fact]
    public void The_first_association_whose_pattern_fits_chooses_the_sequence_and_each_that_cannot_match_is_named()
    {
        var settings = Settings(string.Empty) with
        {
            Associations =
            [
                new AutoTypeAssociation(string.Empty, "{S:Email}"),
                new AutoTypeAssociation("//(//", "unreadable"),
                new AutoTypeAssociation("*{REF:P@I:0}*", "unfillable"),
                new AutoTypeAssociation(@"//^(\w+\s?)*$//", "slow"),
                new AutoTypeAssociation("non-matching window", "should not match"),
                new AutoTypeAssociation("*{TITLE} sign-in*", string.Empty),
                new AutoTypeAssociation("*sign-in*", "{PASSWORD}"),
            ],
        };
        var item = Item(_entry, _custom);
        var title = new string('a', 64) + "! Example sign-in";

        var choice = settings.Choose(title, item.Fill, TimeSpan.FromMilliseconds(10));

        Assert.Equal(AutoTypeSettings.DefaultSequence, choice.Sequence);
        Assert.Equal(AutoTypeSource.Association, choice.Source);
        Assert.Equal(6, choice.Association);
        Assert.Equal(
            [
                "association 2's window pattern is a regular expression keypaste cannot read",
                "association 3's window pattern holds the KeePass placeholder {REF:…}, which keypaste does not fill in",
                "association 4's window pattern took too long to match this title",
            ],
            choice.Unmatched);
    }

    [Fact]
    public void With_no_title_or_no_fitting_pattern_the_items_own_sequence_types()
    {
        var settings = Settings("{USERNAME}") with { Associations = [new AutoTypeAssociation("*", "{PASSWORD}")] };
        var item = Item(_entry, _custom);

        Assert.Equal(("{USERNAME}", AutoTypeSource.Item, (int?)null), Chosen(settings.Choose(string.Empty, item.Fill, WindowPattern.Timeout)));
        Assert.Equal(("{PASSWORD}", AutoTypeSource.Association, (int?)1), Chosen(settings.Choose("x", item.Fill, WindowPattern.Timeout)));

        var narrow = settings with { Associations = [new AutoTypeAssociation("Other", "{PASSWORD}")] };
        Assert.Equal(("{USERNAME}", AutoTypeSource.Item, (int?)null), Chosen(narrow.Choose("x", item.Fill, WindowPattern.Timeout)));
    }

    // ------------------------------------------------------------ helpers

    private static (string, AutoTypeSource, int?) Chosen(AutoTypeChoice choice) => (choice.Sequence, choice.Source, choice.Association);

    private static AutoTypeSettings Settings(string sequence) =>
        new(true, false, sequence, [], [new GroupAutoType(null, string.Empty)], true, true, null);

    private static AutoTypePlan Plan(string sequence, VaultEntry? entry = null, Dictionary<string, string>? custom = null) =>
        Resolve(Settings(sequence), Item(entry ?? _entry, custom ?? _custom));

    private static AutoTypePlan Resolve(AutoTypeSettings settings, AutoTypeItem item) =>
        AutoTypePlan.Resolve(settings, string.Empty, DateTimeOffset.UnixEpoch, item, WindowPattern.Timeout);

    /// <summary>An item read as the vault reads one: standard fields always present, custom fields by exact name.</summary>
    private static AutoTypeItem Item(VaultEntry entry, Dictionary<string, string> custom)
    {
        var fields = PlaceholderFields.Of(entry, custom, custom.Keys);

        return new AutoTypeItem(
            name => Standard(entry, name) is not null || custom.ContainsKey(name),
            name => Standard(entry, name) ?? custom.GetValueOrDefault(name),
            value => KeePassPlaceholders.Fill(value, () => fields),
            (out string? refusal) => TotpSpec.Read(custom, out refusal));
    }

    private static string? Standard(VaultEntry entry, string field) => field switch
    {
        "Title" => entry.Title,
        "UserName" => entry.Username,
        "Password" => entry.Password,
        "URL" => entry.Url,
        "Notes" => entry.Notes,
        _ => null,
    };

    /// <summary>What the steps type, keys in brackets with their modifiers, as KeePassXC's test platform spells actions.</summary>
    private static string Typed(IEnumerable<AutoTypeStep> steps) => string.Concat(steps.Select(step => step switch
    {
        AutoTypeText text => text.Text,
        AutoTypeCharacter character => character.Modifiers == AutoTypeModifiers.None
            ? character.Character
            : $"[{character.Modifiers.ToString().Replace(", ", "+", StringComparison.Ordinal)}+{character.Character}]",
        AutoTypePress press => $"[{press}]",
        AutoTypeCode => "[TOTP]",
        AutoTypePause pause => $"[pause {pause.Duration.TotalMilliseconds}]",
        AutoTypeKeyDelay delay => $"[delay {delay.Delay.TotalMilliseconds}]",
        AutoTypeClearField => "[clear]",
        _ => throw new InvalidOperationException(step.GetType().Name),
    }));
}
