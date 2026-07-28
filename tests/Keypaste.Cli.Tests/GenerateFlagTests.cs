using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>--generate</c> on the two verbs that store a secret.
/// </summary>
/// <remarks>
/// docs/PRODUCT.md law 4.2 is why these exist before the desktop app grows a Generate button: the feature
/// has to be in the CLI first, and "in the CLI" means driven end to end through the shipped verb,
/// not through <see cref="PasswordGenerator"/> with a command wrapped around it.
/// </remarks>
public sealed class GenerateFlagTests
{
    /// <summary>
    /// <c>internal</c> rather than <c>private</c>, because <c>.editorconfig</c> applies
    /// <c>_camelCase</c> to every private field including constants, so this repository has no
    /// <c>private const</c> anywhere.
    /// </summary>
    internal const string Master = "correct horse battery staple";

    [Fact]
    public void Add_Generate_StoresATwentyCharacterPassword_WithoutPrompting()
    {
        using var harness = Seeded();

        // Only the master password is enqueued. A --generate that still prompted would drain an
        // empty queue and fail, which is the point of not enqueueing a second answer.
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("add", "svc/api", "--generate", "--vault", harness.VaultPath));

        Assert.Equal(20, Read(harness, "svc/api").Length);
    }

    [Fact]
    public void Add_Generate_SaysHowLongTheValueIs_AndNotWhatItIs()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(Master);
        harness.Run("add", "svc/api", "--generate", "--length", "32", "--vault", harness.VaultPath);

        Assert.Contains("32-character password generated", harness.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(PasswordGenerator.MinimumLength)]
    [InlineData(64)]
    public void Length_IsHonoured(int length)
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(Master);
        harness.Run(
            "add", "svc/api",
            "--generate", "--length", length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--vault", harness.VaultPath);

        Assert.Equal(length, Read(harness, "svc/api").Length);
    }

    [Fact]
    public void NoSymbols_LeavesOnlyLettersAndDigits()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(Master);
        harness.Run("add", "svc/api", "--generate", "--length", "128", "--no-symbols", "--vault", harness.VaultPath);

        var value = Read(harness, "svc/api");

        Assert.All(value, c => Assert.True(char.IsAsciiLetterOrDigit(c), $"'{c}' is not a letter or a digit"));

        // The paired half: a 128-character draw over letters and digits contains all three classes,
        // so this also catches --no-symbols being read as "digits only".
        Assert.Contains(value, char.IsAsciiDigit);
        Assert.Contains(value, char.IsAsciiLetterLower);
        Assert.Contains(value, char.IsAsciiLetterUpper);
    }

    [Fact]
    public void NoLookalikes_LeavesOutThoseFiveCharacters()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(Master);
        harness.Run("add", "svc/api", "--generate", "--length", "200", "--no-lookalikes", "--vault", harness.VaultPath);

        foreach (var lookalike in PasswordGenerator.Lookalikes)
        {
            Assert.DoesNotContain(lookalike, Read(harness, "svc/api"));
        }
    }

    [Fact]
    public void EnvSet_Generate_StoresAValueUnderTheProject()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(Master);
        Assert.Equal(
            CliApp.ExitSuccess,
            harness.Run("env", "set", "billing", "STRIPE_KEY", "--generate", "--vault", harness.VaultPath));

        Assert.Equal(20, Read(harness, "env/billing/STRIPE_KEY").Length);
    }

    /// <summary>
    /// A shaping flag on its own is a mistake, not a silent no-op.
    /// </summary>
    /// <remarks>
    /// Somebody who typed <c>--length 32</c> and got an interactive prompt has been ignored. This
    /// is the failure mode of every "unknown option we quietly tolerate" design.
    /// </remarks>
    [Theory]
    [InlineData("--length", "32")]
    [InlineData("--no-symbols")]
    [InlineData("--no-lookalikes")]
    public void AShapingFlagWithoutGenerate_IsAUsageError(params string[] flags)
    {
        using var harness = Seeded();

        var args = new[] { "add", "svc/api" }.Concat(flags).Concat(["--vault", harness.VaultPath]).ToArray();

        Assert.Equal(CliApp.ExitUsageError, harness.Run(args));
        Assert.Contains("--generate", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvSet_GenerateAndAnInlineValue_IsAUsageError()
    {
        using var harness = Seeded();

        Assert.Equal(
            CliApp.ExitUsageError,
            harness.Run("env", "set", "billing", "STRIPE_KEY=literal", "--generate", "--vault", harness.VaultPath));
    }

    [Theory]
    [InlineData("7")]
    [InlineData("257")]
    [InlineData("twenty")]
    [InlineData("-4")]
    public void ALengthOutsideTheBounds_IsAUsageError(string length)
    {
        using var harness = Seeded();

        Assert.Equal(
            CliApp.ExitUsageError,
            harness.Run("add", "svc/api", "--generate", "--length", length, "--vault", harness.VaultPath));
    }

    [Fact]
    public void Help_MentionsTheFlags()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitSuccess, harness.Run("add", "--help"));
        Assert.Contains("--generate", harness.Out, StringComparison.Ordinal);
    }

    private static CliHarness Seeded()
    {
        var harness = new CliHarness();
        harness.Prompt.Interactive = false;
        harness.Prompt.Enqueue(Master, Master);
        harness.Run("init", harness.VaultPath);
        harness.Stdout.GetStringBuilder().Clear();
        harness.Stderr.GetStringBuilder().Clear();
        return harness;
    }

    private static string Read(CliHarness harness, string entryPath)
    {
        harness.Stdout.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);

        Assert.Equal(
            CliApp.ExitSuccess,
            harness.Run("get", entryPath, "--show", "--vault", harness.VaultPath));

        return harness.Out.Trim();
    }
}
