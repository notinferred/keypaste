using System.Security.Cryptography;
using System.Text;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A browser pairing with the vault's owner over a real pipe (8.1d): refused unasked, recorded and told while nobody armed
/// pairing; armed, confirmed naming the build the browser named, and saved holding the hash of the extension's key and never
/// the key; one confirmation per arming and two minutes; a check proving only its own build's pairing and key against the vault
/// as its file holds it; a removal purged, recorded and refused from its next check; and a database hash of keypaste's own
/// (D-0633 to D-0637).
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
    public async Task A_request_to_pair_while_nobody_armed_pairing_is_refused_unasked_recorded_and_told()
    {
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);

        var reply = await link.PairAsync(Pair(), Token);

        Assert.Equal(BrowserOutcome.Refused, reply?.Outcome);
        Assert.Empty(_fixture.Channel.PairingPrompts);
        Assert.Same(BrowserIdentity.ChromeWebStore, Assert.Single(_fixture.Channel.PairingNotices).Identity);
        Assert.Empty(Saved());

        var line = Assert.Single(_log.Lines());
        Assert.Equal(AuditTools.Browser, line.GetProperty("tool").GetString());
        Assert.Equal("pair", line.GetProperty("args").GetProperty("field").GetString());
        Assert.Equal("denied", line.GetProperty("decision").GetString());
        Assert.Equal("not-armed", line.GetProperty("method").GetString());
        Assert.Equal("fipabobollckgdobkjfkkchimlphcicg", line.GetProperty("client").GetProperty("name").GetString());
        Assert.Equal("session-one", line.GetProperty("session").GetString());
        Assert.DoesNotContain(_key, _log.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Armed_a_pairing_is_confirmed_naming_the_build_and_saved_holding_only_the_hash_of_the_key()
    {
        _fixture.Channel.AnswerPairing = _ => new PairingAnswer(ApprovalAnswer.ApprovedOnce, "work laptop");
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        owner.Browsers.Arm();

        var reply = await link.PairAsync(Pair(), Token);

        Assert.Equal(new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = "work laptop" }, reply);
        var prompt = Assert.Single(_fixture.Channel.PairingPrompts);
        Assert.Equal("keypaste's extension from the Chrome Web Store (fipabobollckgdobkjfkkchimlphcicg)", prompt.IdentityText);
        Assert.Null(prompt.NameProblem(prompt.SuggestedName));
        Assert.False(prompt.NameChosen);

        var record = Assert.Single(Saved());
        Assert.Equal("work laptop", record.Title);
        Assert.Equal(BrowserPairings.KeyHash(_key), record.Password);
        Assert.Contains(BrowserIdentity.ChromeWebStore.Caller, record.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain(_key, record.Password + record.Notes + record.Username + record.Url, StringComparison.Ordinal);

        var line = Assert.Single(_log.Lines());
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal("prompt", line.GetProperty("method").GetString());
        Assert.Equal("work laptop", line.GetProperty("client").GetProperty("label").GetString());
        Assert.DoesNotContain(_key, _log.Text(), StringComparison.Ordinal);
        Assert.Null(owner.Browsers.ArmedUntil);
    }

    [Fact]
    public async Task An_arming_answers_one_confirmation_for_two_minutes()
    {
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.FirefoxAddOns);

        owner.Browsers.Arm();
        Assert.Equal(_fixture.Clock.GetUtcNow() + TimeSpan.FromMinutes(2), owner.Browsers.ArmedUntil);
        var denied = await link.PairAsync(Pair(), Token);
        var after = await link.PairAsync(Pair(), Token);

        owner.Browsers.Arm();
        _fixture.Clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        var late = await link.PairAsync(Pair(), Token);

        Assert.Equal(BrowserOutcome.Refused, denied?.Outcome);
        Assert.Equal(BrowserOutcome.Refused, after?.Outcome);
        Assert.Equal(BrowserOutcome.Refused, late?.Outcome);
        Assert.Single(_fixture.Channel.PairingPrompts);
        Assert.Equal(["prompt", "not-armed", "not-armed"], _log.Lines().Select(line => line.GetProperty("method").GetString()));
        Assert.Empty(Saved());
    }

    [Fact]
    public async Task A_confirmation_nobody_was_shown_leaves_the_arming_and_one_with_a_name_in_use_pairs_nothing()
    {
        var answers = new Queue<PairingAnswer>([new(ApprovalAnswer.Busy, null), new(ApprovalAnswer.ApprovedOnce, "laptop"), new(ApprovalAnswer.ApprovedOnce, "laptop")]);
        _fixture.Channel.AnswerPairing = _ => answers.Dequeue();
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.EdgeAddOns);
        owner.Browsers.Arm();

        var busy = await link.PairAsync(Pair(), Token);
        var paired = await link.PairAsync(Pair(), Token);
        owner.Browsers.Arm();
        var taken = await link.PairAsync(Pair(), Token);

        Assert.Equal(BrowserOutcome.Refused, busy?.Outcome);
        Assert.Equal("laptop", paired?.Pairing);
        Assert.Equal(BrowserOutcome.Refused, taken?.Outcome);
        Assert.Equal(["laptop"], _fixture.Channel.PairingPrompts[^1].TakenNames);
        Assert.Single(Saved());
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
    public async Task A_check_while_the_vault_differs_from_its_file_proves_nothing()
    {
        await using var owner = Owner.Start(this);
        await PairedAsync(owner, "laptop");
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        _vault.AddEntry(new VaultEntry { Title = "unsaved" });

        var reply = await link.CheckAsync(Check("laptop", _key), Token);

        Assert.Equal(BrowserOutcome.Refused, reply?.Outcome);
        Assert.Equal("vault-changed", _log.Lines()[^1].GetProperty("method").GetString());
    }

    [Fact]
    public async Task Removing_a_pairing_purges_it_records_it_and_refuses_its_next_check()
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
        Assert.Empty(Saved());
        Assert.Empty(owner.Browsers.List());
        Assert.Equal(BrowserOutcome.NotPaired, check?.Outcome);

        var lines = _log.Lines();
        Assert.Equal("removed", lines[^2].GetProperty("method").GetString());
        Assert.Equal("granted", lines[^2].GetProperty("decision").GetString());
        Assert.Equal("laptop", lines[^2].GetProperty("client").GetProperty("label").GetString());
        Assert.Equal("not-paired", lines[^1].GetProperty("method").GetString());
    }

    [Fact]
    public async Task The_database_hash_is_keypaste_s_own_and_a_browser_s_attachment_is_told_it()
    {
        await using var owner = Owner.Start(this);
        await using var link = await owner.ConnectAsync();

        var attached = await link.AttachAsync(new AttachRequest(VaultPath) { Browser = BrowserIdentity.ChromeWebStore.Caller }, Token);

        var root = _vault.RootGroupHex;
        var keepassxcs = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root)));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("keypaste\n" + root))), attached?.DatabaseHash);
        Assert.NotEqual(keepassxcs, attached?.DatabaseHash);
        Assert.Matches("^[0-9a-f]{32}$", root);
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
        owner.Browsers.Arm();

        var reply = await link.PairAsync(Pair(), Token);

        Assert.Equal(BrowserOutcome.Locked, reply?.Outcome);
        Assert.Empty(Saved());
        Assert.Equal("vault-locked", _log.Lines()[^1].GetProperty("method").GetString());
    }

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
    public async Task A_click_asks_the_person_and_fills_the_login_they_chose_with_its_line_written_first()
    {
        var github = AddLogin("web", "GitHub", "dana", "{USERNAME}-s3cret", "https://github.com");
        AddLogin("web", "Bank", "dana", "bank-s3cret", "https://bank.example");
        _fixture.Channel.AnswerFill = (prompt, _) => ValueTask.FromResult(new FillAnswer(ApprovalAnswer.ApprovedOnce, prompt.Candidates[0].Uuid, false));
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var before = _log.Lines().Count;

        var reply = await link.FillAsync(Fill("https://gist.github.com/login?next=/secret#frag", personAsked: true), Token);

        Assert.Equal(BrowserOutcome.Done, reply?.Outcome);
        var login = Assert.Single(reply!.Logins);
        Assert.Equal(("dana", "dana-s3cret", "GitHub"), (login.Login, login.Password, login.Name));
        Assert.Equal(github, login.Uuid, ignoreCase: true);

        var prompt = Assert.Single(_fixture.Channel.FillPrompts);
        Assert.Equal("https://gist.github.com:443", prompt.Origin);
        Assert.Equal("github.com", prompt.Site);
        Assert.Equal("laptop", prompt.Pairing);
        Assert.True(prompt.OffersAlways);
        var candidate = Assert.Single(prompt.Candidates);
        Assert.Equal(("GitHub", "dana", "web"), (candidate.Title, candidate.Username, candidate.Group));
        Assert.Equal(github, candidate.Uuid, ignoreCase: true);

        var line = Assert.Single(_log.Lines().Skip(before));
        Assert.Equal("fill", line.GetProperty("args").GetProperty("field").GetString());
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal("prompt", line.GetProperty("method").GetString());
        Assert.Equal(github, line.GetProperty("args").GetProperty("item").GetString(), ignoreCase: true);
        Assert.Equal("web/GitHub", line.GetProperty("args").GetProperty("entry").GetString());
        Assert.Equal("laptop", line.GetProperty("client").GetProperty("label").GetString());
        Assert.EndsWith("on https://gist.github.com:443", line.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", _log.Text(), StringComparison.Ordinal);
        Assert.DoesNotContain("next=", _log.Text(), StringComparison.Ordinal);
        Assert.DoesNotContain("/login", _log.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_load_asks_nobody_and_releases_only_what_the_person_always_fills_on_that_exact_origin()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var before = _log.Lines().Count;

        var unallowed = await link.FillAsync(Fill("https://github.com/login", personAsked: false), Token);
        _rules.Rules = _rules.Rules.AllowingFill(new BrowserAllowance("laptop", Paired(owner), "https://github.com:443", github));
        var allowed = await link.FillAsync(Fill("https://github.com/login", personAsked: false), Token);
        var subdomain = await link.FillAsync(Fill("https://gist.github.com/", personAsked: false), Token);

        Assert.Equal(BrowserOutcome.Done, unallowed?.Outcome);
        Assert.Empty(unallowed!.Logins);
        Assert.Equal("s3cret", Assert.Single(allowed!.Logins).Password);
        Assert.Empty(subdomain!.Logins);
        Assert.Empty(_fixture.Channel.FillPrompts);

        var line = Assert.Single(_log.Lines().Skip(before));
        Assert.Equal("allowed", line.GetProperty("method").GetString());
        Assert.Equal(github, line.GetProperty("args").GetProperty("item").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task Ticking_always_keeps_an_allowance_for_that_pairing_and_origin_which_the_next_page_load_fills_under()
    {
        var github = AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        _fixture.Channel.AnswerFill = (prompt, _) => ValueTask.FromResult(new FillAnswer(ApprovalAnswer.ApprovedOnce, prompt.Candidates[0].Uuid, true));
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        var chosen = await link.FillAsync(Fill("https://github.com/login", personAsked: true), Token);
        var after = await link.FillAsync(Fill("https://github.com/", personAsked: false), Token);

        Assert.Single(chosen!.Logins);
        Assert.Equal(new BrowserAllowance("laptop", Paired(owner), "https://github.com:443", github), Assert.Single(_rules.Rules.BrowserAllowances));
        Assert.Single(after!.Logins);
        Assert.Single(_fixture.Channel.FillPrompts);
        Assert.Contains("will always fill it here", _log.Lines()[^2].GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal("allowed", _log.Lines()[^1].GetProperty("method").GetString());
    }

    [Theory]
    [InlineData("http://github.com/login", "http://github.com", null)]
    [InlineData("https://nas/", "https://nas", null)]
    [InlineData("https://github.com/login", "https://github.com", "https://collector.example/post")]
    public async Task No_always_is_offered_or_kept_where_every_fill_is_asked(string page, string address, string? submitUrl)
    {
        AddLogin(string.Empty, "Site", "dana", "s3cret", address);
        _fixture.Channel.AnswerFill = (prompt, _) => ValueTask.FromResult(new FillAnswer(ApprovalAnswer.ApprovedOnce, prompt.Candidates[0].Uuid, true));
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        var reply = await link.FillAsync(Fill(page, personAsked: true, submitUrl), Token);

        Assert.Single(reply!.Logins);
        Assert.False(Assert.Single(_fixture.Channel.FillPrompts).OffersAlways);
        Assert.Empty(_rules.Rules.BrowserAllowances);
    }

    [Fact]
    public async Task A_refusal_fills_nothing_and_holds_the_page_back_a_minute()
    {
        AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var before = _log.Lines().Count;

        var denied = await link.FillAsync(Fill("https://github.com/login", personAsked: true), Token);
        var again = await link.FillAsync(Fill("https://github.com/login", personAsked: true), Token);
        _fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var later = await link.FillAsync(Fill("https://github.com/login", personAsked: true), Token);

        Assert.Equal(BrowserOutcome.Refused, denied?.Outcome);
        Assert.Equal(BrowserOutcome.Refused, again?.Outcome);
        Assert.Equal(BrowserOutcome.Refused, later?.Outcome);
        Assert.Equal(2, _fixture.Channel.FillPrompts.Count);
        Assert.Equal(["prompt", "cooldown", "prompt"], _log.Lines().Skip(before).Select(line => line.GetProperty("method").GetString()));
        Assert.All(_log.Lines().Skip(before), line => Assert.Equal("denied", line.GetProperty("decision").GetString()));
    }

    [Fact]
    public async Task An_expired_or_trashed_item_is_never_offered_and_a_click_records_the_expired_one_kept_out()
    {
        var expired = AddLogin(string.Empty, "Old", "dana", "s3cret", "https://github.com", expires: _fixture.Clock.GetUtcNow().AddDays(-1));
        AddLogin(string.Empty, "Trashed", "dana", "s3cret", "https://github.com");
        _vault.RemoveEntry(new EntryName(string.Empty, "Trashed"), out _);
        _vault.Save();
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var before = _log.Lines().Count;

        var reply = await link.FillAsync(Fill("https://github.com/", personAsked: true), Token);

        Assert.Empty(reply!.Logins);
        Assert.Empty(_fixture.Channel.FillPrompts);
        var line = Assert.Single(_log.Lines().Skip(before));
        Assert.Equal(expired, line.GetProperty("args").GetProperty("item").GetString(), ignoreCase: true);
        Assert.Equal("blocked", line.GetProperty("method").GetString());
        Assert.StartsWith("expired ", line.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_items_of_one_title_are_told_apart_by_uuid()
    {
        var work = AddLogin("web", "GitHub", "dana-work", "work-s3cret", "https://github.com");
        AddLogin("web", "GitHub", "dana", "home-s3cret", "https://github.com");
        _fixture.Channel.AnswerFill = (prompt, _) => ValueTask.FromResult(new FillAnswer(ApprovalAnswer.ApprovedOnce, work, false));
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        var reply = await link.FillAsync(Fill("https://github.com/", personAsked: true), Token);

        Assert.Equal(2, Assert.Single(_fixture.Channel.FillPrompts).Candidates.Count);
        Assert.Equal("work-s3cret", Assert.Single(reply!.Logins).Password);
    }

    [Fact]
    public async Task A_pairing_removed_while_the_person_is_asked_fills_nothing()
    {
        AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        _fixture.Channel.AnswerFill = (prompt, _) =>
        {
            Assert.True(owner.Browsers.Remove("laptop").Removed);
            return ValueTask.FromResult(new FillAnswer(ApprovalAnswer.ApprovedOnce, prompt.Candidates[0].Uuid, false));
        };

        var reply = await link.FillAsync(Fill("https://github.com/", personAsked: true), Token);

        Assert.NotEqual(BrowserOutcome.Done, reply?.Outcome);
        Assert.DoesNotContain(_log.Lines(), line => line.GetProperty("args").GetProperty("field").GetString() == "fill" && line.GetProperty("decision").GetString() == "granted");
    }

    [Fact]
    public async Task A_lock_while_the_person_is_asked_fills_nothing()
    {
        AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        _fixture.Channel.AnswerFill = (prompt, _) =>
        {
            _lifetime.End();
            return ValueTask.FromResult(new FillAnswer(ApprovalAnswer.ApprovedOnce, prompt.Candidates[0].Uuid, false));
        };

        var reply = await link.FillAsync(Fill("https://github.com/", personAsked: true), Token);

        Assert.Equal(BrowserOutcome.Locked, reply?.Outcome);
        Assert.Empty(reply!.Logins);
        Assert.DoesNotContain(_log.Lines(), line => line.GetProperty("decision").GetString() == "granted" && line.GetProperty("args").GetProperty("field").GetString() == "fill");
    }

    [Fact]
    public async Task A_click_while_another_prompt_is_up_gets_no_logins_and_nobody_is_asked_twice()
    {
        AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        var answer = new TaskCompletionSource<FillAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Channel.AnswerFill = (_, _) =>
        {
            asked.TrySetResult();
            return new ValueTask<FillAnswer>(answer.Task);
        };
        await using var owner = Owner.Start(this);
        await using var first = await PairedLinkAsync(owner);
        await using var second = await CheckedLinkAsync(owner);

        var waiting = first.FillAsync(Fill("https://github.com/", personAsked: true), Token).AsTask();
        await asked.Task.WaitAsync(_wait, Token);
        var busy = await second.FillAsync(Fill("https://github.com/", personAsked: true), Token);
        answer.SetResult(new FillAnswer(ApprovalAnswer.ApprovedOnce, _fixture.Channel.FillPrompts[0].Candidates[0].Uuid, false));
        var filled = await waiting;

        Assert.Equal(BrowserOutcome.Done, busy?.Outcome);
        Assert.Empty(busy!.Logins);
        Assert.Single(filled!.Logins);
        Assert.Single(_fixture.Channel.FillPrompts);
    }

    [Fact]
    public async Task A_lookup_that_matches_nothing_asks_nobody_and_writes_no_line()
    {
        AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var before = _log.Lines().Count;

        var reply = await link.FillAsync(Fill("https://example.org/login", personAsked: true), Token);
        var suffix = await link.FillAsync(Fill("https://github.io/", personAsked: true), Token);

        Assert.Equal(BrowserOutcome.Done, reply?.Outcome);
        Assert.Empty(reply!.Logins);
        Assert.Equal(BrowserOutcome.Done, suffix?.Outcome);
        Assert.Empty(suffix!.Logins);
        Assert.Empty(_fixture.Channel.FillPrompts);
        Assert.Equal(before, _log.Lines().Count);
    }

    [Fact]
    public async Task A_release_whose_line_cannot_be_written_is_refused()
    {
        AddLogin(string.Empty, "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        _rules.Rules = _rules.Rules.AllowingFill(new BrowserAllowance("laptop", Paired(owner), "https://github.com:443", Uuid("GitHub")));
        _log.Log.Dispose();

        var reply = await link.FillAsync(Fill("https://github.com/", personAsked: false), Token);

        Assert.NotEqual(BrowserOutcome.Done, reply?.Outcome);
        Assert.Empty(reply!.Logins);
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

    private BrowserFillRequest Fill(string url, bool personAsked, string? submitUrl = null) =>
        new("laptop", _key, url) { SubmitUrl = submitUrl, PersonAsked = personAsked, Vault = VaultPath, Session = "session-one" };

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

    private List<VaultEntry> Saved()
    {
        using var reopened = Vault.Open(VaultPath, _master);
        return [.. reopened.ReadEntries(includeReserved: true).Where(entry => entry.GroupPath == ReservedGroups.Browsers)];
    }

    private async Task PairedAsync(Owner owner, string name)
    {
        _fixture.Channel.AnswerPairing = _ => new PairingAnswer(ApprovalAnswer.ApprovedOnce, name);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        owner.Browsers.Arm();
        Assert.Equal(name, (await link.PairAsync(Pair(), Token))?.Pairing);
    }

    private AgentSave Write(SessionLifetime asked, Func<Vault, bool> apply)
    {
        if (!ReferenceEquals(asked, _lifetime) || !_lifetime.IsLive)
        {
            return new AgentSave(AgentSaveOutcome.Locked, "the vault is locked");
        }

        if (!apply(_vault))
        {
            return AgentSave.Unchanged;
        }

        _vault.Save();
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

        internal static Owner Start(SessionBrowsersTests test)
        {
            var identity = VaultIdentity.Of(test._directory, test.VaultPath);
            var browsers = new SessionBrowsers(
                identity,
                test._lifetime,
                test._fixture.Gate,
                lifetime => ReferenceEquals(lifetime, test._lifetime) && lifetime.IsLive ? test._vault : null,
                test.Write,
                test._rules,
                () => test._log.Log,
                new PairingUses(),
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

            return new("keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), authority, browsers, test.VaultPath);
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
