using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Keypaste.Core.CrashReports;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 4.11a: nothing an exception carries but its type, HResult and code frames reaches a crash report, the outbox's file or
/// the request that sends it (D-0556, D-0558).
/// </summary>
/// <remarks>
/// Each test plants sentinels where a value can hide, shows that the exception really carries them, then looks for them
/// in all three places; the first test is the control, finding a value a report does carry in all three. The mutation each
/// must fail: the scrubber reading a message, data, a file name, the runtime's stack-trace text or the environment, or
/// naming a type or frame off the allowlist or outside the identifier grammar.
/// </remarks>
public sealed class CrashReportScrubbingTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-crash-scrubbing-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task A_value_a_report_carries_is_found_in_the_report_the_outbox_and_the_request()
    {
        var screen = $"s{Guid.NewGuid():N}"[..21];
        var report = CrashScrubber.Crash(CrashFixtures.Thrown(() => throw new InvalidOperationException()), CrashFixtures.Context with { Screen = screen });

        Assert.Equal(["report", "outbox", "request"], await PlacesHoldingAsync(report, screen));
    }

    [Fact]
    public async Task A_message_and_an_inner_message_never_leave()
    {
        var outer = CrashFixtures.Sentinel("outer");
        var inner = CrashFixtures.Sentinel("inner");
        var exception = CrashFixtures.Thrown(() =>
        {
            try
            {
                CrashFixtures.Deep(1, () => throw new IOException(inner));
            }
            catch (IOException caught)
            {
                throw new InvalidOperationException(outer, caught);
            }
        });

        Assert.Contains(outer, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains(inner, exception.ToString(), StringComparison.Ordinal);

        await AssertNowhereAsync(CrashScrubber.Crash(exception, CrashFixtures.Context), outer, inner);
    }

    [Fact]
    public async Task Data_never_leaves()
    {
        var key = CrashFixtures.Sentinel("key");
        var value = CrashFixtures.Sentinel("value");
        var exception = CrashFixtures.Thrown(() =>
        {
            var failure = new InvalidOperationException();
            failure.Data[key] = value;
            throw failure;
        });

        Assert.Equal(value, exception.Data[key]);

        await AssertNowhereAsync(CrashScrubber.Unexpected(exception, "LockedNotice.Show", CrashFixtures.Context), key, value);
    }

    [Fact]
    public async Task An_aggregates_children_leave_only_their_types()
    {
        var first = CrashFixtures.Sentinel("first");
        var second = CrashFixtures.Sentinel("second");
        var exception = CrashFixtures.Thrown(() => throw new AggregateException(
            CrashFixtures.Thrown(() => throw new InvalidOperationException(first)),
            CrashFixtures.Thrown(() => throw new ArgumentException(second))));

        Assert.Contains(first, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains(second, exception.ToString(), StringComparison.Ordinal);

        var report = CrashScrubber.UnobservedTask(exception, CrashFixtures.Context);

        Assert.Contains("\"type\":\"System.AggregateException\"", report.Text, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"System.InvalidOperationException\"", report.Text, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"System.ArgumentException\"", report.Text, StringComparison.Ordinal);
        await AssertNowhereAsync(report, first, second);
    }

    [Fact]
    public async Task A_path_never_leaves()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_directory, CrashFixtures.Sentinel("folder"))).FullName;
        var path = Path.Combine(folder, "vault.kdbx");
        var missing = Assert.IsType<FileNotFoundException>(CrashFixtures.Thrown(() => File.ReadAllBytes(path)));

        Assert.Equal(path, missing.FileName);
        Assert.Contains(folder, missing.ToString(), StringComparison.Ordinal);

        await AssertNowhereAsync(CrashScrubber.Crash(missing, CrashFixtures.Context), Path.GetFileName(folder), "vault.kdbx");
    }

    [Fact]
    public async Task A_value_from_the_environment_never_leaves()
    {
        var name = $"KEYPASTE_CRASH_TEST_{Guid.NewGuid():N}";
        var value = CrashFixtures.Sentinel("environment");
        Environment.SetEnvironmentVariable(name, value);

        try
        {
            var exception = CrashFixtures.Thrown(() => throw new InvalidOperationException($"{name} is {Environment.GetEnvironmentVariable(name)}"));

            Assert.Contains(value, exception.ToString(), StringComparison.Ordinal);

            await AssertNowhereAsync(CrashScrubber.Crash(exception, CrashFixtures.Context), name, value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public async Task Names_emitted_at_runtime_off_the_allowlist_never_leave()
    {
        var sentinel = $"Sentinel{Guid.NewGuid():N}";
        var exception = Emitted(sentinel, sentinel + "Type", sentinel + "Method", sentinel + "Exception");

        Assert.Contains(sentinel, exception.GetType().FullName, StringComparison.Ordinal);
        Assert.Contains(sentinel + "Method", exception.StackTrace, StringComparison.Ordinal);

        var report = CrashScrubber.Crash(exception, CrashFixtures.Context);

        Assert.Contains("\"type\":\"System.InvalidOperationException\"", report.Text, StringComparison.Ordinal);
        Assert.Contains("{\"asm\":\"[external]\",\"type\":\"[external]\",\"method\":\"[external]\"", report.Text, StringComparison.Ordinal);
        await AssertNowhereAsync(report, sentinel);
    }

    [Fact]
    public async Task Names_that_are_not_identifiers_never_leave_even_from_an_allowed_assembly()
    {
        var sentinel = CrashFixtures.Sentinel("sentinel");
        var exception = Emitted("Keypaste.Emitted", $"Vault {sentinel}", $"/home/{sentinel}/vault.kdbx", $"Error {sentinel}");

        Assert.Contains(sentinel, exception.GetType().FullName, StringComparison.Ordinal);
        Assert.Contains(sentinel, exception.StackTrace, StringComparison.Ordinal);

        var report = CrashScrubber.Crash(exception, CrashFixtures.Context);

        Assert.Contains("\"type\":\"System.InvalidOperationException\"", report.Text, StringComparison.Ordinal);
        Assert.Contains("{\"asm\":\"Keypaste.Emitted\",\"type\":\"[unnamed]\",\"method\":\"[unnamed]\"", report.Text, StringComparison.Ordinal);
        await AssertNowhereAsync(report, sentinel);
    }

    [Theory]
    [InlineData("Keypaste.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "Keypaste.Core.Vault", "Open", "Keypaste.Core", "Keypaste.Core.Vault", "Open")]
    [InlineData("keypaste-app, Version=1.0.0.0", "Keypaste.App.Session.LockedNotice+<Show>d__4", "MoveNext", "keypaste-app", "Keypaste.App.Session.LockedNotice+<Show>d__4", "MoveNext")]
    [InlineData("Avalonia.Base", "Avalonia.Threading.Dispatcher", "<Run>g__Loop|4_0", "Avalonia.Base", "Avalonia.Threading.Dispatcher", "<Run>g__Loop|4_0")]
    [InlineData("System.Private.CoreLib", "System.Collections.Generic.List`1[[System.String, System.Private.CoreLib, Version=10.0.0.0]]", "Add", "System.Private.CoreLib", "System.Collections.Generic.List`1", "Add")]
    [InlineData("Plugin, Version=1.0.0.0", "Plugin.Type", "Run", "[external]", "[external]", "[external]")]
    [InlineData("KeypasteSentinel", "KeypasteSentinel.Type", "Run", "[external]", "[external]", "[external]")]
    [InlineData("Keypaste Sentinel", "Keypaste.Type", "Run", "[external]", "[external]", "[external]")]
    [InlineData(null, "Keypaste.Core.Vault", "Open", "[external]", "[external]", "[external]")]
    [InlineData("Keypaste.Core", null, "lambda_method1", "[dynamic]", "[dynamic]", "[dynamic]")]
    [InlineData("Keypaste.Core", "Keypaste.Core.Vault", null, "[unnamed]", "[unnamed]", "[unnamed]")]
    [InlineData("Keypaste.Core", "Vault at /home/someone", "Open", "Keypaste.Core", "[unnamed]", "Open")]
    [InlineData("Keypaste.Core", "Keypaste.Core.Vault", "C:\\Users\\someone", "Keypaste.Core", "Keypaste.Core.Vault", "[unnamed]")]
    [InlineData("Keypaste.Core", "Keypaste.Core.Vault", "someone@example.com", "Keypaste.Core", "Keypaste.Core.Vault", "[unnamed]")]
    public void A_frame_is_named_only_from_an_allowed_assembly_and_only_as_identifiers(
        string? assembly, string? type, string? method, string keptAssembly, string keptType, string keptMethod) =>
        Assert.Equal((keptAssembly, keptType, keptMethod), CrashScrubber.FrameNames(assembly, type, method));

    [Fact]
    public void A_name_longer_than_256_characters_is_not_kept()
    {
        Assert.Equal(new string('a', 256), CrashScrubber.Name(new string('a', 256)));
        Assert.Equal("[unnamed]", CrashScrubber.Name(new string('a', 257)));
    }

    [Fact]
    public void A_deep_stack_keeps_its_top_48_frames_and_counts_the_rest()
    {
        var report = CrashScrubber.Crash(CrashFixtures.Thrown(() => CrashFixtures.Deep(100, () => throw new InvalidOperationException())), CrashFixtures.Context);

        using var document = JsonDocument.Parse(report.Json);
        var exception = document.RootElement.GetProperty("exception");

        Assert.Equal(48, exception.GetProperty("frames").GetArrayLength());
        Assert.True(exception.GetProperty("omitted").GetInt32() >= 53, report.Text);
        Assert.Equal("Deep", exception.GetProperty("frames")[47].GetProperty("method").GetString());
    }

    [Fact]
    public void A_report_over_16_KiB_drops_frames_from_the_bottom_until_it_fits()
    {
        var report = CrashScrubber.Crash(CrashFixtures.Large(), CrashFixtures.Context);

        Assert.InRange(report.Json.Length, CrashScrubber.MaximumBytes - 1024, CrashScrubber.MaximumBytes);

        using var document = JsonDocument.Parse(report.Json);
        var exception = document.RootElement.GetProperty("exception");
        var inner = exception.GetProperty("inner").EnumerateArray().ToList();

        Assert.Equal(48, exception.GetProperty("frames").GetArrayLength());
        Assert.Equal(4, inner.Count);
        Assert.Equal(16, inner[0].GetProperty("frames").GetArrayLength());
        Assert.True(inner[3].GetProperty("omitted").GetInt32() > inner[0].GetProperty("omitted").GetInt32(), report.Text);
    }

    private static Exception Emitted(string assembly, string type, string method, string exceptionType)
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assembly), AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule(assembly);

        var thrown = module.DefineType(exceptionType, TypeAttributes.Public | TypeAttributes.Class, typeof(InvalidOperationException));
        thrown.DefineDefaultConstructor(MethodAttributes.Public);
        var constructor = thrown.CreateType()!.GetConstructor(Type.EmptyTypes)!;

        var holder = module.DefineType(type, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var il = holder.DefineMethod(method, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes).GetILGenerator();
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Throw);

        return CrashFixtures.Thrown(holder.CreateType()!.GetMethod(method)!.CreateDelegate<Action>());
    }

    private async Task AssertNowhereAsync(CrashReport report, params string[] sentinels)
    {
        foreach (var sentinel in sentinels)
        {
            Assert.Empty(await PlacesHoldingAsync(report, sentinel));
        }

        Assert.DoesNotContain(" ", report.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("/", report.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", report.Text, StringComparison.Ordinal);
    }

    private async Task<List<string>> PlacesHoldingAsync(CrashReport report, string value)
    {
        var outbox = new CrashOutbox(Path.Combine(_directory, $"{Guid.NewGuid():N}"));
        Assert.True(outbox.Add(report));

        var file = Assert.Single(Directory.GetFiles(outbox.Folder));
        var kept = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);

        using var server = new FakeCrashServer();
        Assert.Equal(1, await new CrashSender(server, CrashSender.KeypasteCom, outbox).SendWaitingAsync(TestContext.Current.CancellationToken));

        List<string> places = [];

        if (report.Text.Contains(value, StringComparison.Ordinal))
        {
            places.Add("report");
        }

        if (kept.Contains(value, StringComparison.Ordinal))
        {
            places.Add("outbox");
        }

        if (server.Transcript.Contains(value, StringComparison.Ordinal))
        {
            places.Add("request");
        }

        return places;
    }
}
