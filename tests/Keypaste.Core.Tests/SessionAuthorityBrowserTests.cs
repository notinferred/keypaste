using System.Collections.Concurrent;
using System.Security.Cryptography;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A vault's owner over a real pipe, as the browser relay reaches it: a browser's attachment names one of the three
/// KeePassXC-Browser builds, makes only a pairing, a pairing check, a fill and a lock, and no other attachment makes those;
/// the lock locks as <c>keypaste lock</c> does, asking nobody, and a fill releases nothing across a lock (D-0627, D-0313). A
/// browser's attachment is told the database hash, and a fill is decided only for the pairing a check or a pairing on the same
/// connection proved (D-0635, D-0636).
/// </summary>
public sealed class SessionAuthorityBrowserTests : IDisposable
{
    private static readonly TimeSpan _bound = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-browser-owner-tests-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly OwnerLog _log;
    private SessionLifetime? _lifetime = new("session-one");

    public SessionAuthorityBrowserTests() => _log = new OwnerLog(_directory);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _lifetime?.Dispose();
        _log.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_browser_attaches_naming_one_of_the_three_builds_and_no_other()
    {
        await using var owner = Owner.Start(this);

        foreach (var identity in BrowserIdentity.All)
        {
            await using var link = await owner.ConnectAsync();
            var attached = await link.AttachAsync(new AttachRequest(VaultPath) { Browser = identity.Caller }, Token);
            Assert.Equal("session-one", attached?.Session);
        }

        await using var other = await owner.ConnectAsync();
        var refused = await other.AttachAsync(new AttachRequest(VaultPath) { Browser = "chrome-extension://abcdefghijklmnopabcdefghijklmnop/" }, Token);

        Assert.NotNull(refused);
        Assert.False(refused.Attached);
        Assert.Equal(AuditMethod.InvalidRequest, refused.Refusal);
        Assert.Equal(BrowserIdentity.Only, refused.Reason);
    }

    [Fact]
    public async Task On_a_browser_s_attachment_a_pairing_a_check_and_a_fill_are_decided_for_its_build()
    {
        var browsers = new FakeBrowsers();
        browsers.Fill = new BrowserFillReply(BrowserOutcome.Done, string.Empty) { Logins = [new BrowserLogin("ada", "hunter2", "GitHub", "0123456789abcdef0123456789abcdef")] };
        await using var owner = Owner.Start(this, browsers: browsers);
        await using var link = await owner.AttachAsync(BrowserIdentity.FirefoxAddOns);

        var paired = await link.PairAsync(new BrowserPairRequest(Key()) { Vault = VaultPath, Session = "session-one" }, Token);
        var checkedPairing = await link.CheckAsync(new BrowserCheckRequest("firefox here", Key()) { Vault = VaultPath, Session = "session-one" }, Token);
        var fill = await link.FillAsync(new BrowserFillRequest("firefox here", Key(), "https://github.com/login") { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.Equal(new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = "firefox here" }, paired);
        Assert.Equal(new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = "firefox here" }, checkedPairing);
        Assert.NotNull(fill);
        Assert.Equal(BrowserOutcome.Done, fill.Outcome);
        Assert.Equal("hunter2", Assert.Single(fill.Logins).Password);
        Assert.Equal(3, browsers.Decided.Count);
        Assert.Equal(["pair", "check", "fill"], browsers.Decided.Select(decided => decided.Act));
        Assert.All(browsers.Decided, decided => Assert.Same(BrowserIdentity.FirefoxAddOns, decided.Browser));
        Assert.Single(browsers.Decided.Select(decided => decided.ConnectionId).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task On_another_attachment_the_four_are_refused_and_decided_by_nothing()
    {
        var browsers = new FakeBrowsers();
        var locks = 0;
        await using var owner = Owner.Start(this, () => locks++, browsers);
        await using var link = await owner.ConnectAsync();
        Assert.True((await link.AttachAsync(new AttachRequest(VaultPath), Token))!.Attached);

        BrowserReply?[] replies =
        [
            await link.PairAsync(new BrowserPairRequest(Key()) { Vault = VaultPath, Session = "session-one" }, Token),
            await link.CheckAsync(new BrowserCheckRequest("pairing", Key()) { Vault = VaultPath, Session = "session-one" }, Token),
            await link.LockAsync(new BrowserLockRequest { Vault = VaultPath, Session = "session-one" }, Token),
        ];
        var fill = await link.FillAsync(new BrowserFillRequest("pairing", Key(), "https://github.com/") { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.All(replies, reply => Assert.Equal(new BrowserReply(BrowserOutcome.Refused, "only a browser's connection pairs, checks a pairing, fills or locks for a browser"), reply));
        Assert.Equal(BrowserOutcome.Refused, fill?.Outcome);
        Assert.Equal("only a browser's connection pairs, checks a pairing, fills or locks for a browser", fill?.Reason);
        Assert.Empty(fill!.Logins);
        Assert.Empty(browsers.Decided);
        Assert.Equal(0, locks);
        Assert.True(_lifetime!.IsLive);
    }

    [Fact]
    public async Task A_browser_s_attachment_makes_no_other_request()
    {
        var locks = 0;
        await using var owner = Owner.Start(this, () => locks++);
        await using var client = await ApproverClient.TryConnectAsync(owner.PipeName, _bound, Token);
        Assert.NotNull(client);
        Assert.True((await client.AttachAsync(new AttachRequest(VaultPath) { Browser = BrowserIdentity.ChromeWebStore.Caller }, Token))!.Attached);

        var listing = await client.ListAsync(new NamesRequest(["**"]) { Vault = VaultPath, Session = "session-one" }, Token);
        var grants = await client.GrantsAsync(new GrantsRequest { Vault = VaultPath, Session = "session-one" }, Token);
        var locking = await client.LockAsync(new LockRequest { Vault = VaultPath, Session = "session-one" }, Token);

        const string onlyFour = "a browser's connection only pairs, checks a pairing, fills and locks";
        Assert.NotNull(listing);
        Assert.False(listing.VaultUnlocked);
        Assert.Empty(listing.Names);
        Assert.Equal(onlyFour, listing.Reason);
        Assert.Equal(onlyFour, grants?.Reason);
        Assert.Equal(new LockReply(false, onlyFour), locking);
        Assert.Equal(0, locks);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task An_owner_given_no_browser_answers_refuses_a_pairing_a_check_and_a_fill()
    {
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);

        var paired = await link.PairAsync(new BrowserPairRequest(Key()) { Vault = VaultPath, Session = "session-one" }, Token);
        var fill = await link.FillAsync(new BrowserFillRequest("pairing", Key(), "https://github.com/") { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.Equal(new BrowserReply(BrowserOutcome.Refused, "this keypaste process does not pair browsers or fill logins in them"), paired);
        Assert.Equal(BrowserOutcome.NotPaired, fill?.Outcome);
    }

    [Fact]
    public async Task A_browser_s_attachment_is_told_the_database_hash_and_no_other_attachment_is()
    {
        var browsers = new FakeBrowsers { Hash = new string('a', 64) };
        await using var owner = Owner.Start(this, browsers: browsers);

        await using var browser = await owner.ConnectAsync();
        var told = await browser.AttachAsync(new AttachRequest(VaultPath) { Browser = BrowserIdentity.EdgeAddOns.Caller }, Token);
        await using var agent = await owner.ConnectAsync();
        var untold = await agent.AttachAsync(new AttachRequest(VaultPath), Token);
        await using var nothing = Owner.Start(this);
        await using var unpaired = await nothing.ConnectAsync();
        var none = await unpaired.AttachAsync(new AttachRequest(VaultPath) { Browser = BrowserIdentity.EdgeAddOns.Caller }, Token);

        Assert.Equal(new string('a', 64), told?.DatabaseHash);
        Assert.True(untold?.Attached);
        Assert.Null(untold?.DatabaseHash);
        Assert.True(none?.Attached);
        Assert.Null(none?.DatabaseHash);
    }

    [Fact]
    public async Task A_fill_is_decided_only_for_the_pairing_its_own_connection_proved()
    {
        var browsers = new FakeBrowsers();
        await using var owner = Owner.Start(this, browsers: browsers);
        await using var unproved = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        await using var proved = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        await CheckAsync(proved, "laptop");

        var beforeAnyCheck = await unproved.FillAsync(new BrowserFillRequest("laptop", Key(), "https://github.com/") { Vault = VaultPath, Session = "session-one" }, Token);
        var anotherPairing = await proved.FillAsync(new BrowserFillRequest("desktop", Key(), "https://github.com/") { Vault = VaultPath, Session = "session-one" }, Token);
        var itsOwn = await proved.FillAsync(new BrowserFillRequest("laptop", Key(), "https://github.com/") { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.Equal(BrowserOutcome.NotPaired, beforeAnyCheck?.Outcome);
        Assert.Equal(BrowserOutcome.NotPaired, anotherPairing?.Outcome);
        Assert.Equal(BrowserOutcome.Done, itsOwn?.Outcome);
        Assert.Equal(2, browsers.Decided.Count);
    }

    [Fact]
    public async Task A_check_that_fails_and_a_removed_pairing_each_leave_the_connection_bound_to_nothing()
    {
        var browsers = new FakeBrowsers();
        SessionAuthority? authority = null;
        await using var owner = Owner.Start(this, browsers: browsers, built: built => authority = built);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);

        await CheckAsync(link, "laptop");
        browsers.CheckOutcome = BrowserOutcome.NotPaired;
        var failed = await link.CheckAsync(new BrowserCheckRequest("laptop", Key()) { Vault = VaultPath, Session = "session-one" }, Token);
        var afterFailure = await link.FillAsync(new BrowserFillRequest("laptop", Key(), "https://github.com/") { Vault = VaultPath, Session = "session-one" }, Token);

        browsers.CheckOutcome = BrowserOutcome.Done;
        await CheckAsync(link, "laptop");
        authority!.Unbind("laptop");
        var afterRemoval = await link.FillAsync(new BrowserFillRequest("laptop", Key(), "https://github.com/") { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.Equal(BrowserOutcome.NotPaired, failed?.Outcome);
        Assert.Equal(BrowserOutcome.NotPaired, afterFailure?.Outcome);
        Assert.Equal(BrowserOutcome.NotPaired, afterRemoval?.Outcome);
        Assert.DoesNotContain(browsers.Decided, decided => decided.Act == "fill");
    }

    [Fact]
    public async Task A_browser_s_lock_locks_as_keypaste_lock_does_asking_nobody_and_recording_nothing()
    {
        var lifetime = _lifetime!;
        await using var owner = Owner.Start(this, lifetime.End);
        await using var link = await owner.AttachAsync(BrowserIdentity.EdgeAddOns);

        var reply = await link.LockAsync(new BrowserLockRequest { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.Equal(new BrowserReply(BrowserOutcome.Done, string.Empty), reply);
        Assert.False(lifetime.IsLive);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Empty(_log.Lines());
    }

    [Fact]
    public async Task A_fill_whose_unlock_ends_while_it_is_decided_releases_nothing()
    {
        var lifetime = _lifetime!;
        var browsers = new FakeBrowsers
        {
            Deciding = lifetime.End,
            Fill = new BrowserFillReply(BrowserOutcome.Done, string.Empty) { Logins = [new BrowserLogin("ada", "hunter2", "GitHub", "0123456789abcdef0123456789abcdef")] },
        };
        await using var owner = Owner.Start(this, browsers: browsers);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        await CheckAsync(link, "pairing");

        var fill = await link.FillAsync(new BrowserFillRequest("pairing", Key(), "https://github.com/") { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.NotNull(fill);
        Assert.Equal(BrowserOutcome.Locked, fill.Outcome);
        Assert.Empty(fill.Logins);
    }

    [Fact]
    public async Task A_fill_too_large_for_one_reply_is_refused_whole()
    {
        var browsers = new FakeBrowsers
        {
            Fill = new BrowserFillReply(BrowserOutcome.Done, string.Empty)
            {
                Logins = [new BrowserLogin("ada", new string('x', 70 * 1024), "GitHub", "0123456789abcdef0123456789abcdef")],
            },
        };
        await using var owner = Owner.Start(this, browsers: browsers);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        await CheckAsync(link, "pairing");

        var fill = await link.FillAsync(new BrowserFillRequest("pairing", Key(), "https://github.com/") { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.NotNull(fill);
        Assert.Equal(BrowserOutcome.Refused, fill.Outcome);
        Assert.Equal("the logins would not fit one reply", fill.Reason);
        Assert.Empty(fill.Logins);
    }

    [Fact]
    public async Task A_browser_s_request_from_an_unlock_that_ended_or_to_a_locked_owner_is_answered_as_locked()
    {
        var browsers = new FakeBrowsers();
        await using var owner = Owner.Start(this, browsers: browsers);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        _lifetime = new SessionLifetime("session-two");

        var stale = await link.CheckAsync(new BrowserCheckRequest("pairing", Key()) { Vault = VaultPath, Session = "session-one" }, Token);
        _lifetime = null;
        var locked = await link.AttachAsync(new AttachRequest(VaultPath) { Browser = BrowserIdentity.ChromeWebStore.Caller }, Token);

        Assert.Equal(BrowserOutcome.Locked, stale?.Outcome);
        Assert.Equal(AuditMethod.VaultLocked, locked?.Refusal);
        Assert.Empty(browsers.Decided);
    }

    [Fact]
    public async Task A_browser_s_link_ends_when_the_owner_stops_answering_between_exchanges()
    {
        await using var owner = Owner.Start(this);
        await using var link = await owner.AttachAsync(BrowserIdentity.ChromeWebStore);
        Assert.False(link.IsSpent);

        await owner.DisposeAsync();

        await link.Ended.WaitAsync(_bound, Token);
        Assert.True(link.IsSpent);
        Assert.Null(await link.LockAsync(new BrowserLockRequest { Vault = VaultPath, Session = "session-one" }, Token));
    }

    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private async Task CheckAsync(BrowserLink link, string pairing)
    {
        var reply = await link.CheckAsync(new BrowserCheckRequest(pairing, Key()) { Vault = VaultPath, Session = "session-one" }, Token);
        Assert.Equal(BrowserOutcome.Done, reply?.Outcome);
    }

    /// <summary>Browser answers that decide every pairing, check and fill as a test sets them, keeping who each was for.</summary>
    private sealed class FakeBrowsers : IBrowserRequests
    {
        internal ConcurrentQueue<(BrowserIdentity Browser, string ConnectionId, string Act)> Decided { get; } = new();

        internal BrowserFillReply Fill { get; set; } = new(BrowserOutcome.Done, string.Empty);

        /// <summary>Runs while a fill is decided.</summary>
        internal Action? Deciding { get; init; }

        /// <summary>The database hash a browser's attachment is told, or null.</summary>
        internal string? Hash { get; init; }

        /// <summary>What a check comes to.</summary>
        internal BrowserOutcome CheckOutcome { get; set; } = BrowserOutcome.Done;

        public string? DatabaseHash() => Hash;

        public ValueTask<BrowserReply> PairAsync(BrowserPairRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken)
        {
            Decided.Enqueue((browser, connectionId, "pair"));
            return ValueTask.FromResult(new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = "firefox here" });
        }

        public ValueTask<BrowserReply> CheckAsync(BrowserCheckRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken)
        {
            Decided.Enqueue((browser, connectionId, "check"));
            return ValueTask.FromResult(CheckOutcome == BrowserOutcome.Done
                ? new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = request.Pairing }
                : new BrowserReply(CheckOutcome, "not this browser's"));
        }

        public ValueTask<BrowserFillReply> FillAsync(BrowserFillRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken)
        {
            Decided.Enqueue((browser, connectionId, "fill"));
            Deciding?.Invoke();
            return ValueTask.FromResult(Fill);
        }
    }

    /// <summary>An owner of the test's vault on its own pipe, whose session the test changes.</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly SessionAuthorityBrowserTests _test;
        private readonly CancellationTokenSource _stop = new();
        private readonly ApproverListener _listener;
        private readonly Task _running;
        private bool _disposed;

        private Owner(SessionAuthorityBrowserTests test, Action? lockNow, IBrowserRequests? browsers, Action<SessionAuthority>? built)
        {
            _test = test;
            PipeName = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            var authority = new SessionAuthority(
                VaultIdentity.Of(test._directory, test.VaultPath),
                () => test._lifetime,
                test._fixture.Handler,
                audit: () => test._log.Log,
                lockNow: lockNow,
                browsers: browsers);
            built?.Invoke(authority);
            _listener = new ApproverListener(PipeName, authority);
            _running = _listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal static Owner Start(SessionAuthorityBrowserTests test, Action? lockNow = null, IBrowserRequests? browsers = null, Action<SessionAuthority>? built = null) =>
            new(test, lockNow, browsers, built);

        internal async Task<BrowserLink> ConnectAsync()
        {
            var link = await BrowserLink.TryConnectAsync(PipeName, _bound, Token);
            Assert.NotNull(link);
            return link;
        }

        internal async Task<BrowserLink> AttachAsync(BrowserIdentity browser)
        {
            var link = await ConnectAsync();
            var attached = await link.AttachAsync(new AttachRequest(_test.VaultPath) { Browser = browser.Caller }, Token);
            Assert.True(attached?.Attached, attached?.Reason);
            return link;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
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
}
