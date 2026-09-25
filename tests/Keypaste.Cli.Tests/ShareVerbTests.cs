using System.Globalization;
using System.Text.Json;
using Keypaste.Cli.Clipboard;
using Keypaste.Cli.Commands;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste share</c> against a fake keypaste.com: where the link goes, what is said about it,
/// and every refusal that has to happen before a vault is unlocked or anything is uploaded.
/// </summary>
public sealed class ShareVerbTests : IDisposable
{
    private const string Master = "share-master-5e1d";
    private const string StripeValue = "sk_live_THE-VALUE-THAT-MUST-NOT-PRINT";

    private readonly CliHarness _cli = new();
    private readonly FakeShareServer _server = new();

    public ShareVerbTests()
    {
        _cli.SeedVault(
            Master,
            ("env/acme-api/STRIPE_KEY", StripeValue),
            ("Banking/Chase", "chase-password-1"),
            ("Work/Chase", "chase-password-2"));
        _cli.Environment[KeypasteHome.EnvironmentVariable] = Home;
        _cli.Prompt.PromptsSeen.Clear();
        _cli.Prompt.SecretPrompts.Clear();
        _cli.Prompt.IssuedSecrets.Clear();
    }

    private string Home => Path.Combine(_cli.Directory, "home");

    public void Dispose()
    {
        _server.Dispose();
        _cli.Dispose();
    }

    private int Share(params string[] args)
    {
        _cli.Prompt.Enqueue(Master);
        return ShareCommand.Execute(["share", .. args, "--vault", _cli.VaultPath], _cli.NewContext(), _server);
    }

    private string CreatePrinted(params string[] args)
    {
        _cli.AssertExit(CliApp.ExitSuccess, Share(["env/acme-api/STRIPE_KEY", "--print", .. args]));
        var link = _cli.Out.Trim();
        Clear();
        return link;
    }

    private void Clear()
    {
        _cli.Stdout.GetStringBuilder().Clear();
        _cli.Stderr.GetStringBuilder().Clear();
    }

    private string Audit()
    {
        var path = KeypasteHome.AuditPath(Home);
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private string LocalExpiry(TimeSpan ttl) =>
        TimeZoneInfo.ConvertTime(_server.Now + ttl, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    [Fact]
    public void Share_CopiesTheLink_AndPrintsTheRule()
    {
        string? copied = null;
        _cli.ClearStrategy.DuringWait = () => copied = _cli.Clipboard.Content;

        var exit = Share("env/acme-api/STRIPE_KEY");

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.NotNull(copied);
        Assert.True(ShareLink.TryParse(copied, out var id, out var key));
        Assert.StartsWith("https://keypaste.com/s/#", copied, StringComparison.Ordinal);
        Assert.True(ShareCrypto.TryOpen(_server.Shares[id].Envelope, key, ReadOnlySpan<char>.Empty, out var payload, out _));
        Assert.Equal(StripeValue, Assert.Single(payload.Fields).Value);

        Assert.Empty(_cli.Out);
        Assert.Contains($"  ✓ link copied · 1 view · expires {LocalExpiry(TimeSpan.FromHours(24))}", _cli.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("via", _cli.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(key, _cli.Err, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(GetCommand.DefaultTimeoutSeconds), _cli.ClearStrategy.RequestedDelay);
        Assert.True(_cli.ClearStrategy.Cleared);

        Assert.Contains("\"method\":\"share-created\"", Audit(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, Audit(), StringComparison.Ordinal);
    }

    [Fact]
    public void Share_Print_PutsOnlyTheLinkOnStdout()
    {
        var exit = Share("STRIPE_KEY", "--print", "--views", "3", "--ttl", "1h", "--to", "sam@acme.dev");

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        var lines = _cli.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var link = Assert.Single(lines);
        Assert.True(ShareLink.TryParse(link, out _, out var key));
        Assert.Equal(0, _cli.Clipboard.SetCount);
        Assert.Contains($"  ✓ link created · 3 views · expires {LocalExpiry(TimeSpan.FromHours(1))}", _cli.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(key, _cli.Err, StringComparison.Ordinal);
        Assert.Contains("\"views\":3", _server.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"ttl_seconds\":3600", _server.Requests[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("sam@acme.dev", _server.Transcript, StringComparison.Ordinal);
    }

    [Fact]
    public void Share_ClipboardFailure_WithdrawsTheLink()
    {
        _cli.Clipboard.SetStatus = ClipboardStatus.NoDisplay;

        var exit = Share("env/acme-api/STRIPE_KEY");

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("keypaste share: no clipboard tool found; pass --print to get the link on stdout", _cli.Err, StringComparison.Ordinal);
        Assert.Equal([HttpMethod.Post, HttpMethod.Delete], _server.Requests.Select(r => r.Method));
        Assert.Empty(_server.Shares);
        Assert.Empty(_cli.Out);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("ls", "--offline", "--json"));
        Assert.Equal("[]", _cli.Out.Trim());
    }

    [Fact]
    public void Share_Passphrase_PromptsTwice_AndMismatchRefuses()
    {
        _cli.Prompt.Interactive = true;
        _cli.Prompt.Enqueue(Master, "a long passphrase", "a long passphrase");

        var exit = ShareCommand.Execute(["share", "env/acme-api/STRIPE_KEY", "--passphrase", "--print", "--vault", _cli.VaultPath], _cli.NewContext(), _server);

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(["Master password: ", "Passphrase: ", "Repeat passphrase: "], _cli.Prompt.SecretPrompts);
        Assert.Contains("  passphrase: send it separately", _cli.Err, StringComparison.Ordinal);
        Assert.True(ShareLink.TryParse(_cli.Out.Trim(), out var id, out var key));
        Assert.True(ShareCrypto.TryOpen(_server.Shares[id].Envelope, key, "a long passphrase", out _, out _));
        Assert.False(ShareCrypto.TryOpen(_server.Shares[id].Envelope, key, ReadOnlySpan<char>.Empty, out _, out _));
        Assert.All(_cli.Prompt.IssuedSecrets, buffer => Assert.True(buffer.IsZeroed));

        Clear();
        _cli.Prompt.Enqueue(Master, "a long passphrase", "another passphrase");
        exit = ShareCommand.Execute(["share", "env/acme-api/STRIPE_KEY", "--passphrase", "--print", "--vault", _cli.VaultPath], _cli.NewContext(), _server);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("the passphrases do not match", _cli.Err, StringComparison.Ordinal);
        Assert.Single(_server.Requests);

        Clear();
        _cli.Prompt.Enqueue(Master, "short", "short");
        exit = ShareCommand.Execute(["share", "env/acme-api/STRIPE_KEY", "--passphrase", "--print", "--vault", _cli.VaultPath], _cli.NewContext(), _server);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("at least 8 characters", _cli.Err, StringComparison.Ordinal);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public void Share_AmbiguousTitle_ListsPaths()
    {
        var exit = Share("Chase");

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("more than one entry is called 'Chase'", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("  Banking/Chase", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("  Work/Chase", _cli.Err, StringComparison.Ordinal);
        Assert.Empty(_server.Requests);

        Clear();
        Assert.Equal(CliApp.ExitNotFound, Share("Nothing"));
        Assert.Contains("no entry 'Nothing'", _cli.Err, StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("Banking/Chase", "--print"));
        Assert.Single(_server.Requests);
    }

    [Fact]
    public void Share_ReservedEntry_IsRefused()
    {
        Assert.Equal(CliApp.ExitUsageError, Share(".keypaste/shares/anything", "--print"));
        Assert.Contains("keypaste's own records cannot be shared", _cli.Err, StringComparison.Ordinal);
        Assert.Empty(_server.Requests);
    }

    [Theory]
    [InlineData("http://share.example.org")]
    [InlineData("https://share.example.org/path")]
    [InlineData("not a url")]
    public void Share_BadEndpoint_RefusesBeforeUnlocking(string endpoint)
    {
        var exit = Share("env/acme-api/STRIPE_KEY", "--endpoint", endpoint);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("--endpoint must be an https origin", _cli.Err, StringComparison.Ordinal);
        Assert.Empty(_cli.Prompt.PromptsSeen);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public void Share_EnvironmentNonLoopbackEndpoint_IsRefused()
    {
        _cli.Environment[ShareEndpoint.EnvironmentVariable] = "https://share.attacker.example";

        var exit = Share("env/acme-api/STRIPE_KEY");

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains(
            "keypaste share: KEYPASTE_SHARE_URL may only name a local development server; pass --endpoint to use another host",
            _cli.Err,
            StringComparison.Ordinal);
        Assert.Empty(_cli.Prompt.PromptsSeen);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public void Share_EndpointOption_IsNamedOnTheSuccessLine()
    {
        var link = CreatePrinted("--endpoint", "https://share.example.org");

        Assert.StartsWith("https://share.example.org/s/#", link, StringComparison.Ordinal);
        Assert.Equal("https://share.example.org/api/share", _server.Requests[0].Uri.ToString());

        _cli.Environment[ShareEndpoint.EnvironmentVariable] = "http://127.0.0.1:8787";
        _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print"));
        Assert.StartsWith("http://127.0.0.1:8787/s/#", _cli.Out, StringComparison.Ordinal);
        Assert.Contains(" · via 127.0.0.1:8787", _cli.Err, StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print", "--endpoint", "https://share.example.org"));
        Assert.Contains(" · via share.example.org", _cli.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Share_WhileTheVaultIsHeld_IsRefused()
    {
        Assert.True(VaultClaim.TryAcquire(Home, _cli.VaultPath, OwnerKind.TerminalAgent, out var claim, out var refusal), refusal);
        using (claim)
        {
            Assert.Equal(CliApp.ExitInternalError, Share("env/acme-api/STRIPE_KEY"));
            Assert.Equal(CliApp.ExitInternalError, Share("revoke", "--expired"));
        }

        Assert.Contains("this vault is already unlocked in", _cli.Err, StringComparison.Ordinal);
        Assert.Empty(_cli.Prompt.PromptsSeen);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public void ShareLs_Table_And_Json()
    {
        var first = CreatePrinted("--to", "sam@acme.dev", "--views", "3");
        var second = CreatePrinted("--ttl", "7d");
        Assert.True(ShareLink.TryParse(first, out var firstId, out _));
        Assert.True(ShareLink.TryParse(second, out var secondId, out _));
        _server.OpenAll(secondId);

        _cli.AssertExit(CliApp.ExitSuccess, Share("ls"));

        var lines = _cli.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Matches(@"^  ID\s+WHAT\s+TO\s+RULE\s+STATUS$", lines[0]);
        var firstRow = Assert.Single(lines, l => l.Contains(firstId[..8], StringComparison.Ordinal));
        Assert.Matches(@"env/acme-api/STRIPE_KEY\s+sam@acme\.dev\s+3 views · 24h\s+3 views left$", firstRow);
        var secondRow = Assert.Single(lines, l => l.Contains(secondId[..8], StringComparison.Ordinal));
        Assert.Matches(@"env/acme-api/STRIPE_KEY\s+-\s+1 view · 7d\s+gone$", secondRow);
        Assert.DoesNotContain(firstId, _cli.Out, StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("ls", "--json"));

        using var json = JsonDocument.Parse(_cli.Out);
        var rows = json.RootElement.EnumerateArray().ToDictionary(row => row.GetProperty("id").GetString()!);
        var open = rows[firstId];
        Assert.Equal("env/acme-api/STRIPE_KEY", open.GetProperty("what").GetString());
        Assert.Equal("password", open.GetProperty("field").GetString());
        Assert.Equal("sam@acme.dev", open.GetProperty("to").GetString());
        Assert.Equal(3, open.GetProperty("views").GetInt32());
        Assert.Equal(86400, open.GetProperty("ttl_seconds").GetInt32());
        Assert.False(open.GetProperty("passphrase").GetBoolean());
        Assert.Equal("open", open.GetProperty("status").GetString());
        Assert.Equal(3, open.GetProperty("views_left").GetInt32());
        Assert.Equal("gone", rows[secondId].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, rows[secondId].GetProperty("to").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[secondId].GetProperty("views_left").ValueKind);

        Clear();
        var requests = _server.Requests.Count;
        _cli.AssertExit(CliApp.ExitSuccess, Share("ls", "--offline"));
        Assert.Equal(requests, _server.Requests.Count);
        Assert.Contains("unknown", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareRevoke_Prefix()
    {
        var link = CreatePrinted();
        Assert.True(ShareLink.TryParse(link, out var id, out _));

        Assert.Equal(CliApp.ExitUsageError, Share("revoke", id[..5]));
        Assert.Contains("at least the first 6 characters", _cli.Err, StringComparison.Ordinal);

        Clear();
        Assert.Equal(CliApp.ExitNotFound, Share("revoke", "zzzzzzzz"));

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("revoke", id[..8]));
        Assert.Contains($"  ✓ revoked {id[..8]} · the link no longer opens", _cli.Err, StringComparison.Ordinal);
        Assert.Empty(_server.Shares);
        Assert.Equal(HttpMethod.Delete, _server.Requests[^1].Method);
        Assert.Contains("\"method\":\"share-revoked\"", Audit(), StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("ls", "--offline", "--json"));
        Assert.Equal("[]", _cli.Out.Trim());
    }

    [Fact]
    public void ShareRevoke_NetworkFailure_KeepsTheRecord()
    {
        var link = CreatePrinted();
        Assert.True(ShareLink.TryParse(link, out var id, out _));
        _server.Unreachable = true;

        Assert.Equal(CliApp.ExitInternalError, Share("revoke", id));
        Assert.Contains("the link still opens", _cli.Err, StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("ls", "--offline", "--json"));
        Assert.Contains(id, _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareRevoke_Expired_IsLocalOnly()
    {
        var shortLived = CreatePrinted("--ttl", "1h");
        var longLived = CreatePrinted("--ttl", "7d");
        Assert.True(ShareLink.TryParse(shortLived, out var shortId, out _));
        Assert.True(ShareLink.TryParse(longLived, out var longId, out _));
        _cli.Clock.Now = _server.Now.AddHours(2);
        var requests = _server.Requests.Count;

        _cli.AssertExit(CliApp.ExitSuccess, Share("revoke", "--expired"));

        Assert.Contains("  ✓ forgot 1 expired link", _cli.Err, StringComparison.Ordinal);
        Assert.Equal(requests, _server.Requests.Count);
        Assert.DoesNotContain("share-revoked", Audit(), StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("ls", "--offline", "--json"));
        Assert.DoesNotContain(shortId, _cli.Out, StringComparison.Ordinal);
        Assert.Contains(longId, _cli.Out, StringComparison.Ordinal);

        Clear();
        Assert.Equal(CliApp.ExitUsageError, Share("revoke", longId, "--expired"));
    }

    [Theory]
    [InlineData("--ttl", "4m")]
    [InlineData("--ttl", "8d")]
    [InlineData("--ttl", "24")]
    [InlineData("--views", "0")]
    [InlineData("--views", "11")]
    [InlineData("--field", "secret")]
    public void Share_LimitsOutsideTheRules_AreUsageErrors(string option, string value)
    {
        Assert.Equal(CliApp.ExitUsageError, Share("env/acme-api/STRIPE_KEY", option, value));
        Assert.Empty(_cli.Prompt.PromptsSeen);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public void Share_TtlAndExpires_Together_IsAUsageError()
    {
        Assert.Equal(CliApp.ExitUsageError, Share("env/acme-api/STRIPE_KEY", "--ttl", "1h", "--expires", "2h"));
        Assert.Contains("not both", _cli.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Share_IsDispatchedByTheApp()
    {
        Assert.Equal(CliApp.ExitUsageError, _cli.Run("share"));
        Assert.Contains("usage: keypaste share", _cli.Err, StringComparison.Ordinal);
    }
}
