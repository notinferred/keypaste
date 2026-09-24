using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// A <c>.env</c> imported on the app's Env Sets screen is exactly what the next <c>keypaste run</c>
/// injects (V-E.1b).
/// </summary>
/// <remarks>
/// The CLI is asked rather than the file, because the claim is that both front ends read the import
/// under one convention. The mutations this must catch: an import written under another group, a
/// value changed on the way in, and an import that writes a variable the file did not name.
/// </remarks>
public sealed class ImportedDotEnvIsVisibleToTheCliTests : IDisposable
{
    private const string _first = "sk_live_imported_4e1b+/=";
    private const string _second = "postgres://u:p@db/app?x=1";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-import-cli-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_dotenv_imported_in_the_app_is_what_keypaste_run_injects()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        Assert.Equal(CliApp.ExitSuccess, fixture.RunAnswering(["the-old-value"], "env", "set", "billing", "STRIPE_KEY"));
        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());

        var file = Path.Combine(_directory, ".env");
        File.WriteAllText(file, $"STRIPE_KEY=\"{_first}\"\nexport DATABASE_URL={_second}\n");

        using var countdown = new ClipboardCountdown(new FakeClipboard(), TimeProvider.System);
        using var screen = new EnvSetsViewModel(fixture.Session, countdown);
        screen.OpenCommand.Execute("billing");
        var import = screen.OpenProject!.Import;

        import.Preview(file);
        Assert.True(import.IsPreviewing, screen.Error);
        import.ConfirmCommand.Execute(null);
        Assert.Null(screen.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("env", "ls", "billing"));
        Assert.Equal(["DATABASE_URL", "STRIPE_KEY"], fixture.Cli.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));
        Assert.Equal(_first, fixture.Cli.ProcessLauncher.Environment["STRIPE_KEY"]);
        Assert.Equal(_second, fixture.Cli.ProcessLauncher.Environment["DATABASE_URL"]);
    }
}
