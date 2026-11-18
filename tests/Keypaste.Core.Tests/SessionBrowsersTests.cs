using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A browser pairing with this computer through a vault's owner over a real pipe: asked at once with the code the extension
/// shows, one request at a time and held back after a refusal, and kept in this computer's pairings file holding the hash of
/// the extension's key and never the key, valid only while an unlocked vault's seal holds it; a check proving only its own
/// build's pairing and key; a removal recorded and refused from its next check; a database hash naming this computer; and a
/// page's list of names across every unlocked vault, whose pick fills at once only on a site it was filled on before.
/// </summary>
public sealed class SessionBrowsersTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-session-browsers-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly OwnerLog _log;
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly Vault _vault;
    private readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly HeldRules _rules = new();

    public SessionBrowsersTests()
    {
        _log = new OwnerLog(_directory);
        _vault = Vault.Create(VaultPath, _master);
        _vault.Save();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _vault.Dispose();
        _lifetime.Dispose();
        _log.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_request_to_pair_is_asked_at_once_with_the_code_and_this_computer_keeps_only_the_hash_of_the_key()
    {
        _fixture.Channel.AnswerPairing = _ => new PairingAnswer(ApprovalAnswer.ApprovedOnce, "work laptop");
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);

        var reply = await link.PairAsync(Pair(), Token);

        Assert.Equal(new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = "work laptop" }, reply);
        var prompt = Assert.Single(_fixture.Channel.PairingPrompts);
        Assert.Equal(PairingCode.Of(_key), prompt.Code);
        Assert.Matches("^[0-9]{3} [0-9]{3}$", prompt.Code);
        Assert.Equal("keypaste's extension from the Chrome Web Store (fipabobollckgdobkjfkkchimlphcicg)", prompt.IdentityText);
        Assert.Null(prompt.NameProblem(prompt.SuggestedName));

        var file = File.ReadAllText(PairingsPath);
        Assert.Contains(BrowserPairings.KeyHash(_key)!, file, StringComparison.Ordinal);
        Assert.Contains("work laptop", file, StringComparison.Ordinal);
        Assert.DoesNotContain(_key, file, StringComparison.Ordinal);
        Assert.Empty(VaultPairings());
        Assert.Equal("work laptop", Assert.Single(owner.Browsers.List()).Pairing.Id);

        var line = Assert.Single(_log.Lines());
        Assert.Equal(AuditTools.Browser, line.GetProperty("tool").GetString());
        Assert.Equal("pair", line.GetProperty("args").GetProperty("field").GetString());
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal("prompt", line.GetProperty("method").GetString());
        Assert.Equal("work laptop", line.GetProperty("client").GetProperty("label").GetString());
        Assert.DoesNotContain(_key, _log.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_code_is_the_one_keypaste_s_extension_derives_from_the_same_key()
    {
        // extension/src/background/keepass.js's pairingCode gives "848 873" for the key of bytes 0 to 31.
        Assert.Equal("848 873", PairingCode.Of(Convert.ToBase64String([.. Enumerable.Range(0, 32).Select(b => (byte)b)])));
        Assert.Null(PairingCode.Of(Convert.ToBase64String(new byte[31])));
        Assert.Null(PairingCode.Of("not a key"));
    }

    [Fact]
    public async Task A_second_request_while_one_is_asked_is_refused_unasked()
    {
        await using var owner = Owner.Start(this);
        await using var first = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        await using var second = await owner.AttachAsync(BrowserIdentity.FirefoxAddOns);
        BrowserReply? meanwhile = null;
        _fixture.Channel.AnswerPairing = _ =>
        {
            meanwhile = second.PairAsync(Pair(), Token).AsTask().GetAwaiter().GetResult();
            return new PairingAnswer(ApprovalAnswer.ApprovedOnce, "laptop");
        };

        var asked = await first.PairAsync(Pair(), Token);

        Assert.Equal("laptop", asked?.Pairing);
        Assert.Equal(BrowserOutcome.Refused, meanwhile?.Outcome);
        Assert.Contains("already asking", meanwhile?.Reason, StringComparison.Ordinal);
        Assert.Single(_fixture.Channel.PairingPrompts);
        Assert.Equal(["prompt"], _log.Lines().Select(line => line.GetProperty("method").GetString()));
    }

    [Fact]
    public async Task A_refusal_holds_every_request_back_a_minute_then_twice_as_long_each_time()
    {
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.EdgeAddOns);

        var denied = await link.PairAsync(Pair(), Token);
        var heldBack = await link.PairAsync(Pair(), Token);
        _fixture.Clock.Advance(OwnerPairings.FirstHoldBack);
        var deniedAgain = await link.PairAsync(Pair(), Token);
        _fixture.Clock.Advance(OwnerPairings.FirstHoldBack);
        var stillHeldBack = await link.PairAsync(Pair(), Token);
        _fixture.Clock.Advance(OwnerPairings.FirstHoldBack);
        _fixture.Channel.AnswerPairing = _ => new PairingAnswer(ApprovalAnswer.ApprovedOnce, "laptop");
        var paired = await link.PairAsync(Pair(), Token);

        Assert.Equal(BrowserOutcome.Refused, denied?.Outcome);
        Assert.Equal(BrowserOutcome.Refused, heldBack?.Outcome);
        Assert.Contains("connect again in", heldBack?.Reason, StringComparison.Ordinal);
        Assert.Equal(BrowserOutcome.Refused, deniedAgain?.Outcome);
        Assert.Equal(BrowserOutcome.Refused, stillHeldBack?.Outcome);
        Assert.Equal("laptop", paired?.Pairing);
        Assert.Equal(3, _fixture.Channel.PairingPrompts.Count);
        Assert.Equal(["prompt", "prompt", "prompt"], _log.Lines().Select(line => line.GetProperty("method").GetString()));
    }

    [Fact]
    public async Task A_confirmation_nobody_was_shown_holds_nothing_back_and_a_name_in_use_pairs_nothing()
    {
        var answers = new Queue<PairingAnswer>([new(ApprovalAnswer.Busy, null), new(ApprovalAnswer.ApprovedOnce, "laptop"), new(ApprovalAnswer.ApprovedOnce, "laptop")]);
        _fixture.Channel.AnswerPairing = _ => answers.Dequeue();
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.EdgeAddOns);

        var busy = await link.PairAsync(Pair(), Token);
        var paired = await link.PairAsync(Pair(), Token);
        var taken = await link.PairAsync(Pair(), Token);

        Assert.Equal(BrowserOutcome.Refused, busy?.Outcome);
        Assert.Equal("laptop", paired?.Pairing);
        Assert.Equal(BrowserOutcome.Refused, taken?.Outcome);
        Assert.Equal(["laptop"], _fixture.Channel.PairingPrompts[^1].TakenNames);
        Assert.Single(owner.Browsers.List());
    }

    [Fact]
    public async Task A_check_proves_only_its_own_build_s_pairing_with_its_key_and_records_each_refusal()
    {
        await using var owner = Owner.Start(this);
        await PairedAsync(owner, "laptop");
        await using var chrome = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        await using var edge = await owner.AttachAsync(BrowserIdentity.EdgeAddOns);
        var before = _log.Lines().Count;

        var own = await chrome.CheckAsync(Check("laptop", _key), Token);
        var anotherBuild = await edge.CheckAsync(Check("laptop", _key), Token);
        var anotherKey = await chrome.CheckAsync(Check("laptop", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), Token);
        var unknown = await chrome.CheckAsync(Check("desktop", _key), Token);

        Assert.Equal(new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = "laptop" }, own);
        Assert.Equal(BrowserOutcome.NotPaired, anotherBuild?.Outcome);
        Assert.Equal(BrowserOutcome.NotPaired, anotherKey?.Outcome);
        Assert.Equal(BrowserOutcome.NotPaired, unknown?.Outcome);

        var refusals = _log.Lines().Skip(before).ToList();
        Assert.Equal(3, refusals.Count);
        Assert.All(refusals, line => Assert.Equal("not-paired", line.GetProperty("method").GetString()));
        Assert.All(refusals, line => Assert.Equal("check", line.GetProperty("args").GetProperty("field").GetString()));
        Assert.Equal("iobkkhmaklkkeiadigbohfaplldmaogd", refusals[0].GetProperty("client").GetProperty("name").GetString());
        Assert.DoesNotContain(_key, _log.Text(), StringComparison.Ordinal);
        Assert.NotNull(Assert.Single(owner.Browsers.List()).LastUsed);
    }

    [Fact]
    public async Task A_pairing_another_program_writes_into_the_file_proves_nothing()
    {
        await using var owner = Owner.Start(this);
        await PairedAsync(owner, "laptop");
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        var forged = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var file = JsonNode.Parse(File.ReadAllText(PairingsPath))!;
        var real = file["pairings"]![0]!;

        // One record unsealed under a new name, and the real record's seal carried over to a key the forger holds.
        var added = real.DeepClone();
        added["name"] = "intruder";
        added["key"] = BrowserPairings.KeyHash(forged);
        added["seals"] = new JsonObject();
        var swapped = real.DeepClone();
        swapped["key"] = BrowserPairings.KeyHash(forged);
        file["pairings"] = new JsonArray(added, swapped);
        File.WriteAllText(PairingsPath, file.ToJsonString());

        var intruder = await link.CheckAsync(Check("intruder", forged), Token);
        var impostor = await link.CheckAsync(Check("laptop", forged), Token);

        Assert.Equal(BrowserOutcome.NotPaired, intruder?.Outcome);
        Assert.Equal(BrowserOutcome.NotPaired, impostor?.Outcome);
        Assert.Empty(owner.Browsers.List());
    }

    [Fact]
    public async Task A_file_put_back_from_before_a_removal_proves_nothing()
    {
        await using var owner = Owner.Start(this);
        await PairedAsync(owner, "laptop");
        var before = File.ReadAllText(PairingsPath);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);

        Assert.True(owner.Browsers.Remove("laptop").Removed);
        File.WriteAllText(PairingsPath, before);
        var check = await link.CheckAsync(Check("laptop", _key), Token);

        Assert.Equal(BrowserOutcome.NotPaired, check?.Outcome);
        Assert.Empty(owner.Browsers.List());
    }

    [Fact]
    public async Task A_pairing_an_earlier_version_kept_in_the_vault_needs_pairing_again_which_retires_it()
    {
        _vault.AddEntry(new VaultEntry
        {
            GroupPath = ReservedGroups.Browsers,
            Title = "laptop",
            Password = BrowserPairings.KeyHash(_key)!,
            Notes = """{"v":1,"identity":"chrome-extension://fipabobollckgdobkjfkkchimlphcicg/","created":"2026-11-01T10:00:00Z"}""",
        });
        _vault.Save();
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);

        var check = await link.CheckAsync(Check("laptop", _key), Token);
        Assert.Equal(BrowserOutcome.NotPaired, check?.Outcome);
        Assert.Empty(owner.Browsers.List());

        await PairedAsync(owner, "laptop");

        Assert.Empty(VaultPairings());
        Assert.Equal("laptop", Assert.Single(owner.Browsers.List()).Pairing.Id);
    }

    [Fact]
    public async Task One_pairing_serves_every_vault_unlocked_beside_it_and_a_check_seals_it_into_one_that_was_not()
    {
        using var other = Vault.Create(Path.Combine(_directory, "other.kdbx"), _master);
        other.Save();
        List<PairingVault> besides = [];
        await using var owner = Owner.Start(this, () => besides);
        await PairedAsync(owner, "laptop");
        var pairings = BrowserPairings.In(Path.Combine(_directory, "home"));

        Assert.Equal(PairingCheck.Unknown, pairings.Verify([other], "laptop", BrowserIdentity.ChromeWebStore, _key, out _));
        Assert.Equal(PairingCheck.Valid, pairings.Verify([other, _vault], "laptop", BrowserIdentity.ChromeWebStore, _key, out _));

        besides.Add(new PairingVault(other, edit => Saving(other, edit)));
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        Assert.Equal(BrowserOutcome.Done, (await link.CheckAsync(Check("laptop", _key), Token))?.Outcome);

        Assert.Equal(PairingCheck.Valid, pairings.Verify([other], "laptop", BrowserIdentity.ChromeWebStore, _key, out var pairing));
        Assert.Equal("laptop", pairing?.Id);

        Assert.True(owner.Browsers.Remove("laptop").Removed);
        Assert.Equal(PairingCheck.Unknown, pairings.Verify([other], "laptop", BrowserIdentity.ChromeWebStore, _key, out _));
    }

    [Fact]
    public async Task Removing_a_pairing_records_it_and_refuses_its_next_check()
    {
        await using var owner = Owner.Start(this);
        await PairedAsync(owner, "laptop");
        var removedNames = new List<string>();
        owner.Browsers.Removed += (_, name) => removedNames.Add(name);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);

        var removal = owner.Browsers.Remove("laptop");
        var again = owner.Browsers.Remove("laptop");
        var check = await link.CheckAsync(Check("laptop", _key), Token);

        Assert.Equal(new PairingRemoval(true, string.Empty), removal);
        Assert.False(again.Removed);
        Assert.Equal(["laptop"], removedNames);
        Assert.DoesNotContain("laptop", File.ReadAllText(PairingsPath), StringComparison.Ordinal);
        Assert.Empty(owner.Browsers.List());
        Assert.Equal(BrowserOutcome.NotPaired, check?.Outcome);

        var lines = _log.Lines();
        Assert.Equal("removed", lines[^2].GetProperty("method").GetString());
        Assert.Equal("granted", lines[^2].GetProperty("decision").GetString());
        Assert.Equal("laptop", lines[^2].GetProperty("client").GetProperty("label").GetString());
        Assert.Equal("not-paired", lines[^1].GetProperty("method").GetString());
    }

    [Fact]
    public async Task The_database_hash_names_this_computer_and_a_browser_s_attachment_is_told_it()
    {
        await using var owner = Owner.Start(this);
        await using var link = await owner.ConnectAsync();

        var attached = await link.AttachAsync(new AttachRequest(VaultPath) { Browser = BrowserIdentity.ChromeWebStore.Caller }, Token);

        var home = JsonNode.Parse(File.ReadAllText(PairingsPath))!["home"]!.GetValue<string>();
        Assert.Matches("^[0-9a-f]{32}$", home);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("keypaste\n" + home))), attached?.DatabaseHash);
        Assert.Equal(attached?.DatabaseHash, owner.Browsers.DatabaseHash());
    }

    [Fact]
    public async Task A_lock_while_the_person_is_asked_saves_nothing()
    {
        _fixture.Channel.AnswerPairing = _ =>
        {
            _lifetime.End();
            return new PairingAnswer(ApprovalAnswer.ApprovedOnce, "laptop");
        };
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);

        var reply = await link.PairAsync(Pair(), Token);

        Assert.Equal(BrowserOutcome.Locked, reply?.Outcome);
        Assert.DoesNotContain("laptop", File.Exists(PairingsPath) ? File.ReadAllText(PairingsPath) : string.Empty, StringComparison.Ordinal);
        Assert.Equal("vault-locked", _log.Lines()[^1].GetProperty("method").GetString());
    }

    /// <summary>A new login from a page's list starts with the page's origin alone, so no path, query or token the address carried reaches the item.</summary>
    [Theory]
    [InlineData("https://github.com/login?return_to=%2Fsettings&token=abc#top", "https://github.com")]
    [InlineData("http://intranet.example.com:8080/sign-in", "http://intranet.example.com:8080")]
    [InlineData("https://maya@github.com/login", "")]
    [InlineData("chrome://settings/passwords", "")]
    [InlineData("", "")]
    public void A_new_login_starts_with_its_pages_origin(string url, string website) =>
        Assert.Equal(website, SessionBrowsers.NewLoginAddress(url));

    [Theory]
    [InlineData("", "the name cannot be empty")]
    [InlineData(" laptop", "the name cannot begin or end with whitespace")]
    [InlineData("work/laptop", "the name cannot contain '/'")]
    [InlineData("work‮laptop", "the name cannot contain invisible or unassigned characters")]
    [InlineData("taken", "another pairing already has this name")]
    public void A_name_must_be_plain_short_and_new(string name, string problem) =>
        Assert.Equal(problem, PairingNames.Problem(name, ["taken"]));

    [Fact]
    public void A_suggested_name_is_new_and_names_the_browser_and_this_computer()
    {
        Assert.Equal("Chrome on laptop", PairingNames.Suggested(BrowserIdentity.ChromeWebStore, "laptop", []));
        Assert.Equal("Firefox on laptop 2", PairingNames.Suggested(BrowserIdentity.FirefoxAddOns, "laptop", ["Firefox on laptop"]));
        Assert.Equal("Edge", PairingNames.Suggested(BrowserIdentity.EdgeAddOns, "  ", []));
        Assert.NotNull(PairingNames.Problem(new string('x', PairingNames.MaximumLength + 1), []));
    }

    [Fact]
    public async Task A_list_names_the_page_s_logins_with_no_password_asks_nobody_and_writes_one_line()
    {
        var github = AddLogin("web", "GitHub", "dana", "{USERNAME}-s3cret", "https://github.com");
        AddLogin("web", "Bank", "dana", "bank-s3cret", "https://bank.example");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var before = _log.Lines().Count;

        var reply = await link.ListAsync(List("https://github.com/login?next=/secret#frag"), Token);

        Assert.Equal(BrowserOutcome.Done, reply?.Outcome);
        Assert.Equal("github.com", reply!.Site);
        Assert.Equal(new BrowserLoginName(github, "GitHub", "dana"), Assert.Single(reply.Names), new NameComparer());
        Assert.Empty(reply.Names[0].Vault);
        Assert.Empty(reply.LockedVaults);
        Assert.Empty(reply.Warning);
        Assert.DoesNotContain("s3cret", System.Text.Encoding.UTF8.GetString(ApproverProtocol.Encode(reply)), StringComparison.Ordinal);
        Assert.Empty(_fixture.Channel.FillPrompts);

        var line = Assert.Single(_log.Lines().Skip(before));
        Assert.Equal(("list", "granted", "exposure"), (line.GetProperty("args").GetProperty("field").GetString(), line.GetProperty("decision").GetString(), line.GetProperty("method").GetString()));
        Assert.EndsWith("on https://github.com:443", line.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("next=", _log.Text(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://github.com.evil.example/login")]
    [InlineData("https://githubb.com/")]
    [InlineData("https://github.io/")]
    [InlineData("https://example.org/login")]
    public async Task A_lookalike_another_site_or_a_public_suffix_lists_nothing_and_writes_no_line(string page)
    {
        AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var before = _log.Lines().Count;

        var reply = await link.ListAsync(List(page), Token);

        Assert.Equal(BrowserOutcome.Done, reply?.Outcome);
        Assert.Empty(reply!.Names);
        Assert.Equal(before, _log.Lines().Count);
    }

    [Fact]
    public async Task The_first_pick_on_a_site_is_asked_fills_once_allowed_and_a_later_pick_there_fills_at_once()
    {
        var github = AddLogin("web", "GitHub", "dana", "{USERNAME}-s3cret", "https://github.com");
        _fixture.Channel.AnswerFill = (_, _) => ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var before = _log.Lines().Count;

        var first = await link.ReleaseAsync(Release("https://github.com/login", github), Token);

        var login = Assert.Single(first!.Logins);
        Assert.Equal(("dana", "dana-s3cret", "GitHub"), (login.Login, login.Password, login.Name));
        var prompt = Assert.Single(_fixture.Channel.FillPrompts);
        Assert.Equal(("Fill GitHub on github.com?", "https://github.com:443", "laptop", "dana"), (prompt.Question, prompt.Origin, prompt.Pairing, prompt.Username));
        Assert.Equal(new BrowserAllowance("laptop", Paired(owner), "https://github.com:443", github), Assert.Single(_rules.Rules.BrowserAllowances));

        var asked = Assert.Single(_log.Lines().Skip(before));
        Assert.Equal(("fill", "granted", "prompt"), (asked.GetProperty("args").GetProperty("field").GetString(), asked.GetProperty("decision").GetString(), asked.GetProperty("method").GetString()));
        Assert.Equal(github, asked.GetProperty("args").GetProperty("item").GetString(), ignoreCase: true);
        Assert.Equal("web/GitHub", asked.GetProperty("args").GetProperty("entry").GetString());

        var again = await link.ReleaseAsync(Release("https://github.com/", github), Token);

        Assert.Equal("dana-s3cret", Assert.Single(again!.Logins).Password);
        Assert.Single(_fixture.Channel.FillPrompts);
        Assert.Equal(("granted", "allowed"), (_log.Lines()[^1].GetProperty("decision").GetString(), _log.Lines()[^1].GetProperty("method").GetString()));
        Assert.EndsWith("on https://github.com:443", _log.Lines()[^1].GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", _log.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_subdomain_lists_its_site_s_login_and_its_first_fill_is_asked_though_the_site_was_filled_before()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        _fixture.Channel.AnswerFill = (_, _) => ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        _rules.Rules = _rules.Rules.AllowingFill(new BrowserAllowance("laptop", Paired(owner), "https://github.com:443", github));

        var listed = await link.ListAsync(List("https://gist.github.com/login"), Token);
        var first = await link.ReleaseAsync(Release("https://gist.github.com/login", github), Token);
        var again = await link.ReleaseAsync(Release("https://gist.github.com/", github), Token);

        Assert.Equal("gist.github.com", listed!.Site);
        Assert.Single(listed.Names);
        Assert.Single(first!.Logins);
        Assert.Single(again!.Logins);
        Assert.Equal("Fill GitHub on gist.github.com?", Assert.Single(_fixture.Channel.FillPrompts).Question);
        Assert.Contains(new BrowserAllowance("laptop", Paired(owner), "https://gist.github.com:443", github), _rules.Rules.BrowserAllowances);
    }

    [Fact]
    public async Task No_password_reaches_the_browser_before_a_pick()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        _rules.Rules = _rules.Rules.AllowingFill(new BrowserAllowance("laptop", Paired(owner), "https://github.com:443", github));

        var reply = await link.ListAsync(List("https://github.com/"), Token);

        Assert.Single(reply!.Names);
        Assert.DoesNotContain("s3cret", System.Text.Encoding.UTF8.GetString(ApproverProtocol.Encode(reply)), StringComparison.Ordinal);
        Assert.DoesNotContain(_log.Lines(), line => line.GetProperty("args").GetProperty("field").GetString() == "fill");
    }

    [Fact]
    public async Task A_site_filled_before_by_another_pairing_or_on_another_origin_is_asked()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com:8443");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        _rules.Rules = _rules.Rules
            .AllowingFill(new BrowserAllowance("laptop", Paired(owner).AddDays(-1), "https://github.com:443", github))
            .AllowingFill(new BrowserAllowance("laptop", Paired(owner), "https://github.com:8443", github));

        var reply = await link.ReleaseAsync(Release("https://github.com/", github), Token);

        Assert.Equal(BrowserOutcome.Refused, reply?.Outcome);
        Assert.Single(_fixture.Channel.FillPrompts);
    }

    [Theory]
    [InlineData("http://github.com/login", "http://github.com", null, "This page's connection is not secure. keypaste asks before each fill.")]
    [InlineData("https://nas/", "https://nas", null, "Anyone on this network can answer to nas. keypaste asks before each fill.")]
    [InlineData("https://github.com/login", "https://github.com", "https://collector.example/post", "This form sends what you fill to collector.example, another site. keypaste asks before each fill.")]
    public async Task Where_every_fill_is_asked_the_list_warns_and_each_pick_is_asked_and_never_remembered(string page, string address, string? submitUrl, string warning)
    {
        var item = AddLogin(string.Empty, "Site", "dana", "s3cret", address);
        _fixture.Channel.AnswerFill = (_, _) => ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        var listed = await link.ListAsync(List(page, submitUrl), Token);
        var first = await link.ReleaseAsync(Release(page, item, submitUrl), Token);
        var second = await link.ReleaseAsync(Release(page, item, submitUrl), Token);

        Assert.Equal(warning, listed!.Warning);
        Assert.Single(first!.Logins);
        Assert.Single(second!.Logins);
        Assert.Equal(2, _fixture.Channel.FillPrompts.Count);
        Assert.All(_fixture.Channel.FillPrompts, prompt => Assert.Equal(warning, prompt.Warning));
        Assert.Empty(_rules.Rules.BrowserAllowances);
    }

    [Fact]
    public async Task A_refusal_fills_nothing_and_holds_the_page_back_a_minute()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var before = _log.Lines().Count;

        var denied = await link.ReleaseAsync(Release("https://github.com/login", github), Token);
        var again = await link.ReleaseAsync(Release("https://github.com/login", github), Token);
        _fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var later = await link.ReleaseAsync(Release("https://github.com/login", github), Token);

        Assert.Equal(BrowserOutcome.Refused, denied?.Outcome);
        Assert.Equal(BrowserOutcome.Refused, again?.Outcome);
        Assert.Equal(BrowserOutcome.Refused, later?.Outcome);
        Assert.Equal(2, _fixture.Channel.FillPrompts.Count);
        Assert.Equal(["prompt", "cooldown", "prompt"], _log.Lines().Skip(before).Select(line => line.GetProperty("method").GetString()));
        Assert.All(_log.Lines().Skip(before), line => Assert.Equal("denied", line.GetProperty("decision").GetString()));
        Assert.Empty(_rules.Rules.BrowserAllowances);
    }

    [Fact]
    public async Task An_expired_or_trashed_item_is_never_listed_and_a_pick_of_the_expired_one_is_refused_and_recorded()
    {
        var expired = AddLogin(string.Empty, "Old", "dana", "s3cret", "https://github.com", expires: _fixture.Clock.GetUtcNow().AddDays(-1));
        AddLogin(string.Empty, "Trashed", "dana", "s3cret", "https://github.com");
        _vault.RemoveEntry(new EntryName(string.Empty, "Trashed"), out _);
        _vault.Save();
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var before = _log.Lines().Count;

        var listed = await link.ListAsync(List("https://github.com/"), Token);
        var picked = await link.ReleaseAsync(Release("https://github.com/", expired), Token);

        Assert.Empty(listed!.Names);
        Assert.Empty(picked!.Logins);
        Assert.Empty(_fixture.Channel.FillPrompts);
        var line = Assert.Single(_log.Lines().Skip(before));
        Assert.Equal(expired, line.GetProperty("args").GetProperty("item").GetString(), ignoreCase: true);
        Assert.Equal("blocked", line.GetProperty("method").GetString());
        Assert.StartsWith("expired ", line.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pick_of_an_item_on_a_page_it_does_not_match_releases_nothing_and_asks_nobody()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        _fixture.Channel.AnswerFill = (_, _) => ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        _rules.Rules = _rules.Rules.AllowingFill(new BrowserAllowance("laptop", Paired(owner), "https://github.com:443", github));

        var lookalike = await link.ReleaseAsync(Release("https://github.com.evil.example/", github), Token);
        var tenant = await link.ReleaseAsync(Release("https://githubb.com/", github), Token);

        Assert.Equal(BrowserOutcome.Refused, lookalike?.Outcome);
        Assert.Equal(BrowserOutcome.Refused, tenant?.Outcome);
        Assert.Empty(lookalike!.Logins);
        Assert.Empty(tenant!.Logins);
        Assert.Empty(_fixture.Channel.FillPrompts);
        Assert.All(_log.Lines().TakeLast(2), line => Assert.Equal(("denied", "out-of-scope"), (line.GetProperty("decision").GetString(), line.GetProperty("method").GetString())));
    }

    [Fact]
    public async Task Two_items_of_one_title_are_told_apart_by_uuid()
    {
        var work = AddLogin("web", "GitHub", "dana-work", "work-s3cret", "https://github.com");
        AddLogin("web", "GitHub", "dana", "home-s3cret", "https://github.com");
        _fixture.Channel.AnswerFill = (_, _) => ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        var listed = await link.ListAsync(List("https://github.com/"), Token);
        var reply = await link.ReleaseAsync(Release("https://github.com/", work), Token);

        Assert.Equal(2, listed!.Names.Count);
        Assert.Equal("dana-work", Assert.Single(_fixture.Channel.FillPrompts).Username);
        Assert.Equal("work-s3cret", Assert.Single(reply!.Logins).Password);
    }

    [Fact]
    public async Task A_pairing_removed_while_the_person_is_asked_fills_nothing()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        _fixture.Channel.AnswerFill = (_, _) =>
        {
            Assert.True(owner.Browsers.Remove("laptop").Removed);
            return ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
        };

        var reply = await link.ReleaseAsync(Release("https://github.com/", github), Token);

        Assert.NotEqual(BrowserOutcome.Done, reply?.Outcome);
        Assert.DoesNotContain(_log.Lines(), line => line.GetProperty("args").GetProperty("field").GetString() == "fill" && line.GetProperty("decision").GetString() == "granted");
    }

    [Fact]
    public async Task A_lock_while_the_person_is_asked_fills_nothing()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        _fixture.Channel.AnswerFill = (_, _) =>
        {
            _lifetime.End();
            return ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
        };

        var reply = await link.ReleaseAsync(Release("https://github.com/", github), Token);

        Assert.Equal(BrowserOutcome.Locked, reply?.Outcome);
        Assert.Empty(reply!.Logins);
        Assert.DoesNotContain(_log.Lines(), line => line.GetProperty("decision").GetString() == "granted" && line.GetProperty("args").GetProperty("field").GetString() == "fill");
    }

    [Fact]
    public async Task A_pick_while_another_prompt_is_up_gets_nothing_and_nobody_is_asked_twice()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        var answer = new TaskCompletionSource<ApprovalAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Channel.AnswerFill = (_, _) =>
        {
            asked.TrySetResult();
            return new ValueTask<ApprovalAnswer>(answer.Task);
        };
        await using var owner = Owner.Start(this);
        await using var first = await PairedLinkAsync(owner);
        await using var second = await CheckedLinkAsync(owner);

        var waiting = first.ReleaseAsync(Release("https://github.com/", github), Token).AsTask();
        await asked.Task.WaitAsync(_wait, Token);
        var busy = await second.ReleaseAsync(Release("https://github.com/", github), Token);
        answer.SetResult(ApprovalAnswer.ApprovedOnce);
        var filled = await waiting;

        Assert.Equal(BrowserOutcome.Refused, busy?.Outcome);
        Assert.Empty(busy!.Logins);
        Assert.Single(filled!.Logins);
        Assert.Single(_fixture.Channel.FillPrompts);
    }

    [Fact]
    public async Task A_release_whose_line_cannot_be_written_is_refused()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        _rules.Rules = _rules.Rules.AllowingFill(new BrowserAllowance("laptop", Paired(owner), "https://github.com:443", github));
        _log.Log.Dispose();

        var reply = await link.ReleaseAsync(Release("https://github.com/", github), Token);

        Assert.NotEqual(BrowserOutcome.Done, reply?.Outcome);
        Assert.Empty(reply!.Logins);
    }

    [Fact]
    public async Task A_list_spans_every_unlocked_vault_names_each_and_the_locked_ones_and_a_pick_is_decided_by_its_own_vault()
    {
        AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        using var other = new SecondVault(this, "acme");
        var acme = other.AddLogin("GitHub", "dana@acme", "acme-s3cret", "https://github.com");
        _fixture.Channel.AnswerFill = (_, _) => ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
        await using var owner = Owner.Start(this, () => [new PairingVault(_vault, edit => Write(_lifetime, edit)), other.Pairing], () => ["Old"]);
        other.Serve(owner);
        await using var link = await PairedLinkAsync(owner);

        var listed = await link.ListAsync(List("https://github.com/login"), Token);

        Assert.Equal([("GitHub", "dana", "vault"), ("GitHub", "dana@acme", "acme")], listed!.Names.Select(name => (name.Title, name.Username, name.Vault)));
        Assert.Equal(["Old"], listed.LockedVaults);

        var picked = await link.ReleaseAsync(Release("https://github.com/login", acme), Token);

        Assert.Equal("acme-s3cret", Assert.Single(picked!.Logins).Password);
        Assert.Equal("acme", Assert.Single(_fixture.Channel.FillPrompts).Vault);
        Assert.Empty(_rules.Rules.BrowserAllowances);
        Assert.Single(other.Rules.Rules.BrowserAllowances);

        other.Lifetime.End();
        var afterLock = await link.ListAsync(List("https://github.com/login"), Token);
        var lockedPick = await link.ReleaseAsync(Release("https://github.com/login", acme), Token);

        Assert.Equal(["vault"], afterLock!.Names.Select(name => name.Vault));
        Assert.Empty(lockedPick!.Logins);
    }

    [Fact]
    public async Task Settings_lists_the_allowances_of_pairings_that_exist_and_removes_one_with_a_line()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var kept = new BrowserAllowance("laptop", Paired(owner), "https://github.com:443", github);
        _rules.Rules = _rules.Rules
            .AllowingFill(kept)
            .AllowingFill(new BrowserAllowance("laptop", Paired(owner).AddDays(-1), "https://github.com:443", github));

        var listed = owner.Browsers.Allowances();
        Assert.True(owner.Browsers.Unallow(kept, out var problem), problem);

        Assert.Equal(new AllowanceRow(kept, "GitHub"), Assert.Single(listed));
        Assert.DoesNotContain(kept, _rules.Rules.BrowserAllowances);
        Assert.Equal("unallow", _log.Lines()[^1].GetProperty("args").GetProperty("field").GetString());
        Assert.Equal("removed", _log.Lines()[^1].GetProperty("method").GetString());
    }

    /// <summary>Adds a login and returns its UUID as the vault's rules keep one, in lower case.</summary>
    private string AddLogin(string group, string title, string username, string password, string url, DateTimeOffset? expires = null)
    {
        _vault.AddEntry(new VaultEntry { GroupPath = group, Title = title, Username = username, Password = password, Url = url });

        if (expires is { } at)
        {
            _vault.SetExpiryUnchecked(new EntryName(group, title), at);
        }

        _vault.Save();
        return Lower(_vault.ReadEntries().Last(entry => entry.Title == title && entry.Username == username).Uuid!);
    }

    private string Uuid(string title) => Lower(_vault.ReadEntries().First(entry => entry.Title == title).Uuid!);

    private static string Lower(string hex) => hex.ToLowerInvariant();

    private BrowserListRequest List(string url, string? submitUrl = null) =>
        new("laptop", _key, url) { SubmitUrl = submitUrl, Vault = VaultPath, Session = "session-one" };

    private BrowserReleaseRequest Release(string url, string item, string? submitUrl = null) =>
        new("laptop", _key, url, item) { SubmitUrl = submitUrl, Vault = VaultPath, Session = "session-one" };

    private static DateTimeOffset Paired(Owner owner) => owner.Browsers.List().Single(row => row.Pairing.Id == "laptop").Pairing.Created;

    /// <summary>A browser paired as <c>laptop</c> whose connection a check bound to that pairing.</summary>
    private async Task<BrowserLink> PairedLinkAsync(Owner owner)
    {
        await PairedAsync(owner, "laptop");
        return await CheckedLinkAsync(owner);
    }

    private async Task<BrowserLink> CheckedLinkAsync(Owner owner)
    {
        var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        Assert.Equal(BrowserOutcome.Done, (await link.CheckAsync(Check("laptop", _key), Token))?.Outcome);
        return link;
    }

    private BrowserPairRequest Pair() => new(_key) { Vault = VaultPath, Session = "session-one" };

    private BrowserCheckRequest Check(string pairing, string key) => new(pairing, key) { Vault = VaultPath, Session = "session-one" };

    private string PairingsPath => Path.Combine(_directory, "home", BrowserPairings.FileName);

    /// <summary>The pairing records an earlier version kept inside the vault, as its file holds them.</summary>
    private List<VaultEntry> VaultPairings()
    {
        using var reopened = Vault.Open(VaultPath, _master);
        return [.. reopened.ReadEntries(includeReserved: true).Where(entry => entry.GroupPath == ReservedGroups.Browsers)];
    }

    private async Task PairedAsync(Owner owner, string name)
    {
        _fixture.Channel.AnswerPairing = _ => new PairingAnswer(ApprovalAnswer.ApprovedOnce, name);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        Assert.Equal(name, (await link.PairAsync(Pair(), Token))?.Pairing);
    }

    private AgentSave Write(SessionLifetime asked, Func<Vault, bool> apply) =>
        !ReferenceEquals(asked, _lifetime) || !_lifetime.IsLive ? new AgentSave(AgentSaveOutcome.Locked, "the vault is locked") : Saving(_vault, apply);

    private static AgentSave Saving(Vault vault, Func<Vault, bool> apply)
    {
        if (!apply(vault))
        {
            return AgentSave.Unchanged;
        }

        vault.Save();
        return AgentSave.Saved;
    }

    /// <summary>An owner of the test's vault on its own pipe, pairing through <see cref="SessionBrowsers"/>.</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ApproverListener _listener;
        private readonly Task _running;
        private readonly string _vault;

        private Owner(string pipeName, SessionAuthority authority, SessionBrowsers browsers, string vault)
        {
            PipeName = pipeName;
            Browsers = browsers;
            _vault = vault;
            _listener = new ApproverListener(pipeName, authority);
            _running = _listener.RunAsync(_stop.Token);
        }

        internal SessionBrowsers Browsers { get; }

        private string PipeName { get; }

        internal OwnerPairings Pairings { get; private init; } = null!;

        internal static Owner Start(SessionBrowsersTests test, Func<IReadOnlyList<PairingVault>>? besides = null, Func<IReadOnlyList<string>>? locked = null)
        {
            var identity = VaultIdentity.Of(test._directory, test.VaultPath);
            var pairings = new OwnerPairings(BrowserPairings.In(Path.Combine(test._directory, "home")), besides, locked);
            var browsers = new SessionBrowsers(
                identity,
                test._lifetime,
                test._fixture.Gate,
                lifetime => ReferenceEquals(lifetime, test._lifetime) && lifetime.IsLive ? test._vault : null,
                test.Write,
                test._rules,
                () => test._log.Log,
                pairings,
                test._fixture.Clock,
                narrate: null);
            var authority = new SessionAuthority(
                identity,
                () => test._lifetime.IsLive ? test._lifetime : null,
                test._fixture.Handler,
                () => test._log.Log,
                clock: test._fixture.Clock,
                browsers: browsers);
            browsers.Removed += (_, pairing) => authority.Unbind(pairing);

            return new("keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), authority, browsers, test.VaultPath) { Pairings = pairings };
        }

        internal async Task<BrowserLink> ConnectAsync()
        {
            var link = await BrowserLink.TryConnectAsync(PipeName, _wait, Token);
            Assert.NotNull(link);
            return link;
        }

        internal async Task<BrowserLink> AttachAsync(BrowserIdentity browser)
        {
            var link = await ConnectAsync();
            var attached = await link.AttachAsync(new AttachRequest(_vault) { Browser = browser.Caller }, Token);
            Assert.True(attached?.Attached, attached?.Reason);
            return link;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();

            try
            {
                await _running;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }

            _listener.Dispose();
            _stop.Dispose();
        }
    }

    /// <summary>A second vault unlocked in the same owner process, with its own unlock, rules and browsers.</summary>
    private sealed class SecondVault : IDisposable
    {
        private readonly SessionBrowsersTests _test;
        private readonly string _path;
        private readonly Vault _vault;

        internal SecondVault(SessionBrowsersTests test, string name)
        {
            _test = test;
            _path = Path.Combine(test._directory, name + ".kdbx");
            _vault = Vault.Create(_path, _master);
            _vault.Save();
            Pairing = new PairingVault(_vault, edit => Saving(_vault, edit));
        }

        internal SessionLifetime Lifetime { get; } = new("session-acme");

        internal HeldRules Rules { get; } = new();

        internal PairingVault Pairing { get; }

        internal string AddLogin(string title, string username, string password, string url)
        {
            _vault.AddEntry(new VaultEntry { Title = title, Username = username, Password = password, Url = url });
            _vault.Save();
            return Lower(_vault.ReadEntries().Last(entry => entry.Title == title && entry.Username == username).Uuid!);
        }

        /// <summary>Serves this vault's browsers beside the owner's, as the app's second open vault does.</summary>
        internal void Serve(Owner owner) =>
            _ = new SessionBrowsers(
                VaultIdentity.Of(_test._directory, _path),
                Lifetime,
                _test._fixture.Gate,
                lifetime => ReferenceEquals(lifetime, Lifetime) && lifetime.IsLive ? _vault : null,
                (_, edit) => Saving(_vault, edit),
                Rules,
                () => _test._log.Log,
                owner.Pairings,
                _test._fixture.Clock,
                narrate: null);

        public void Dispose()
        {
            Lifetime.Dispose();
            _vault.Dispose();
        }
    }

    /// <summary>Compares a listed name by what the list shows.</summary>
    private sealed class NameComparer : IEqualityComparer<BrowserLoginName>
    {
        public bool Equals(BrowserLoginName? x, BrowserLoginName? y) =>
            x is not null && y is not null && string.Equals(x.Uuid, y.Uuid, StringComparison.OrdinalIgnoreCase) && x.Title == y.Title && x.Username == y.Username;

        public int GetHashCode(BrowserLoginName obj) => obj.Title.GetHashCode(StringComparison.Ordinal);
    }

    /// <summary>Rules held in memory that change as the sealed file would, or refuse to while <see cref="Refuses"/> says so.</summary>
    private sealed class HeldRules : IAgentRules
    {
        internal AgentRules Rules { get; set; } = AgentRules.Defaults;

        internal string? Refuses { get; set; }

        public bool TryRead([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AgentRules? rules, out string problem)
        {
            rules = Rules;
            problem = string.Empty;
            return true;
        }

        public bool TryChange(Func<AgentRules, AgentRules> change, out string problem)
        {
            if (Refuses is { } refused)
            {
                problem = refused;
                return false;
            }

            Rules = change(Rules);
            problem = string.Empty;
            return true;
        }
    }
}
