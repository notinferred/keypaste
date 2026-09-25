using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// A variable added to a profile on the Env Sets screen is what <c>keypaste run -p</c> injects, and
/// a profile the CLI writes is the one the screen shows (D-0347).
/// </summary>
/// <remarks>
/// The mutations this must catch: the screen writing a profile under another group than the CLI
/// resolves, the default profile moving out of the project group, and an edit landing in the
/// profile that was selected before.
/// </remarks>
public sealed class ProfileSetInTheAppIsWhatTheCliRunsTests
{
    private const string _staging = "sk_staging_consistency_7e21";

    [Fact]
    public void ProfileSetInTheAppIsWhatTheCliRuns()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("env", "set", "billing", "DATABASE_URL=dev-db"));
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("env", "set", "billing", "DATABASE_URL=prod-db", "-p", "prod"));
        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());

        using var countdown = new ClipboardCountdown(new FakeClipboard(), TimeProvider.System);
        using var screen = new EnvSetsViewModel(fixture.Session, countdown);
        screen.OpenCommand.Execute("billing");
        var project = screen.OpenProject!;

        Assert.Equal(["dev", "prod"], project.Profiles.Select(profile => profile.Name));
        project.SelectedProfile = "prod";
        Assert.Equal(["DATABASE_URL"], project.Variables.Select(row => row.Key));

        project.SelectedProfile = "staging";
        project.BeginAddCommand.Execute(null);
        project.NewKey = "STRIPE_KEY";
        project.GenerateValue = false;
        foreach (var c in _staging)
        {
            project.NewValue.Type(c);
        }

        project.ConfirmAddCommand.Execute(null);
        Assert.Null(screen.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("env", "ls", "billing", "-p", "staging"));
        Assert.Equal(["STRIPE_KEY"], fixture.Cli.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "-p", "staging", "billing", "--", "deploy"));
        Assert.Equal(_staging, fixture.Cli.ProcessLauncher.Environment["STRIPE_KEY"]);
        Assert.False(fixture.Cli.ProcessLauncher.Environment.ContainsKey("DATABASE_URL"));

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));
        Assert.Equal("dev-db", fixture.Cli.ProcessLauncher.Environment["DATABASE_URL"]);
        Assert.False(fixture.Cli.ProcessLauncher.Environment.ContainsKey("STRIPE_KEY"));
    }
}
