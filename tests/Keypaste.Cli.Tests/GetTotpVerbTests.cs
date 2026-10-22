using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Totp;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste get &lt;item&gt; totp</c> gives the item's current code under get's rule, on the clipboard
/// unless <c>--reveal</c>, and never its seed; <c>keypaste show</c> lists <c>totp</c> with no value.
/// </summary>
public sealed class GetTotpVerbTests : IDisposable
{
    private const string _master = "get-totp-master";
    private const string _seed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private const string _code = "94287082";

    private readonly CliHarness _harness = new();

    public GetTotpVerbTests()
    {
        _harness.SeedVault(_master);
        _harness.Clock.Now = DateTimeOffset.FromUnixTimeSeconds(59);

        using var vault = Vault.Open(_harness.VaultPath, _master);
        vault.AddEntry(new VaultEntry { GroupPath = "web", Title = "Bank", Password = "bank-password" });
        vault.AddEntry(new VaultEntry { GroupPath = "web", Title = "Plain", Password = "plain-password" });
        vault.AddEntry(new VaultEntry { GroupPath = "web", Title = "Counter", Password = "counter-password" });
        vault.SetFieldsUnchecked(new EntryName("web", "Bank"), [new FieldWrite(TotpSpec.OtpField, $"otpauth://totp/Bank?secret={_seed}&digits=8")]);
        vault.SetFieldsUnchecked(new EntryName("web", "Counter"), [new FieldWrite(TotpSpec.OtpField, $"otpauth://hotp/Counter?secret={_seed}&counter=1")]);
        vault.Save();

        _harness.Prompt.PromptsSeen.Clear();
    }

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] args)
    {
        _harness.Prompt.Enqueue(_master);
        return _harness.Run([.. args, "--vault", _harness.VaultPath]);
    }

    [Fact]
    public void With_reveal_it_prints_the_current_code_and_nothing_of_the_seed()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("get", "web/Bank", "totp", "--reveal"));

        Assert.Equal(_code + Environment.NewLine, _harness.Out);
        Assert.Equal(0, _harness.Clipboard.SetCount);
        Assert.DoesNotContain(_seed, _harness.Out + _harness.Err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Without_reveal_the_code_goes_to_the_clipboard_and_is_cleared()
    {
        string? copied = null;
        _harness.ClearStrategy.DuringWait = () => copied = _harness.Clipboard.Content;

        _harness.AssertExit(CliApp.ExitSuccess, Run("get", "web/Bank", "totp"));

        Assert.Equal(_code, copied);
        Assert.DoesNotContain(_code, _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(_code, _harness.Err, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(20), _harness.ClearStrategy.RequestedDelay);
        Assert.True(_harness.ClearStrategy.Cleared);
        Assert.Null(_harness.Clipboard.Content);
    }

    [Fact]
    public void The_code_follows_the_clock()
    {
        _harness.Clock.Now = DateTimeOffset.FromUnixTimeSeconds(1111111109);

        _harness.AssertExit(CliApp.ExitSuccess, Run("get", "web/Bank", "totp", "--reveal"));

        Assert.Equal("07081804" + Environment.NewLine, _harness.Out);
    }

    [Fact]
    public void An_item_with_no_TOTP_is_not_found_and_one_keypaste_refuses_says_why_without_its_seed()
    {
        _harness.AssertExit(CliApp.ExitNotFound, Run("get", "web/Plain", "totp", "--reveal"));
        Assert.Contains("'web/Plain' has no TOTP", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Out);

        _harness.AssertExit(CliApp.ExitInternalError, Run("get", "web/Counter", "totp", "--reveal"));
        Assert.Contains("counter-based HOTP", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(_seed, _harness.Err, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_harness.Out);
    }

    [Theory]
    [InlineData("otp")]
    [InlineData("TOTP")]
    [InlineData("password")]
    public void Only_totp_may_follow_the_item_name(string operand)
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("get", "web/Bank", operand, "--vault", _harness.VaultPath));

        Assert.Contains("only totp may follow", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Out);
    }

    [Fact]
    public void Totp_and_field_are_not_used_together()
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("get", "web/Bank", "totp", "--field", "otp", "--vault", _harness.VaultPath));

        Assert.Contains("totp and --field cannot be used together", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Out);
    }

    [Fact]
    public void Show_lists_totp_with_no_value_even_with_reveal()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("show", "web/Bank", "--reveal"));

        Assert.Contains("  totp      ********" + Environment.NewLine, _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(_code, _harness.Out, StringComparison.Ordinal);
        Assert.Contains("keypaste get web/Bank totp", _harness.Err, StringComparison.Ordinal);

        _harness.Stdout.GetStringBuilder().Clear();
        _harness.AssertExit(CliApp.ExitSuccess, Run("show", "web/Plain", "--reveal"));
        Assert.DoesNotContain("totp", _harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_json_names_totp_and_carries_no_value_for_it()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("show", "web/Bank", "--json", "--reveal"));

        using var document = JsonDocument.Parse(_harness.Out);
        var fields = document.RootElement[0].GetProperty("fields").EnumerateArray().ToList();
        var totp = Assert.Single(fields, field => field.GetProperty("name").GetString() == "totp");

        Assert.False(totp.TryGetProperty("value", out _));
        Assert.Equal(["password", "totp", "otp"], fields.Select(field => field.GetProperty("name").GetString()));
    }
}
