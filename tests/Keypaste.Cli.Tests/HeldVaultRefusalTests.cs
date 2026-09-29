using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The seven verbs that used to save without the vault's claim are refused, before any password is read,
/// while the desktop app or <c>keypaste agent</c> holds the vault, and the refusal names the holder and
/// what to do next (N.10).
/// </summary>
/// <remarks>
/// The claim here is this test process's own, standing in for the holder, so this is the fast
/// regression; <c>scripts/verify-held-saves.sh</c> is V-N.10's evidence, against the app and the agent
/// themselves.
/// </remarks>
public sealed class HeldVaultRefusalTests : IDisposable
{
    private const string _master = "held-vault-master";

    private readonly CliHarness _harness = new();

    public HeldVaultRefusalTests() => _harness.SeedVault(_master, ("Work/github", "gh-held"));

    public void Dispose() => _harness.Dispose();

    public static TheoryData<string, OwnerKind> Verbs()
    {
        var data = new TheoryData<string, OwnerKind>();

        foreach (var verb in new[] { "add", "rm", "access", "env set", "env rm", "env pull", "import" })
        {
            data.Add(verb, OwnerKind.DesktopApp);
            data.Add(verb, OwnerKind.TerminalAgent);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Verbs))]
    public void A_saving_verb_is_refused_a_held_vault_before_its_password(string verb, OwnerKind holder)
    {
        var args = Arguments(verb);
        var home = KeypasteHome.Resolve(_harness.Environment[KeypasteHome.EnvironmentVariable]);
        var before = File.ReadAllBytes(_harness.VaultPath);
        _harness.Prompt.PromptsSeen.Clear();

        Assert.True(VaultClaim.TryAcquire(home, _harness.VaultPath, holder, out var claim, out var refusal), refusal);

        using (claim)
        {
            _harness.Prompt.Enqueue(_master, _master, "value");
            _harness.AssertExit(CliApp.ExitInternalError, _harness.Run(args));
        }

        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Equal(before, File.ReadAllBytes(_harness.VaultPath));

        var named = new VaultOwner(holder, Environment.ProcessId, _harness.VaultPath).Describe();
        Assert.Contains($"keypaste: this vault is already unlocked in {named}.", _harness.Err, StringComparison.Ordinal);
        Assert.Contains(
            holder == OwnerKind.DesktopApp ? "Make the change there, or run `keypaste lock` and try again." : "Run `keypaste lock` and try again.",
            _harness.Err,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_dry_run_import_reads_a_held_vault_without_the_claim()
    {
        var home = KeypasteHome.Resolve(_harness.Environment[KeypasteHome.EnvironmentVariable]);
        var args = Arguments("import").Append("--dry-run").ToArray();

        Assert.True(VaultClaim.TryAcquire(home, _harness.VaultPath, OwnerKind.DesktopApp, out var claim, out var refusal), refusal);

        using (claim)
        {
            _harness.Prompt.Enqueue("source-held", _master);
            _harness.AssertExit(CliApp.ExitSuccess, _harness.Run(args));
        }

        Assert.Contains("nothing was written (--dry-run)", _harness.Out, StringComparison.Ordinal);
    }

    private string[] Arguments(string verb)
    {
        var vault = new[] { "--vault", _harness.VaultPath };

        return verb switch
        {
            "add" => ["add", "Work/new", .. vault],
            "rm" => ["rm", "Work/github", "--yes", .. vault],
            "access" => ["access", "--password", .. vault],
            "env set" => ["env", "set", "ci", "DEPLOY_KEY=held", .. vault],
            "env rm" => ["env", "rm", "ci", "DEPLOY_KEY", "--yes", .. vault],
            "env pull" => ["env", "pull", "ci", DotEnv(), "--yes", "--keep", .. vault],
            "import" => ["import", Source(), .. vault],
            _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, null),
        };
    }

    private string DotEnv()
    {
        var path = Path.Combine(_harness.Directory, ".env");
        File.WriteAllText(path, "DEPLOY_KEY=held\n");
        return path;
    }

    private string Source()
    {
        var path = Path.Combine(_harness.Directory, "source.kdbx");

        if (!File.Exists(path))
        {
            using var source = Vault.Create(path, "source-held");
            source.AddEntry(new VaultEntry { GroupPath = "Imported", Title = "old", Password = "old-held" });
            source.Save();
        }

        return path;
    }
}
