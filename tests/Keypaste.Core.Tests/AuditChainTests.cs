using System.Text;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The chain is the whole of THREATS.md T-5's mitigation, so these tests are written against the
/// bytes of a file rather than against the writer that produced them.
/// </summary>
/// <remarks>
/// <para>
/// Half of what is asserted here is that the verifier stays <em>quiet</em>. A chain checker that
/// reddens after an ordinary crash, or on a log copied through a tool that rewrote its line endings,
/// teaches its user to ignore it — and then it is worth less than nothing, because the one alarm
/// that mattered is the one they have learned to skip past. Every forgiveness below is paired with
/// the attack it must not forgive.
/// </para>
/// <para>
/// The tampering is done by editing the file, not by calling an API that simulates it. That is the
/// only way to prove the property the threat model claims, which is about a file on a disk.
/// </para>
/// </remarks>
public sealed class AuditChainTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-chain-tests-").FullName;

    private string LogPath => Path.Combine(_directory, "audit.jsonl");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AuditRecord Record(string reason = "the vault is locked") => new()
    {
        Tool = "list_entry_names",
        Client = new AuditClient("claude-code", "1.2.3", "work-laptop"),
        Decision = AuditDecision.Denied,
        Method = AuditMethod.VaultLocked,
        Reason = reason,
        Exposure = ["env/**"],
    };

    /// <summary>Writes <paramref name="count"/> real records through the real writer.</summary>
    private void Write(int count)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 26, 14, 3, 11, 482, TimeSpan.Zero));
        Assert.True(AuditLog.TryOpen(LogPath, clock, out var log, out var error), error);

        using (log)
        {
            for (var i = 0; i < count; i++)
            {
                Assert.True(log.TryAppend(Record($"record {i}"), out var appendError), appendError);
            }
        }
    }

    private string[] Lines() => File.ReadAllText(LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private void Rewrite(IEnumerable<string> lines) =>
        File.WriteAllText(LogPath, string.Join('\n', lines) + "\n");

    private AuditChainReport Verify() => AuditChainVerifier.Verify(LogPath);

    [Fact]
    public void AFileTheWriterProduced_Verifies()
    {
        Write(5);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(5, report.Records);
        Assert.Empty(report.Findings);
        Assert.Equal(5, report.LatestSequence);
        Assert.True(AuditChainVerifier.IsHash(report.LatestHash));
    }

    /// <summary>
    /// The headline case, and the one THREATS.md T-5 is about: a denial edited into a grant.
    /// </summary>
    /// <remarks>
    /// Changing <c>denied</c> to <c>granted</c> keeps the line the same length, which is what an
    /// attacker would choose, and is exactly the edit a byte-length check would miss.
    /// </remarks>
    [Fact]
    public void ADenialEditedIntoAGrant_IsCaught()
    {
        Write(3);

        var lines = Lines();
        lines[1] = lines[1].Replace("\"decision\":\"denied\"", "\"decision\":\"granted\"", StringComparison.Ordinal);
        Rewrite(lines);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.Line == 2 && f.Fault == AuditChainFault.Altered);
    }

    /// <summary>
    /// The other half of an edit: recomputing the changed line's own hash so it verifies. That
    /// leaves the record after it pointing at a hash nothing has any more.
    /// </summary>
    [Fact]
    public void ARecordRemoved_LeavesTheNextOneUnlinked()
    {
        Write(4);

        var lines = Lines().ToList();
        lines.RemoveAt(1);
        Rewrite(lines);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.Fault == AuditChainFault.Unlinked);
    }

    [Fact]
    public void ARecordInserted_LeavesTheNextOneUnlinked()
    {
        Write(3);

        var lines = Lines().ToList();
        lines.Insert(1, lines[2]);
        Rewrite(lines);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.IsBreak);
    }

    /// <summary>
    /// Cutting a log short and appending to it again leaves one signature: a record that starts the
    /// chain over in the middle of a file. It is named separately because it is the one thing the
    /// chain can say about a truncation at all.
    /// </summary>
    [Fact]
    public void AChainThatStartsAgainMidFile_IsCaught()
    {
        Write(2);
        var first = Lines();

        // A second chain, built from scratch in its own file, then pasted on the end of the first.
        File.Delete(LogPath);
        Write(2);
        var second = Lines();

        Rewrite([first[0], first[1], second[0], second[1]]);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.Fault == AuditChainFault.Restarted);
    }

    [Fact]
    public void SomethingElseWritingIntoTheLog_IsCaught()
    {
        Write(2);
        File.AppendAllText(LogPath, "{\"tampered\":true}\n");

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.Fault == AuditChainFault.Foreign);
    }

    /// <summary>
    /// The most important thing the verifier does not do. A crash between a record and its newline
    /// leaves a fragment, and calling that tampering would mean every machine that lost power reads
    /// as attacked.
    /// </summary>
    [Fact]
    public void AWriteCutShortAtTheEnd_IsIntactAndSaidToBe()
    {
        Write(2);
        File.AppendAllText(LogPath, "{\"v\":2,\"ts\":\"2026-07-26T14:03:11.4");

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.True(report.Unfinished);
        Assert.Equal(2, report.Records);
    }

    /// <summary>
    /// The same fragment, once a later append has put a newline after it. It is now a whole line of
    /// its own, and it must still not be tampering — the records either side of it link to each
    /// other, which is the whole point of stepping over what is not in the chain.
    /// </summary>
    [Fact]
    public void AWriteCutShort_ThenAppendedTo_IsStillIntact()
    {
        Write(2);
        File.AppendAllText(LogPath, "{\"v\":2,\"ts\":\"2026-07-26T14:03:11.4");
        Write(1);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(3, report.Records);
        Assert.Contains(report.Findings, f => f.Fault == AuditChainFault.Torn && !f.IsBreak);
    }

    /// <summary>
    /// And the attack that shape could otherwise hide: a record mangled down to a fragment. The
    /// record after it is what notices.
    /// </summary>
    [Fact]
    public void ARecordMangledIntoAFragment_IsStillCaught()
    {
        Write(3);

        var lines = Lines();
        lines[1] = "{\"v\":2,";
        Rewrite(lines);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.Line == 3 && f.Fault == AuditChainFault.Unlinked);
    }

    /// <summary>
    /// A forged record claiming to predate the chain, spliced into the middle of one.
    /// </summary>
    /// <remarks>
    /// It breaks no link — nothing before or after it changes — which makes it the one insertion the
    /// chain cannot catch arithmetically. What catches it is that keypaste never writes a v1 record
    /// after a v2 one, so its position is the evidence. Without this, "insert a record nobody can
    /// check" is a way to write history into an audit trail while every link still verifies.
    /// </remarks>
    [Fact]
    public void AForgedRecordClaimingToPredateTheChain_IsCaughtByItsPosition()
    {
        Write(3);

        var lines = Lines().ToList();
        lines.Insert(2, "{\"v\":1,\"ts\":\"2026-07-26T14:10:00.000Z\",\"seq\":9,\"decision\":\"granted\"}");
        Rewrite(lines);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.Line == 3 && f.Fault == AuditChainFault.Backdated);
    }

    /// <summary>
    /// The same shape at the front of the file is what every upgraded log looks like, and is not
    /// condemned — but it is still named, so a renderer can mark the row.
    /// </summary>
    [Fact]
    public void RecordsPredatingTheChain_AreNamedSoTheyCanBeMarked()
    {
        File.WriteAllText(LogPath, "{\"v\":1,\"ts\":\"2026-07-01T00:00:00.000Z\",\"seq\":1}\n");
        Write(2);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Contains(report.Findings, f => f.Line == 1 && f.Fault == AuditChainFault.Predates);
        Assert.Contains(1, report.Unverified);
        Assert.DoesNotContain(2, report.Unverified);
    }

    /// <summary>
    /// A record from a newer schema cannot be checked, so it is reported rather than vouched for.
    /// </summary>
    [Fact]
    public void ARecordFromANewerSchema_IsNamedRatherThanTrusted()
    {
        Write(2);
        File.AppendAllText(LogPath, "{\"v\":99,\"ts\":\"2027-01-01T00:00:00.000Z\"}\n");

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(1, report.Newer);
        Assert.Contains(3, report.Unverified);
    }

    /// <summary>
    /// A version number too large to hold is garbage, not the future. Reporting it as a newer
    /// keypaste would have the log tell its owner to upgrade in answer to a broken line.
    /// </summary>
    [Fact]
    public void AVersionNumberTooLargeToHold_IsNotTreatedAsNewer()
    {
        Write(1);
        File.AppendAllText(LogPath, "{\"v\":99999999999999999999,\"x\":1}\n");

        var report = Verify();

        Assert.Equal(0, report.Newer);
        Assert.Contains(report.Findings, f => f.Line == 2 && f.Fault == AuditChainFault.Torn);
    }

    /// <summary>
    /// The last record cannot be edited by also deleting the file's final newline.
    /// </summary>
    /// <remarks>
    /// A record and its newline are one write, so what a crash leaves is a record that stops
    /// partway — not a complete one missing its terminator. Skipping every unterminated last line
    /// would have made removing one byte the way to rewrite the newest record, which is the one an
    /// attacker has just caused.
    /// </remarks>
    [Fact]
    public void EditingTheLastRecordAndDroppingTheNewline_IsStillCaught()
    {
        Write(3);

        var edited = File.ReadAllText(LogPath)
            .Replace("record 2", "record X", StringComparison.Ordinal)
            .TrimEnd('\n');

        File.WriteAllText(LogPath, edited);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.Fault == AuditChainFault.Altered);
    }

    /// <summary>
    /// And the case that has to survive it: an unterminated last line that <em>is</em> a whole
    /// record verifies, rather than being skipped for want of a newline.
    /// </summary>
    [Fact]
    public void AWholeRecordWithoutATrailingNewline_Verifies()
    {
        Write(2);
        File.WriteAllText(LogPath, File.ReadAllText(LogPath).TrimEnd('\n'));

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(2, report.Records);
        Assert.False(report.Unfinished);
    }

    /// <summary>
    /// The anchor names a record, not a string. A hash sitting in a field the agent writes must not
    /// be able to vouch for the record it names, or truncation has no detection at all.
    /// </summary>
    [Fact]
    public void AnAnchorPlantedInAFieldTheAgentWrites_DoesNotCount()
    {
        Write(2);
        var anchor = Verify().LatestHash;

        // Truncate away the record that carried it, then plant the hash where an agent could put it.
        Rewrite([Lines()[0]]);

        var clock = new FixedClock(new DateTimeOffset(2026, 7, 26, 14, 3, 11, 482, TimeSpan.Zero));
        Assert.True(AuditLog.TryOpen(LogPath, clock, out var log, out var error), error);

        using (log)
        {
            var args = AuditArgs.ForCredentialRequest($"env/dev/{anchor}", "password", 60, "why");
            Assert.True(log.TryAppend(Record() with { Args = args }, out var appendError), appendError);
        }

        Assert.Contains(anchor, File.ReadAllText(LogPath), StringComparison.Ordinal);
        Assert.False(AuditChainVerifier.Verify(LogPath, anchor).Anchored);
    }

    /// <summary>
    /// A single enormous line is classified without being read whole. The file is attacker-writable
    /// (THREATS.md assumption 1), so "read it all to discover it is not a record" is a lever.
    /// </summary>
    [Fact]
    public void AnEnormousLine_IsRejectedWithoutBeingReadWhole()
    {
        Write(1);
        File.AppendAllText(LogPath, new string('x', (AuditChainVerifier.MaximumLineBytes * 2) + 1) + "\n");

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.Line == 2 && f.Fault == AuditChainFault.Foreign);
    }

    /// <summary>
    /// A log that has been through a tool that rewrote its line endings verifies, and says that it
    /// has been rewritten. Two different facts, and reporting either one as the other is wrong.
    /// </summary>
    [Fact]
    public void ALogCopiedWithWindowsLineEndings_VerifiesAndSaysSo()
    {
        Write(3);
        File.WriteAllText(LogPath, File.ReadAllText(LogPath).Replace("\n", "\r\n", StringComparison.Ordinal));

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.True(report.Rewritten);
        Assert.Equal(3, report.Records);
    }

    [Fact]
    public void ALogThatGrewAByteOrderMark_VerifiesAndSaysSo()
    {
        Write(2);

        var bytes = File.ReadAllBytes(LogPath);
        var withMark = new byte[bytes.Length + 3];
        Encoding.UTF8.GetPreamble().CopyTo(withMark, 0);
        bytes.CopyTo(withMark, 3);
        File.WriteAllBytes(LogPath, withMark);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.True(report.Rewritten);
    }

    /// <summary>
    /// Records written before the chain existed are reported as exactly that. Calling them tampered
    /// is what <see cref="AuditRecord.SchemaVersion"/> was put on line one of every record to avoid.
    /// </summary>
    [Fact]
    public void RecordsThatPredateTheChain_AreNotCondemned()
    {
        File.WriteAllText(
            LogPath,
            "{\"v\":1,\"ts\":\"2026-07-01T00:00:00.000Z\",\"seq\":1,\"pid\":1,\"decision\":\"denied\"}\n");

        Write(2);

        var report = Verify();

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(1, report.Legacy);
        Assert.Equal(2, report.Records);
        Assert.DoesNotContain(report.Findings, f => f.IsBreak);
    }

    /// <summary>The chain starts after the old records rather than reaching back over them.</summary>
    [Fact]
    public void TheFirstChainedRecordAfterOldOnes_StartsTheChain()
    {
        File.WriteAllText(LogPath, "{\"v\":1,\"ts\":\"2026-07-01T00:00:00.000Z\",\"seq\":9,\"pid\":1}\n");
        Write(1);

        var line = Lines()[1];

        Assert.Contains($"\"prev\":\"{new string('0', 64)}\"", line, StringComparison.Ordinal);
        Assert.Contains("\"seq\":1,", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyLog_HasNothingToVerify()
    {
        File.WriteAllText(LogPath, string.Empty);

        Assert.Equal(AuditChainVerdict.Empty, Verify().Verdict);
    }

    [Fact]
    public void AMissingLog_IsUnreadableRatherThanIntact()
    {
        var report = AuditChainVerifier.Verify(Path.Combine(_directory, "nothing.jsonl"));

        Assert.Equal(AuditChainVerdict.Unreadable, report.Verdict);
        Assert.NotEmpty(report.Error);
    }

    /// <summary>
    /// The hash is always the last member and always the same width, because that is what lets
    /// verification be a slice of bytes rather than a second parse of the record.
    /// </summary>
    [Fact]
    public void EveryRecordEndsWithItsHash()
    {
        Write(3);

        foreach (var line in Lines())
        {
            Assert.EndsWith("\"}", line, StringComparison.Ordinal);
            Assert.Equal(",\"hash\":\"", line[^75..^66]);
            Assert.True(AuditChainVerifier.IsHash(line[^66..^2]), line);
        }
    }

    /// <summary>
    /// A hash written in the other case would be a second spelling of one field, and two spellings
    /// are two implementations waiting to disagree.
    /// </summary>
    [Fact]
    public void AHashInUppercase_IsNotAHash()
    {
        Assert.False(AuditChainVerifier.IsHash(new string('A', 64)));
        Assert.False(AuditChainVerifier.IsHash(new string('0', 63)));
        Assert.False(AuditChainVerifier.IsHash(null));
        Assert.True(AuditChainVerifier.IsHash(new string('0', 64)));
    }
}
