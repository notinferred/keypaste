using System.Text;
using System.Text.Json;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Run lines carry the vault, the entries and the command (D-0361); older lines still read and still
/// verify; a maximal run line still fits after a person approved it (R14); and the log is read
/// incrementally from where the last read stopped.
/// </summary>
public sealed class AuditRunLineTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-audit-run-").FullName;

    private string LogPath => Path.Combine(_directory, "audit.jsonl");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private AuditLog Open()
    {
        Assert.True(AuditLog.TryOpen(LogPath, TimeProvider.System, out var log, out var error), error);
        return log;
    }

    private static AuditRecord RunLine(IReadOnlyList<string>? entries = null, string? command = "npm run migrate") => new()
    {
        Tool = "run",
        Client = new AuditClient("claude-code", "1.2.3", "cc"),
        Args = AuditArgs.ForRun("env/acme-api", "run the pending migration"),
        Decision = AuditDecision.Granted,
        Method = AuditMethod.Prompt,
        Reason = "a person approved this one run",
        Exposure = ["env/**"],
        Session = "session-one",
        GrantedSeconds = 0,
        Vault = "0123456789abcdef",
        Entries = entries ?? ["env/acme-api/DATABASE_URL"],
        Command = command,
        CommandSha256 = AuditRecord.HashOf(["/usr/bin/npm", "run", "migrate"]),
    };

    private string[] Lines()
    {
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void ARunLine_CarriesCommandEntriesAndVault()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(RunLine(), out var error), error);
        }

        using var parsed = JsonDocument.Parse(Assert.Single(Lines()));
        var root = parsed.RootElement;
        Assert.Equal("0123456789abcdef", root.GetProperty("vault").GetString());
        Assert.Equal(["env/acme-api/DATABASE_URL"], root.GetProperty("entries").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("npm run migrate", root.GetProperty("command").GetString());
        Assert.Equal(64, root.GetProperty("command_sha256").GetString()!.Length);

        Assert.True(AuditReader.TryRead(LogPath, out var entries, out _, out _));
        var entry = Assert.Single(entries);
        Assert.Equal(("0123456789abcdef", "npm run migrate"), (entry.Vault, entry.Command));
        Assert.Equal(["env/acme-api/DATABASE_URL"], entry.Entries);
    }

    [Fact]
    public void OldLines_ReadWithEmptyNewFields_AndAMixedLogStillVerifies()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(RunLine() with { Vault = null, Entries = null, Command = null, CommandSha256 = null }, out var error), error);
            Assert.True(log.TryAppend(RunLine(), out error), error);
        }

        Assert.True(AuditReader.TryRead(LogPath, out var entries, out _, out _));
        Assert.Equal((string.Empty, string.Empty), (entries[0].Vault, entries[0].Command));
        Assert.Empty(entries[0].Entries);
        Assert.Equal(AuditChainVerdict.Intact, AuditChainVerifier.Verify(LogPath).Verdict);
    }

    [Fact]
    public void AMaximalRunLine_Fits()
    {
        var entries = Enumerable.Range(0, 32).Select(i => $"env/acme-api/KEY_{i}_" + new string('K', 40)).ToList();
        Assert.True(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(entries)) <= 2048);

        var line = RunLine(entries, new string('é', AuditRecord.CommandLength)) with
        {
            Client = new AuditClient(new string('n', 64), new string('v', 64), new string('l', 64)),
            Args = AuditArgs.ForRun("env/" + new string('p', 100), new string('r', 2000)),
        };

        using var log = Open();
        Assert.True(log.TryAppend(line, out var error), error);
        Assert.Contains("command_sha256", Lines()[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnOversizedRunLine_IsRefused()
    {
        var entries = Enumerable.Range(0, 200).Select(i => $"env/acme-api/KEY_{i}_" + new string('K', 60)).ToList();

        using var log = Open();
        Assert.False(log.TryAppend(RunLine(entries), out var error));
        Assert.Contains("over the", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadFrom_ReadsOnlyNewCompleteLines()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(RunLine(), out _));
        }

        Assert.True(AuditReader.TryReadFrom(LogPath, AuditPosition.Start, out var first, out var next, out _, out _));
        Assert.Single(first);

        File.AppendAllText(LogPath, "{\"partial\":");
        Assert.True(AuditReader.TryReadFrom(LogPath, next, out var none, out var same, out _, out _));
        Assert.Empty(none);
        Assert.Equal(next, same);

        using (var log = Open())
        {
            Assert.True(log.TryAppend(RunLine(), out _));
        }

        Assert.True(AuditReader.TryReadFrom(LogPath, next, out var later, out var after, out var unreadable, out _));
        Assert.Single(later);
        Assert.Equal(1, unreadable);
        Assert.Equal(3, after.Line);
        Assert.Equal(new FileInfo(LogPath).Length, after.Offset);
    }

    [Fact]
    public void TryReadFrom_RestartsWhenTheFileShrank()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(RunLine(), out _));
            Assert.True(log.TryAppend(RunLine(), out _));
        }

        Assert.True(AuditReader.TryReadFrom(LogPath, AuditPosition.Start, out _, out var next, out _, out _));

        File.Delete(LogPath);
        using (var log = Open())
        {
            Assert.True(log.TryAppend(RunLine(), out _));
        }

        Assert.True(AuditReader.TryReadFrom(LogPath, next, out var entries, out var restarted, out _, out _));
        var entry = Assert.Single(entries);
        Assert.Equal(1, entry.Line);
        Assert.Equal(1, restarted.Line);
    }
}
