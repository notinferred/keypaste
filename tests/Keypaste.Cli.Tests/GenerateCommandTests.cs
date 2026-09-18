using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste generate --words N</c>, the one verb that prints a secret and stores nothing.
/// </summary>
/// <remarks>
/// <para>
/// V-V.6 asks that it print <em>only</em> the passphrase on stdout, which is stricter than
/// printing the passphrase: a provenance line, a prompt or a trailing note on the same stream
/// would all satisfy the loose reading and break
/// <c>keypaste generate --words 6 &gt; passphrase.txt</c>. So the assertion is on the whole of
/// stdout, not on a substring of it.
/// </para>
/// <para>
/// The other half is that this verb cannot be made to emit a secret by accident: no argument
/// list shorter than the intended one produces a passphrase, which is why <c>--words</c> is
/// required rather than defaulted (DECISIONS.md D-0237).
/// </para>
/// </remarks>
public sealed class GenerateCommandTests
{
    [Fact]
    public void Words_PrintsOnlyThePassphraseOnStdout()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitSuccess, harness.Run("generate", "--words", "6"));

        var lines = harness.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines);

        var pieces = lines[0].Trim().Split(PasswordGenerator.DefaultSeparator);
        var known = VendoredWords();

        Assert.Equal(6, pieces.Length);
        Assert.All(pieces, piece => Assert.Contains(piece, known));
    }

    /// <summary>
    /// What the passphrase is made of is on stderr, where a redirect does not take it.
    /// </summary>
    /// <remarks>
    /// This is V.6's "stated where a person choosing a count can read them": the list, its size
    /// and the entropy the chosen count buys. It must not restate the value, or the split-stream
    /// discipline above buys nothing.
    /// </remarks>
    [Fact]
    public void Words_PutsTheListAndTheBitsOnStderr()
    {
        using var harness = new CliHarness();

        harness.Run("generate", "--words", "6");

        Assert.Contains(WordList.Provenance, harness.Err, StringComparison.Ordinal);
        Assert.Contains("7,776", harness.Err, StringComparison.Ordinal);
        Assert.Contains("78 bits", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Out.Trim(), harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Words_SaysThatNothingWasStored()
    {
        using var harness = new CliHarness();

        harness.Run("generate", "--words", "6");

        Assert.Contains("Nothing was stored", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// The verb needs no vault, no master password and no prompt.
    /// </summary>
    /// <remarks>
    /// The harness's prompt queue is left empty on purpose: a verb that reached for a master
    /// password would drain it and fail. Nothing is enqueued and no vault is created, so this also
    /// covers the case of somebody choosing a passphrase before they have a vault to put it in.
    /// </remarks>
    [Fact]
    public void Words_NeedsNoVaultAndPromptsForNothing()
    {
        using var harness = new CliHarness();
        harness.Prompt.Interactive = false;

        Assert.Equal(CliApp.ExitSuccess, harness.Run("generate", "--words", "6"));
        Assert.False(File.Exists(harness.VaultPath));
    }

    [Fact]
    public void BareGenerate_IsAUsageErrorAndPrintsNoSecret()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitUsageError, harness.Run("generate"));
        Assert.Empty(harness.Out);
        Assert.Contains("--words", harness.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("33")]
    [InlineData("0")]
    [InlineData("six")]
    [InlineData("-4")]
    public void AWordCountOutsideTheBounds_IsAUsageErrorWithAnEmptyStdout(string words)
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitUsageError, harness.Run("generate", "--words", words));
        Assert.Empty(harness.Out);
        Assert.NotEmpty(harness.Err);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("")]
    public void ASeparatorTheWordsUseOrCannotBeOne_IsAUsageError(string separator)
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitUsageError, harness.Run("generate", "--words", "6", "--separator", separator));
        Assert.Empty(harness.Out);
    }

    [Fact]
    public void Separator_IsHonoured()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitSuccess, harness.Run("generate", "--words", "7", "--separator", "_"));

        var value = harness.Out.Trim();

        Assert.Equal(7, value.Split('_').Length);
        Assert.DoesNotContain(PasswordGenerator.DefaultSeparator, value);
    }

    [Fact]
    public void AnOperand_IsAUsageError()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitUsageError, harness.Run("generate", "6"));
        Assert.Empty(harness.Out);
    }

    [Fact]
    public void Help_SaysNothingIsStoredAndNamesTheBounds()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitSuccess, harness.Run("generate", "--help"));
        Assert.Contains("not stored", harness.Out, StringComparison.Ordinal);
        Assert.Contains("--words", harness.Out, StringComparison.Ordinal);
        Assert.Contains("32", harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTopLevelUsage_ListsTheVerb()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitSuccess, harness.Run("help"));
        Assert.Contains("generate --words", harness.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two invocations do not print the same passphrase.
    /// </summary>
    /// <remarks>
    /// V-V.6's "two generators do not repeat" clause, at the surface a person actually uses. It is
    /// a weak assertion on its own — two draws of 78 bits colliding would be remarkable — and it
    /// is here because a generator that seeded itself per process would fail exactly this and
    /// nothing else in the suite.
    /// </remarks>
    [Fact]
    public void TwoInvocations_DoNotPrintTheSamePassphrase()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 20; i++)
        {
            using var harness = new CliHarness();
            harness.Run("generate", "--words", "6");
            Assert.True(seen.Add(harness.Out.Trim()), "two invocations printed the same passphrase");
        }
    }

    /// <summary>The words, read from the vendored file rather than from the shipped list.</summary>
    private static HashSet<string> VendoredWords()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            Assert.False(string.IsNullOrEmpty(parent), "this test must run from inside a checkout");
            directory = parent!;
        }

        return File
            .ReadAllLines(Path.Combine(directory, "third_party", "eff-large-wordlist", "eff_large_wordlist.txt"))
            .Where(line => line.Length > 0)
            .Select(line => line[(line.IndexOf('\t', StringComparison.Ordinal) + 1)..])
            .ToHashSet(StringComparer.Ordinal);
    }
}
