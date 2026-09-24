using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The app's session answers agents from the vault as last saved: an edit made on a screen is the next
/// value released, what it touched is asked about again, and a file another program saved is refused
/// until a person unlocks it again (U.3, D-0317, D-0318).
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
            vault.Save();
        }

        _session = new AppVaultSession(new ManualClock(), home: _fixture.Home);
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

    [Fact]
    public async Task After_another_program_saves_the_vault_nothing_is_released_until_it_is_unlocked_again()
    {
        await using (var agent = await Agent.AttachAsync(this))
        {
            Assert.Equal((AuditMethod.Prompt, "v1"), await agent.RequestAsync(_token));

            using (var writer = Vault.Open(_fixture.Path_, TempVault.Password))
            {
                writer.UpdateEntry(new VaultEntry { GroupPath = "env/dev", Title = "TOKEN", Password = "external" });
                writer.Save();
            }

            var external = File.ReadAllBytes(_fixture.Path_);

            var refused = await agent.RequestAsync(_token);
            Assert.Equal(AuditMethod.VaultChanged, refused.Method);
            Assert.Null(refused.Value);

            OnScreen(entries =>
            {
                var detail = Select(entries, _token);
                detail.EditCommand.Execute(null);
                detail.NewPassword.Type('x');
                detail.SaveCommand.Execute(null);
                Assert.True(detail.IsEditing);
            });

            Assert.Equal(AuditMethod.VaultChanged, (await agent.RequestAsync(_token)).Method);
            Assert.Equal(external, File.ReadAllBytes(_fixture.Path_));
            Assert.Equal(1, _prompt.Asked);
        }

        _session.Lock(VaultLockReason.Manual);
        Unlock();

        await using var after = await Agent.AttachAsync(this);
        Assert.Equal((AuditMethod.Prompt, "external"), await after.RequestAsync(_token));
    }

    private void Unlock()
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    /// <summary>Does something on the entries screen, as a person would, over the held session.</summary>
    private void OnScreen(Action<EntriesViewModel> act)
    {
        using var countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock());
        using var entries = new EntriesViewModel(_session, countdown);
        act(entries);
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
                    Vault = _vault,
                    Session = _session,
                },
                Cancel);

            Assert.NotNull(reply);
            Assert.Equal(_session, reply.Session);
            return (reply.Method, reply.Value);
        }

        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }

    /// <summary>A person who approves every request put to them.</summary>
    private sealed class ApprovingPrompt : IApprovalChannel
    {
        internal int Asked { get; private set; }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Asked++;
            return ValueTask.FromResult(ApprovalAnswer.Approved);
        }
    }
}
