using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// Custom fields from the terminal: <c>get --field</c>, <c>set --field</c>, <c>field ls</c> and
/// <c>field rm</c>. Values are typed or piped, never arguments, and never listed.
/// </summary>
public sealed class FieldVerbTests : IDisposable
{
    private const string _master = "field-verb-master";
    private const string _secret = "FIELD-SENTINEL-3b7c";
    private const string _plain = "PLAIN-SENTINEL-91aa";

    private static readonly EntryName _stripe = new("api", "Stripe");

    private readonly CliHarness _harness = new();

    public FieldVerbTests()
    {
        _harness.SeedVault(_master, ("api/Stripe", "login-password"));
        _harness.Prompt.PromptsSeen.Clear();
    }

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] args) => _harness.Run([.. args, "--vault", _harness.VaultPath]);

    private void Seed(params FieldWrite[] writes)
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);
        vault.SetFields(_stripe, writes);
        vault.Save();
    }

    private IReadOnlyList<EntryField> Fields()
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);
        return vault.Fields(_stripe)!;
    }

    private string? Value(string field)
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);
        return vault.ReadField(_stripe, field);
    }

    private int Revisions()
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);
        return vault.ReadHistory(_stripe)!.Count;
    }

    private byte[] Bytes() => File.ReadAllBytes(_harness.VaultPath);

    [Fact]
    public void Set_writes_a_new_field_protected_and_says_so_by_name_only()
    {
        _harness.Prompt.Enqueue(_master, _secret);

        _harness.AssertExit(CliApp.ExitSuccess, Run("set", "api/Stripe", "--field", "STRIPE_SECRET_KEY"));

        Assert.Equal([new EntryField("STRIPE_SECRET_KEY", true, false)], Fields());
        Assert.Equal(_secret, Value("STRIPE_SECRET_KEY"));
        Assert.Equal(["Master password: ", "Value for STRIPE_SECRET_KEY: "], _harness.Prompt.PromptsSeen);
        Assert.Contains("set STRIPE_SECRET_KEY on api/Stripe", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(_secret, _harness.Err + _harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_plain_writes_a_new_field_plain_and_an_existing_field_keeps_its_flag()
    {
        _harness.Prompt.Enqueue(_master, _plain);
        _harness.AssertExit(CliApp.ExitSuccess, Run("set", "api/Stripe", "--field", "Region", "--plain"));
        Assert.Equal([new EntryField("Region", false, false)], Fields());

        _harness.Prompt.Enqueue(_master, "us");
        _harness.AssertExit(CliApp.ExitSuccess, Run("set", "api/Stripe", "--field", "Region"));
        Assert.Equal([new EntryField("Region", false, false)], Fields());
        Assert.Equal("us", Value("Region"));
    }

    [Fact]
    public void Set_three_fields_is_one_revision_asking_for_each_value_in_turn()
    {
        _harness.Prompt.Enqueue(_master, "1", "2", "3");

        _harness.AssertExit(CliApp.ExitSuccess, Run("set", "api/Stripe", "--field", "A", "--field", "B", "--field", "C"));

        Assert.Equal(1, Revisions());
        Assert.Equal("1", Value("A"));
        Assert.Equal("2", Value("B"));
        Assert.Equal("3", Value("C"));
        Assert.Equal(["Master password: ", "Value for A: ", "Value for B: ", "Value for C: "], _harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void Set_asks_twice_for_each_value_when_a_person_is_typing_and_a_mismatch_writes_nothing()
    {
        _harness.Prompt.Interactive = true;
        _harness.Prompt.Enqueue(_master, "one", "one", "two", "different");
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitUsageError, Run("set", "api/Stripe", "--field", "A", "--field", "B"));

        Assert.Equal(before, Bytes());
        Assert.Contains("nothing was saved", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_plain_naming_an_existing_field_is_refused_before_any_value_is_asked()
    {
        Seed(new FieldWrite("KEY", "k"));
        _harness.Prompt.PromptsSeen.Clear();
        _harness.Prompt.Enqueue(_master, "new");
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitUsageError, Run("set", "api/Stripe", "--field", "KEY", "--plain"));

        Assert.Equal(before, Bytes());
        Assert.Equal(["Master password: "], _harness.Prompt.PromptsSeen);
        Assert.Contains("keeps its protection", _harness.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("otp")]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("TOTP Seed")]
    [InlineData("KPXC_X")]
    [InlineData(" edge")]
    [InlineData("")]
    public void A_refused_name_is_a_usage_error_before_the_vault_opens(string name)
    {
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitUsageError, Run("set", "api/Stripe", "--field", name));
        _harness.AssertExit(CliApp.ExitUsageError, Run("field", "rm", "api/Stripe", name));

        Assert.Equal(before, Bytes());
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Theory]
    [InlineData("--field", "A", "--field", "A")]
    [InlineData("--plain")]
    [InlineData("--field", "A", "--generate")]
    public void Malformed_set_lines_are_usage_errors_before_the_vault_opens(params string[] extra)
    {
        _harness.AssertExit(CliApp.ExitUsageError, Run(["set", "api/Stripe", .. extra]));

        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void Set_field_on_an_entry_that_is_not_there_is_not_found_and_creates_nothing()
    {
        _harness.Prompt.Enqueue(_master, "v");
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitNotFound, Run("set", "api/Nobody", "--field", "KEY"));

        Assert.Equal(before, Bytes());
    }

    [Fact]
    public void Get_field_copies_by_default_and_prints_only_when_asked()
    {
        Seed(new FieldWrite("STRIPE_SECRET_KEY", _secret));

        string? copied = null;
        _harness.ClearStrategy.DuringWait = () => copied = _harness.Clipboard.Content;

        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitSuccess, Run("get", "api/Stripe", "--field", "STRIPE_SECRET_KEY"));
        Assert.Equal(_secret, copied);
        Assert.Equal(TimeSpan.FromSeconds(20), _harness.ClearStrategy.RequestedDelay);
        Assert.True(_harness.ClearStrategy.Cleared);
        Assert.DoesNotContain(_secret, _harness.Out, StringComparison.Ordinal);

        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitSuccess, Run("get", "api/Stripe", "--field", "STRIPE_SECRET_KEY", "--show"));
        Assert.Equal(_secret + Environment.NewLine, _harness.Out);
    }

    [Fact]
    public void Get_field_reads_a_field_keypaste_will_not_write()
    {
        var path = Path.Combine(_harness.Directory, "foreign.kdbx");
        Keypaste.Core.Internal.KeePassInterop.WriteForeignUnchecked(path, System.Text.Encoding.UTF8.GetBytes(_master), null, "AES-KDF", "AES-256");

        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("get", "Banking/Checking", "--field", "otp", "--show", "--vault", path));

        Assert.StartsWith("otpauth://", _harness.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("")]
    public void Get_field_refuses_a_standard_name_before_the_vault_opens(string name)
    {
        _harness.AssertExit(CliApp.ExitUsageError, Run("get", "api/Stripe", "--field", name));

        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Null(_harness.Clipboard.Content);
    }

    [Fact]
    public void Get_field_that_is_not_there_is_not_found()
    {
        _harness.Prompt.Enqueue(_master);

        _harness.AssertExit(CliApp.ExitNotFound, Run("get", "api/Stripe", "--field", "ABSENT"));

        Assert.Contains("has no field 'ABSENT'", _harness.Err, StringComparison.Ordinal);
        Assert.Null(_harness.Clipboard.Content);
    }

    [Fact]
    public void Field_ls_names_fields_and_protection_and_never_a_value()
    {
        Seed(new FieldWrite("STRIPE_SECRET_KEY", _secret), new FieldWrite("Region", _plain, Protect: false));

        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitSuccess, Run("field", "ls", "api/Stripe"));
        Assert.Equal(
            "Region             plain" + Environment.NewLine + "STRIPE_SECRET_KEY  protected" + Environment.NewLine,
            _harness.Out);

        _harness.Stdout.GetStringBuilder().Clear();
        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitSuccess, Run("field", "ls", "api/Stripe", "--json"));
        Assert.Equal(
            """[{"name":"Region","protected":false,"readonly":false},{"name":"STRIPE_SECRET_KEY","protected":true,"readonly":false}]""" + Environment.NewLine,
            _harness.Out);

        Assert.DoesNotContain(_secret, _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(_plain, _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_ls_marks_KeePassXC_attributes_read_only()
    {
        var path = Path.Combine(_harness.Directory, "foreign.kdbx");
        Keypaste.Core.Internal.KeePassInterop.WriteForeignUnchecked(path, System.Text.Encoding.UTF8.GetBytes(_master), null, "AES-KDF", "AES-256");

        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("field", "ls", "Banking/Checking", "--vault", path));

        Assert.Equal("PIN  protected" + Environment.NewLine + "otp  protected, read-only" + Environment.NewLine, _harness.Out);
    }

    [Fact]
    public void Field_rm_removes_one_field_in_one_revision()
    {
        Seed(new FieldWrite("KEY", _secret), new FieldWrite("OTHER", "o"));
        var revisions = Revisions();

        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitSuccess, Run("field", "rm", "api/Stripe", "KEY"));

        Assert.Equal([new EntryField("OTHER", true, false)], Fields());
        Assert.Equal(revisions + 1, Revisions());
        Assert.Contains("removed KEY from api/Stripe", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_rm_of_a_field_that_is_not_there_is_not_found_and_writes_nothing()
    {
        var before = Bytes();

        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitNotFound, Run("field", "rm", "api/Stripe", "ABSENT"));

        Assert.Equal(before, Bytes());
    }

    [Theory]
    [InlineData("field")]
    [InlineData("field", "nope")]
    [InlineData("field", "ls")]
    [InlineData("field", "rm", "api/Stripe")]
    public void Malformed_field_lines_are_usage_errors(params string[] args)
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run(args));
    }
}
