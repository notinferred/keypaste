using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The tests that exist to catch a future well-meaning refactor rather than a present bug.
/// CORE.md law 4.5 makes tests on the secret path mandatory; these are that path's backstop.
/// </summary>
public sealed class SecretHygieneTests
{
    internal const string Master = "sentinel-master-7b21";
    internal const string SentinelPassword = "SENTINEL-PW-9f3c";
    internal const string SentinelUsername = "SENTINEL-USER-4a17";
    internal const string SentinelNotes = "SENTINEL-NOTES-c08e";
    internal const string SentinelUrl = "https://example.invalid/SENTINEL-URL-2d55";

    /// <summary>
    /// Sweeps every verb in every shape that is not <c>--show</c> and asserts no field value
    /// appears anywhere in the output. This is what catches the change that helpfully echoes an
    /// entry back for confirmation, or logs the record it just wrote.
    /// </summary>
    [Theory]
    [InlineData("ls")]
    [InlineData("ls", "--flat")]
    [InlineData("get", "secrets/target")]
    [InlineData("rm", "secrets/target", "--yes")]
    public void NoVerb_LeaksAFieldValue_ToStdoutOrStderr(params string[] verb)
    {
        using var harness = new CliHarness();
        Seed(harness);

        harness.Prompt.Enqueue(Master);
        var args = verb.Concat(["--vault", harness.VaultPath]).ToArray();
        harness.Run(args);

        foreach (var sentinel in new[] { SentinelPassword, SentinelUsername, SentinelNotes, SentinelUrl })
        {
            Assert.DoesNotContain(sentinel, harness.Out, StringComparison.Ordinal);
            Assert.DoesNotContain(sentinel, harness.Err, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The master password must never be echoed, and the buffer the CLI was handed must be
    /// zeroed by the time the command returns. That is what keeps D-0007's zeroing promise
    /// honest one layer up, and it is only assertable because the prompt returns a
    /// <c>SecretBuffer</c> rather than a <see cref="string"/>.
    /// </summary>
    [Fact]
    public void MasterPassword_IsReadWithoutEcho_AndTheBufferIsZeroedAfterwards()
    {
        using var harness = new CliHarness();
        Seed(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("ls", "--vault", harness.VaultPath);

        Assert.DoesNotContain(Master, harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(Master, harness.Err, StringComparison.Ordinal);

        // The master password went through ReadSecret, not the echoing ReadLine.
        Assert.Contains(harness.Prompt.SecretPrompts, p => p.Contains("Master", StringComparison.Ordinal));

        // Inspects the backing array, not just Value — a disposed buffer reports an empty span
        // regardless, so asserting on Value alone would pass even if nothing had been wiped.
        Assert.NotEmpty(harness.Prompt.IssuedSecrets);
        foreach (var issued in harness.Prompt.IssuedSecrets)
        {
            Assert.True(issued.IsZeroed, "a secret buffer handed to the CLI was not zeroed");
        }
    }

    /// <summary>
    /// Prompts are wired to stderr in the real program, not merely by convention in the fakes.
    /// Without this, every other "stdout is clean" assertion would only prove the harness is
    /// clean.
    /// </summary>
    [Fact]
    public void CreateDefault_WiresPromptsToStderr_NotStdout()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var context = CliContext.CreateDefault(stdout, stderr);

        Assert.Same(stderr, context.Stderr);
        Assert.Same(stdout, context.Stdout);

        // A prompt written now must land in stderr. ReadLine is not called, so nothing blocks.
        context.Stderr.Write("probe");
        Assert.Contains("probe", stderr.ToString(), StringComparison.Ordinal);
        Assert.Empty(stdout.ToString());
    }

    private static void Seed(CliHarness harness)
    {
        harness.Prompt.Interactive = false;
        harness.Prompt.Enqueue(Master, Master);
        harness.Run("init", harness.VaultPath);

        harness.Prompt.Enqueue(Master, SentinelPassword);
        harness.Run(
            "add", "secrets/target",
            "--vault", harness.VaultPath,
            "--username", SentinelUsername,
            "--url", SentinelUrl,
            "--notes", SentinelNotes);

        harness.Stdout.GetStringBuilder().Clear();
        harness.Stderr.GetStringBuilder().Clear();
    }
}
