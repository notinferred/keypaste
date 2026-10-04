using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// An entry the app's Projects screen adds to an environment is in the next <c>keypaste run</c>, and
/// one it takes out is not (C.4).
/// </summary>
/// <remarks>
/// The CLI is asked rather than the tags, because the claim is that the screen's tag change is what
/// both front ends read as membership. The mutations this must catch: a tag written for another
/// environment, a removal that leaves one of the entry's tags behind, and a removal that deletes the
/// entry instead.
/// </remarks>
public sealed class EntriesTaggedOnTheProjectsScreenAreWhatTheCliRunsTests
{
    [Fact]
    public void An_entry_added_on_the_screen_is_run_and_one_taken_out_is_not()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());
        var stripe = Entry(fixture.Unlocked, "Stripe", "STRIPE_KEY", "sk_live_screen_7c1e", "env:billing", "env:billing:dev");
        Entry(fixture.Unlocked, "Queue", "QUEUE_KEY", "queue-screen-7c1e");
        fixture.Unlocked.Save();

        using var countdown = new ClipboardCountdown(new FakeClipboard(), TimeProvider.System);
        using var screen = new EnvSetsViewModel(fixture.Session, countdown);
        screen.OpenCommand.Execute("billing");
        var project = screen.OpenProject!;

        project.Environments[0].AddEntry.Execute(null);
        project.EntryFilter = "services/Queue";
        project.ChosenEntry = Assert.Single(project.EntryCandidates);
        project.ConfirmTagChangeCommand.Execute(null);
        Assert.Null(screen.Error);

        project.Environments[0].Members.Single(member => member.Entry == stripe).Remove.Execute(null);
        project.ConfirmTagChangeCommand.Execute(null);
        Assert.Null(screen.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("env", "ls", "billing"));
        Assert.Equal(["dev", "services/Queue", "QUEUE_KEY"], fixture.Cli.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));
        Assert.Equal("queue-screen-7c1e", fixture.Cli.ProcessLauncher.Environment["QUEUE_KEY"]);
        Assert.False(fixture.Cli.ProcessLauncher.Environment.ContainsKey("STRIPE_KEY"));

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "services/Stripe", "--field", "STRIPE_KEY", "--reveal"));
        Assert.Contains("sk_live_screen_7c1e", fixture.Cli.Out, StringComparison.Ordinal);
    }

    private static EntryName Entry(Vault vault, string title, string key, string value, params string[] tags)
    {
        var entry = new EntryName("services", title);
        vault.AddEntry(new VaultEntry { GroupPath = "services", Title = title, Password = title + "-login" });
        Assert.True(vault.SetFields(entry, [new FieldWrite(key, value)]));

        foreach (var tag in tags)
        {
            Assert.True(vault.AddTag(entry, tag));
        }

        return entry;
    }
}
