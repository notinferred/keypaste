using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A value's KeePass placeholders fill in from its own item's fields wherever it leaves keypaste, and anything
/// keypaste cannot fill in refuses the value whole, naming the placeholder and never a value (P.3b1).
/// </summary>
public sealed class KeePassPlaceholdersTests
{
    private const string _password = "s3cret-pw";
    private const string _apiKey = "sk-live-key";
    private const string _recovery = "rc-1234";
    private const string _refused = "holds the KeePass placeholder ";

    private static readonly PlaceholderFields _stripe = Fields(
        new VaultEntry { Title = "Stripe", Username = "alice", Password = _password, Url = "https://stripe.example", Notes = "line one" },
        ("API_KEY", _apiKey),
        ("Recovery codes", _recovery),
        ("EMPTY_KEY", string.Empty));

    [Theory]
    [InlineData("{PASSWORD}", _password)]
    [InlineData("a{password}b", "a" + _password + "b")]
    [InlineData("{USERNAME}:{URL}", "alice:https://stripe.example")]
    [InlineData("{Title}/{notes}", "Stripe/line one")]
    [InlineData("{s:API_KEY}", _apiKey)]
    [InlineData("{S:Password}", _password)]
    [InlineData("[{S:EMPTY_KEY}]", "[]")]
    [InlineData("{{PASSWORD}}", "{" + _password + "}")]
    [InlineData("{a{PASSWORD}", "{a" + _password)]
    [InlineData("{PASSWORD}{", _password + "{")]
    [InlineData("{\"a\":{\"b\":1}}", "{\"a\":{\"b\":1}}")]
    [InlineData("{}", "{}")]
    [InlineData("{PASSWORDS}", "{PASSWORDS}")]
    [InlineData("${HOME}", "${HOME}")]
    [InlineData("{PASSWORD", "{PASSWORD")]
    public void A_placeholder_fills_in_in_any_case_and_other_braces_are_text(string value, string filled)
    {
        var fill = KeePassPlaceholders.Fill(value, () => _stripe);

        Assert.Null(fill.Refusal);
        Assert.Equal(filled, fill.Value);
    }

    [Theory]
    [InlineData("{PASSWORD}", true)]
    [InlineData("a{password}b", true)]
    [InlineData("{USERNAME}:{URL}", true)]
    [InlineData("{S:API Key}", true)]
    [InlineData("{REF:P@I:0123}", true)]
    [InlineData("{DT_SIMPLE}", true)]
    [InlineData("{t-conv:/x/upper/}", true)]
    [InlineData("{T-CONV:/{Foo}/upper/}", true)]
    [InlineData("{T-REPLACE-RX:/a/(.{8})/b/}", true)]
    [InlineData("{dt_hunter}", true)]
    [InlineData("{T-CONV:/{x", false)]
    [InlineData("{\"a\":{\"b\":1}}", false)]
    [InlineData("{}", false)]
    [InlineData("{PASSWORDS}", false)]
    [InlineData("${HOME}", false)]
    [InlineData("{PASSWORD", false)]
    public void A_placeholder_is_held_by_name_and_other_braces_are_not(string value, bool holds) =>
        Assert.Equal(holds, KeePassPlaceholders.Holds(value));

    [Theory]
    [InlineData("{REF:P@I:0123}", "{REF:…}, which keypaste does not fill in")]
    [InlineData("{t-conv:/x/upper/}", "{T-CONV:…}, which keypaste does not fill in")]
    [InlineData("{T-Replace-RX:/a/b/}", "{T-REPLACE-RX:…}, which keypaste does not fill in")]
    [InlineData("{T-CONV:/{USERNAME}/Upper/}", "{T-CONV:…}, which keypaste does not fill in")]
    [InlineData("{t-replace-rx:/{PASSWORD}/a/b/}", "{T-REPLACE-RX:…}, which keypaste does not fill in")]
    [InlineData("{T-REPLACE-RX:/{S:API_KEY}/^(.{8}).*$/$1/}", "{T-REPLACE-RX:…}, which keypaste does not fill in")]
    [InlineData("{url:rmvscm}", "{URL:…}, which keypaste does not fill in")]
    [InlineData("{DT_SIMPLE}", "{DT_SIMPLE}, which keypaste does not fill in")]
    [InlineData("{dt_utc_year}", "{DT_UTC_YEAR}, which keypaste does not fill in")]
    [InlineData("x{dt_hunter}y", "{DT_…}, which keypaste does not fill in")]
    [InlineData("{totp}", "{TOTP}, which keypaste does not fill in")]
    [InlineData("{TimeOtp}", "{TIMEOTP}, which keypaste does not fill in")]
    [InlineData("{uuid}", "{UUID}, which keypaste does not fill in")]
    [InlineData("{Db_Dir}", "{DB_DIR}, which keypaste does not fill in")]
    [InlineData("{S:Recovery codes}", "{S:Recovery codes}, which names a field keypaste never releases")]
    [InlineData("{S:API Key}", "{S:…}, which names no field of its item")]
    [InlineData("{S:password}", "{S:…}, which names no field of its item")]
    [InlineData("{S:}", "{S:…}, which names no field of its item")]
    [InlineData("postgres://{USERNAME}:{PASSWORD}@{REF:T@I:0}", "{REF:…}, which keypaste does not fill in")]
    public void Anything_keypaste_cannot_fill_in_refuses_the_value_whole_naming_the_placeholder(string value, string refusal)
    {
        var fill = KeePassPlaceholders.Fill(value, () => _stripe);

        Assert.Null(fill.Value);
        Assert.Equal(_refused + refusal, fill.Refusal);
        Assert.Equal(fill.Refusal, fill.ToString());

        foreach (var secret in new[] { _password, _apiKey, _recovery, "alice", "line one" })
        {
            Assert.DoesNotContain(secret, fill.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_placeholder_that_leads_back_to_itself_is_refused()
    {
        var direct = Fields(new VaultEntry { Title = "Loop", Password = "x{password}" });
        var indirect = Fields(new VaultEntry { Title = "Loop" }, ("A_KEY", "{S:B_KEY}"), ("B_KEY", "x{S:A_KEY}"));

        Assert.Equal(_refused + "{PASSWORD}, which leads back to itself", KeePassPlaceholders.Fill("{PASSWORD}", () => direct).Refusal);
        Assert.Equal(_refused + "{S:A_KEY}, which leads back to itself", KeePassPlaceholders.Fill("{S:A_KEY}", () => indirect).Refusal);
    }

    [Fact]
    public void Ten_nested_placeholders_fill_in_and_an_eleventh_is_refused()
    {
        Assert.Equal("end", KeePassPlaceholders.Fill("{S:F1}", () => Chain(10)).Value);
        Assert.Equal(_refused + "{S:F11}, which nests more than 10 deep", KeePassPlaceholders.Fill("{S:F1}", () => Chain(11)).Refusal);
    }

    [Fact]
    public void A_field_filled_in_once_still_counts_its_depth_where_it_is_reached_again()
    {
        // G nests one more level, and F1 reaches it through a chain ending in F9.
        var fields = Fields(
            new VaultEntry { Title = "Depth" },
            [("G", "{S:H}"), ("H", "end"), .. Enumerable.Range(1, 9).Select(n => ($"F{n}", n == 9 ? "{S:G}" : $"{{S:F{n + 1}}}"))]);

        Assert.Equal("endend", KeePassPlaceholders.Fill("{S:G}{S:F2}", () => fields).Value);
        Assert.Equal(_refused + "{S:G}, which nests more than 10 deep", KeePassPlaceholders.Fill("{S:G}{S:F1}", () => fields).Refusal);
    }

    [Fact]
    public void A_wide_fan_out_is_refused_at_the_cap_without_being_filled_out()
    {
        // Each field holds 64 of the next, so A_KEY would fill in to 256 times the cap.
        var fields = Fields(
            new VaultEntry { Title = "Fan" },
            ("A_KEY", Repeated("{S:B_KEY}", 64)),
            ("B_KEY", Repeated("{S:C_KEY}", 64)),
            ("C_KEY", Repeated("{S:D_KEY}", 64)),
            ("D_KEY", Repeated("{S:E_KEY}", 64)),
            ("E_KEY", "0123456789abcdef"));

        Assert.Equal(_refused + "{S:C_KEY}, which fills it past 1 MiB", KeePassPlaceholders.Fill("{S:A_KEY}", () => fields).Refusal);
    }

    [Fact]
    public void A_value_filled_in_to_1_MiB_leaves_and_one_character_more_is_refused()
    {
        var fields = Fields(new VaultEntry { Title = "Big", Password = new string('p', KeePassPlaceholders.MaximumLength - 2) });

        Assert.Equal(KeePassPlaceholders.MaximumLength, KeePassPlaceholders.Fill("{PASSWORD}xy", () => fields).Value!.Length);
        Assert.Equal(_refused + "{PASSWORD}, which fills it past 1 MiB", KeePassPlaceholders.Fill("{PASSWORD}xyz", () => fields).Refusal);
    }

    [Fact]
    public void Each_text_filled_in_at_any_depth_is_a_part_once_and_an_empty_one_is_none()
    {
        var fields = Fields(
            new VaultEntry { Title = "Database", Username = "dbuser", Password = "pw-{S:DB_PASS}" },
            ("DB_PASS", "inner-secret"),
            ("EMPTY_KEY", string.Empty));

        var fill = KeePassPlaceholders.Fill("postgres://{USERNAME}:{PASSWORD}@db/{PASSWORD}{S:EMPTY_KEY}", () => fields);

        Assert.Equal("postgres://dbuser:pw-inner-secret@db/pw-inner-secret", fill.Value);
        Assert.Equal(["dbuser", "inner-secret", "pw-inner-secret"], fill.Parts);
        Assert.DoesNotContain("secret", fill.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Fields_are_read_once_and_only_for_a_placeholder_that_could_fill_in()
    {
        var reads = 0;

        PlaceholderFields Read()
        {
            reads++;
            return _stripe;
        }

        Assert.Equal("no placeholder {here}", KeePassPlaceholders.Fill("no placeholder {here}", Read).Value);
        Assert.NotNull(KeePassPlaceholders.Fill("{REF:P@I:0123}{PASSWORD}", Read).Refusal);
        Assert.Equal(0, reads);

        Assert.Equal($"alice:{_password}:{_apiKey}", KeePassPlaceholders.Fill("{USERNAME}:{PASSWORD}:{S:API_KEY}", Read).Value);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void A_planted_value_names_what_fills_in_or_the_first_placeholder_that_refuses_it()
    {
        var planted = KeePassPlaceholders.Planted("{USERNAME}@{url}/{USERNAME}/{S:NEW_KEY}", ["NEW_KEY"]);

        Assert.Equal(["{USERNAME}", "{URL}", "{S:NEW_KEY}"], planted.Fills);
        Assert.Null(planted.Refused);
        Assert.Empty(KeePassPlaceholders.Planted("{\"json\":true}", []).Fills);
        Assert.Equal(new PlaceholderRefusal("{REF:…}", "keypaste does not fill in"), KeePassPlaceholders.Planted("a{PASSWORD}{REF:T@I:0}", []).Refused);
        Assert.Equal(new PlaceholderRefusal("{T-CONV:…}", "keypaste does not fill in"), KeePassPlaceholders.Planted("{T-CONV:/{USERNAME}/Upper/}", []).Refused);
        Assert.Equal(new PlaceholderRefusal("{S:…}", "names no field of its item"), KeePassPlaceholders.Planted("{S:NEW_KEY}", []).Refused);
        Assert.Equal(
            new PlaceholderRefusal("{S:Recovery codes}", "names a field keypaste never releases"),
            KeePassPlaceholders.Planted("{S:Recovery codes}", ["Recovery codes"]).Refused);
        Assert.Equal(new PlaceholderRefusal("{PASSWORD}", "leads back to itself"), KeePassPlaceholders.Planted("x{PASSWORD}", [], "Password").Refused);
    }

    private static PlaceholderFields Fields(VaultEntry entry, params (string Name, string Value)[] custom) =>
        PlaceholderFields.Of(entry, custom.Select(field => KeyValuePair.Create(field.Name, field.Value)), custom.Select(field => field.Name));

    // F1 holds {S:F2}, and so on, until the last holds "end".
    private static PlaceholderFields Chain(int length) =>
        Fields(
            new VaultEntry { Title = "Chain" },
            [.. Enumerable.Range(1, length).Select(n => ($"F{n}", n == length ? "end" : $"{{S:F{n + 1}}}"))]);

    private static string Repeated(string text, int count) => string.Concat(Enumerable.Repeat(text, count));
}
