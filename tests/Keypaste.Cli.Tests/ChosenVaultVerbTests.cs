using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The CLI uses <c>--vault</c>, then <c>KEYPASTE_VAULT</c>, then the vault chosen in <c>app.toml</c>,
/// and <c>keypaste use</c> shows and changes that choice (D-0389).
/// </summary>
public sealed class ChosenVaultVerbTests
{
    private const string _master = VerbTests.Master;

    private static string Home(CliHarness harness) => harness.Environment[KeypasteHome.EnvironmentVariable];

    private static string Missing(CliHarness harness, string name) => Path.Combine(harness.Directory, name);

    private static CliHarness Seeded()
    {
        var harness = new CliHarness();
        harness.SeedVault(_master, ("solo", "solo-secret"));
        return harness;
    }

    [Fact]
    public void TheChosenVault_IsUsedWhenNothingNamesOne()
    {
        using var harness = Seeded();
        ChosenVault.Choose(Home(harness), harness.VaultPath, onlyIfNone: false);

        harness.Prompt.Enqueue(_master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("get", "solo", "--show"));

        Assert.Equal("solo-secret", harness.Out.TrimEnd());
    }

    [Fact]
    public void TheEnvironment_WinsOverTheChosenVault()
    {
        using var harness = Seeded();
        ChosenVault.Choose(Home(harness), harness.VaultPath, onlyIfNone: false);
        harness.Environment[VaultLocator.EnvironmentVariable] = Missing(harness, "from-env.kdbx");

        Assert.Equal(CliApp.ExitNotFound, harness.Run("ls"));
        Assert.Contains("from-env.kdbx", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFlag_WinsOverTheEnvironmentAndTheChosenVault()
    {
        using var harness = Seeded();
        ChosenVault.Choose(Home(harness), Missing(harness, "chosen.kdbx"), onlyIfNone: false);
        harness.Environment[VaultLocator.EnvironmentVariable] = Missing(harness, "from-env.kdbx");

        harness.Prompt.Enqueue(_master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("get", "solo", "--show", "--vault", harness.VaultPath));

        Assert.Equal("solo-secret", harness.Out.TrimEnd());
    }

    [Fact]
    public void WithNoVaultAnywhere_TheErrorNamesTheNextStep()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitUsageError, harness.Run("ls"));

        Assert.Contains("keypaste use <path>", harness.Err, StringComparison.Ordinal);
        Assert.Contains("keypaste app", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Init_WithNoVaultAnywhere_AsksForThePathToCreate()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitUsageError, harness.Run("init"));

        Assert.Contains("keypaste init <path>", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("keypaste use", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Use_ChoosesAnExistingVault_AndPrintsIt()
    {
        using var harness = Seeded();

        Assert.Equal(CliApp.ExitSuccess, harness.Run("use", harness.VaultPath));
        Assert.Contains($"agents and the CLI now use {harness.VaultPath}", harness.Err, StringComparison.Ordinal);
        Assert.Equal(harness.VaultPath, ChosenVault.Read(Home(harness)));

        using var again = new CliHarness();
        again.Environment[KeypasteHome.EnvironmentVariable] = Home(harness);
        Assert.Equal(CliApp.ExitSuccess, again.Run("use"));
        Assert.Equal(harness.VaultPath, again.Out.TrimEnd());
    }

    [Fact]
    public void Use_KeepsTheOtherPreferences()
    {
        using var harness = Seeded();
        AppSettings.Save(KeypasteHome.SettingsPath(Home(harness)), AppSettings.Default with { IdleTimeoutSeconds = 900, Theme = AppTheme.Light });

        Assert.Equal(CliApp.ExitSuccess, harness.Run("use", harness.VaultPath));

        var settings = AppSettings.Load(KeypasteHome.SettingsPath(Home(harness)));
        Assert.Equal(900, settings.IdleTimeoutSeconds);
        Assert.Equal(AppTheme.Light, settings.Theme);
    }

    [Fact]
    public void Use_RefusesAFileThatIsNotThere_AndChoosesNothing()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitNotFound, harness.Run("use", Missing(harness, "nope.kdbx")));

        Assert.Contains("keypaste init", harness.Err, StringComparison.Ordinal);
        Assert.Null(ChosenVault.Read(Home(harness)));
    }

    [Fact]
    public void Use_WithNothingChosen_SaysHowToChoose()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitNotFound, harness.Run("use"));

        Assert.Empty(harness.Out);
        Assert.Contains("keypaste use <path>", harness.Err, StringComparison.Ordinal);
    }
}
