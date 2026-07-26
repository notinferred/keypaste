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
    [InlineData("env", "ls")]
    [InlineData("env", "ls", "hygiene")]
    [InlineData("env", "rm", "hygiene", "API_KEY", "--yes")]
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
    /// <c>env set</c> is swept separately because it is the one verb that is handed a value
    /// rather than reading one, and both of its input forms have to stay silent about it. The
    /// <c>KEY=value</c> form is the likelier accident: the value is right there in the arguments,
    /// which makes echoing it back in a confirmation message feel harmless.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnvSet_NeverEchoesTheValue_FromEitherInputForm(bool inlineValue)
    {
        using var harness = new CliHarness();
        Seed(harness);

        harness.Prompt.Enqueue(Master);
        if (!inlineValue)
        {
            harness.Prompt.Enqueue(SentinelPassword);
        }

        var exit = harness.Run(
            "env", "set", "hygiene",
            inlineValue ? "FRESH_KEY=" + SentinelPassword : "FRESH_KEY",
            "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.DoesNotContain(SentinelPassword, harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelPassword, harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>env pull</c> handles more values at once than any other verb, and it is the only one
    /// that reports on input it refused. Both halves are swept.
    /// </summary>
    /// <remarks>
    /// The second half is the one that matters. The obvious phrasing of "line 3: unterminated
    /// quote" includes the line, and on a malformed <c>.env</c> the line is the secret — so a
    /// diagnostic is the likeliest place for a value to escape, not the summary.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnvPull_NeverEchoesAValue_FromTheFileOrFromADiagnostic(bool wellFormed)
    {
        using var harness = new CliHarness();
        Seed(harness);

        var body = wellFormed
            ? $"GOOD={SentinelPassword}\nOTHER={SentinelNotes}\n"
            : $"BAD-NAME={SentinelPassword}\nno equals {SentinelUsername}\nOPEN=\"{SentinelNotes}\n";

        var path = Path.Combine(harness.Directory, "sweep.env");
        File.WriteAllText(path, body);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "pull", "hygiene", path, "--yes", "--keep", "--vault", harness.VaultPath);

        foreach (var sentinel in new[] { SentinelPassword, SentinelUsername, SentinelNotes, SentinelUrl })
        {
            Assert.DoesNotContain(sentinel, harness.Out, StringComparison.Ordinal);
            Assert.DoesNotContain(sentinel, harness.Err, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <c>env export</c> is the one verb whose job is to put secrets somewhere readable, so the
    /// sweep asks a narrower question: they belong in the file, and nowhere else.
    /// </summary>
    /// <remarks>
    /// A dedicated fact rather than a row in the theory above, for two reasons. The theory appends
    /// <c>--vault</c> and cannot carry a per-harness output path; and <c>--stdout</c> is the second
    /// documented exception after <c>get --show</c> — there the secret on stdout <em>is</em> the
    /// command, so sweeping it would assert the opposite of what it promises. What is asserted for
    /// that form instead, in <c>EnvExportTests</c>, is that nothing leaks onto stderr, which is
    /// what keeps the pipe usable.
    /// </remarks>
    [Fact]
    public void EnvExport_PutsTheValuesInTheFile_AndNowhereOnTheTerminal()
    {
        using var harness = new CliHarness();
        Seed(harness);

        var path = Path.Combine(harness.Directory, "sweep.env");

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run(
            "env", "export", "hygiene", path, "--dotenv", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);

        // The file is the point of the command, so it had better be in there.
        Assert.Contains(SentinelPassword, File.ReadAllText(path), StringComparison.Ordinal);

        Assert.DoesNotContain(SentinelPassword, harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelPassword, harness.Err, StringComparison.Ordinal);
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

        // An env variable whose value is the same sentinel, so `env ls` and `env rm` are swept
        // against a real secret rather than an empty project.
        harness.Prompt.Enqueue(Master, SentinelPassword);
        harness.Run("env", "set", "hygiene", "API_KEY", "--vault", harness.VaultPath);

        harness.Stdout.GetStringBuilder().Clear();
        harness.Stderr.GetStringBuilder().Clear();
    }
}
