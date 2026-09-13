using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The first vaults a process creates at the same moment all see one completely built KDF
/// registry (docs/STEPS.md F.11).
/// </summary>
/// <remarks>
/// Each iteration loads keypaste's core and KeePassLib into a fresh collectible load context, so
/// every burst is a genuine first use of <c>KdfPool</c>'s static list; a warmed process cannot
/// test it. KeePassLib is reached by reflection over names only: KeePassInterop remains the one
/// file that references its types.
/// </remarks>
public sealed class KdfFirstUseTests(ITestOutputHelper output)
{
    private const int _iterationCount = 400;
    private const int _threadCount = 16;

    private static readonly string[] _completeRegistry = ["AES-KDF", "Argon2d", "Argon2id"];

    private delegate object CreateVault(string path, ReadOnlySpan<char> password);

    [Fact]
    public void ConcurrentFirstVaults_AllSucceed_AndLeaveACompleteRegistry()
    {
        var tally = Burst(warm: false);
        output.WriteLine(tally.ToString());

        Assert.True(tally.IsClean, tally.ToString());
    }

    [Fact]
    public void ConcurrentVaults_AfterOneSequentialFirstUse_AllSucceed()
    {
        var tally = Burst(warm: true);
        output.WriteLine(tally.ToString());

        Assert.True(tally.IsClean, tally.ToString());
    }

    private static Tally Burst(bool warm)
    {
        var tally = new Tally(warm);
        for (var i = 0; i < _iterationCount; i++)
        {
            RunIsolated(warm, tally);
        }

        return tally;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunIsolated(bool warm, Tally tally)
    {
        var context = new FirstUseContext();
        try
        {
            var core = context.LoadFromAssemblyName(typeof(Vault).Assembly.GetName());
            var create = core.GetType("Keypaste.Core.Vault", throwOnError: true)!
                .GetMethod(nameof(Vault.Create), BindingFlags.Public | BindingFlags.Static)!
                .CreateDelegate<CreateVault>();
            var path = Path.Combine(Path.GetTempPath(), "keypaste-kdf-first-use-never-written.kdbx");

            if (warm)
            {
                Dispose(create(path, "warm"));
            }

            var failures = new string?[_threadCount];
            using var barrier = new Barrier(_threadCount);
            var threads = Enumerable.Range(0, _threadCount).Select(slot => new Thread(() =>
            {
                barrier.SignalAndWait();
                try
                {
                    Dispose(create(path, "first use"));
                }
                catch (Exception ex)
                {
                    failures[slot] = Shape(ex);
                }
            })).ToArray();

            foreach (var thread in threads)
            {
                thread.Start();
            }

            foreach (var thread in threads)
            {
                thread.Join();
            }

            var registry = ReadRegistry(context);
            string? afterwards = null;
            try
            {
                Dispose(create(path, "afterwards"));
            }
            catch (Exception ex)
            {
                afterwards = Shape(ex);
            }

            tally.Record(failures.OfType<string>().ToArray(), registry, afterwards);
        }
        finally
        {
            context.Unload();
        }
    }

    private static string ReadRegistry(AssemblyLoadContext context)
    {
        var keePassLib = context.Assemblies.Single(a => a.GetName().Name == "KeePassLib");
        var engines = (IList)keePassLib.GetType("KeePassLib.Cryptography.KeyDerivation.KdfPool", throwOnError: true)!
            .GetField("g_l", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        return string.Join(",", engines.Cast<object?>().Select(e =>
            e is null ? "null" : (string)e.GetType().GetProperty("Name")!.GetValue(e)!));
    }

    // Types and frames only: messages can carry values (D-0124).
    private static string Shape(Exception ex)
    {
        var frames = new StackTrace(ex, fNeedFileInfo: true).GetFrames()
            .Select(f => f.GetMethod() is { } m
                ? $"{m.DeclaringType?.Name}.{m.Name}{(f.GetFileLineNumber() > 0 ? ":" + f.GetFileLineNumber() : "")}"
                : "?")
            .Take(4);
        return $"{ex.GetType().Name} at {string.Join(" <- ", frames)}";
    }

    private static void Dispose(object vault) => ((IDisposable)vault).Dispose();

    private sealed class FirstUseContext() : AssemblyLoadContext(nameof(KdfFirstUseTests), isCollectible: true)
    {
        private static readonly string[] _isolated = ["Keypaste.Core", "KeePassLib"];

        protected override Assembly? Load(AssemblyName name) =>
            _isolated.Contains(name.Name)
                ? LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, name.Name + ".dll"))
                : null;
    }

    private sealed class Tally(bool warm)
    {
        private readonly Dictionary<string, int> _shapes = [];
        private readonly Dictionary<string, int> _registries = [];
        private int _iterations;
        private int _failedIterations;
        private int _failedCalls;
        private int _transient;
        private int _persistent;

        public bool IsClean => _failedCalls == 0 && _persistent == 0 && _registries.Keys.All(IsComplete);

        public void Record(string[] failures, string registry, string? afterwards)
        {
            _iterations++;
            _failedCalls += failures.Length;
            Count(_registries, registry);
            foreach (var failure in failures)
            {
                Count(_shapes, failure);
            }

            var poisoned = afterwards is not null || !IsComplete(registry);
            if (poisoned)
            {
                _persistent++;
                if (afterwards is not null)
                {
                    Count(_shapes, "afterwards: " + afterwards);
                }
            }

            if (failures.Length > 0)
            {
                _failedIterations++;
                if (!poisoned)
                {
                    _transient++;
                }
            }
        }

        public override string ToString()
        {
            var lines = new List<string>
            {
                $"warm={warm} iterations={_iterations} threads={_threadCount} failed-iterations={_failedIterations} failed-calls={_failedCalls}",
                $"transient (failure, healthy registry afterwards)={_transient} persistent (registry incomplete or later create fails)={_persistent}",
            };
            lines.AddRange(_registries.OrderByDescending(p => p.Value).Select(p => $"  registry [{p.Key}] x{p.Value}"));
            lines.AddRange(_shapes.OrderByDescending(p => p.Value).Select(p => $"  {p.Value} x {p.Key}"));
            return string.Join(Environment.NewLine, lines);
        }

        private static bool IsComplete(string registry) => registry == string.Join(",", _completeRegistry);

        private static void Count(Dictionary<string, int> counts, string key) =>
            counts[key] = counts.GetValueOrDefault(key) + 1;
    }
}
