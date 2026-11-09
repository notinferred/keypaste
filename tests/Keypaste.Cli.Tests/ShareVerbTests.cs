using System.Globalization;
using System.Text.Json;
using Keypaste.Cli.Clipboard;
using Keypaste.Cli.Commands;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Core.Sharing;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste share</c> against a fake keypaste.com: where the link goes, what is said about it,
/// and every refusal that has to happen before a vault is unlocked or anything is uploaded.
/// </summary>
public sealed class ShareVerbTests : IDisposable
{
    private const string _master = "share-master-5e1d";
    private const string _stripeValue = "sk_live_THE-VALUE-THAT-MUST-NOT-PRINT";
    private const string _termsLine = "  keypaste.com's terms of use cover share links: https://keypaste.com/terms";

    private readonly CliHarness _cli = new();
    private readonly FakeShareServer _server = new();

    public ShareVerbTests()
    {
        _cli.SeedVault(
            _master,
            ("env/acme-api/STRIPE_KEY", _stripeValue),
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
        _cli.Prompt.Enqueue(_master);
        return CliApp.Run(["share", .. args, "--vault", _cli.VaultPath], _cli.NewContext(shareTransport: _server));
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

    private static string[] Lines(string text) => text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

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

    [Fact]
    public void A_share_and_its_revoke_name_the_items_uuid_on_their_lines_and_its_listing()
    {
        string uuid;
        using (var vault = Vault.Open(_cli.VaultPath, _master))
        {
            uuid = vault.Find(new EntryName("env/acme-api", "STRIPE_KEY"))!.Uuid!;
        }

        Assert.True(ShareLink.TryParse(CreatePrinted(), out var id, out _));

        _cli.AssertExit(CliApp.ExitSuccess, Share("ls", "--json", "--offline"));
        using (var json = JsonDocument.Parse(_cli.Out))
        {
            Assert.Equal(uuid, Assert.Single(json.RootElement.EnumerateArray()).GetProperty("item").GetString());
        }

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("revoke", id));

        Assert.True(AuditReader.TryRead(KeypasteHome.AuditPath(Home), out var lines, out _, out var error), error);
        Assert.Equal([("share-created", uuid), ("share-revoked", uuid)], lines.Select(line => (line.Method, line.Item)));
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
        Assert.Equal(_stripeValue, Assert.Single(payload.Fields).Value);

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
    public void The_first_share_from_a_home_links_the_terms_on_stderr_and_the_next_does_not()
    {
        _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print"));

        Assert.Single(Lines(_cli.Err), line => line == _termsLine);
        Assert.True(ShareLink.TryParse(Assert.Single(Lines(_cli.Out)), out var id, out var key));
        var remembered = File.ReadAllText(KeypasteHome.SettingsPath(Home));
        Assert.DoesNotContain(key, remembered, StringComparison.Ordinal);
        Assert.DoesNotContain(id, remembered, StringComparison.Ordinal);
        Assert.DoesNotContain(_stripeValue, remembered, StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print"));
        Assert.DoesNotContain("terms of use", _cli.Err, StringComparison.Ordinal);

        Clear();
        _cli.Environment[KeypasteHome.EnvironmentVariable] = Path.Combine(_cli.Directory, "another-home");
        _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print"));
        Assert.Contains(_termsLine, Lines(_cli.Err));
    }

    [Fact]
    public void A_listing_in_json_stays_parseable_and_leaves_the_terms_line_for_the_first_share()
    {
        _cli.AssertExit(CliApp.ExitSuccess, Share("ls", "--offline", "--json"));
        using (JsonDocument.Parse(_cli.Out))
        {
            Assert.DoesNotContain("terms of use", _cli.Err, StringComparison.Ordinal);
        }

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print"));
        Assert.Contains(_termsLine, Lines(_cli.Err));
        Assert.DoesNotContain("terms of use", _cli.Out, StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("ls", "--offline", "--json"));
        using var json = JsonDocument.Parse(_cli.Out);
        Assert.Single(json.RootElement.EnumerateArray());
        Assert.DoesNotContain("terms of use", _cli.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_share_that_cannot_remember_the_line_still_succeeds_and_shows_it_again(bool settingsIsDirectory)
    {
        var settings = KeypasteHome.SettingsPath(Home);
        Directory.CreateDirectory(Home);
        const string handEdit = "this is = not [[ a settings file\n";
        if (settingsIsDirectory)
        {
            Directory.CreateDirectory(settings);
        }
        else
        {
            File.WriteAllText(settings, handEdit);
        }

        for (var share = 0; share < 2; share++)
        {
            Clear();
            _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print"));
            Assert.Contains(_termsLine, Lines(_cli.Err));
            Assert.True(ShareLink.TryParse(Assert.Single(Lines(_cli.Out)), out _, out _));
        }

        if (!settingsIsDirectory)
        {
            Assert.Equal(handEdit, File.ReadAllText(settings));
        }
    }

    [Fact]
    public void A_share_whose_write_of_the_fact_is_refused_still_succeeds()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var viaOwnServer = Share("env/acme-api/STRIPE_KEY", "--print", "--endpoint", "https://share.example.org");
        _cli.AssertExit(CliApp.ExitSuccess, viaOwnServer);
        Clear();

        var writable = File.GetUnixFileMode(Home);
        File.SetUnixFileMode(Home, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print"));
            Assert.Contains(_termsLine, Lines(_cli.Err));
            Assert.True(ShareLink.TryParse(Assert.Single(Lines(_cli.Out)), out _, out _));
        }
        finally
        {
            File.SetUnixFileMode(Home, writable);
        }
    }

    [Fact]
    public void A_share_withdrawn_for_want_of_a_clipboard_keeps_the_terms_line_for_the_first_that_lands()
    {
        _cli.Clipboard.SetStatus = ClipboardStatus.NoDisplay;
        Assert.Equal(CliApp.ExitInternalError, Share("env/acme-api/STRIPE_KEY"));
        Assert.DoesNotContain("terms of use", _cli.Err, StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print"));
        Assert.Contains(_termsLine, Lines(_cli.Err));
    }

    [Fact]
    public void A_share_to_another_service_does_not_link_keypaste_coms_terms_or_use_up_the_line()
    {
        _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print", "--endpoint", "https://share.example.org"));
        Assert.DoesNotContain("terms of use", _cli.Err, StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("env/acme-api/STRIPE_KEY", "--print"));
        Assert.Contains(_termsLine, Lines(_cli.Err));
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
        _cli.Prompt.Enqueue(_master, "a long enough passphrase", "a long enough passphrase");

        var exit = CliApp.Run(["share", "env/acme-api/STRIPE_KEY", "--passphrase", "--print", "--vault", _cli.VaultPath], _cli.NewContext(shareTransport: _server));

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(["Master password: ", "Passphrase: ", "Repeat passphrase: "], _cli.Prompt.SecretPrompts);
        Assert.Contains("  passphrase: send it separately", _cli.Err, StringComparison.Ordinal);
        Assert.True(ShareLink.TryParse(_cli.Out.Trim(), out var id, out var key));
        Assert.True(ShareCrypto.TryOpen(_server.Shares[id].Envelope, key, "a long enough passphrase", out _, out _));
        Assert.False(ShareCrypto.TryOpen(_server.Shares[id].Envelope, key, ReadOnlySpan<char>.Empty, out _, out _));
        Assert.All(_cli.Prompt.IssuedSecrets, buffer => Assert.True(buffer.IsZeroed));

        Clear();
        _cli.Prompt.Enqueue(_master, "a long enough passphrase", "another long passphrase");
        exit = CliApp.Run(["share", "env/acme-api/STRIPE_KEY", "--passphrase", "--print", "--vault", _cli.VaultPath], _cli.NewContext(shareTransport: _server));

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("the passphrases do not match", _cli.Err, StringComparison.Ordinal);
        Assert.Single(_server.Requests);

        Clear();
        _cli.Prompt.Enqueue(_master, "nineteen characters", "nineteen characters");
        exit = CliApp.Run(["share", "env/acme-api/STRIPE_KEY", "--passphrase", "--print", "--vault", _cli.VaultPath], _cli.NewContext(shareTransport: _server));

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("at least 20 characters; `keypaste generate --words 6` makes one", _cli.Err, StringComparison.Ordinal);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public void Share_AmbiguousTitle_ListsPaths()
    {
        var exit = Share("Chase");

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("more than one item is called 'Chase'", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("  Banking/Chase", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("  Work/Chase", _cli.Err, StringComparison.Ordinal);
        Assert.Empty(_server.Requests);

        Clear();
        Assert.Equal(CliApp.ExitNotFound, Share("Nothing"));
        Assert.Contains("no item 'Nothing'", _cli.Err, StringComparison.Ordinal);

        Clear();
        _cli.AssertExit(CliApp.ExitSuccess, Share("Banking/Chase", "--print"));
        Assert.Single(_server.Requests);
    }

    [Theory]
    [InlineData("kp://acme-api/dev/STRIPE_KEY")]
    [InlineData("kp:///env/acme-api/STRIPE_KEY")]
    public void Share_AReference_SharesTheValueItNames(string reference)
    {
        using (var vault = Keypaste.Core.Vault.Open(_cli.VaultPath, _master))
        {
            ProjectVariables.Set(vault, "acme-api", "STRIPE_KEY", _stripeValue);
            vault.Save();
        }

        var exit = Share(reference, "--print");

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.True(ShareLink.TryParse(_cli.Out.Trim(), out var id, out var key));
        Assert.True(ShareCrypto.TryOpen(_server.Shares[id].Envelope, key, ReadOnlySpan<char>.Empty, out var payload, out _));
        Assert.Equal(_stripeValue, Assert.Single(payload.Fields).Value);
    }

    [Theory]
    [InlineData("Banking/Chase", "--field", "Recovery codes")]
    [InlineData("Banking/Chase", "--field", "otp")]
    [InlineData("kp:///Banking/Chase#Recovery%20codes")]
    public void Share_ACustomFieldThatNeverLeaves_UploadsNothing(params string[] args)
    {
        SeedCustomFields();

        Assert.Equal(CliApp.ExitUsageError, Share([.. args, "--print"]));
        Assert.Empty(_server.Requests);
    }

    private void SeedCustomFields()
    {
        using var vault = Keypaste.Core.Vault.Open(_cli.VaultPath, _master);
        Assert.True(vault.SetFields(
            new Keypaste.Core.EntryName("Banking", "Chase"),
            [new Keypaste.Core.FieldWrite("OPENAI_API_KEY", "sk-proj-SHARED-FIELD-SENTINEL"), new Keypaste.Core.FieldWrite("Recovery codes", "RECOVERY-SENTINEL")]));
        vault.Save();
    }

    [Theory]
    [InlineData(CliApp.ExitUsageError, "kp://acme-api/dev/STRIPE_KEY", "--field", "notes")]
    [InlineData(CliApp.ExitUsageError, "kp://acme-api")]
    [InlineData(CliApp.ExitNotFound, "kp://acme-api/staging/STRIPE_KEY")]
    [InlineData(CliApp.ExitUsageError, "kp:///.keypaste/shares/anything")]
    public void Share_AReferenceThatNamesNothingShareable_IsRefused(int expected, params string[] args)
    {
        Assert.Equal(expected, Share([.. args, "--print"]));
        Assert.Empty(_server.Requests);
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
        Assert.Contains("see keypaste share --help", _cli.Err, StringComparison.Ordinal);
    }
}
