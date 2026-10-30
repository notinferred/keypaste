using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The owner merging another program's save, over a real pipe to a real vault (1.4b2, D-0542, D-0545): agents refused from the
/// other program's save until the merged copy is saved, every grant ended and every request waiting on a person withdrawn as
/// merged, the next request answered from the merged copy and asked again, and one line naming items and never a value.
/// </summary>
public sealed class SessionAuthorityMergeTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _surface = "keypaste app";
    private const string _mine = "MERGE-MINE-SENTINEL-5d2e";
    private const string _theirs = "MERGE-THEIRS-SENTINEL-a91c";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly EntryName _github = new("work", "github");
    private static readonly EntryName _stripe = new("work", "stripe");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-merge-").FullName;
    private readonly OwnerLog _log;
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly FakeChannel _channel = new();
    private readonly FakeAgentSettings _settings = new();
    private Vault _vault;

    public SessionAuthorityMergeTests()
    {
        _log = new OwnerLog(_directory);
        _settings.Records[ApproverFixture.Agent] = ApproverFixture.SeesAll;

        using (var seed = Vault.Create(VaultPath, _master))
        {
            seed.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Password = "github-0" });
            seed.AddEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Password = "stripe-0" });
            Assert.Equal(AgentAccessWrite.Written, new AgentAccessStore(seed).Write(ApproverFixture.Agent, AgentAccessBuilders.Automatic));
            seed.Save();
        }

        _vault = Vault.Open(VaultPath, _master);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _vault.Dispose();
        _lifetime.Dispose();
        _log.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Agents_AreRefusedFromAnotherProgramsSave_UntilTheMergedCopyIsSaved()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        Assert.Equal("github-0", (await client.RequestAsync(Request(_github), Token))!.Value);

        InFile(database => Edit(database, _github, _theirs));
        var beforeTheMerge = await client.RequestAsync(Request(_github), Token);

        // A change the owner's save refused because the file changed, which the merge carries.
        Update(_stripe, _mine);
        Assert.Throws<VaultChangedOnDiskException>(_vault.Save);

        CredentialReply? whileSaving = null;
        var merged = await Task.Run(
            () => owner.Authority.Merge(_vault, Swap, _surface, open => open.MergeAnotherSave(
                waitBetweenAttempts: null,
                duringAttempt: _ => whileSaving ??= RequestNow(client, Request(_github)))),
            Token);

        var afterTheMerge = await client.RequestAsync(Request(_github), Token);

        Assert.Equal(VaultMergeSaveOutcome.Saved, merged.Outcome);
        AssertRefusedAsChanged(beforeTheMerge);
        AssertRefusedAsChanged(whileSaving);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (afterTheMerge!.Decision, afterTheMerge.Method));
        Assert.Equal(_theirs, afterTheMerge.Value);
        Assert.Equal("session-one", afterTheMerge.Session);
        Assert.Equal(2, _channel.Asked);
        Assert.Equal(_mine, _vault.Find(_stripe)!.Password);
    }

    /// <summary>The merge's withdrawal ends a timed allowance with no refusal before it, so the next request is asked again (D-0542, D-0516).</summary>
    [Fact]
    public async Task TheMerge_EndsEveryGrant_SoTheNextRequestIsAskedAgain()
    {
        _channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        Assert.Equal(AuditMethod.Prompt, (await client.RequestAsync(Request(_github), Token))!.Method);
        Assert.Equal(AuditMethod.GrantCache, (await client.RequestAsync(Request(_github), Token))!.Method);
        Assert.Single(owner.Authority.Activity.Grants);

        InFile(database => Edit(database, _stripe, _theirs));
        var merged = owner.Authority.Merge(_vault, Swap, _surface);

        Assert.Equal(VaultMergeSaveOutcome.Updated, merged.Outcome);
        Assert.Empty(owner.Authority.Activity.Grants);

        var again = await client.RequestAsync(Request(_github), Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (again!.Decision, again.Method));
        Assert.Equal(2, _channel.Asked);
        Assert.Equal(_theirs, _vault.Find(_stripe)!.Password);
    }

    [Fact]
    public async Task ARequestWaitingOnAPerson_IsWithdrawn_AndRecordedAsMerged()
    {
        _channel.Hold = true;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var pending = client.RequestAsync(Request(_github), Token);
        await _channel.Waiting.WaitAsync(_wait, Token);

        InFile(database => Edit(database, _stripe, _theirs));
        var merged = owner.Authority.Merge(_vault, Swap, _surface);
        var reply = await pending.AsTask().WaitAsync(_wait, Token);

        Assert.Equal(VaultMergeSaveOutcome.Updated, merged.Outcome);
        Assert.True(_channel.Withdrawn);
        Assert.Equal((AuditDecision.Denied, AuditMethod.VaultChanged), (reply!.Decision, reply.Method));
        Assert.Equal("the vault was merged with another program's save before anybody answered", reply.Reason);
        Assert.Null(reply.Value);
        Assert.True(_lifetime.IsLive);

        var line = Assert.Single(_log.Lines());
        Assert.Equal(AuditTools.Credential, line.GetProperty("tool").GetString());
        Assert.Equal("vault-changed", line.GetProperty("method").GetString());
        Assert.Equal("the vault was merged with another program's save before anybody answered", line.GetProperty("reason").GetString());
    }

    /// <summary>
    /// A merge the owner saved writes one line, naming the item changed in both copies, the one kept against a deletion and the agent
    /// whose settings the other program changed, and no value from either copy (D-0545).
    /// </summary>
    [Fact]
    public async Task ASavedMerge_IsRecordedOnOneLine_NamingItemsAndNeverAValue()
    {
        await using var owner = Owner.Start(this);
        Update(_github, _mine);
        Update(_stripe, _mine);
        InFile(database =>
        {
            var later = DateTime.UtcNow.AddHours(1);
            Edit(database, _github, _theirs, later);
            Purge(database, _stripe, later);

            var agent = database.RootGroup.GetEntries(true).Single(entry => entry.Strings.ReadSafe(PwDefs.TitleField) == ApproverFixture.Agent);
            agent.CreateBackup(database);
            agent.Strings.Set(AgentAccessStore.Field, new ProtectedString(false, AcafRecord.Write(AgentAccessBuilders.Asks)));
            agent.LastModificationTime = later;
        });

        var merged = owner.Authority.Merge(_vault, Swap, _surface);

        Assert.Equal(VaultMergeSaveOutcome.Saved, merged.Outcome);
        Assert.True(merged.Recorded);
        Assert.Equal([new MergeConflict(_github, OpenCopyCurrent: false)], merged.Report!.Conflicts);
        Assert.Equal([new MergeKeptItem(_stripe, DeletedInFile: true, Recycled: true)], merged.Report.Kept);

        var line = Assert.Single(_log.Lines());
        Assert.Equal(AuditTools.Merge, line.GetProperty("tool").GetString());
        Assert.Equal(("granted", "merged"), (line.GetProperty("decision").GetString(), line.GetProperty("method").GetString()));
        Assert.Equal(_surface, line.GetProperty("client").GetProperty("name").GetString());
        Assert.Equal("session-one", line.GetProperty("session").GetString());
        Assert.Equal(["work/github", "work/stripe"], line.GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal(
            "merged another program's save with the changes this copy held, and saved it: 1 item changed in both copies; 1 item kept against a deletion; the settings of claude-code changed",
            line.GetProperty("reason").GetString());

        var text = _log.Text();
        Assert.DoesNotContain(_mine, text, StringComparison.Ordinal);
        Assert.DoesNotContain(_theirs, text, StringComparison.Ordinal);
        Assert.DoesNotContain("github-0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stripe-0", text, StringComparison.Ordinal);
    }

    private static void AssertRefusedAsChanged(CredentialReply? reply)
    {
        Assert.NotNull(reply);
        Assert.Equal((AuditDecision.Denied, AuditMethod.VaultChanged), (reply.Decision, reply.Method));
        Assert.Null(reply.Value);
    }

    /// <summary>A request made from inside the merge, which runs on a thread of its own, answered before the merge goes on.</summary>
    private static CredentialReply? RequestNow(ApproverClient client, CredentialRequest request) =>
        client.RequestAsync(request, Token).AsTask().GetAwaiter().GetResult();

    private CredentialRequest Request(EntryName entry) => new()
    {
        Entry = $"{entry.GroupPath}/{entry.Title}",
        Field = "password",
        Reason = "deploy billing",
        TtlSeconds = 60,
        Exposure = ["work/**"],
        ClientLabel = ApproverFixture.Agent,
        Vault = VaultPath,
        Session = "session-one",
    };

    /// <summary>Puts the merged copy in place of the open one, as an owner does under its edit gate.</summary>
    private bool Swap(Vault merged)
    {
        var replaced = Interlocked.Exchange(ref _vault, merged);
        replaced.Dispose();
        return true;
    }

    private void Update(EntryName name, string password) =>
        Assert.True(_vault.UpdateEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = password }));

    /// <summary>Changes the vault's file as another KeePass program would, and saves it there.</summary>
    private void InFile(Action<PwDatabase> work)
    {
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(_master), false));
        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(VaultPath), key, null);
            work(database);
            database.Save(null);
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>An edit as KeePass saves one: the entry as it was into its history, then the change, stamped as given or now.</summary>
    private static void Edit(PwDatabase database, EntryName name, string password, DateTime? at = null)
    {
        var entry = Entry(database, name);
        entry.CreateBackup(database);
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
        entry.LastModificationTime = at ?? DateTime.UtcNow;
    }

    /// <summary>Deletes an entry for good as KeePass does, leaving its tombstone.</summary>
    private static void Purge(PwDatabase database, EntryName name, DateTime at)
    {
        var entry = Entry(database, name);
        entry.ParentGroup.Entries.Remove(entry);
        database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, at));
    }

    private static PwEntry Entry(PwDatabase database, EntryName name) =>
        database.RootGroup.GetEntries(true).Single(entry =>
            entry.Strings.ReadSafe(PwDefs.TitleField) == name.Title && entry.ParentGroup.Name == name.GroupPath);

    /// <summary>The owner's server for this unlock, listening on a pipe of its own, its vault the test's open copy.</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly SessionServer _server;
        private readonly Task _running;
        private readonly string _pipeName;
        private readonly string _vault;

        private Owner(string pipeName, SessionServer server, string vault)
        {
            _pipeName = pipeName;
            _server = server;
            _vault = vault;
            _running = server.RunAsync(_stop.Token);
        }

        internal SessionAuthority Authority => _server.Authority;

        internal static Owner Start(SessionAuthorityMergeTests test)
        {
            var name = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

            return new Owner(
                name,
                SessionServer.Listen(name, new SessionServerOptions
                {
                    Vault = VaultIdentity.Of(test._directory, test.VaultPath),
                    Lifetime = test._lifetime,
                    CurrentLifetime = () => test._lifetime,
                    UnlockedFor = lifetime => ReferenceEquals(lifetime, test._lifetime) && lifetime.IsLive ? Volatile.Read(ref test._vault) : null,
                    Channel = test._channel,
                    Settings = test._settings,
                    Hourly = new HourlyLimit(TimeProvider.System),
                    Audit = () => test._log.Log,
                    WatchEdits = _ => { },
                    UnwatchEdits = _ => { },
                }),
                test.VaultPath);
        }

        internal async Task<ApproverClient> AttachAsync()
        {
            var client = await ApproverClient.TryConnectAsync(_pipeName, _wait, Token);
            Assert.NotNull(client);
            Assert.True((await client.AttachAsync(new AttachRequest(_vault), Token))?.Attached);
            return client;
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

            _server.Dispose();
            _stop.Dispose();
        }
    }
}
