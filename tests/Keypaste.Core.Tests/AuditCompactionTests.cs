using System.Security.Cryptography;
using System.Text;
using Keypaste.Core.Activity;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The vault's owner folds old lookups, removes whole expired days and seals what it kept (PRODUCT §3.3).</summary>
public sealed class AuditCompactionTests : IDisposable
{
    private const string _vault = "0123456789abcdef";

    private static readonly DateTimeOffset _today = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-compaction-tests-").FullName;
    private readonly AuditCheckpointKey _key = new(RandomNumberGenerator.GetBytes(32), _vault);

    private string LogPath => Path.Combine(_directory, "audit.jsonl");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Lookups_older_than_30_days_fold_into_one_line_per_agent_per_day_and_every_other_line_keeps_its_bytes()
    {
        Write(_today.AddDays(-40), Lookup("claude-code", AuditTools.List, granted: true), Lookup("claude-code", AuditTools.List, granted: true), Lookup("claude-code", AuditTools.Find, granted: false), Lookup("cursor", AuditTools.List, granted: true), Release("claude-code"));
        Write(_today.AddDays(-39), Lookup("claude-code", AuditTools.List, granted: true));
        Write(_today.AddDays(-29), Lookup("claude-code", AuditTools.List, granted: true));
        Write(_today, Release("claude-code"));
        var before = Lines();

        var result = Compact();

        Assert.Equal((AuditCompactionOutcome.Compacted, 5, 0), (result.Outcome, result.Folded, result.Removed));
        var after = Lines();
        Assert.Contains("\"tool\":\"log_compaction\"", after[0], StringComparison.Ordinal);
        Assert.Equal(before.Where(line => !IsOldLookup(line)), after.Where(line => !line.Contains("\"tool\":\"log_", StringComparison.Ordinal)));
        Assert.Equal(3, after.Count(line => line.Contains("\"tool\":\"log_summary\"", StringComparison.Ordinal)));

        Assert.True(AuditReader.TryRead(LogPath, out var entries, out var unreadable, out _));
        Assert.Equal(0, unreadable);
        var summaries = entries.Where(entry => entry.Summary is not null).ToList();
        Assert.Equal(["claude-code", "cursor", "claude-code"], summaries.Select(entry => entry.Client));
        Assert.Equal(["2026-10-06", "2026-10-06", "2026-10-07"], summaries.Select(entry => entry.Summary!.Day));
        Assert.Equal(
            [new AuditFoldCount(AuditTools.Find, AuditDecision.Denied, 1), new AuditFoldCount(AuditTools.List, AuditDecision.Granted, 2)],
            summaries[0].Summary!.Counts);
        Assert.Equal(3, summaries[0].Requests);
        Assert.Equal("3 lookups on 2026-10-06: 2 listings, 1 search, 1 refused", AuditText.Folded(summaries[0]));
        Assert.DoesNotContain(entries, entry => entry.IsCompaction);

        var report = AuditChainVerifier.Verify(LogPath, checkpoints: _key);
        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(AuditCompactionCheck.Verified, report.Compaction);
        Assert.Equal(before.Length, report.CompactedFrom);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.UnsignedRecords);
    }

    [Fact]
    public void Retention_removes_only_whole_days_behind_one_line_that_a_later_removal_carries_forward()
    {
        Write(_today.AddDays(-400), Release("claude-code"), Lookup("claude-code", AuditTools.List, granted: true));
        Write(_today.AddDays(-366), Release("claude-code"));
        Write(_today.AddDays(-365), Release("claude-code"));
        Write(_today.AddDays(-10), Release("claude-code"));

        var first = Compact();

        Assert.Equal((AuditCompactionOutcome.Compacted, 3), (first.Outcome, first.Removed));
        Assert.True(AuditReader.TryRead(LogPath, out var entries, out _, out _));
        var removal = Assert.Single(entries, entry => entry.Removal is not null).Removal!;
        Assert.Equal(("2025-10-11", "2025-11-14", 3), (removal.From, removal.To, removal.Count));
        Assert.Equal(["2025-11-15", "2026-11-05"], entries.Where(entry => entry.Removal is null).Select(entry => entry.Timestamp[..10]));

        Write(_today.AddDays(30), Release("claude-code"));
        var second = Compact(_today.AddDays(30));

        Assert.Equal(AuditCompactionOutcome.Compacted, second.Outcome);
        Assert.True(AuditReader.TryRead(LogPath, out var later, out _, out _));
        var carried = Assert.Single(later, entry => entry.Removal is not null).Removal!;
        Assert.Equal(("2025-10-11", "2025-11-15", 4), (carried.From, carried.To, carried.Count));
        Assert.Equal(AuditChainVerdict.Intact, AuditChainVerifier.Verify(LogPath, checkpoints: _key).Verdict);
    }

    [Fact]
    public void Nothing_old_enough_and_forever_leave_the_file_untouched()
    {
        Write(_today.AddDays(-500), Release("claude-code"));
        Write(_today.AddDays(-5), Lookup("claude-code", AuditTools.List, granted: true));
        var before = File.ReadAllBytes(LogPath);

        Assert.Equal(AuditCompactionOutcome.Unchanged, Compact(keepDays: AuditRetention.Forever).Outcome);
        Assert.Equal(before, File.ReadAllBytes(LogPath));
    }

    [Fact]
    public void The_last_lines_day_is_kept_whole_so_the_next_append_still_links()
    {
        Write(_today.AddDays(-400), Lookup("claude-code", AuditTools.List, granted: true), Lookup("claude-code", AuditTools.List, granted: true));

        Assert.Equal(AuditCompactionOutcome.Unchanged, Compact().Outcome);

        Write(_today, Release("claude-code"));
        Assert.Equal(AuditCompactionOutcome.Compacted, Compact().Outcome);
        Assert.Equal(AuditChainVerdict.Intact, AuditChainVerifier.Verify(LogPath, checkpoints: _key).Verdict);
    }

    [Fact]
    public void Without_the_key_a_compacted_log_verifies_with_its_seal_reported_not_checked()
    {
        CompactedLog();

        var report = AuditChainVerifier.Verify(LogPath);

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(AuditCompactionCheck.NotChecked, report.Compaction);
        Assert.Contains("not checked", AuditText.Compacted(report), StringComparison.Ordinal);
        Assert.Contains(AuditText.Compacted(report), AuditText.Verdict(report));
    }

    [Fact]
    public void A_seal_recomputed_without_the_vaults_key_is_forged_under_it()
    {
        CompactedLog();
        var lines = Lines();
        var record = lines[0];
        var mac = record[^(AuditChain.ChainFooterBytes + 65)..^(AuditChain.ChainFooterBytes + 1)];
        lines[0] = Resealed(record.Replace(mac, new string('a', 64), StringComparison.Ordinal));
        File.WriteAllText(LogPath, string.Join('\n', lines) + "\n");

        var report = AuditChainVerifier.Verify(LogPath, checkpoints: _key);

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Equal(AuditCompactionCheck.Failed, report.Compaction);
        Assert.Contains(report.Findings, finding => finding is { Line: 1, Fault: AuditChainFault.Forged });
        Assert.Equal(AuditChainVerdict.Intact, AuditChainVerifier.Verify(LogPath).Verdict);
    }

    [Fact]
    public void A_kept_line_removed_from_the_sealed_lines_breaks_the_seal_with_or_without_the_key()
    {
        CompactedLog();
        var lines = Lines().ToList();
        var kept = lines.FindIndex(line => line.Contains("\"tool\":\"request_credential\"", StringComparison.Ordinal));
        lines.RemoveAt(kept);
        File.WriteAllText(LogPath, string.Join('\n', lines) + "\n");

        foreach (var key in new[] { _key, null })
        {
            var report = AuditChainVerifier.Verify(LogPath, checkpoints: key);

            Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
            Assert.Contains(report.Findings, finding => finding is { Line: 1, Fault: AuditChainFault.Unsealed });
        }
    }

    [Fact]
    public void A_log_that_does_not_verify_or_was_sealed_under_another_vault_is_not_compacted()
    {
        CompactedLog();
        Write(_today.AddDays(40), Release("claude-code"));
        var before = File.ReadAllBytes(LogPath);

        var other = AuditCompaction.Compact(LogPath, new AuditCheckpointKey(RandomNumberGenerator.GetBytes(32), "fedcba9876543210"), 365, _today.AddDays(40), "session");
        Assert.Equal(AuditCompactionOutcome.Refused, other.Outcome);

        var lines = Lines();
        lines[^1] = lines[^1].Replace("claude-code", "claude-c0de", StringComparison.Ordinal);
        File.WriteAllText(LogPath, string.Join('\n', lines) + "\n");
        var edited = File.ReadAllBytes(LogPath);

        Assert.Equal(AuditCompactionOutcome.Refused, Compact(_today.AddDays(40)).Outcome);
        Assert.Equal(edited, File.ReadAllBytes(LogPath));
        Assert.NotEqual(before, edited);
    }

    [Fact]
    public void A_compaction_that_stops_before_its_rename_leaves_the_old_file_and_nothing_beside_it()
    {
        OldLog();
        var before = File.ReadAllBytes(LogPath);

        Assert.Throws<InvalidOperationException>(() => AuditCompaction.Compact(LogPath, _key, 365, _today, "session", () => throw new InvalidOperationException("crash")));

        Assert.Equal(before, File.ReadAllBytes(LogPath));
        Assert.DoesNotContain(Directory.GetFiles(_directory), file => file.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void A_line_another_writer_appends_during_compaction_is_kept_and_later_lines_link_to_it()
    {
        OldLog();
        Assert.True(AuditLog.TryOpen(LogPath, new ManualClock(_today), out var other, out var error), error);
        using var bridge = other;

        var result = AuditCompaction.Compact(LogPath, _key, 365, _today, "session", () => Assert.True(bridge.TryAppend(Release("bridge"), out var failure), failure));

        Assert.Equal(AuditCompactionOutcome.Compacted, result.Outcome);
        Assert.Contains(Lines(), line => line.Contains("\"name\":\"bridge\"", StringComparison.Ordinal));
        Assert.True(bridge.TryAppend(Release("bridge-after"), out var later), later);

        var report = AuditChainVerifier.Verify(LogPath, checkpoints: _key);
        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task Appends_racing_compactions_are_all_kept()
    {
        OldLog();
        Assert.True(AuditLog.TryOpen(LogPath, new ManualClock(_today), out var other, out var error), error);
        using var bridge = other;
        const int count = 200;

        var appending = Task.Run(() =>
        {
            for (var i = 0; i < count; i++)
            {
                Assert.True(bridge.TryAppend(Release($"racer-{i}"), out var failure), failure);
            }
        }, TestContext.Current.CancellationToken);

        for (var day = 0; !appending.IsCompleted; day++)
        {
            Compact(_today.AddDays(day % 3));
        }

        await appending;

        var lines = Lines();
        Assert.Equal(count, lines.Count(line => line.Contains("\"name\":\"racer-", StringComparison.Ordinal)));
        Assert.Equal(AuditChainVerdict.Intact, AuditChainVerifier.Verify(LogPath, checkpoints: _key).Verdict);
    }

    [Fact]
    public void An_incremental_reader_starts_again_on_a_compacted_file_even_when_it_is_no_shorter()
    {
        OldLog();
        Assert.True(AuditReader.TryReadFrom(LogPath, AuditPosition.Start, out _, out var position, out _, out _));

        Compact();
        Write(_today, Enumerable.Range(0, 20).Select(i => Release($"after-{i}")).ToArray());
        Assert.True(new FileInfo(LogPath).Length >= position.Offset);

        Assert.True(AuditReader.TryReadFrom(LogPath, position, out var read, out var next, out var unreadable, out _));

        Assert.Equal(0, unreadable);
        Assert.Equal(2, read[0].Line);
        Assert.Contains(read, entry => entry.Summary is not null);
        Assert.Equal(Lines().Length, next.Line);
    }

    [Fact]
    public void Cards_and_item_activity_pass_over_summary_and_removal_lines()
    {
        Write(_today.AddDays(-400), Release("claude-code"));
        Write(_today.AddDays(-40), Lookup("claude-code", AuditTools.List, granted: true));
        Write(_today, Release("claude-code"));
        Compact();
        Assert.True(AuditReader.TryRead(LogPath, out var entries, out _, out _));
        Assert.Contains(entries, entry => entry.Summary is not null);
        Assert.Contains(entries, entry => entry.Removal is not null);

        var cards = McpClientCards.Build([], entries, _vault, AgentRules.Defaults, [], _today);
        var activity = EntryActivity.Build(entries, _vault, ApproverActivity.None, [], _today);

        Assert.Contains(cards, card => card.Key == "name:claude-code");
        Assert.NotNull(activity);
        Assert.Contains(AuditText.Table(entries), line => line.Contains("from 2025-10-11 to 2025-10-11 removed", StringComparison.Ordinal));
    }

    [Fact]
    public void The_owner_compacts_nothing_while_its_vault_holds_no_key_and_compacts_once_it_does()
    {
        OldLog();
        var before = File.ReadAllBytes(LogPath);
        var vaultPath = Path.Combine(_directory, "vault.kdbx");
        using var vault = Vault.Create(vaultPath, EnvStoreTests.MasterPassword);
        vault.Save();
        using var compactions = new AuditCompactions(LogPath, () => vault, VaultIdentity.Of(_directory, vaultPath), "session", () => 365, new ManualClock(_today));

        Assert.Null(compactions.Run());
        Assert.Equal(before, File.ReadAllBytes(LogPath));

        Assert.True(AuditCheckpoint.WriteNewKey(vault));
        vault.Save();

        Assert.Equal(AuditCompactionOutcome.Compacted, compactions.Run()?.Outcome);
        Assert.Equal(AuditCompactionCheck.Verified, AuditChainVerifier.Verify(LogPath, checkpoints: AuditCheckpointKey.Read(vault, VaultIdentity.Of(_directory, vaultPath))).Compaction);
    }

    [Fact]
    public void An_unlock_whose_vault_holds_a_key_creates_no_log_when_there_is_none()
    {
        var vaultPath = Path.Combine(_directory, "vault.kdbx");
        using var vault = Vault.Create(vaultPath, EnvStoreTests.MasterPassword);
        Assert.True(AuditCheckpoint.WriteNewKey(vault));
        vault.Save();
        using var compactions = new AuditCompactions(LogPath, () => vault, VaultIdentity.Of(_directory, vaultPath), "session", () => 365, new ManualClock(_today));

        Assert.Equal(AuditCompactionOutcome.Refused, compactions.Run()?.Outcome);
        Assert.False(File.Exists(LogPath));
    }

    private void OldLog()
    {
        Write(_today.AddDays(-40), Lookup("claude-code", AuditTools.List, granted: true), Release("claude-code"), Lookup("claude-code", AuditTools.Find, granted: true));
        Write(_today, Release("claude-code"));
    }

    private void CompactedLog()
    {
        OldLog();
        Assert.Equal(AuditCompactionOutcome.Compacted, Compact().Outcome);
    }

    private AuditCompactionResult Compact(DateTimeOffset? now = null, int keepDays = 365) =>
        AuditCompaction.Compact(LogPath, _key, keepDays, now ?? _today, "session");

    private void Write(DateTimeOffset at, params AuditRecord[] records)
    {
        Assert.True(AuditLog.TryOpen(LogPath, new ManualClock(at), out var log, out var error), error);
        using (log)
        {
            foreach (var record in records)
            {
                Assert.True(log.TryAppend(record, out var failure), failure);
            }
        }
    }

    private string[] Lines() => File.ReadAllText(LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsOldLookup(string line) =>
        (line.Contains("\"tool\":\"list_entry_names\"", StringComparison.Ordinal) || line.Contains("\"tool\":\"find_access\"", StringComparison.Ordinal))
        && string.CompareOrdinal(line[13..23], "2026-10-16") < 0;

    /// <summary>The line with its hash recomputed, as anyone can without the vault.</summary>
    private static string Resealed(string line)
    {
        var committed = line[..^AuditChain.ChainSuffixBytes];
        return committed + ",\"hash\":\"" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(committed))) + "\"}";
    }

    private static AuditRecord Lookup(string agent, string tool, bool granted) => new()
    {
        Tool = tool,
        Client = new AuditClient(agent, "1.0", null),
        Decision = granted ? AuditDecision.Granted : AuditDecision.Denied,
        Method = granted ? AuditMethod.Exposure : AuditMethod.OutOfScope,
        Reason = "a lookup",
        Vault = _vault,
    };

    private static AuditRecord Release(string agent) => new()
    {
        Tool = AuditTools.Credential,
        Client = new AuditClient(agent, "1.0", null),
        Args = new AuditArgs { Entry = "env/app/KEY", Field = "password" },
        Decision = AuditDecision.Granted,
        Method = AuditMethod.Prompt,
        Reason = "approved",
        Vault = _vault,
    };
}
