using System.Text.Json;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>A vault the built keypaste made, the home its owner logs to and a log of the bridge's own, in a directory deleted with the test.</summary>
internal sealed class Workspace : IDisposable
{
    internal const string Master = "ci-process-master-pw";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-processes-").FullName;

    internal Workspace()
    {
        Home = Path.Combine(_directory, "home");
        Directory.CreateDirectory(Home);
        Vault = Path.Combine(_directory, "vault.kdbx");
        BridgeLog = Path.Combine(_directory, "audit.jsonl");
        Keypaste("init", Master, Master, Vault);
    }

    internal string Home { get; }

    internal string Vault { get; }

    /// <summary>Where a bridge logs what reaches no owner.</summary>
    internal string BridgeLog { get; }

    /// <summary>Where the owner the home names logs what it answers.</summary>
    internal string OwnerLog => Path.Combine(Home, "audit.jsonl");

    internal Dictionary<string, string> Environment => new() { ["KEYPASTE_HOME"] = Home };

    /// <summary>Stores <paramref name="secret"/> at <paramref name="entry"/> with the built keypaste.</summary>
    internal void Add(string entry, string secret) => Keypaste("add", Master, secret, entry, "--vault", Vault);

    /// <summary>The owner's or bridge's log lines from <paramref name="after"/> on.</summary>
    internal static IReadOnlyList<JsonElement> Lines(string log, int after = 0) =>
        [.. Read(log).Skip(after).Select(line => JsonDocument.Parse(line).RootElement.Clone())];

    internal static int Count(string log) => Read(log).Count;

    /// <summary>The whole log, to show a value never reached it.</summary>
    internal static string Text(string log) => string.Join('\n', Read(log));

    // The process that writes a log keeps it open, which Windows lets another read only when that read shares writing.
    private static List<string> Read(string log)
    {
        if (!File.Exists(log))
        {
            return [];
        }

        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r'))];
    }

    /// <summary>Runs the built keypaste with <paramref name="first"/> and <paramref name="second"/> as its two lines of standard input, which must succeed.</summary>
    internal void Keypaste(string verb, string first, string second, params string[] arguments)
    {
        using var run = RunningProcess.Start(Built.Keypaste, Environment, [verb, .. arguments]);
        run.Send(first);
        run.Send(second);
        run.CloseInput();
        Assert.True(run.WaitForExit() == 0, $"keypaste {verb} failed: {string.Join('\n', run.Error)}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A killed process's files are freed on Windows only once it has gone; the temporary directory is left then.
        }
    }
}
