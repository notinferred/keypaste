using Keypaste.Core.Recent;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What the recent-vaults list promises: it round-trips, it forgets on request, and a file it
/// cannot read costs a shortcut rather than the file.
/// </summary>
public sealed class RecentVaultsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "keypaste-recent-tests", Guid.NewGuid().ToString("n"));

    private string RecentFile => Path.Combine(_directory, "recent.toml");

    public RecentVaultsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void An_absent_file_reads_as_an_empty_list() =>
        Assert.Empty(RecentVaults.Load(RecentFile));

    [Fact]
    public void It_round_trips()
    {
        var when = new DateTimeOffset(2026, 7, 28, 9, 12, 44, TimeSpan.Zero);
        var vault = Path.Combine(_directory, "personal.kdbx");

        Assert.True(RecentVaults.Save(RecentFile, [new RecentVault(vault, when)]));

        var read = RecentVaults.Load(RecentFile);

        var only = Assert.Single(read);
        Assert.Equal(Path.GetFullPath(vault), only.Path);
        Assert.Equal(when, only.OpenedAt);
    }

    /// <summary>
    /// The reason paths are written with forward slashes: the parser refuses a backslash, and that
    /// refusal is a property of the policy file worth keeping.
    /// </summary>
    [Fact]
    public void The_file_it_writes_contains_no_backslash()
    {
        var vault = Path.Combine(_directory, "personal.kdbx");
        RecentVaults.Save(RecentFile, [new RecentVault(vault, DateTimeOffset.UtcNow)]);

        var text = File.ReadAllText(RecentFile);

        Assert.DoesNotContain('\\', text);
        Assert.Contains("personal.kdbx", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_the_same_vault_again_moves_it_to_the_front_without_duplicating()
    {
        var first = Path.Combine(_directory, "one.kdbx");
        var second = Path.Combine(_directory, "two.kdbx");
        var now = DateTimeOffset.UtcNow;

        var list = RecentVaults.Remember([], first, now);
        list = RecentVaults.Remember(list, second, now.AddMinutes(1));
        list = RecentVaults.Remember(list, first, now.AddMinutes(2));

        Assert.Equal(2, list.Count);
        Assert.Equal(Path.GetFullPath(first), list[0].Path);
        Assert.Equal(Path.GetFullPath(second), list[1].Path);
    }

    [Fact]
    public void The_cap_drops_the_oldest()
    {
        IReadOnlyList<RecentVault> list = [];
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < RecentVaults.Capacity + 5; i++)
        {
            list = RecentVaults.Remember(list, Path.Combine(_directory, $"v{i}.kdbx"), now.AddMinutes(i));
        }

        Assert.Equal(RecentVaults.Capacity, list.Count);
        Assert.Equal(Path.GetFullPath(Path.Combine(_directory, $"v{RecentVaults.Capacity + 4}.kdbx")), list[0].Path);
        Assert.DoesNotContain(list, v => v.Path.EndsWith("v0.kdbx", StringComparison.Ordinal));
    }

    [Fact]
    public void Forgetting_removes_exactly_one()
    {
        var first = Path.Combine(_directory, "one.kdbx");
        var second = Path.Combine(_directory, "two.kdbx");
        var now = DateTimeOffset.UtcNow;

        var list = RecentVaults.Remember(RecentVaults.Remember([], first, now), second, now);
        list = RecentVaults.Forget(list, first);

        var only = Assert.Single(list);
        Assert.Equal(Path.GetFullPath(second), only.Path);
    }

    /// <summary>
    /// Fail closed, and do not clobber. A file somebody is part-way through editing by hand must
    /// survive being read.
    /// </summary>
    [Fact]
    public void A_malformed_file_reads_as_empty_and_is_left_alone()
    {
        const string Broken = "[[vault]]\nthis is not a pair at all\n";
        File.WriteAllText(RecentFile, Broken);

        Assert.Empty(RecentVaults.Load(RecentFile));
        Assert.Equal(Broken, File.ReadAllText(RecentFile));
    }

    /// <summary>
    /// One unreadable section is skipped rather than voiding the file. A policy file is used whole
    /// or ignored whole because a rule nobody can read may have been the rule that said no; a
    /// forgotten shortcut is only a forgotten shortcut.
    /// </summary>
    [Fact]
    public void A_section_missing_its_path_is_skipped_and_the_rest_survive()
    {
        var good = RecentVaults.Portable(Path.Combine(_directory, "good.kdbx"));
        File.WriteAllText(
            RecentFile,
            $"[[vault]]\nopened_at = \"2026-07-28T09:12:44Z\"\n\n[[vault]]\npath = \"{good}\"\n");

        var only = Assert.Single(RecentVaults.Load(RecentFile));
        Assert.Equal(Path.GetFullPath(good), only.Path);
    }

    [Fact]
    public void A_section_with_no_timestamp_still_loads()
    {
        var vault = RecentVaults.Portable(Path.Combine(_directory, "v.kdbx"));
        File.WriteAllText(RecentFile, $"[[vault]]\npath = \"{vault}\"\n");

        var only = Assert.Single(RecentVaults.Load(RecentFile));
        Assert.Equal(DateTimeOffset.MinValue, only.OpenedAt);
    }

    [Fact]
    public void Saving_creates_the_directory_if_it_is_missing()
    {
        var nested = Path.Combine(_directory, "nested", "recent.toml");

        Assert.True(RecentVaults.Save(nested, [new RecentVault(Path.Combine(_directory, "v.kdbx"), DateTimeOffset.UtcNow)]));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Only_the_capacity_is_ever_written()
    {
        var vaults = Enumerable
            .Range(0, RecentVaults.Capacity + 3)
            .Select(i => new RecentVault(Path.Combine(_directory, $"v{i}.kdbx"), DateTimeOffset.UtcNow))
            .ToList();

        RecentVaults.Save(RecentFile, vaults);

        Assert.Equal(RecentVaults.Capacity, RecentVaults.Load(RecentFile).Count);
    }
}
