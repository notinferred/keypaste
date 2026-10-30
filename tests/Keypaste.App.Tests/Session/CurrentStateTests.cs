using System.Text.Json;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The app's session answers agents from the vault as last saved: an edit made on a screen is the next
/// value released, what it touched is asked about again, and a file another program saved is refused
/// until keypaste has merged that save in the same session, or a person reloads a file it would not
/// merge, either withdrawing what was waiting on a person (U.3, N.16, D-0317, D-0318, D-0412, D-0542).
/// </summary>
/// <remarks>
/// Each act goes through the screen a person uses, and each request through the app's real endpoint,
/// so a grant withdrawn only in a view model would not pass.
/// </remarks>
public sealed class CurrentStateTests : IDisposable
{
    private const string _token = "env/dev/TOKEN";

    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private readonly TempVault _fixture = new();
    private readonly ApprovingPrompt _prompt = new();
    private readonly AppVaultSession _session;
    private readonly SessionHost _host;

    public CurrentStateTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "TOKEN", Password = "v1" });
            vault.AddEntry(new VaultEntry { GroupPath = "personal", Title = "KEEP", Password = "k" });
            TempVault.Sees(vault, TempVault.Agent);
            vault.Save();
        }

        _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home);
        _host = new SessionHost(_session, approverOverride: null, () => _prompt);
        Unlock();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _host.Dispose();
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task An_edit_saved_in_the_app_is_the_next_value_released_and_is_asked_about_again()
    {
        await using var agent = await Agent.AttachAsync(this);

        Assert.Equal((AuditMethod.Prompt, "v1"), await agent.RequestAsync(_token));
        Assert.Equal((AuditMethod.GrantCache, "v1"), await agent.RequestAsync(_token));

        OnScreen(entries =>
        {
            var detail = Select(entries, _token);
            detail.EditCommand.Execute(null);

            foreach (var c in "v2")
            {
                detail.NewPassword.Type(c);
            }

            detail.SaveCommand.Execute(null);
            Assert.False(detail.IsEditing, entries.Error);
        });

        Assert.Equal((AuditMethod.Prompt, "v2"), await agent.RequestAsync(_token));
        Assert.Equal(2, _prompt.Asked);
    }

    public static TheoryData<string> Changes => ["delete", "move", "group-rename"];

    /// <summary>
    /// Each change refuses the next request for the name it took the entry from, and undoing it puts
    /// the entry back under that name to be asked about again rather than served from the grant.
    /// </summary>
    [Theory]
    [MemberData(nameof(Changes))]
    public async Task A_deleted_moved_or_regrouped_entry_is_refused_and_its_grant_is_gone(string change)
    {
        await using var agent = await Agent.AttachAsync(this);
        Assert.Equal((AuditMethod.Prompt, "v1"), await agent.RequestAsync(_token));

        OnScreen(entries =>
        {
            switch (change)
            {
                case "delete":
                    Select(entries, _token);
                    entries.DeleteCommand.Execute(null);
                    entries.ConfirmDeleteCommand.Execute(null);
                    break;

                case "move":
                    Relocate(entries, _token, "personal");
                    break;

                case "group-rename":
                    RenameGroup(entries, "env/dev", "prod");
                    break;
            }

            Assert.Null(entries.Error);
        });

        var refused = await agent.RequestAsync(_token);
        Assert.Equal(AuditMethod.OutOfScope, refused.Method);
        Assert.Null(refused.Value);

        OnScreen(entries =>
        {
            switch (change)
            {
                case "delete":
                    Relocate(entries, "personal/KEEP", "env/dev", "TOKEN");
                    break;

                case "move":
                    Relocate(entries, "personal/TOKEN", "env/dev");
                    break;

                case "group-rename":
                    RenameGroup(entries, "env/prod", "dev");
                    break;
            }

            Assert.Null(entries.Error);
        });

        var again = await agent.RequestAsync(_token);
        Assert.Equal(AuditMethod.Prompt, again.Method);
        Assert.Equal(change == "delete" ? "k" : "v1", again.Value);
        Assert.Equal(2, _prompt.Asked);
    }

    [Fact]
    public async Task After_an_access_change_no_earlier_grant_releases_anything()
    {
        await using var agent = await Agent.AttachAsync(this);
        Assert.Equal((AuditMethod.Prompt, "v1"), await agent.RequestAsync(_token));

        using (var access = new VaultAccessViewModel(_session, _fixture.Home, new FakeVaultFilePicker()))
        {
            foreach (var c in TempVault.Password)
            {
                access.TypeCurrent(c);
            }

            access.SetPassword = true;

            foreach (var c in "a different master password")
            {
                access.TypeNew(c);
                access.TypeConfirm(c);
            }

            access.ReviewCommand.Execute(null);
            await access.ChangeAsync();
            Assert.StartsWith("Changed.", access.Message, StringComparison.Ordinal);
        }

        Assert.True(_session.IsUnlocked);
        Assert.Equal((AuditMethod.Prompt, "v1"), await agent.RequestAsync(_token));
        Assert.Equal(2, _prompt.Asked);
    }

    /// <summary>
    /// Another program's save refuses requests until keypaste merges it; the app's edit whose save it refused is merged with it
    /// and saved, and the next request is answered from the merged copy in the same session, asked about again (D-0542).
    /// </summary>
    [Fact]
    public async Task A_merge_keeps_the_session_and_the_next_request_is_asked()
    {
        await using var agent = await Agent.AttachAsync(this);
        var session = _session.SessionId;
        Assert.Equal((AuditMethod.Prompt, "v1"), await agent.RequestAsync(_token));
        Assert.Equal((AuditMethod.GrantCache, "v1"), await agent.RequestAsync(_token));

        using (var writer = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            writer.UpdateEntry(new VaultEntry { GroupPath = "personal", Title = "KEEP", Password = "external" });
            writer.Save();
        }

        var refused = await agent.RequestAsync(_token);
        Assert.Equal(AuditMethod.VaultChanged, refused.Method);
        Assert.Null(refused.Value);

        OnScreen(entries =>
        {
            var detail = Select(entries, _token);
            detail.EditCommand.Execute(null);
            detail.NewPassword.Type('x');
            detail.SaveCommand.Execute(null);
            Assert.False(detail.IsEditing, entries.Error);
        });

        Assert.Equal(session, _session.SessionId);
        Assert.Equal((AuditMethod.Prompt, "x"), await agent.RequestAsync(_token));
        Assert.Equal(2, _prompt.Asked);

        using var saved = Vault.Open(_fixture.Path_, TempVault.Password);
        Assert.Equal("x", saved.Find(_token)!.Password);
        Assert.Equal("external", saved.Find("personal/KEEP")!.Password);
    }

    /// <summary>
    /// A terminal's edit allowed after another program saved the vault under another key is answered, and the app then locks as a
    /// refused merge locks, rather than the lock waiting for the very connection the edit came on (D-0392, D-0543).
    /// </summary>
    [Fact]
    public async Task A_terminal_edit_meeting_another_key_is_answered_and_the_app_locks()
    {
        var locked = new TaskCompletionSource<VaultLockReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.Locked += (_, reason) => locked.TrySetResult(reason);
        await using var agent = await Agent.AttachAsync(this);
        var held = _prompt.HoldNext();
        var editing = agent.EditAsync(new SetPassword(new EntryName("personal", "KEEP"), NewSecret.Of("terminal")));
        await held.WaitAsync(_connect, Cancel);

        using (var other = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            const string next = "a different horse battery staple";
            Assert.Equal(
                VaultAccessOutcome.Changed,
                other.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), next, next).Outcome);
        }

        _prompt.Allow();

        var reply = await editing.WaitAsync(_connect, Cancel);
        Assert.Equal((AuditDecision.Denied, AuditMethod.VaultChanged), (reply.Decision, reply.Method));
        Assert.Equal(VaultLockReason.ReloadRefused, await locked.Task.WaitAsync(_connect, Cancel));
        Assert.False(_session.IsUnlocked);
    }

    /// <summary>
    /// A file holding another vault is not merged: requests stay refused, the notice says why and offers Reload, and Reload
    /// answers from what the file holds in the same session, asking again (D-0412, D-0543).
    /// </summary>
    [Fact]
    public async Task Reload_serves_a_file_keypaste_would_not_merge_in_the_same_session_and_asks_again()
    {
        await using var agent = await Agent.AttachAsync(this);
        Assert.Equal((AuditMethod.Prompt, "v1"), await agent.RequestAsync(_token));
        var session = _session.SessionId;

        var other = Path.Combine(_fixture.Home, "other.kdbx");

        using (var vault = Vault.Create(other, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "TOKEN", Password = "another-vault" });
            TempVault.Sees(vault, TempVault.Agent);
            vault.Save();
        }

        File.Copy(other, _fixture.Path_, overwrite: true);

        using var shell = new ShellViewModel(_session, _fixture.Home, authority: null, clock: new ManualClock(AppClock.Start));
        await shell.Merging.WaitAsync(_connect, Cancel);

        Assert.True(shell.HasVaultChanged);
        Assert.Contains("could not merge it: the file now holds another vault.", shell.VaultChanged, StringComparison.Ordinal);
        Assert.Equal(AuditMethod.VaultChanged, (await agent.RequestAsync(_token)).Method);

        shell.ReloadCommand.Execute(null);

        Assert.False(shell.HasVaultChanged);
        Assert.Equal(session, _session.SessionId);
        Assert.Equal((AuditMethod.Prompt, "another-vault"), await agent.RequestAsync(_token));
        Assert.Equal(2, _prompt.Asked);
    }

    /// <summary>
    /// The person presses Allow only after the shell merged another program's save, on the prompt the merge withdrew: nothing
    /// leaves, the line says the vault was merged, and the agent's next request is asked about anew (D-0542, D-0620).
    /// </summary>
    [Fact]
    public async Task A_prompt_waiting_when_another_programs_save_is_merged_is_withdrawn_and_releases_nothing()
    {
        await using var agent = await Agent.AttachAsync(this);
        var shown = _prompt.HoldNext();
        var asked = agent.RequestAsync(_token);
        var withdrawal = await shown.WaitAsync(_connect, Cancel);

        using (var writer = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            writer.UpdateEntry(new VaultEntry { GroupPath = "env/dev", Title = "TOKEN", Password = "external" });
            writer.Save();
        }

        using var shell = new ShellViewModel(_session, _fixture.Home, authority: null, clock: new ManualClock(AppClock.Start));
        await shell.Merging.WaitAsync(_connect, Cancel);

        Assert.False(shell.HasVaultChanged);
        Assert.True(withdrawal.IsCancellationRequested);

        _prompt.Allow();
        var refused = await asked.WaitAsync(_connect, Cancel);

        Assert.Equal(AuditMethod.VaultChanged, refused.Method);
        Assert.Null(refused.Value);

        using (var line = LastAuditLine())
        {
            Assert.Equal("denied", line.RootElement.GetProperty("decision").GetString());
            Assert.Equal("vault-changed", line.RootElement.GetProperty("method").GetString());
            Assert.Equal("the vault was merged with another program's save before anybody answered", line.RootElement.GetProperty("reason").GetString());
        }

        Assert.Equal((AuditMethod.Prompt, "external"), await agent.RequestAsync(_token));
        Assert.Equal(2, _prompt.Asked);
    }

    private void Unlock()
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    /// <summary>Does something on the entries screen, as a person would, over the held session.</summary>
    private void OnScreen(Action<EntriesViewModel> act)
    {
        using var countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock(AppClock.Start));
        using var entries = new EntriesViewModel(_session, countdown);
        act(entries);
    }

    /// <summary>The newest audit line, read beside the session holding the log open for writing.</summary>
    private JsonDocument LastAuditLine()
    {
        using var stream = new FileStream(KeypasteHome.AuditPath(_fixture.Home), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return JsonDocument.Parse(reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1]);
    }

    private static EntryDetailViewModel Select(EntriesViewModel entries, string path)
    {
        entries.Selected = entries.Rows.Single(row => row.Path == path);
        return entries.Detail!;
    }

    private static void Relocate(EntriesViewModel entries, string path, string group, string? title = null)
    {
        var row = entries.Rows.Single(candidate => candidate.Path == path);
        entries.Selected = row;
        entries.OrganizeCommand.Execute(null);
        entries.DraftTitle = title ?? row.Title;
        entries.MoveTarget = entries.MoveTargets.Single(node => node.Path == group);
        entries.ConfirmOrganizeCommand.Execute(null);
    }

    private static void RenameGroup(EntriesViewModel entries, string group, string name)
    {
        entries.SelectedGroup = entries.Groups.Single(node => node.Path == group);
        entries.BeginRenameGroupCommand.Execute(null);
        entries.DraftGroupName = name;
        entries.ConfirmRenameGroupCommand.Execute(null);
    }

    /// <summary>An agent attached to the app's session over its real endpoint.</summary>
    private sealed class Agent : IAsyncDisposable
    {
        private readonly ApproverClient _client;
        private readonly string _vault;
        private readonly string _session;

        private Agent(ApproverClient client, string vault, string session)
        {
            _client = client;
            _vault = vault;
            _session = session;
        }

        internal static async Task<Agent> AttachAsync(CurrentStateTests test)
        {
            Assert.NotNull(test._host.Endpoint);
            var client = await ApproverClient.TryConnectAsync(test._host.Endpoint, _connect, Cancel);
            Assert.NotNull(client);

            var attached = await client.AttachAsync(new AttachRequest(test._fixture.Path_), Cancel);
            Assert.True(attached!.Attached);

            return new Agent(client, test._fixture.Path_, attached.Session!);
        }

        internal async Task<(AuditMethod Method, string? Value)> RequestAsync(string entry)
        {
            var reply = await _client.RequestAsync(
                new CredentialRequest
                {
                    Entry = entry,
                    Field = "password",
                    Reason = "deploy",
                    TtlSeconds = 60,
                    Exposure = ["env/**"],
                    ClientLabel = TempVault.Agent,
                    Vault = _vault,
                    Session = _session,
                },
                Cancel);

            Assert.NotNull(reply);
            Assert.Equal(_session, reply.Session);
            return (reply.Method, reply.Value);
        }

        internal async Task<EditReply> EditAsync(TerminalEdit edit)
        {
            var reply = await _client.EditAsync(new EditRequest(edit) { Vault = _vault, Session = _session }, Cancel);

            Assert.NotNull(reply);
            return reply;
        }

        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }

    /// <summary>A person who approves every request put to them, at once unless told to hold the next one.</summary>
    private sealed class ApprovingPrompt : IApprovalChannel
    {
        private readonly TaskCompletionSource _allowed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<CancellationToken>? _held;

        internal int Asked { get; private set; }

        /// <summary>Keeps the next request on screen until <see cref="Allow"/>.</summary>
        /// <returns>What withdraws that request, once it is on screen.</returns>
        internal Task<CancellationToken> HoldNext()
        {
            _held = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _held.Task;
        }

        /// <summary>Presses Allow on the held request, whether or not it was withdrawn meanwhile.</summary>
        internal void Allow() => _allowed.TrySetResult();

        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            await AnswerAsync(cancellationToken).ConfigureAwait(false);
            return ApprovalAnswer.Approved;
        }

        public async ValueTask<ApprovalAnswer> AskAsync(TerminalEditPrompt prompt, CancellationToken cancellationToken)
        {
            await AnswerAsync(cancellationToken).ConfigureAwait(false);
            return ApprovalAnswer.ApprovedOnce;
        }

        private async Task AnswerAsync(CancellationToken cancellationToken)
        {
            Asked++;

            if (Interlocked.Exchange(ref _held, null) is { } held)
            {
                held.SetResult(cancellationToken);
                await _allowed.Task.ConfigureAwait(false);
            }
        }
    }
}
