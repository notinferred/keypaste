using System.Security.Cryptography;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A terminal's edit, sent over a real pipe to the vault's owner (D-0464): checked before anybody is
/// asked, put to the person by name only, written and saved only once they allow it and only within the
/// live unlock, taken back when its save fails, refused while the vault differs from its file, cooled
/// with every request for more from a terminal or an agent, and recorded on a line of its own.
/// </summary>
public sealed class SessionAuthorityEditTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _sentinel = "EDIT-OWNER-SENTINEL-41c7";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly EntryName _github = new("work", "github");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-edit-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly OwnerLog _log;
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly Vault _vault;

    public SessionAuthorityEditTests()
    {
        _log = new OwnerLog(_directory);
        _vault = Vault.Create(VaultPath, _master);
        _vault.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Password = "old-password" });
        _vault.Save();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private List<EditDone> Edited { get; } = [];

    /// <summary>What the owner's save does instead of saving, when a test makes it fail.</summary>
    private AgentSave? FailSaveWith { get; set; }

    public void Dispose()
    {
        _vault.Dispose();
        _lifetime.Dispose();
        _log.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private EditRequest Edit(TerminalEdit edit) => new(edit) { Vault = VaultPath, Session = "session-one" };

    private string? Password(EntryName name)
    {
        using var reopened = Vault.Open(VaultPath, _master);
        return reopened.Find(name)?.Password;
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

        if (FailSaveWith is { } failure)
        {
            return failure;
        }

        _vault.Save();
        return AgentSave.Saved;
    }

    [Fact]
    public async Task AnAllowedEdit_IsSavedByTheOwner_AndRecordedByNameOnly()
    {
        _fixture.Channel.EditAnswer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.EditAsync(Edit(new SetPassword(_github, NewSecret.Of(_sentinel))), Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (reply!.Decision, reply.Method));
        Assert.Equal(EditOutcome.Updated, reply.Done!.Outcome);
        Assert.True(reply.Recorded);
        Assert.Equal(_sentinel, Password(_github));

        var prompt = _fixture.Channel.LastEditPrompt!;
        Assert.Equal("keypaste set", prompt.Command);
        Assert.Equal("work/github", prompt.Item);
        Assert.Equal(AccessChangePrompt.FromTerminalText, prompt.OriginText);
        Assert.DoesNotContain(prompt.Changes, sentence => sentence.Contains(_sentinel, StringComparison.Ordinal));

        var line = Assert.Single(_log.Lines());
        Assert.Equal(AuditTools.Edit, line.GetProperty("tool").GetString());
        Assert.Equal("prompt", line.GetProperty("method").GetString());
        Assert.DoesNotContain(_sentinel, _log.Text(), StringComparison.Ordinal);
        Assert.Single(Edited);
    }

    [Fact]
    public async Task APlaceholderTheValuePlants_IsNamedInThePrompt_AndNoValueIsShownOrRecorded()
    {
        _fixture.Channel.EditAnswer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();
        var stored = "postgres://u:{PASSWORD}@db-" + _sentinel;

        var reply = await client.EditAsync(Edit(new SetFields(_github, [new FieldWrite("DATABASE_URL", stored)])), Token);

        Assert.Equal(EditOutcome.FieldsSet, reply!.Done!.Outcome);
        Assert.Equal(stored, _vault.ReadField(_github, "DATABASE_URL"));

        var prompt = _fixture.Channel.LastEditPrompt!;
        Assert.Equal("The new value of DATABASE_URL fills in {PASSWORD} from this item when it leaves.", prompt.Changes[1]);
        Assert.DoesNotContain(prompt.Changes, sentence => sentence.Contains(_sentinel, StringComparison.Ordinal) || sentence.Contains("old-password", StringComparison.Ordinal));

        Assert.Equal(AuditTools.Edit, Assert.Single(_log.Lines()).GetProperty("tool").GetString());
        Assert.DoesNotContain(_sentinel, _log.Text(), StringComparison.Ordinal);
        Assert.DoesNotContain("old-password", _log.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeniedEdit_WritesNothing_AndHoldsBackEveryRequestForMore()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var denied = await client.EditAsync(Edit(new SetPassword(_github, NewSecret.Of(_sentinel))), Token);
        var next = await client.EditAsync(Edit(new RemoveItem("work/github")), Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (denied!.Decision, denied.Method));
        Assert.Equal(AuditMethod.Cooldown, next!.Method);
        Assert.Equal(1, _fixture.Channel.EditAsked);
        Assert.True(_fixture.Gate.IsInCooldown(ApprovalGate.AskedForMore));
        Assert.Equal("old-password", Password(_github));
    }

    [Fact]
    public async Task AnEditTheVaultRefuses_IsAnsweredWithoutAskingAnybody()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.EditAsync(Edit(new RemoveItem("work/absent")), Token);

        Assert.Equal((AuditMethod.InvalidRequest, EditRefusalKind.NotFound), (reply!.Method, reply.Refusal));
        Assert.Equal("no item 'work/absent'", reply.Reason);
        Assert.Equal(0, _fixture.Channel.EditAsked);
    }

    [Fact]
    public async Task AnEditTheVaultAlreadyHolds_IsUnchanged_AndAsksNobody()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.EditAsync(Edit(new TagItem("billing", "dev", "work/github", Tagging: false)), Token);

        Assert.Equal(AuditMethod.Unchanged, reply!.Method);
        Assert.Equal(EditOutcome.Unchanged, reply.Done!.Outcome);
        Assert.Equal(0, _fixture.Channel.EditAsked);
    }

    [Fact]
    public async Task AVaultAnotherProgramSaved_IsRefusedBeforeAnybodyIsAsked()
    {
        using (var other = Vault.Open(VaultPath, _master))
        {
            other.AddEntry(new VaultEntry { Title = "elsewhere", Password = "x" });
            other.Save();
        }

        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.EditAsync(Edit(new SetPassword(_github, NewSecret.Of(_sentinel))), Token);

        Assert.Equal(AuditMethod.VaultChanged, reply!.Method);
        Assert.Equal(0, _fixture.Channel.EditAsked);
    }

    [Fact]
    public async Task AnEditWhoseSaveFails_IsTakenBackOutOfTheOpenVault()
    {
        _fixture.Channel.EditAnswer = ApprovalAnswer.ApprovedOnce;
        FailSaveWith = new AgentSave(AgentSaveOutcome.Failed, "the disk is full");
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.EditAsync(Edit(new AddItem(new EntryName("new/group", "item"), string.Empty, string.Empty, string.Empty, NewSecret.Of(_sentinel))), Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed), (reply!.Decision, reply.Method));
        Assert.Contains("the disk is full", reply.Reason, StringComparison.Ordinal);
        Assert.Null(_vault.Find(new EntryName("new/group", "item")));
        Assert.DoesNotContain("new", _vault.ReadGroupPaths());
        Assert.Equal(VaultSaveStatus.Saved, _vault.SaveState().Status);
    }

    [Fact]
    public async Task ALockWhileThePersonIsAsked_WritesNothing()
    {
        _fixture.Channel.EditAnswer = ApprovalAnswer.ApprovedOnce;
        _fixture.Channel.WhileEditing = _lifetime.End;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.EditAsync(Edit(new SetPassword(_github, NewSecret.Of(_sentinel))), Token);

        Assert.Equal(AuditMethod.VaultLocked, reply!.Method);
        Assert.Equal("old-password", Password(_github));
    }

    [Fact]
    public async Task AGeneratedValue_IsMadeByTheOwner()
    {
        _fixture.Channel.EditAnswer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.EditAsync(Edit(new RotatePassword(_github, SecretRecipe.Default)), Token);

        Assert.Equal(EditOutcome.Rotated, reply!.Done!.Outcome);
        Assert.Equal(20, Password(_github)!.Length);
        Assert.Contains("a generated 20-character password", _fixture.Channel.LastEditPrompt!.Changes[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOwnerThatTakesNoEdits_RefusesThem()
    {
        await using var owner = Owner.Start(this, editing: false);
        await using var client = await owner.AttachAsync();

        var reply = await client.EditAsync(Edit(new RemoveItem("work/github")), Token);

        Assert.Equal(AuditMethod.NoSession, reply!.Method);
        Assert.Equal("this keypaste process does not take changes from a terminal", reply.Reason);
        Assert.NotNull(_vault.Find(_github));
    }

    [Fact]
    public async Task AnOwnerWhoseLogWillNotOpen_RefusesAnEditBeforeAnybodyIsAsked()
    {
        _fixture.Channel.EditAnswer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this, audit: () => null);
        await using var client = await owner.AttachAsync();

        var reply = await client.EditAsync(Edit(new RemoveItem("work/github")), Token);

        Assert.Equal(AuditMethod.Failed, reply!.Method);
        Assert.Equal(0, _fixture.Channel.EditAsked);
        Assert.NotNull(_vault.Find(_github));
    }

    private sealed class Owner : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ApproverListener _listener;
        private readonly Task _running;
        private readonly string _vault;

        private Owner(string pipeName, SessionAuthority authority, string vault)
        {
            PipeName = pipeName;
            _vault = vault;
            _listener = new ApproverListener(pipeName, authority);
            _running = _listener.RunAsync(_stop.Token);
        }

        private string PipeName { get; }

        internal static Owner Start(SessionAuthorityEditTests test, Func<AuditLog?>? audit = null, bool editing = true)
        {
            var editor = editing
                ? new SessionEditor(
                    test._fixture.Gate,
                    lifetime => ReferenceEquals(lifetime, test._lifetime) && lifetime.IsLive ? test._vault : null,
                    test.Write,
                    Path.Combine(test._directory, "clients.toml"),
                    test._fixture.Clock,
                    narrate: null,
                    edited: test.Edited.Add)
                : null;

            return new(
                "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)),
                new SessionAuthority(
                    VaultIdentity.Of(test._directory, test.VaultPath),
                    () => test._lifetime,
                    test._fixture.Handler,
                    audit ?? (() => test._log.Log),
                    clock: test._fixture.Clock,
                    editor: editor),
                test.VaultPath);
        }

        internal async Task<ApproverClient> AttachAsync()
        {
            var client = await ApproverClient.TryConnectAsync(PipeName, _wait, Token);
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

            _listener.Dispose();
            _stop.Dispose();
        }
    }
}
