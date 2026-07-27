using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste log</c> is the command an operator reaches for when something already looks wrong, so
/// what it does when the log is missing, filtered, or edited matters more than what it does when
/// everything is fine.
/// </summary>
/// <remarks>
/// Records are written through the real <see cref="AuditLog"/> rather than by writing lines by hand:
/// a table built from a file this suite composed itself would prove nothing about the file the
/// bridge actually writes, and the chain assertions would be checking their own arithmetic.
/// </remarks>
public sealed class LogVerbTests : IDisposable
{
    private static readonly string _escape = "\u001b[31mred";

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private string LogPath => Path.Combine(_cli.Directory, ".keypaste", "audit.jsonl");

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AuditRecord Record(
        string client,
        string entry,
        AuditDecision decision,
        AuditMethod method) => new()
        {
            Tool = "request_credential",
            Client = new AuditClient(client, "1.0", client),
            Args = AuditArgs.ForCredentialRequest(entry, "password", 900, "deploy the billing service"),
            Decision = decision,
            Method = method,
            Reason = decision == AuditDecision.Granted ? "a person approved this request" : "nobody was asked",
            Exposure = ["env/**"],
        };

    /// <summary>Writes records at a chosen moment, through the real writer.</summary>
    private void Write(DateTimeOffset at, params AuditRecord[] records)
    {
        _cli.Environment[KeypasteHome.EnvironmentVariable] = Path.Combine(_cli.Directory, ".keypaste");

        Assert.True(AuditLog.TryOpen(LogPath, new FixedClock(at), out var log, out var error), error);

        using (log)
        {
            foreach (var record in records)
            {
                Assert.True(log.TryAppend(record, out var appendError), appendError);
            }
        }
    }

    private void Seed() => Write(
        new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero),
        Record("claude-code", "env/dev/STRIPE_KEY", AuditDecision.Granted, AuditMethod.Prompt),
        Record("claude-desktop", "env/dev/DB_URL", AuditDecision.Denied, AuditMethod.OutOfScope));

    private void Tamper()
    {
        var text = File.ReadAllText(LogPath);
        File.WriteAllText(
            LogPath,
            text.Replace("\"decision\":\"denied\"", "\"decision\":\"granted\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// A machine no agent has used has an empty answer, not an error. The same absence is an error
    /// for <c>verify</c>, and the difference is deliberate.
    /// </summary>
    [Fact]
    public void WithNoLogYet_ItSaysSoAndSucceeds()
    {
        _cli.Environment[KeypasteHome.EnvironmentVariable] = Path.Combine(_cli.Directory, ".keypaste");

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log"));
        Assert.Contains("No audit log at", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTableNamesTheClientTheEntryTheDecisionAndTheMethod()
    {
        Seed();

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log"));

        Assert.Contains("2026-07-26 14:00:00", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("claude-code", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("env/dev/STRIPE_KEY", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("granted", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("prompt", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("2 records", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void DeniedShowsOnlyTheRefusals_AndSaysThatItIsFiltered()
    {
        Seed();

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log", "--denied"));

        Assert.Contains("env/dev/DB_URL", _cli.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("STRIPE_KEY", _cli.Out, StringComparison.Ordinal);

        // A filtered view that looked like the whole log would be a view that can prove anything.
        Assert.Contains("1 record of 2", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("refused calls only", _cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// A substring, and case-insensitive. Exact matching would let somebody type <c>claude</c>, see
    /// an empty table, and conclude nothing happened.
    /// </summary>
    [Fact]
    public void ClientMatchesPartOfTheName()
    {
        Seed();

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log", "--client", "DESKTOP"));

        Assert.Contains("env/dev/DB_URL", _cli.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("STRIPE_KEY", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void SinceTakesASpanAndCutsAtIt()
    {
        _cli.Clock.Now = new DateTimeOffset(2026, 7, 26, 15, 0, 0, TimeSpan.Zero);

        Write(
            new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero),
            Record("claude-code", "env/dev/OLD_KEY", AuditDecision.Granted, AuditMethod.Prompt));

        Write(
            new DateTimeOffset(2026, 7, 26, 14, 30, 0, TimeSpan.Zero),
            Record("claude-code", "env/dev/NEW_KEY", AuditDecision.Granted, AuditMethod.Prompt));

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log", "--since", "2h"));

        Assert.Contains("NEW_KEY", _cli.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("OLD_KEY", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void SinceAlsoTakesADate()
    {
        _cli.Clock.Now = new DateTimeOffset(2026, 7, 26, 15, 0, 0, TimeSpan.Zero);

        Write(
            new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero),
            Record("claude-code", "env/dev/OLD_KEY", AuditDecision.Granted, AuditMethod.Prompt));

        Write(
            new DateTimeOffset(2026, 7, 26, 14, 30, 0, TimeSpan.Zero),
            Record("claude-code", "env/dev/NEW_KEY", AuditDecision.Granted, AuditMethod.Prompt));

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log", "--since", "2026-07-25"));

        Assert.Contains("NEW_KEY", _cli.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("OLD_KEY", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ASinceThatIsNotAMoment_IsAUsageError()
    {
        Seed();

        _cli.AssertExit(CliApp.ExitUsageError, _cli.Run("log", "--since", "last tuesday"));
        Assert.Contains("is not a moment", _cli.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AFilterThatMatchesNothing_SaysSoRatherThanPrintingNothing()
    {
        Seed();

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log", "--client", "nobody"));
        Assert.Contains("No records matched", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownOption_IsAUsageError()
    {
        Seed();

        _cli.AssertExit(CliApp.ExitUsageError, _cli.Run("log", "--everything"));
        Assert.Contains("keypaste log:", _cli.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// Text an agent or a vault chose reaches a terminal here, so it arrives with its mechanism
    /// removed. THREATS.md T-1 and T-2 name this table as the place that matters.
    /// </summary>
    /// <remarks>
    /// The escape is planted in the two fields that reach the reader intact — the name a client
    /// asserts, which nothing sanitizes on the way in, and an <c>entry</c> built directly rather
    /// than through <see cref="AuditArgs.ForCredentialRequest"/>. Planting it somewhere the writer
    /// already scrubs would test the writer twice and the reader not at all.
    /// </remarks>
    [Fact]
    public void AnEscapeSequenceInARecord_ReachesTheTerminalInert()
    {
        Write(
            new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero),
            new AuditRecord
            {
                Tool = "request_credential",
                Client = new AuditClient(_escape, "1.0", null),
                Args = new AuditArgs { Entry = _escape, Field = "password" },
                Decision = AuditDecision.Denied,
                Method = AuditMethod.OutOfScope,
                Reason = "outside the exposure",
                Exposure = ["env/**"],
            });

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log"));

        Assert.DoesNotContain("\u001b", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("31mred", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyOnAnUntouchedLog_Succeeds()
    {
        Seed();

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log", "verify"));

        Assert.Contains("2 records verified", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("Latest: seq 2, hash ", _cli.Out, StringComparison.Ordinal);

        // Said on green as well as on red: a check that over-claims when it passes is as useless as
        // one that cries wolf when it fails.
        Assert.Contains("cannot detect records deleted from the end", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyOnAnEditedLog_FailsLoudly()
    {
        Seed();
        Tamper();

        _cli.AssertExit(CliApp.ExitTamperDetected, _cli.Run("log", "verify"));

        Assert.Contains("THE CHAIN IS BROKEN", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("its own bytes have changed", _cli.Out, StringComparison.Ordinal);
        Assert.Contains(_cli.ConsoleStyle.Alarms, a => a.Contains("tampered", StringComparison.Ordinal));
    }

    /// <summary>
    /// The table is still printed. Refusing to show an edited log would hand whoever edited it a way
    /// to make the record unreadable, which is a worse outcome than showing it with an alarm on it.
    /// </summary>
    [Fact]
    public void ListingAnEditedLog_StillShowsItAndStillFails()
    {
        Seed();
        Tamper();

        _cli.AssertExit(CliApp.ExitTamperDetected, _cli.Run("log"));

        Assert.Contains("env/dev/DB_URL", _cli.Out, StringComparison.Ordinal);
        Assert.Contains(_cli.ConsoleStyle.Alarms, a => a.Contains("tampered", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifying nothing is not an answer, and a script that read exit 0 from a missing file as
    /// "the log is intact" would be taking a reassurance out of an absence.
    /// </summary>
    [Fact]
    public void VerifyWithNoLog_IsNotFound()
    {
        _cli.Environment[KeypasteHome.EnvironmentVariable] = Path.Combine(_cli.Directory, ".keypaste");

        _cli.AssertExit(CliApp.ExitNotFound, _cli.Run("log", "verify"));
        Assert.Contains("nothing was checked", _cli.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// The anchor, which is the only thing that can catch a truncation: records deleted from the end
    /// leave a chain that is internally perfect (THREATS.md T-5).
    /// </summary>
    [Fact]
    public void AnAnchorThatIsStillThere_Passes_AndOneThatIsGone_Fails()
    {
        Seed();

        var report = AuditChainVerifier.Verify(LogPath);
        var anchor = report.LatestHash;

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log", "verify", "--expect", anchor));
        Assert.Contains("is here, and it verifies", _cli.Out, StringComparison.Ordinal);

        // Truncation: the last record is removed and everything left still verifies perfectly.
        var lines = File.ReadAllText(LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        File.WriteAllText(LogPath, lines[0] + "\n");

        Assert.Equal(AuditChainVerdict.Intact, AuditChainVerifier.Verify(LogPath).Verdict);

        _cli.AssertExit(CliApp.ExitTamperDetected, _cli.Run("log", "verify", "--expect", anchor));
        Assert.Contains("NOT IN THIS FILE", _cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// The anchor is answered from the chain, not from the file's text. A hash planted in a field an
    /// agent writes must not be able to vouch for a record that is gone.
    /// </summary>
    /// <remarks>
    /// Without this the whole feature is decorative: truncate the log, then have the agent ask for
    /// an entry named after the hash you destroyed, and the anchor is "found" in the record of that
    /// request. The entry argument is the attacker's own text, so it does not even need file access.
    /// </remarks>
    [Fact]
    public void AnAnchorPlantedInAnEntryName_DoesNotCountAsTheRecord()
    {
        Seed();
        var anchor = AuditChainVerifier.Verify(LogPath).LatestHash;

        var lines = File.ReadAllText(LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        File.WriteAllText(LogPath, lines[0] + "\n");

        Write(
            new DateTimeOffset(2026, 7, 26, 14, 30, 0, TimeSpan.Zero),
            Record("claude-code", $"env/dev/{anchor}", AuditDecision.Denied, AuditMethod.OutOfScope));

        Assert.Contains(anchor, File.ReadAllText(LogPath), StringComparison.Ordinal);

        _cli.AssertExit(CliApp.ExitTamperDetected, _cli.Run("log", "verify", "--expect", anchor));
        Assert.Contains("NOT IN THIS FILE", _cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// A forged record that claims to predate the chain is rendered, because it parses — so it is
    /// marked, and the chain calls it what it is.
    /// </summary>
    /// <remarks>
    /// Inserting an unverifiable record is the one way to add a line without breaking a link, since
    /// nothing before or after it changes. What stops it being a way to write history is that
    /// keypaste never puts a v1 record after a v2 one, and that the table says which rows the chain
    /// does not vouch for.
    /// </remarks>
    [Fact]
    public void AForgedRecordInsertedMidFile_IsMarkedAndBreaksTheChain()
    {
        Seed();

        var lines = File.ReadAllText(LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        lines.Insert(
            1,
            "{\"v\":1,\"ts\":\"2026-07-26T14:10:00.000Z\",\"seq\":9,\"pid\":1,"
            + "\"client\":{\"label\":\"claude-code\"},\"tool\":\"request_credential\","
            + "\"args\":{\"entry\":\"env/prod/PAYROLL_DB\"},\"decision\":\"granted\",\"method\":\"prompt\"}");

        File.WriteAllText(LogPath, string.Join('\n', lines) + "\n");

        _cli.AssertExit(CliApp.ExitTamperDetected, _cli.Run("log"));

        // Shown, because hiding it would be worse - but never as a record the chain stands behind.
        Assert.Contains("env/prod/PAYROLL_DB", _cli.Out, StringComparison.Ordinal);
        Assert.Contains($"{AuditText.UnverifiedMark}  the hash chain does not vouch", _cli.Out, StringComparison.Ordinal);

        _cli.AssertExit(CliApp.ExitTamperDetected, _cli.Run("log", "verify"));
        Assert.Contains("keypaste never writes one there", _cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deleting the file's last newline must not turn the last record into one nothing checks.
    /// </summary>
    /// <remarks>
    /// A record and its newline are a single write, so what a crash leaves is a record that stops
    /// partway — not a complete one missing its terminator. Treating every unterminated last line as
    /// unexaminable would have made removing one byte the way to edit the newest record freely.
    /// </remarks>
    [Fact]
    public void EditingTheLastRecordAndDroppingTheNewline_IsStillCaught()
    {
        Seed();

        var text = File.ReadAllText(LogPath)
            .Replace("\"decision\":\"denied\"", "\"decision\":\"granted\"", StringComparison.Ordinal);

        File.WriteAllText(LogPath, text.TrimEnd('\n'));

        _cli.AssertExit(CliApp.ExitTamperDetected, _cli.Run("log", "verify"));
        Assert.Contains("own bytes have changed", _cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the case that must survive it: a genuinely interrupted write is still forgiven, even
    /// though the last line is now examined rather than skipped.
    /// </summary>
    [Fact]
    public void AGenuinelyInterruptedWrite_IsStillForgiven()
    {
        Seed();
        File.AppendAllText(LogPath, "{\"v\":2,\"ts\":\"2026-07-26T14:03:1");

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log", "verify"));
        Assert.Contains("interrupted write", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExpectThatIsNotAHash_IsAUsageError()
    {
        Seed();

        _cli.AssertExit(CliApp.ExitUsageError, _cli.Run("log", "verify", "--expect", "probably"));
        Assert.Contains("64 lowercase hex", _cli.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpNamesBothFormsAndTheTamperExitCode()
    {
        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("log", "--help"));

        Assert.Contains("keypaste log verify", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("--denied", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("A broken chain exits 5", _cli.Out, StringComparison.Ordinal);
    }
}
