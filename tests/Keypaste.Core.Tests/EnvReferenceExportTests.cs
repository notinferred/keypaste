using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A profile written as references by one Core operation, never over a vault (F.55).</summary>
public sealed class EnvReferenceExportTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-reference-export-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_profile_is_written_as_references_and_no_value()
    {
        using var vault = Seeded();
        var target = Path.Combine(_directory, EnvReferenceFile.FileName);

        var export = EnvReferenceExport.Run(vault, "billing", "dev", target, replace: false);

        Assert.Equal(EnvReferenceExportOutcome.Written, export.Outcome);
        Assert.Equal(1, export.Count);
        var written = File.ReadAllText(target);
        Assert.Contains("API_KEY=kp://billing/dev/API_KEY", written, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_live_secret", written, StringComparison.Ordinal);
    }

    [Fact]
    public void The_vault_itself_and_any_other_vault_are_never_written_over()
    {
        using var vault = Seeded();
        var other = Path.Combine(_directory, "other.kdbx");
        using (var created = Vault.Create(other, _master))
        {
            created.Save();
        }

        var before = File.ReadAllBytes(other);

        Assert.Equal(VaultOverwrite.TheVault, EnvReferenceExport.Run(vault, "billing", "dev", vault.Path, replace: true).Overwrite);
        Assert.Equal(VaultOverwrite.AnotherVault, EnvReferenceExport.Run(vault, "billing", "dev", other, replace: true).Overwrite);
        Assert.Equal(before, File.ReadAllBytes(other));
        Assert.Equal(VaultOverwrite.None, VaultOverwriteRule.Check(vault.Path, Path.Combine(_directory, "free.env")));
    }

    [Fact]
    public void A_missing_profile_and_an_existing_file_are_refused()
    {
        using var vault = Seeded();
        var target = Path.Combine(_directory, "taken.env");
        File.WriteAllText(target, "KEEP=1\n");

        Assert.Equal(EnvReferenceExportOutcome.NoProject, EnvReferenceExport.Run(vault, "nothing", "dev", null, replace: false).Outcome);
        Assert.Equal(EnvReferenceExportOutcome.NoProfile, EnvReferenceExport.Run(vault, "billing", "qa", null, replace: false).Outcome);
        Assert.Equal(EnvReferenceExportOutcome.Unwritable, EnvReferenceExport.Run(vault, "billing", "dev", target, replace: false).Outcome);
        Assert.Equal("KEEP=1\n", File.ReadAllText(target));
    }

    private Vault Seeded()
    {
        var vault = Vault.Create(Path.Combine(_directory, "vault.kdbx"), _master);
        Assert.Null(new EnvStore(vault).Set("billing", "dev", "API_KEY", "sk_live_secret").Refusal);
        vault.Save();
        return vault;
    }
}
