using System.Security.Cryptography;
using System.Text;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The owner signs its log with the vault's checkpoint key, so a chain recomputed without the vault no longer verifies under it.</summary>
public sealed class AuditCheckpointTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-checkpoint-tests-").FullName;
    private readonly Vault _vault;
    private readonly OwnerLog _log;

    public AuditCheckpointTests()
    {
        _vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        Assert.True(AuditCheckpoint.WriteNewKey(_vault));
        _vault.Save();
        _log = new OwnerLog(_directory);
    }

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private VaultIdentity Identity => VaultIdentity.Of(_directory, VaultPath);

    public void Dispose()
    {
        _log.Dispose();
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_owner_signs_after_every_64_lines_it_writes()
    {
        var checkpoints = Checkpoints();

        Append(checkpoints, AuditCheckpoint.Every - 1);
        Assert.Empty(CheckpointLines());

        Append(checkpoints, 1);
        Assert.Equal(new[] { AuditCheckpoint.Every + 1 }, CheckpointLines());

        Append(checkpoints, AuditCheckpoint.Every);
        Assert.Equal(new[] { AuditCheckpoint.Every + 1, (2 * AuditCheckpoint.Every) + 2 }, CheckpointLines());
    }

    [Fact]
    public void A_vault_without_a_key_is_never_saved_for_one_and_its_next_save_writes_the_key_a_later_session_keeps()
    {
        var path = Path.Combine(_directory, "fresh.kdbx");
        var identity = VaultIdentity.Of(_directory, path);
        using var fresh = Vault.Create(path, EnvStoreTests.MasterPassword);
        fresh.Save();
        var written = File.ReadAllBytes(path);
        var checkpoints = new AuditCheckpoints(() => _log.Log, () => fresh, identity, "session-one");

        Append(checkpoints, AuditCheckpoint.Every);

        Assert.Equal(written, File.ReadAllBytes(path));
        Assert.Empty(CheckpointLines());

        fresh.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "MINE", Password = "secret-mine" });
        fresh.Save();
        Append(checkpoints, 1);

        Assert.Equal(new[] { AuditCheckpoint.Every + 2 }, CheckpointLines());

        using var reopened = Vault.Open(path, EnvStoreTests.MasterPassword);
        var later = new AuditCheckpoints(() => _log.Log, () => reopened, identity, "session-two");
        Append(later, 1);
        later.Seal();

        var report = AuditChainVerifier.Verify(_log.Path, checkpoints: Assert.IsType<AuditCheckpointKey>(AuditCheckpointKey.Read(reopened, identity)));
        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(2, report.SignedCheckpoints);
    }

    [Fact]
    public void A_merge_of_another_programs_save_with_the_owners_change_writes_the_key_and_a_merge_alone_saves_nothing()
    {
        var path = Path.Combine(_directory, "fresh.kdbx");
        var identity = VaultIdentity.Of(_directory, path);
        using var fresh = Vault.Create(path, EnvStoreTests.MasterPassword);
        fresh.Save();
        Append(new AuditCheckpoints(() => _log.Log, () => fresh, identity, "session-one"), 1);

        SaveElsewhere(path, "THEIRS");
        var theirs = File.ReadAllBytes(path);
        var updated = fresh.MergeAnotherSave();
        using var copy = updated.Copy;

        Assert.Equal(VaultMergeSaveOutcome.Updated, updated.Outcome);
        Assert.Equal(theirs, File.ReadAllBytes(path));

        copy!.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "MINE", Password = "secret-mine" });
        SaveElsewhere(path, "LATER");
        Assert.Throws<VaultChangedOnDiskException>(copy.Save);
        var merged = copy.MergeAnotherSave();
        using var saved = merged.Copy;

        Assert.Equal(VaultMergeSaveOutcome.Saved, merged.Outcome);
        using var file = Vault.Open(path, EnvStoreTests.MasterPassword);
        Assert.NotNull(AuditCheckpointKey.Read(file, identity));
    }

    [Fact]
    public void Ending_the_session_signs_its_tail_and_an_idle_session_signs_nothing()
    {
        var busy = Checkpoints();
        Append(busy, 3);
        busy.Seal();

        Assert.Equal(new[] { 4 }, CheckpointLines());

        var idle = Checkpoints();
        idle.Watched();
        idle.Seal();

        Assert.Equal(4, Lines().Length);
    }

    [Fact]
    public void A_signed_log_verifies_and_reports_its_unsigned_tail()
    {
        Append(Checkpoints(), AuditCheckpoint.Every + 5);

        var withoutKey = AuditChainVerifier.Verify(_log.Path);
        var report = AuditChainVerifier.Verify(_log.Path, checkpoints: Key());

        Assert.Equal(AuditChainVerdict.Intact, withoutKey.Verdict);
        Assert.Null(withoutKey.SignedCheckpoints);
        Assert.Equal(1, withoutKey.Checkpoints);
        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(1, report.SignedCheckpoints);
        Assert.Equal(5, report.UnsignedRecords);
        Assert.Contains("Unsigned tail: 5 records after the last signed checkpoint.", AuditText.Verdict(report));
    }

    [Fact]
    public void A_chain_recomputed_without_the_key_passes_the_chain_and_fails_the_checkpoint()
    {
        Append(Checkpoints(), AuditCheckpoint.Every + 2);
        var lines = Lines();
        lines[2] = lines[2].Replace("\"reason\":\"record 2\"", "\"reason\":\"record X\"", StringComparison.Ordinal);
        Rewrite(Rechained(lines));

        var report = AuditChainVerifier.Verify(_log.Path, checkpoints: Key());

        Assert.Equal(AuditChainVerdict.Intact, AuditChainVerifier.Verify(_log.Path).Verdict);
        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Equal(AuditCheckpoint.Every + 1, Assert.Single(report.Findings, f => f.Fault == AuditChainFault.Forged).Line);
        Assert.Equal(0, report.SignedCheckpoints);
    }

    [Fact]
    public void Another_key_for_the_same_vault_does_not_verify()
    {
        Append(Checkpoints(), AuditCheckpoint.Every);

        var report = AuditChainVerifier.Verify(_log.Path, checkpoints: new AuditCheckpointKey(RandomNumberGenerator.GetBytes(32), Identity.Key));

        Assert.Equal(AuditChainVerdict.Broken, report.Verdict);
        Assert.Contains(report.Findings, f => f.Fault == AuditChainFault.Forged);
    }

    [Fact]
    public void A_cut_tail_is_reported_as_unsigned_and_a_cut_past_the_checkpoint_signs_nothing()
    {
        Append(Checkpoints(), AuditCheckpoint.Every + 5);
        var lines = Lines();

        Rewrite(lines[..^3]);
        var cutAfter = AuditChainVerifier.Verify(_log.Path, checkpoints: Key());

        Rewrite(lines[..AuditCheckpoint.Every]);
        var cutPast = AuditChainVerifier.Verify(_log.Path, checkpoints: Key());

        Assert.Equal(AuditChainVerdict.Intact, cutAfter.Verdict);
        Assert.Equal(2, cutAfter.UnsignedRecords);
        Assert.Equal(AuditChainVerdict.Intact, cutPast.Verdict);
        Assert.Equal(0, cutPast.SignedCheckpoints);
        Assert.Equal(AuditCheckpoint.Every, cutPast.UnsignedRecords);
        Assert.Contains("No checkpoint is signed by this vault's key, so no record is signed.", AuditText.Verdict(cutPast));
    }

    [Fact]
    public void A_checkpoint_naming_another_vault_is_counted_and_not_checked()
    {
        var other = new AuditCheckpointKey(RandomNumberGenerator.GetBytes(32), "0123456789abcdef");
        Append(Checkpoints(), 1);
        Assert.True(_log.Log.TryCheckpoint(other, "session-two", out var error), error);

        var report = AuditChainVerifier.Verify(_log.Path, checkpoints: Key());

        Assert.Equal(AuditChainVerdict.Intact, report.Verdict);
        Assert.Equal(1, report.Checkpoints);
        Assert.Equal(0, report.SignedCheckpoints);
        Assert.Contains("Not checked: 1 checkpoint naming another vault.", AuditText.Verdict(report));
    }

    [Fact]
    public void A_checkpoint_is_a_chained_record_that_listings_skip()
    {
        var checkpoints = Checkpoints();
        Append(checkpoints, 2);
        checkpoints.Seal();
        var line = Encoding.UTF8.GetBytes(Lines()[^1]);

        Assert.True(AuditReader.TryRead(_log.Path, out var entries, out var unreadable, out var error), error);
        Assert.Equal(2, entries.Count);
        Assert.Equal(0, unreadable);
        Assert.Equal(AuditLineKind.Chained, AuditChain.Inspect(line).Kind);
        Assert.True(AuditCheckpoint.TryRead(line, out var vault, out _));
        Assert.Equal(Identity.Key, vault);
        Assert.True(line.Length < AuditLog.MaximumRecordBytes);
    }

    [Fact]
    public void A_checkpoint_that_cannot_be_written_refuses_nothing_and_the_next_line_retries()
    {
        Vault? open = null;
        var checkpoints = new AuditCheckpoints(() => _log.Log, () => open, Identity, "session-one");

        Append(checkpoints, AuditCheckpoint.Every + 3);
        Assert.Empty(CheckpointLines());

        open = _vault;
        Append(checkpoints, 1);

        Assert.Equal(new[] { AuditCheckpoint.Every + 5 }, CheckpointLines());
    }

    private AuditCheckpoints Checkpoints() => new(() => _log.Log, () => _vault, Identity, "session-one");

    private static void SaveElsewhere(string path, string title)
    {
        using var other = Vault.Open(path, EnvStoreTests.MasterPassword);
        other.AddEntry(new VaultEntry { GroupPath = "env/app", Title = title, Password = "secret-theirs" });
        other.Save();
    }

    private AuditCheckpointKey Key() => Assert.IsType<AuditCheckpointKey>(AuditCheckpointKey.Read(_vault, Identity));

    private static void Append(AuditCheckpoints checkpoints, int count)
    {
        var log = Assert.IsType<AuditLog>(checkpoints.Watched());

        for (var i = 0; i < count; i++)
        {
            Assert.True(log.TryAppend(Request(i), out var error), error);
        }
    }

    private static AuditRecord Request(int i) => new()
    {
        Tool = "list_entry_names",
        Client = new AuditClient("claude-code", "1.0", null),
        Decision = AuditDecision.Denied,
        Method = AuditMethod.VaultLocked,
        Reason = $"record {i}",
    };

    private string[] Lines() => _log.Text().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private int[] CheckpointLines() =>
        [.. Lines().Select((line, index) => (line, index)).Where(l => l.line.Contains("\"method\":\"checkpoint\"", StringComparison.Ordinal)).Select(l => l.index + 1)];

    private void Rewrite(IEnumerable<string> lines)
    {
        _log.Dispose();
        File.WriteAllText(_log.Path, string.Join('\n', lines) + "\n");
    }

    /// <summary>What a same-user attacker can do without the vault: rehash every line and relink it.</summary>
    private static IEnumerable<string> Rechained(IEnumerable<string> lines)
    {
        var previous = AuditChain.Genesis;

        foreach (var line in lines)
        {
            var committed = $"{line[..^AuditChain.ChainFooterBytes]},\"prev\":\"{previous}\"";
            previous = AuditChain.HashOf(Encoding.UTF8.GetBytes(committed));
            yield return $"{committed},\"hash\":\"{previous}\"}}";
        }
    }
}
