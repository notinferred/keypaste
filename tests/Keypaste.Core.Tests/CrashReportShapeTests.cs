using System.Text;
using System.Text.Json;
using Keypaste.Core.CrashReports;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 4.11a: a crash report holds exactly the fields D-0556 names, in one order, and <c>site/test/crash-vector.json</c>, which
/// keypaste.com's route is to accept, is byte for byte what the scrubber writes and reads back (D-0558).
/// </summary>
/// <remarks>
/// A field added to the report fails the comparison with the vector until the vector, the documents and the route change
/// with it; a report the scrubber would not have written fails to read back, so the outbox deletes it.
/// </remarks>
public sealed class CrashReportShapeTests
{
    private static readonly string[] _fields =
        ["schema", "kind", "site", "version", "build", "os", "os_version", "arch", "runtime", "uptime", "screen", "occurred_on", "count", "exception"];

    [Fact]
    public void A_report_holds_the_fields_in_order_with_this_runtimes_facts()
    {
        var thrown = Nested();
        using var document = JsonDocument.Parse(CrashScrubber.Crash(thrown, CrashFixtures.Context).Json);
        var root = document.RootElement;

        Assert.Equal(_fields, root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal("crash", root.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("site").ValueKind);
        Assert.Equal(CoreInfo.Version, root.GetProperty("version").GetString());
        Assert.Matches("^([0-9a-f]{40})?$", root.GetProperty("build").GetString() ?? string.Empty);
        Assert.Equal(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux", root.GetProperty("os").GetString());
        Assert.Matches(@"^\d+\.\d+(\.\d+)?$", root.GetProperty("os_version").GetString());
        Assert.Equal(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64", root.GetProperty("arch").GetString());
        Assert.Equal($"{Environment.Version.Major}.{Environment.Version.Minor}.{Environment.Version.Build}", root.GetProperty("runtime").GetString());
        Assert.Equal("<10m", root.GetProperty("uptime").GetString());
        Assert.Equal("agents", root.GetProperty("screen").GetString());
        Assert.Equal("2026-10-28", root.GetProperty("occurred_on").GetString());
        Assert.Equal(1, root.GetProperty("count").GetInt32());

        var exception = root.GetProperty("exception");
        Assert.Equal("System.InvalidOperationException", exception.GetProperty("type").GetString());
        Assert.Equal(thrown.HResult, exception.GetProperty("hresult").GetInt32());
        Assert.Equal(0, exception.GetProperty("omitted").GetInt32());
        Assert.Contains(exception.GetProperty("frames").EnumerateArray(), frame => frame.GetProperty("asm").GetString() == "Keypaste.Core.Tests");
        Assert.All(exception.GetProperty("frames").EnumerateArray(), frame => Assert.True(frame.GetProperty("il").GetInt32() >= -1));

        var inner = Assert.Single(exception.GetProperty("inner").EnumerateArray());
        Assert.Equal("System.IO.IOException", inner.GetProperty("type").GetString());
        Assert.NotEmpty(inner.GetProperty("frames").EnumerateArray());
        Assert.False(inner.TryGetProperty("inner", out _));
    }

    [Fact]
    public void Each_kind_holds_what_it_should()
    {
        var thrown = CrashFixtures.Thrown(() => throw new InvalidOperationException());

        Assert.Equal("unobserved-task |", KindAndSite(CrashScrubber.UnobservedTask(thrown, CrashFixtures.Context)));
        Assert.Equal("unexpected | LockedNotice.Show", KindAndSite(CrashScrubber.Unexpected(thrown, "LockedNotice.Show", CrashFixtures.Context)));
        Assert.Equal("unexpected | [unnamed]", KindAndSite(CrashScrubber.Unexpected(thrown, "the notice for /home/someone", CrashFixtures.Context)));

        using var ended = JsonDocument.Parse(CrashScrubber.EndedUnexpectedly(new CrashContext(CrashFixtures.At, null, null)).Json);
        Assert.Equal("ended-unexpectedly", ended.RootElement.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, ended.RootElement.GetProperty("exception").ValueKind);
        Assert.Equal(JsonValueKind.Null, ended.RootElement.GetProperty("uptime").ValueKind);
        Assert.Equal(JsonValueKind.Null, ended.RootElement.GetProperty("screen").ValueKind);
    }

    [Theory]
    [InlineData(0, "<1m")]
    [InlineData(59, "<1m")]
    [InlineData(60, "<10m")]
    [InlineData(599, "<10m")]
    [InlineData(600, "<1h")]
    [InlineData(3_599, "<1h")]
    [InlineData(3_600, "<1d")]
    [InlineData(86_399, "<1d")]
    [InlineData(86_400, ">1d")]
    [InlineData(3_000_000, ">1d")]
    public void Uptime_is_kept_only_as_a_bucket(int seconds, string bucket) =>
        Assert.Equal(bucket, CrashScrubber.Bucket(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData("agents", "agents")]
    [InlineData("env-sets", "env-sets")]
    [InlineData("Agents", "[unnamed]")]
    [InlineData("my vault", "[unnamed]")]
    [InlineData("/home/someone", "[unnamed]")]
    [InlineData("a-screen-name-longer-than-32-char", "[unnamed]")]
    public void A_screen_is_kept_only_as_a_lowercase_name(string screen, string kept) => Assert.Equal(kept, CrashScrubber.Screen(screen));

    [Fact]
    public void The_vector_is_byte_for_byte_what_the_scrubber_writes()
    {
        var cases = VectorReports();
        Assert.Equal(3, cases.Count);

        foreach (var report in cases)
        {
            var bytes = Encoding.UTF8.GetBytes(report);

            Assert.True(CrashScrubber.TryRead(bytes, out var read), report);
            Assert.Equal(bytes, read.Json.ToArray());
        }
    }

    [Fact]
    public void A_report_made_now_has_the_vectors_shape_for_each_kind()
    {
        var reports = VectorReports();
        var withoutInner = CrashFixtures.Thrown(() => throw new InvalidOperationException());

        Assert.Equal(Paths(reports[0]), Paths(CrashScrubber.Crash(Nested(), CrashFixtures.Context).Text));
        Assert.Equal(Paths(reports[1]), Paths(CrashScrubber.Unexpected(withoutInner, "LockedNotice.Show", CrashFixtures.Context).Text));
        Assert.Equal(Paths(reports[2]), Paths(CrashScrubber.EndedUnexpectedly(new CrashContext(CrashFixtures.At, null, null)).Text));
    }

    [Fact]
    public void A_field_added_to_a_report_is_found()
    {
        var crash = VectorReports()[0];
        var added = crash.Replace("\"hresult\":", "\"message\":\"x\",\"hresult\":", StringComparison.Ordinal);

        Assert.NotEqual(crash, added);
        Assert.NotEqual(Paths(crash), Paths(added));
        Assert.False(CrashScrubber.TryRead(Encoding.UTF8.GetBytes(added), out _));
    }

    [Theory]
    [InlineData("\"count\":1", "\"count\":0")]
    [InlineData("\"schema\":1", "\"schema\":2")]
    [InlineData("\"kind\":\"crash\"", "\"kind\":\"fatal\"")]
    [InlineData("\"site\":null", "\"site\":\"LockedNotice.Show\"")]
    [InlineData("\"version\":\"1.0.0\"", "\"version\":\"1.0.0 on alice-laptop\"")]
    [InlineData("\"build\":\"0787be6e2871203c8b2f4e80155a49f0a78ae1cb\"", "\"build\":\"0787be6e\"")]
    [InlineData("\"os\":\"macos\"", "\"os\":\"Ubuntu\"")]
    [InlineData("\"os_version\":\"15.7.1\"", "\"os_version\":\"15.7.1 (Alice's Mac)\"")]
    [InlineData("\"arch\":\"arm64\"", "\"arch\":\"M3\"")]
    [InlineData("\"uptime\":\"<10m\"", "\"uptime\":\"312s\"")]
    [InlineData("\"screen\":\"agents\"", "\"screen\":\"Personal vault\"")]
    [InlineData("\"occurred_on\":\"2026-10-28\"", "\"occurred_on\":\"2026-10-28T09:30:00Z\"")]
    [InlineData("\"exception\":{", "\"message\":\"x\",\"exception\":{")]
    [InlineData("\"type\":\"System.InvalidOperationException\"", "\"type\":\"Could not open /home/alice/vault.kdbx\"")]
    [InlineData("\"asm\":\"Keypaste.Core\"", "\"asm\":\"Plugin\"")]
    [InlineData("\"method\":\"MoveNext\"", "\"method\":\"C:\\\\Users\\\\alice\"")]
    [InlineData("\"type\":\"[external]\"", "\"type\":\"Sentinel.Type\"")]
    [InlineData("\"il\":212", "\"il\":-2")]
    [InlineData("\"omitted\":0,\"frames\":[{\"asm\":\"Keypaste.Core\",\"type\":\"Keypaste.Core.Infrastructure", "\"omitted\":0,\"inner\":[],\"frames\":[{\"asm\":\"Keypaste.Core\",\"type\":\"Keypaste.Core.Infrastructure")]
    [InlineData("{\"schema\":1,", "{ \"schema\":1,")]
    [InlineData("\"kind\":\"crash\",\"site\":null,", "\"site\":null,\"kind\":\"crash\",")]
    public void Only_what_the_scrubber_would_write_reads_back(string from, string to)
    {
        var crash = VectorReports()[0];
        var changed = crash.Replace(from, to, StringComparison.Ordinal);

        Assert.NotEqual(crash, changed);
        Assert.False(CrashScrubber.TryRead(Encoding.UTF8.GetBytes(changed), out _), changed);
    }

    [Fact]
    public void A_crash_that_says_it_ended_with_no_hook_does_not_read_back()
    {
        var ended = VectorReports()[2];

        Assert.True(CrashScrubber.TryRead(Encoding.UTF8.GetBytes(ended), out _));
        Assert.False(CrashScrubber.TryRead(Encoding.UTF8.GetBytes(ended.Replace("\"kind\":\"ended-unexpectedly\"", "\"kind\":\"crash\"", StringComparison.Ordinal)), out _));
    }

    private static Exception Nested() => CrashFixtures.Thrown(() =>
    {
        try
        {
            CrashFixtures.Deep(2, () => throw new IOException());
        }
        catch (IOException inner)
        {
            throw new InvalidOperationException(null, inner);
        }
    });

    private static string KindAndSite(CrashReport report)
    {
        using var document = JsonDocument.Parse(report.Json);
        return $"{document.RootElement.GetProperty("kind").GetString()} | {document.RootElement.GetProperty("site").GetString()}".TrimEnd();
    }

    private static List<string> VectorReports()
    {
        using var vector = JsonDocument.Parse(File.ReadAllText(Path.Combine(VendoredWordList.RepoRoot(), "site", "test", "crash-vector.json")));

        return [.. vector.RootElement.GetProperty("cases").EnumerateArray().Select(sample => sample.GetProperty("report").GetString()!)];
    }

    private static SortedSet<string> Paths(string json)
    {
        using var document = JsonDocument.Parse(json);
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        Collect(document.RootElement, string.Empty, paths);
        return paths;
    }

    private static void Collect(JsonElement element, string prefix, SortedSet<string> paths)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
                paths.Add(path);
                Collect(property.Value, path, paths);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, prefix + "[]", paths);
            }
        }
    }
}
