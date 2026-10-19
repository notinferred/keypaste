using System.Text.Json;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste show</c>: one item's fields and tags, every value masked unless <c>--reveal</c>
/// (D-0463).
/// </summary>
public sealed class ShowVerbTests : IDisposable
{
    private const string _master = "show-verb-master";
    private const string _username = "stripe-user-7c1d";
    private const string _password = "stripe-password-0be4";
    private const string _url = "https://stripe.example/a91f";
    private const string _notes = "first note line\nsecond note line";
    private const string _secret = "sk_live_secret_52aa";
    private const string _region = "us-east-3e08";

    private static readonly string[] _values = [_username, _password, _url, "first note line", "second note line", _secret, _region];

    private readonly CliHarness _harness = new();

    public ShowVerbTests()
    {
        _harness.SeedVault(_master);

        using var vault = Vault.Open(_harness.VaultPath, _master);
        vault.AddEntry(new VaultEntry
        {
            GroupPath = "api",
            Title = "Stripe",
            Username = _username,
            Password = _password,
            Url = _url,
            Notes = _notes,
        });
        vault.AddEntry(new VaultEntry { GroupPath = "api", Title = "Bare", Password = "bare-password" });

        var stripe = new EntryName("api", "Stripe");
        Assert.True(vault.SetFields(stripe, [new FieldWrite("STRIPE_SECRET_KEY", _secret), new FieldWrite("Region", _region, Protect: false)]));
        Assert.True(vault.AddTag(stripe, "env:billing"));
        vault.Save();

        _harness.Prompt.PromptsSeen.Clear();
    }

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] args)
    {
        _harness.Prompt.Enqueue(_master);
        return _harness.Run([.. args, "--vault", _harness.VaultPath]);
    }

    private static string Row(string label, string value, string flags = "") =>
        "  " + label.PadRight("STRIPE_SECRET_KEY".Length) + "  " + value + (flags.Length == 0 ? string.Empty : "  " + flags);

    private static string Text(params string[] lines) => string.Concat(lines.Select(line => line + Environment.NewLine));

    [Fact]
    public void Every_value_is_masked_without_reveal()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("show", "api/Stripe"));

        Assert.Equal(
            Text(
                "api/Stripe",
                "tags: env:billing",
                Row("username", "********"),
                Row("password", "********"),
                Row("URL", "********"),
                Row("notes", "********"),
                Row("Region", "********", "plain"),
                Row("STRIPE_SECRET_KEY", "********", "protected")),
            _harness.Out);
        Assert.Contains("--reveal prints them", _harness.Err, StringComparison.Ordinal);

        foreach (var value in _values)
        {
            Assert.DoesNotContain(value, _harness.Out + _harness.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Reveal_prints_each_value_and_indents_a_notes_line_under_the_first()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("show", "api/Stripe", "--reveal"));

        Assert.Equal(
            Text(
                "api/Stripe",
                "tags: env:billing",
                Row("username", _username),
                Row("password", _password),
                Row("URL", _url),
                Row("notes", "first note line"),
                new string(' ', 2 + "STRIPE_SECRET_KEY".Length + 2) + "second note line",
                Row("Region", _region, "plain"),
                Row("STRIPE_SECRET_KEY", _secret, "protected")),
            _harness.Out);
        Assert.DoesNotContain("not what the vault holds", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("--reveal prints them", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_standard_field_is_left_out()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("show", "api/Bare"));

        Assert.Equal(Text("api/Bare", "  password  ********"), _harness.Out);
    }

    [Fact]
    public void Json_without_reveal_names_every_field_and_carries_no_value()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("show", "api/Stripe", "--json"));

        using var document = JsonDocument.Parse(_harness.Out);
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("api/Stripe", item.GetProperty("path").GetString());
        Assert.Equal("api", item.GetProperty("group").GetString());
        Assert.Equal("Stripe", item.GetProperty("title").GetString());
        Assert.Equal(["env:billing"], item.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));

        var fields = item.GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal(
            ["username", "password", "url", "notes", "Region", "STRIPE_SECRET_KEY"],
            fields.Select(field => field.GetProperty("name").GetString()));
        Assert.All(fields.Take(4), field => Assert.True(field.GetProperty("standard").GetBoolean()));
        Assert.False(fields[4].GetProperty("standard").GetBoolean());
        Assert.False(fields[4].GetProperty("protected").GetBoolean());
        Assert.True(fields[5].GetProperty("protected").GetBoolean());
        Assert.False(fields[5].GetProperty("readonly").GetBoolean());
        Assert.All(fields, field => Assert.False(field.TryGetProperty("value", out _)));

        foreach (var value in _values)
        {
            Assert.DoesNotContain(value, _harness.Out, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Json_with_reveal_carries_each_value_as_the_vault_holds_it()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("show", "api/Stripe", "--json", "--reveal"));

        using var document = JsonDocument.Parse(_harness.Out);
        var values = Assert.Single(document.RootElement.EnumerateArray())
            .GetProperty("fields")
            .EnumerateArray()
            .ToDictionary(field => field.GetProperty("name").GetString()!, field => field.GetProperty("value").GetString());

        Assert.Equal(_username, values["username"]);
        Assert.Equal(_password, values["password"]);
        Assert.Equal(_url, values["url"]);
        Assert.Equal(_notes, values["notes"]);
        Assert.Equal(_region, values["Region"]);
        Assert.Equal(_secret, values["STRIPE_SECRET_KEY"]);
    }

    [Fact]
    public void A_revealed_value_that_could_misrepresent_itself_is_cleaned_and_told()
    {
        var hostile = "safe" + (char)0x202E + "looking";
        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            Assert.True(vault.SetFields(new EntryName("api", "Bare"), [new FieldWrite("TOKEN", hostile)]));
            vault.Save();
        }

        _harness.AssertExit(CliApp.ExitSuccess, Run("show", "api/Bare", "--reveal"));
        Assert.DoesNotContain(((char)0x202E).ToString(), _harness.Out, StringComparison.Ordinal);
        Assert.Contains("safe looking", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("Use --json --reveal to see it", _harness.Err, StringComparison.Ordinal);

        _harness.Stdout.GetStringBuilder().Clear();
        _harness.AssertExit(CliApp.ExitSuccess, Run("show", "api/Bare", "--reveal", "--json"));
        using var document = JsonDocument.Parse(_harness.Out);
        var token = Assert.Single(document.RootElement[0].GetProperty("fields").EnumerateArray(), field => field.GetProperty("name").GetString() == "TOKEN");
        Assert.Equal(hostile, token.GetProperty("value").GetString());
    }

    [Fact]
    public void KeePassXCs_own_attributes_are_shown_read_only_and_revealed()
    {
        var path = Path.Combine(_harness.Directory, "foreign.kdbx");
        Keypaste.Core.Internal.KeePassInterop.WriteForeignUnchecked(path, System.Text.Encoding.UTF8.GetBytes(_master), null, "AES-KDF", "AES-256");

        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("show", "Banking/Checking", "--vault", path));
        Assert.Contains("  otp  ", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("********  protected, read-only", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("otpauth://", _harness.Out, StringComparison.Ordinal);

        _harness.Stdout.GetStringBuilder().Clear();
        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("show", "Banking/Checking", "--reveal", "--vault", path));
        Assert.Contains("otpauth://", _harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_item_is_not_found()
    {
        _harness.AssertExit(CliApp.ExitNotFound, Run("show", "api/Absent"));

        Assert.Contains("no item 'api/Absent'", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Out);
    }

    [Fact]
    public void An_item_name_is_required()
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("show", "--vault", _harness.VaultPath));

        Assert.Empty(_harness.Prompt.PromptsSeen);
    }
}
