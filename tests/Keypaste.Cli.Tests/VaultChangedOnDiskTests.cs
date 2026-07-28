using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// What the CLI says when something wrote to the vault while it held it open.
/// </summary>
/// <remarks>
/// <para>
/// The branch is unreachable in ordinary use — a command opens, edits and saves in milliseconds —
/// which is exactly why it is worth executing here. A <c>catch</c> nobody has ever run is an
/// assertion about the world rather than a check on it (DECISIONS.md D-0043), and this one carries
/// the sentence a user sees when their data was about to be lost.
/// </para>
/// <para>
/// The second writer is planted through <c>FakeSecretPrompt.OnPrompt</c>, which fires on the entry
/// password prompt — after <c>Vault.Open</c> and before <c>vault.Save</c>. That is a real second
/// writer holding a real second <see cref="Vault"/>, not a corrupted file standing in for one.
/// </para>
/// </remarks>
public sealed class VaultChangedOnDiskTests
{
    internal const string Master = "correct horse battery staple";

    [Fact]
    public void AWriteDuringACommand_IsRefused_AndSaysToRunItAgain()
    {
        using var harness = Seeded();

        harness.Prompt.OnPrompt = prompt =>
        {
            if (!prompt.StartsWith("Password", StringComparison.Ordinal))
            {
                return;
            }

            harness.Prompt.OnPrompt = null;

            using var elsewhere = Vault.Open(harness.VaultPath, Master);
            elsewhere.AddEntry(new VaultEntry { Title = "from-the-terminal", Password = "second" });
            elsewhere.Save();
        };

        harness.Prompt.Enqueue(Master, "first");
        var exit = harness.Run("add", "from-the-window", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("changed while keypaste was writing it", harness.Err, StringComparison.Ordinal);
        Assert.Contains("Nothing was saved", harness.Err, StringComparison.Ordinal);

        // The sentence is only worth anything if it is true, so check the file rather than the text.
        using var reopened = Vault.Open(harness.VaultPath, Master);
        Assert.NotNull(reopened.Find("from-the-terminal"));
        Assert.Null(reopened.Find("from-the-window"));
    }

    /// <summary>
    /// The positive control: without a second writer the same command succeeds.
    /// </summary>
    /// <remarks>
    /// Without this, an <c>add</c> broken for any reason at all would satisfy the test above by
    /// failing for a different one.
    /// </remarks>
    [Fact]
    public void TheSameCommand_WithNobodyElseWriting_Succeeds()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(Master, "first");

        Assert.Equal(CliApp.ExitSuccess, harness.Run("add", "from-the-window", "--vault", harness.VaultPath));

        using var reopened = Vault.Open(harness.VaultPath, Master);
        Assert.NotNull(reopened.Find("from-the-window"));
    }

    private static CliHarness Seeded()
    {
        var harness = new CliHarness();
        harness.SeedVault(Master, ("seed", "seed-password"));
        return harness;
    }
}
