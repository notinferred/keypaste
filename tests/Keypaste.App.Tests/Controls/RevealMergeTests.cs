using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Editing;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>
/// Another program's save merged by the poll, through the shell as it runs: the value on screen goes, whether the
/// merge kept the item, changed it or removed it, and the pane's password is hidden even where the screen keeps its draft.
/// </summary>
public sealed class RevealMergeTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    public static TheoryData<string> Saves => ["kept the item", "changed its password", "removed the item"];

    [Theory]
    [MemberData(nameof(Saves))]
    public Task A_merge_hides_the_value_on_the_screen(string what) => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NobodyAnswers());
#pragma warning restore CA2000
        var session = authority.Session;
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(session, fixture.Home, authority, clipboard: new FakeClipboard(), clock: clock, post: action => Dispatcher.UIThread.Post(action));
        var window = new Window { Width = 1200, Height = 900, Content = new ShellView { DataContext = shell } };
        window.Show();
        WindowInput.Drain();

        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        entries.Selected = entries.Rows.Single(row => row.Title == "example");
        WindowInput.Drain();
        var cell = window.GetVisualDescendants().OfType<RevealedValue>().Single(value => value.Name == "CurrentPassword");
        cell.BeginReveal();
        Assert.True(cell.IsRevealed);

        Elsewhere(fixture, what);

        for (var tick = 0; tick < 5 && shell.Merging.IsCompleted; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            WindowInput.Drain();
        }

        await shell.Merging.WaitAsync(_connect, TestContext.Current.CancellationToken);
        WindowInput.Drain();

        Assert.False(cell.IsRevealed);
        Assert.Null(RevealedValue.Shown);

        if (what == "changed its password")
        {
            cell.BeginReveal();
            Assert.Equal("SENTINEL-CHANGED-ELSEWHERE", cell.Rendered);
            cell.EndReveal();
        }

        window.Content = null;
        window.Close();
    });

    [Fact]
    public Task A_value_shown_behind_a_form_that_held_off_a_merge_goes_when_the_form_closes() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NobodyAnswers());
#pragma warning restore CA2000
        using var shell = Shell(authority, fixture, clock);
        var (window, entries) = Showing(shell);
        var detail = entries.Detail!;
        var cell = window.GetVisualDescendants().OfType<RevealedValue>().Single(value => value.Name == "CurrentPassword");
        detail.BeginAddFieldCommand.Press();
        detail.DraftFieldName = "HALF_TYPED";
        WindowInput.Drain();

        Elsewhere(fixture, "changed its password");
        await Merged(shell, clock);
        Assert.False(cell.IsRevealed);

        cell.BeginReveal();
        Assert.True(cell.IsRevealed);
        detail.CancelAddFieldCommand.Press();
        WindowInput.Drain();

        Assert.False(cell.IsRevealed);
        Assert.Null(RevealedValue.Shown);

        window.Content = null;
        window.Close();
    });

    [Fact]
    public Task A_terminal_edit_hides_the_value_on_the_screen() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new AllowsEdits());
#pragma warning restore CA2000
        using var shell = Shell(authority, fixture, clock);
        var (window, _) = Showing(shell);
        var cell = window.GetVisualDescendants().OfType<RevealedValue>().Single(value => value.Name == "CurrentPassword");
        cell.BeginReveal();
        Assert.True(cell.IsRevealed);

        var edited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        authority.TerminalEdited += (_, _) => edited.TrySetResult();
        var token = TestContext.Current.CancellationToken;
        var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
        await using (var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, token))
        {
            Assert.NotNull(client);
            var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), token);
            var edit = new EditRequest(new SetPassword(new EntryName(string.Empty, "example"), NewSecret.Of("SENTINEL-FROM-THE-TERMINAL"))) { Vault = fixture.Path_, Session = attached!.Session! };
            Assert.NotNull(await client.EditAsync(edit, token));
        }

        await edited.Task.WaitAsync(_connect, token);
        WindowInput.Drain();

        Assert.False(cell.IsRevealed);
        Assert.Null(RevealedValue.Shown);

        window.Content = null;
        window.Close();
    });

    private static ShellViewModel Shell(AppAuthority authority, TempVault fixture, ManualClock clock)
    {
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        return new ShellViewModel(authority.Session, fixture.Home, authority, clipboard: new FakeClipboard(), clock: clock, post: action => Dispatcher.UIThread.Post(action));
    }

    private static (Window Window, EntriesViewModel Entries) Showing(ShellViewModel shell)
    {
        var window = new Window { Width = 1200, Height = 900, Content = new ShellView { DataContext = shell } };
        window.Show();
        WindowInput.Drain();
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        entries.Selected = entries.Rows.Single(row => row.Title == "example");
        WindowInput.Drain();
        return (window, entries);
    }

    private static async Task Merged(ShellViewModel shell, ManualClock clock)
    {
        for (var tick = 0; tick < 5 && shell.Merging.IsCompleted; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            WindowInput.Drain();
        }

        await shell.Merging.WaitAsync(_connect, TestContext.Current.CancellationToken);
        WindowInput.Drain();
    }

    private static void Elsewhere(TempVault fixture, string what)
    {
        using var other = Vault.Open(fixture.Path_, TempVault.Password);

        switch (what)
        {
            case "kept the item":
                other.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "ELSEWHERE", Password = "y" });
                break;
            case "changed its password":
                other.UpdateEntry(new VaultEntry { Title = "example", Password = "SENTINEL-CHANGED-ELSEWHERE" });
                break;
            default:
                Assert.Equal(DeletionOutcome.Recycled, other.RemoveEntry(new EntryName(string.Empty, "example"), out var recycled));
                Assert.True(other.PurgeRecycled(recycled));
                break;
        }

        other.Save();
    }

    private sealed class AllowsEdits : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) => ValueTask.FromResult(ApprovalAnswer.Denied);

        public ValueTask<ApprovalAnswer> AskAsync(TerminalEditPrompt prompt, CancellationToken cancellationToken) => ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
    }

    private sealed class NobodyAnswers : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) => ValueTask.FromResult(ApprovalAnswer.Denied);
    }
}
