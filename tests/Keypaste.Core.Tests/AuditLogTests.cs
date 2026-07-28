using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The audit log is a precondition for disclosure, not observability (docs/PRODUCT.md laws 3.3 and 3.7,
/// THREATS.md T-6), so what it does when it cannot write matters as much as what it writes.
/// </summary>
public sealed class AuditLogTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-audit-tests-").FullName;

    private string LogPath => Path.Combine(_directory, "audit.jsonl");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A clock that does not move, so timestamps are assertable.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AuditRecord Denial(string tool = "list_entry_names", AuditArgs? args = null) => new()
    {
        Tool = tool,
        Client = new AuditClient("claude-code", "1.2.3", "work-laptop"),
        Args = args ?? AuditArgs.None,
        Decision = AuditDecision.Denied,
        Method = AuditMethod.VaultLocked,
        Reason = "the vault is locked",
        Exposure = ["env/**"],
    };

    private AuditLog Open(DateTimeOffset? now = null)
    {
        var clock = new FixedClock(now ?? new DateTimeOffset(2026, 7, 26, 14, 3, 11, 482, TimeSpan.Zero));
        Assert.True(AuditLog.TryOpen(LogPath, clock, out var log, out var error), error);
        Assert.NotNull(log);
        return log;
    }

    /// <summary>
    /// Reads the log the way a reader has to while a server still holds it open.
    /// </summary>
    /// <remarks>
    /// Not <see cref="File.ReadAllLines(string)"/>: that asks for <see cref="FileShare.Read"/>,
    /// which denies other <em>writers</em>, so on Windows it fails outright while any keypaste-mcp
    /// has the log open. This is the same constraint <c>keypaste log</c> is under.
    /// </remarks>
    private string[] Lines()
    {
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Every <see cref="AuditMethod"/> is written as itself, and none of them lands on the
    /// fallback.
    /// </summary>
    /// <remarks>
    /// This is the tripwire for a specific, quiet bug. The wire switch used to fall back to
    /// <c>vault-locked</c>, so a member added without a matching case — which is exactly what
    /// happens when a stage adds a new way to say no — would have been recorded as a denial that
    /// never occurred. A log that lies about which decisions were taken is worse than no log, and
    /// nothing else in the suite would have noticed.
    /// </remarks>
    [Fact]
    public void EveryAuditMethod_HasItsOwnWireString()
    {
        var methods = Enum.GetValues<AuditMethod>();
        var written = new List<string>();

        using (var log = Open())
        {
            foreach (var method in methods)
            {
                var record = Denial() with { Method = method };
                Assert.True(log.TryAppend(record, out var error), error);
            }
        }

        foreach (var line in Lines())
        {
            using var parsed = JsonDocument.Parse(line);
            written.Add(parsed.RootElement.GetProperty("method").GetString()!);
        }

        Assert.Equal(methods.Length, written.Count);
        Assert.DoesNotContain("unknown", written, StringComparer.Ordinal);
        Assert.Equal(methods.Length, written.Distinct(StringComparer.Ordinal).Count());

        // The four the release paths turn on, named outright: a rename would otherwise slip past
        // the distinctness check above while changing what every existing log line means. "policy"
        // matters most of the four — it is the only word in the vocabulary that means a credential
        // left the machine with nobody watching, so it is the only evidence that it happened.
        Assert.Contains("prompt", written, StringComparer.Ordinal);
        Assert.Contains("grant-cache", written, StringComparer.Ordinal);
        Assert.Contains("policy", written, StringComparer.Ordinal);
        Assert.Contains("policy-limit", written, StringComparer.Ordinal);
    }

    [Fact]
    public void EachRecord_IsExactlyOneLineOfValidJson()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
            Assert.True(log.TryAppend(Denial(), out error), error);
        }

        var lines = Lines();
        Assert.Equal(2, lines.Length);

        foreach (var line in lines)
        {
            using var parsed = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        }
    }

    /// <summary>
    /// The chain hashes the raw bytes of each line, so the key order has to be a property of the
    /// writer rather than an accident of whatever the serializer felt like doing.
    /// </summary>
    /// <remarks>
    /// The ordering check below cannot prove the one thing the chain actually depends on — that
    /// <c>hash</c> is <em>last</em> — because <c>IndexOf</c> is satisfied by anything that merely
    /// comes after the others. The suffix assertion is what holds that up, and with it the fact that
    /// verification can be a slice of bytes instead of a second parse.
    /// </remarks>
    [Fact]
    public void TheKeyOrder_IsFixed()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
        }

        var line = Lines()[0];
        var order = new[] { "\"v\"", "\"ts\"", "\"seq\"", "\"pid\"", "\"client\"", "\"tool\"", "\"args\"", "\"decision\"", "\"method\"", "\"reason\"", "\"exposure\"", "\"prev\"", "\"hash\"" };

        var previous = -1;
        foreach (var key in order)
        {
            var at = line.IndexOf(key, StringComparison.Ordinal);
            Assert.True(at > previous, $"{key} is out of order in: {line}");
            previous = at;
        }

        Assert.Equal(",\"hash\":\"", line[^75..^66]);
        Assert.EndsWith("\"}", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The version on the wire, pinned. Every reader of an old log — including
    /// <c>keypaste log verify</c>, which uses it to tell "predates the chain" from "tampered with" —
    /// depends on this number meaning one thing forever.
    /// </summary>
    [Fact]
    public void TheSchemaVersion_IsWrittenOnEveryLine()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
        }

        using var parsed = JsonDocument.Parse(Lines()[0]);

        Assert.Equal(2, AuditRecord.SchemaVersion);
        Assert.Equal(AuditRecord.SchemaVersion, parsed.RootElement.GetProperty("v").GetInt32());
    }

    [Fact]
    public void TheTimestamp_ComesFromTheClock_AndIsUtc()
    {
        using (var log = Open(new DateTimeOffset(2026, 7, 26, 14, 3, 11, 482, TimeSpan.Zero)))
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
        }

        using var parsed = JsonDocument.Parse(Lines()[0]);
        Assert.Equal("2026-07-26T14:03:11.482Z", parsed.RootElement.GetProperty("ts").GetString());
    }

    /// <summary>
    /// One writer, one file: the positions run 1, 2, 3. The interesting cases are the two below,
    /// where the number has to come from the file rather than from this object's memory.
    /// </summary>
    [Fact]
    public void TheSequence_StartsAtOneAndIncrements()
    {
        using (var log = Open())
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.True(log.TryAppend(Denial(), out var error), error);
            }
        }

        var sequences = new List<long>();
        foreach (var line in Lines())
        {
            using var parsed = JsonDocument.Parse(line);
            sequences.Add(parsed.RootElement.GetProperty("seq").GetInt64());
        }

        Assert.Equal([1L, 2L, 3L], sequences);
    }

    [Fact]
    public void AToolThatTakesNoArguments_RecordsAnEmptyArgsObject()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
        }

        using var parsed = JsonDocument.Parse(Lines()[0]);
        Assert.Empty(parsed.RootElement.GetProperty("args").EnumerateObject());
    }

    /// <summary>
    /// The redaction rule for the one field an agent writes freely: an excerpt for the human, the
    /// true length so truncation is visible, and a hash so 2.2 can prove the dialog showed the same
    /// text that was recorded.
    /// </summary>
    [Fact]
    public void AnOverlongReason_IsExcerptedButItsLengthAndHashAreExact()
    {
        var reason = new string('r', 5000);
        var args = AuditArgs.ForCredentialRequest("k1_0123456789abcdef", "password", 900, reason);

        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial("request_credential", args), out var error), error);
        }

        using var parsed = JsonDocument.Parse(Lines()[0]);
        var recorded = parsed.RootElement.GetProperty("args");

        Assert.Equal(AuditArgs.ReasonExcerptLength, recorded.GetProperty("reason_excerpt").GetString()!.Length);
        Assert.Equal(5000, recorded.GetProperty("reason_len").GetInt32());

        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reason)));
        Assert.Equal(expected, recorded.GetProperty("reason_sha256").GetString());
    }

    [Fact]
    public void TheRequestedFieldIsRecorded_ButNeverAValue()
    {
        var args = AuditArgs.ForCredentialRequest("env/dev/STRIPE_KEY", "password", 900, "deploy billing");

        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial("request_credential", args), out var error), error);
        }

        var line = Lines()[0];
        using var parsed = JsonDocument.Parse(line);
        var recorded = parsed.RootElement.GetProperty("args");

        Assert.Equal("password", recorded.GetProperty("field").GetString());
        Assert.Equal("env/dev/STRIPE_KEY", recorded.GetProperty("entry").GetString());
        Assert.Equal("path", recorded.GetProperty("entry_kind").GetString());
        Assert.Equal(900, recorded.GetProperty("ttl_seconds").GetInt32());
    }

    [Fact]
    public void AHandleArgument_IsRecordedAsAHandle()
    {
        var args = AuditArgs.ForCredentialRequest("k1_0123456789abcdef", "password", 60, "why");

        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial("request_credential", args), out var error), error);
        }

        using var parsed = JsonDocument.Parse(Lines()[0]);
        Assert.Equal("handle", parsed.RootElement.GetProperty("args").GetProperty("entry_kind").GetString());
    }

    /// <summary>
    /// A record split across two physical lines would break <c>jq</c>, <c>keypaste log</c>, and
    /// the chain verifier all at once, so the newline case is pinned rather than assumed.
    /// </summary>
    /// <remarks>
    /// The newlines are put in fields that do <b>not</b> pass through the sanitizer \u2014 the client's
    /// self-declared name and keypaste's own reason \u2014 because the sanitizer would otherwise replace
    /// them long before the writer saw them, and the test would prove nothing about the writer.
    /// What is under test here is the JSON encoder.
    /// </remarks>
    [Fact]
    public void AValueFullOfNewlines_StillProducesOneLine()
    {
        var record = new AuditRecord
        {
            Tool = "list_entry_names",
            Client = new AuditClient("evil\nclient", "1.0\r\n2.0", "label\u2028here"),
            Decision = AuditDecision.Denied,
            Method = AuditMethod.VaultLocked,
            Reason = "one\ntwo\r\nthree",
            Exposure = ["env/**", "a\nb"],
        };

        using (var log = Open())
        {
            Assert.True(log.TryAppend(record, out var error), error);
            Assert.True(log.TryAppend(Denial(), out error), error);
        }

        var lines = Lines();
        Assert.Equal(2, lines.Length);

        using var parsed = JsonDocument.Parse(lines[0]);
        Assert.Equal("evil\nclient", parsed.RootElement.GetProperty("client").GetProperty("name").GetString());
    }

    /// <summary>
    /// The default JSON encoder escapes everything outside ASCII, which is what makes the previous
    /// guarantee hold no matter what an entry is called.
    /// </summary>
    [Fact]
    public void TheFileIsPlainAscii()
    {
        var args = AuditArgs.ForCredentialRequest("日本語/エントリ", "password", 60, "理由\u202e");

        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial("request_credential", args), out var error), error);
        }

        foreach (var b in File.ReadAllBytes(LogPath))
        {
            Assert.True(b < 0x80, $"non-ASCII byte in the log: 0x{b:X2}");
        }
    }

    /// <summary>
    /// Reopening appends, and — since 2.4 — the second record links to the first. That is where the
    /// writer proves it learns what to link to from the <em>file</em>: a fresh
    /// <see cref="AuditLog"/> has no memory of what the last one wrote.
    /// </summary>
    [Fact]
    public void ReopeningAppendsRatherThanTruncating()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
        }

        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
        }

        var lines = Lines();
        Assert.Equal(2, lines.Length);

        using var first = JsonDocument.Parse(lines[0]);
        using var second = JsonDocument.Parse(lines[1]);

        Assert.Equal(
            first.RootElement.GetProperty("hash").GetString(),
            second.RootElement.GetProperty("prev").GetString());

        Assert.Equal(1, first.RootElement.GetProperty("seq").GetInt64());
        Assert.Equal(2, second.RootElement.GetProperty("seq").GetInt64());
    }

    /// <summary>
    /// Claude Desktop and Claude Code each spawn their own server, so two processes really do share
    /// one file. Interleaving them must not lose or tear a line — and, since 2.4, must produce one
    /// unbroken chain rather than two interleaved ones.
    /// </summary>
    /// <remarks>
    /// This is the test that holds up both halves of the 2.4 writer at once: the second file handle
    /// that lets an append read the end of the file, and the positions being the file's rather than
    /// each object's. Before 2.4 this file would have run 1, 1, 2, 2, 3, 3 — a number that looks
    /// like a record index and is not one, in the file whose job is to be read back afterwards.
    /// </remarks>
    [Fact]
    public void TwoLogsOverOneFile_BothAppendWithoutLoss()
    {
        using (var first = Open())
        using (var second = Open())
        {
            for (var i = 0; i < 10; i++)
            {
                Assert.True(first.TryAppend(Denial(), out var error), error);
                Assert.True(second.TryAppend(Denial(), out error), error);
            }
        }

        var lines = Lines();
        Assert.Equal(20, lines.Length);

        var sequences = new List<long>();
        foreach (var line in lines)
        {
            using var parsed = JsonDocument.Parse(line);
            Assert.Equal("list_entry_names", parsed.RootElement.GetProperty("tool").GetString());
            sequences.Add(parsed.RootElement.GetProperty("seq").GetInt64());
        }

        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i), sequences);

        var report = AuditChainVerifier.Verify(LogPath);
        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(20, report.Records);
    }

    /// <summary>
    /// A line something else appended does not stop keypaste writing. It links past it, to the last
    /// record that is actually part of the chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the same linking rule the verifier applies forwards, and the two have to agree or a
    /// healthy log reads as a broken one. Stepping back cannot be steered anywhere useful: the last
    /// chained record is the last chained record, and reaching an older one would mean deleting the
    /// newer ones — which is truncation, and is already THREATS.md T-5's residual.
    /// </para>
    /// <para>
    /// It used to refuse, and that was worse. One appended byte — a blank line out of an editor, an
    /// <c>echo</c> — became a permanent denial of every credential request, which under assumption 1
    /// is a cheaper lever than the one the refusal was meant to close. The honest answer to a line
    /// keypaste did not write is to record around it and let the verifier report it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("somebody wrote this by hand", "a line of prose")]
    [InlineData("", "a blank line, which any editor leaves behind")]
    [InlineData("{\"v\":1,\"ts\":\"2026-07-01T00:00:00.000Z\"}", "a forged record predating the chain")]
    [InlineData("{\"v\":2,", "a write cut short")]
    public void ALineSomethingElseAppended_DoesNotStopTheLogWorking(string junk, string what)
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
        }

        File.AppendAllText(LogPath, junk + "\n");

        Assert.True(AuditLog.TryOpen(LogPath, TimeProvider.System, out var reopened, out var openError), openError);
        using (reopened)
        {
            Assert.True(reopened.TryAppend(Denial(), out var appendError), appendError);
        }

        // Indexed from the end, because a blank line leaves no entry for `Lines` to return.
        var lines = Lines();
        using var first = JsonDocument.Parse(lines[0]);
        using var latest = JsonDocument.Parse(lines[^1]);

        // The new record links past the junk, to the record before it.
        Assert.Equal(
            first.RootElement.GetProperty("hash").GetString(),
            latest.RootElement.GetProperty("prev").GetString());

        Assert.Equal(2, latest.RootElement.GetProperty("seq").GetInt64());
        Assert.NotEmpty(what);
    }

    /// <summary>
    /// The one thing still worth refusing over: a record from a schema this version cannot read.
    /// </summary>
    /// <remarks>
    /// Appending beneath it would fork the chain — the newer records would sit unlinked between two
    /// of ours — and unlike every other unreadable line, "upgrade keypaste" is something the person
    /// holding the machine can actually do. The message has to name a way out, because the
    /// alternative is somebody deleting their audit trail to make an error go away.
    /// </remarks>
    [Fact]
    public void ALogFromANewerKeypaste_WillNotOpen()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
        }

        File.AppendAllText(LogPath, "{\"v\":99,\"ts\":\"2027-01-01T00:00:00.000Z\"}\n");

        Assert.False(AuditLog.TryOpen(LogPath, TimeProvider.System, out var reopened, out var openError));
        using (reopened)
        {
            Assert.Null(reopened);
            Assert.Contains("newer version of keypaste", openError, StringComparison.Ordinal);
            Assert.Contains("keypaste log verify", openError, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A planted record predating the chain must not make keypaste start the chain over. A genesis
    /// link after a chained record is the signature of a truncation, and manufacturing one would be
    /// keypaste reporting an attack on itself, permanently.
    /// </summary>
    [Fact]
    public void APlantedLegacyRecord_DoesNotMakeTheWriterStartAgain()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
            Assert.True(log.TryAppend(Denial(), out error), error);
        }

        File.AppendAllText(LogPath, "{\"v\":1,\"ts\":\"2026-07-01T00:00:00.000Z\",\"seq\":1}\n");

        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
        }

        using var latest = JsonDocument.Parse(Lines()[3]);
        Assert.NotEqual(new string('0', 64), latest.RootElement.GetProperty("prev").GetString());
        Assert.Equal(3, latest.RootElement.GetProperty("seq").GetInt64());

        // The planted line is still reported — as an insertion, which is what it is.
        var report = AuditChainVerifier.Verify(LogPath);
        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.Fault == AuditChainFault.Backdated);
    }

    /// <summary>
    /// A record too large to be written atomically is refused rather than written torn, and the
    /// caller that sees false is required to deny the call it was about to answer.
    /// </summary>
    /// <remarks>
    /// Never covered before 2.4, and worth covering now: the chain spends
    /// <c>AuditChain.ChainOverheadBytes</c> of the same budget, so the cliff is closer than it was.
    /// <c>exposure</c> is the lever because it is the one field with no cap of its own.
    /// </remarks>
    [Fact]
    public void ARecordTooLargeToWriteWhole_IsRefusedAndNothingIsWritten()
    {
        using var log = Open();

        var enormous = new AuditRecord
        {
            Tool = "list_entry_names",
            Client = AuditClient.Unknown,
            Decision = AuditDecision.Denied,
            Method = AuditMethod.VaultLocked,
            Reason = "the vault is locked",
            Exposure = [new string('x', AuditLog.MaximumRecordBytes)],
        };

        Assert.False(log.TryAppend(enormous, out var error));
        Assert.Contains("over the", error, StringComparison.Ordinal);
        Assert.Empty(Lines());

        // And the log still works afterwards, starting the chain at the first record that fits.
        Assert.True(log.TryAppend(Denial(), out var next), next);
        Assert.Single(Lines());
    }

    /// <summary>
    /// A record that only just does not fit is rewritten without the agent's reason rather than
    /// refused, and what is dropped is only ever the untrusted text — the length and the hash of it
    /// stay, so the truncation is visible rather than silent.
    /// </summary>
    [Fact]
    public void ARecordThatOnlyJustDoesNotFit_LosesTheAgentsReasonAndNothingElse()
    {
        var reason = new string('r', AuditArgs.ReasonExcerptLength);
        var args = AuditArgs.ForCredentialRequest("env/dev/KEY", "password", 900, reason);

        AuditRecord Padded(string glob) => new()
        {
            Tool = "request_credential",
            Client = AuditClient.Unknown,
            Args = args,
            Decision = AuditDecision.Denied,
            Method = AuditMethod.VaultLocked,
            Reason = "the vault is locked",
            Exposure = glob.Length == 0 ? [] : [glob],
        };

        using var log = Open();

        // Measured rather than guessed: every byte here is plain ASCII, so one glob of n characters
        // grows the same record by exactly n + 2, and the excerpt is then the only thing standing
        // between fitting and not.
        Assert.True(log.TryAppend(Padded(string.Empty), out var error), error);
        var measured = Lines()[0].Length + 1;
        var padding = AuditLog.MaximumRecordBytes + 50 - measured - 2;

        Assert.True(log.TryAppend(Padded(new string('x', padding)), out var padError), padError);

        var line = Lines()[1];
        Assert.True(line.Length + 1 <= AuditLog.MaximumRecordBytes, $"{line.Length + 1} bytes");

        using var parsed = JsonDocument.Parse(line);
        var recorded = parsed.RootElement.GetProperty("args");

        Assert.False(recorded.TryGetProperty("reason_excerpt", out _));
        Assert.Equal(AuditArgs.ReasonExcerptLength, recorded.GetProperty("reason_len").GetInt32());
        Assert.NotEmpty(recorded.GetProperty("reason_sha256").GetString()!);

        // The bytes that were hashed are the bytes that were written, which is the trap in composing
        // a record twice.
        Assert.Equal(AuditChainVerdict.Intact, AuditChainVerifier.Verify(LogPath).Verdict);
    }

    /// <summary>
    /// The failure that must never be silent. A caller seeing false here is required to deny the
    /// call it was about to answer.
    /// </summary>
    [Fact]
    public void AnUnopenableLog_FailsWithAReason()
    {
        // A directory where the file should be: unopenable on all three operating systems, unlike
        // a permission trick, which is meaningless on Windows and on a root CI runner.
        Directory.CreateDirectory(LogPath);

        // The `using` is for CA2000, which cannot see that a refused open leaves nothing to close.
        Assert.False(AuditLog.TryOpen(LogPath, TimeProvider.System, out var log, out var error));
        using (log)
        {
            Assert.Null(log);
            Assert.NotEmpty(error);
        }
    }

    [Fact]
    public void TheDirectoryIsCreatedIfItIsMissing()
    {
        var nested = Path.Combine(_directory, "deeper", "still", "audit.jsonl");

        Assert.True(AuditLog.TryOpen(nested, TimeProvider.System, out var log, out var error), error);
        Assert.NotNull(log);

        using (log)
        {
            Assert.True(log.TryAppend(Denial(), out var appendError), appendError);
        }

        Assert.Single(File.ReadAllLines(nested));
    }

    [Fact]
    public void AnAppendAfterDisposal_Throws()
    {
        var log = Open();
        log.Dispose();

        Assert.Throws<ObjectDisposedException>(() => log.TryAppend(Denial(), out _));
    }

    [Fact]
    public void TryOpen_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AuditLog.TryOpen(null!, TimeProvider.System, out _, out _));
        Assert.Throws<ArgumentNullException>(() => AuditLog.TryOpen(LogPath, null!, out _, out _));
    }
}
