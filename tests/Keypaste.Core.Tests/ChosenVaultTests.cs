using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The vault agents and the CLI use without <c>--vault</c>, as <c>app.toml</c> keeps it (D-0389).</summary>
public sealed class ChosenVaultTests : IDisposable
{
    private readonly string _home =
        Path.Combine(Path.GetTempPath(), "keypaste-chosen-tests", Guid.NewGuid().ToString("n"));

    private string SettingsFile => KeypasteHome.SettingsPath(_home);

    private string Vault(string name) => Path.Combine(_home, "vaults dir", name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void NothingIsChosen_UntilAVaultIs()
    {
        Assert.Null(ChosenVault.Read(_home));

        Assert.Equal(ChooseOutcome.Chosen, ChosenVault.Choose(_home, Vault("a.kdbx"), onlyIfNone: true));

        Assert.Equal(Vault("a.kdbx"), ChosenVault.Read(_home));
    }

    [Fact]
    public void AFirstChoice_DoesNotReplaceOneAlreadyMade()
    {
        ChosenVault.Choose(_home, Vault("a.kdbx"), onlyIfNone: true);

        Assert.Equal(ChooseOutcome.AlreadyChosen, ChosenVault.Choose(_home, Vault("b.kdbx"), onlyIfNone: true));
        Assert.Equal(ChooseOutcome.AlreadyChosen, ChosenVault.Choose(_home, Vault("a.kdbx"), onlyIfNone: true));
        Assert.Equal(Vault("a.kdbx"), ChosenVault.Read(_home));
    }

    [Fact]
    public void ChoosingAnother_ReplacesIt_AndKeepsEveryOtherPreference()
    {
        AppSettings.Save(SettingsFile, AppSettings.Default with { IdleTimeoutSeconds = 900, Theme = AppTheme.Dark, LockWhenMinimized = true });
        ChosenVault.Choose(_home, Vault("a.kdbx"), onlyIfNone: true);

        Assert.Equal(ChooseOutcome.Chosen, ChosenVault.Choose(_home, Vault("b.kdbx"), onlyIfNone: false));

        var settings = AppSettings.Load(SettingsFile);
        Assert.Equal(Vault("b.kdbx"), settings.Vault);
        Assert.Equal(900, settings.IdleTimeoutSeconds);
        Assert.Equal(AppTheme.Dark, settings.Theme);
        Assert.True(settings.LockWhenMinimized);
    }

    [Fact]
    public void AFileThatDoesNotParse_IsLeftExactlyAsItIs()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(SettingsFile, "[[settings]]\ntheme = \"dark\nvault = ");
        var before = File.ReadAllBytes(SettingsFile);

        Assert.Equal(ChooseOutcome.Unreadable, ChosenVault.Choose(_home, Vault("a.kdbx"), onlyIfNone: false));

        Assert.Equal(before, File.ReadAllBytes(SettingsFile));
        Assert.Null(ChosenVault.Read(_home));
    }

    [Fact]
    public void APathTheFileCannotHold_IsNotRecordedAsSomethingElse()
    {
        Assert.Equal(ChooseOutcome.Unrecordable, ChosenVault.Choose(_home, Vault("quote\"d.kdbx"), onlyIfNone: false));

        Assert.Null(ChosenVault.Read(_home));
    }

    [Fact]
    public void TheFileHoldsThePath_WithForwardSlashes()
    {
        ChosenVault.Choose(_home, Vault("a.kdbx"), onlyIfNone: false);

        var text = File.ReadAllText(SettingsFile);
        Assert.Contains($"vault = \"{Vault("a.kdbx").Replace('\\', '/')}\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AHandWrittenRelativeOrEmptyPath_ReadsAsAFullPathOrNothing()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(SettingsFile, "[[settings]]\nvault = \"\"\n");
        Assert.Null(ChosenVault.Read(_home));

        File.WriteAllText(SettingsFile, "[[settings]]\nvault = \"rel/v.kdbx\"\n");
        Assert.Equal(Path.GetFullPath("rel/v.kdbx"), ChosenVault.Read(_home));
    }
}
