using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The audit log is a precondition for disclosure, not observability (CORE.md laws 3.3 and 3.7,
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

    private string[] Lines() => File.ReadAllLines(LogPath);

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
    /// Stage 2.4 hashes the raw bytes of each line, so the key order has to be a property of the
    /// writer rather than an accident of whatever the serializer felt like doing.
    /// </summary>
    [Fact]
    public void TheKeyOrder_IsFixed()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(Denial(), out var error), error);
        }

        var line = Lines()[0];
        var order = new[] { "\"v\"", "\"ts\"", "\"seq\"", "\"pid\"", "\"client\"", "\"tool\"", "\"args\"", "\"decision\"", "\"method\"", "\"reason\"", "\"exposure\"" };

        var previous = -1;
        foreach (var key in order)
        {
            var at = line.IndexOf(key, StringComparison.Ordinal);
            Assert.True(at > previous, $"{key} is out of order in: {line}");
            previous = at;
        }
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
    /// Stage 2.4's chain verifier all at once, so the newline case is pinned rather than assumed.
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

        Assert.Equal(2, Lines().Length);
    }

    /// <summary>
    /// Claude Desktop and Claude Code each spawn their own server, so two processes really do share
    /// one file. Interleaving them must not lose or tear a line.
    /// </summary>
    [Fact]
    public void TwoLogsOverOneFile_BothAppendWithoutLoss()
    {
        using var first = Open();
        using var second = Open();

        for (var i = 0; i < 10; i++)
        {
            Assert.True(first.TryAppend(Denial(), out var error), error);
            Assert.True(second.TryAppend(Denial(), out error), error);
        }

        var lines = Lines();
        Assert.Equal(20, lines.Length);

        foreach (var line in lines)
        {
            using var parsed = JsonDocument.Parse(line);
            Assert.Equal("list_entry_names", parsed.RootElement.GetProperty("tool").GetString());
        }
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
