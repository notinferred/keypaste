using Keypaste.Core.CrashReports;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 4.11a: the crash-report outbox keeps only what the scrubber wrote, owner-only, merged, within its limits, and nothing once
/// cleared (D-0559).
/// </summary>
/// <remarks>
/// The mutation each must fail: a file kept that is not exactly a scrubbed report, a limit or the age rule not applied, the
/// newest report dropped before the oldest, a merge lost to a removal, or a file left behind by clearing.
/// </remarks>
public sealed class CrashOutboxTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-crash-outbox-").FullName;
    private readonly ManualClock _clock = new(CrashFixtures.At);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Folder => Path.Combine(_directory, "crash-reports");

    [Fact]
    public void A_report_is_kept_as_the_scrubbers_bytes_owner_only()
    {
        var outbox = new CrashOutbox(Folder, _clock);
        var report = Report("agents");

        Assert.True(outbox.Add(report));

        var file = Assert.Single(Directory.GetFiles(Folder));
        Assert.Equal(report.Json.ToArray(), File.ReadAllBytes(file));
        Assert.Equal(report.Text, Assert.Single(outbox.Waiting()).Text);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Folder));
        }
    }

    [Fact]
    public void Nothing_is_written_until_a_report_is_added()
    {
        var outbox = new CrashOutbox(Folder, _clock);

        Assert.Empty(outbox.Waiting());
        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void Identical_reports_merge_into_one_with_a_count()
    {
        var outbox = new CrashOutbox(Folder, _clock);
        var thrown = CrashFixtures.Thrown(() => throw new InvalidOperationException());

        Assert.True(outbox.Add(CrashScrubber.Crash(thrown, CrashFixtures.Context)));
        Assert.True(outbox.Add(CrashScrubber.Crash(thrown, CrashFixtures.Context)));

        var merged = Assert.Single(outbox.Waiting());
        Assert.Equal(2, merged.Count);
        Assert.Contains("\"count\":2", merged.Text, StringComparison.Ordinal);

        Assert.True(outbox.Add(CrashScrubber.UnobservedTask(thrown, CrashFixtures.Context)));
        Assert.Equal(2, outbox.Waiting().Count);
    }

    [Fact]
    public void Past_twenty_reports_the_oldest_are_dropped()
    {
        var outbox = new CrashOutbox(Folder, _clock);

        for (var i = 0; i < CrashOutbox.MaximumReports; i++)
        {
            Assert.True(outbox.Add(Report($"screen-{i}")));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(CrashOutbox.MaximumReports, outbox.Waiting().Count);

        Assert.True(outbox.Add(Report("screen-20")));

        var waiting = outbox.Waiting();
        Assert.Equal(CrashOutbox.MaximumReports, waiting.Count);
        Assert.DoesNotContain(waiting, report => report.Text.Contains("\"screen\":\"screen-0\"", StringComparison.Ordinal));
        Assert.Contains("\"screen\":\"screen-1\"", waiting[0].Text, StringComparison.Ordinal);
        Assert.Contains("\"screen\":\"screen-20\"", waiting[^1].Text, StringComparison.Ordinal);
        Assert.Equal(CrashOutbox.MaximumReports, Directory.GetFiles(Folder).Length);
    }

    [Fact]
    public void Past_256_KiB_the_oldest_are_dropped()
    {
        var outbox = new CrashOutbox(Folder, _clock);
        var large = CrashFixtures.Large();
        var sizes = 0;

        for (var i = 0; i < CrashOutbox.MaximumReports; i++)
        {
            var report = CrashScrubber.Crash(large, CrashFixtures.Context with { Screen = $"screen-{i}" });
            sizes += report.Json.Length;
            Assert.True(outbox.Add(report));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        var waiting = outbox.Waiting();

        Assert.True(sizes > CrashOutbox.MaximumBytes, $"the reports came to {sizes} bytes, within the cap");
        Assert.True(waiting.Count < CrashOutbox.MaximumReports);
        Assert.InRange(waiting.Sum(report => report.Json.Length), 0, CrashOutbox.MaximumBytes);
        Assert.DoesNotContain(waiting, report => report.Text.Contains("\"screen\":\"screen-0\"", StringComparison.Ordinal));
        Assert.Contains("\"screen\":\"screen-19\"", waiting[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_from_more_than_thirty_days_ago_is_dropped()
    {
        var outbox = new CrashOutbox(Folder, _clock);

        Assert.True(outbox.Add(CrashScrubber.Crash(Thrown(), new CrashContext(CrashFixtures.At.AddDays(-CrashOutbox.MaximumDays), null, "kept"))));
        Assert.True(outbox.Add(CrashScrubber.Crash(Thrown(), new CrashContext(CrashFixtures.At.AddDays(-CrashOutbox.MaximumDays - 1), null, "dropped"))));

        var kept = Assert.Single(outbox.Waiting());
        Assert.Contains("\"screen\":\"kept\"", kept.Text, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Folder));
    }

    [Fact]
    public void A_file_that_is_not_exactly_a_scrubbed_report_is_deleted()
    {
        var outbox = new CrashOutbox(Folder, _clock);
        var report = Report("agents");
        Assert.True(outbox.Add(report));

        var kept = Assert.Single(Directory.GetFiles(Folder));
        var other = Report("settings");
        var renamed = Path.Combine(Folder, new string('0', 64) + ".json");
        var edited = Path.Combine(Folder, Path.GetFileName(PathOf(other)));
        var garbage = Path.Combine(Folder, "notes.json");
        var stage = Path.Combine(Folder, ".partial.tmp");

        File.WriteAllBytes(renamed, other.Json.ToArray());
        File.WriteAllText(edited, other.Text.Replace("\"hresult\":", "\"message\":\"the vault at /home/alice\",\"hresult\":", StringComparison.Ordinal));
        File.WriteAllText(garbage, "{\"kind\":\"crash\"}");
        File.WriteAllText(stage, "partial");

        Assert.Equal(report.Text, Assert.Single(outbox.Waiting()).Text);
        Assert.Equal([stage, kept], Directory.GetFiles(Folder).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Removing_keeps_a_failure_merged_since_the_report_was_read()
    {
        var outbox = new CrashOutbox(Folder, _clock);
        var report = Report("agents");
        Assert.True(outbox.Add(report));

        var read = Assert.Single(outbox.Waiting());
        Assert.True(outbox.Add(report));
        Assert.True(outbox.Remove(read));

        var merged = Assert.Single(outbox.Waiting());
        Assert.Equal(2, merged.Count);

        Assert.True(outbox.Remove(merged));
        Assert.Empty(outbox.Waiting());
    }

    [Fact]
    public void Clearing_deletes_every_report_and_the_folder()
    {
        var outbox = new CrashOutbox(Folder, _clock);
        Assert.True(outbox.Add(Report("agents")));
        Assert.True(outbox.Add(Report("settings")));

        Assert.True(outbox.Clear());

        Assert.False(Directory.Exists(Folder));
        Assert.Empty(outbox.Waiting());
    }

    private static Exception Thrown() => CrashFixtures.Thrown(() => throw new InvalidOperationException());

    private static CrashReport Report(string screen) => CrashScrubber.Crash(Thrown(), CrashFixtures.Context with { Screen = screen });

    private string PathOf(CrashReport report)
    {
        var probe = new CrashOutbox(Path.Combine(_directory, $"{Guid.NewGuid():N}"), _clock);
        Assert.True(probe.Add(report));
        return Assert.Single(Directory.GetFiles(probe.Folder));
    }
}
