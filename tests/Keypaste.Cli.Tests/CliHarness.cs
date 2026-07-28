using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Keypaste.Cli.Clipboard;
using Keypaste.Cli.Execution;
using Keypaste.Cli.Prompting;
using Keypaste.Cli.Styling;
using Keypaste.Core;

namespace Keypaste.Cli.Tests;

/// <summary>
/// Runs the CLI in-process against fakes: no real console, clipboard, environment, or waiting.
/// </summary>
internal sealed class CliHarness : IDisposable
{
    internal CliHarness()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("keypaste-cli-tests-").FullName;
        VaultPath = Path.Combine(Directory, "vault.kdbx");
    }

    internal string Directory { get; }

    internal string VaultPath { get; }

    internal StringWriter Stdout { get; } = new(CultureInfo.InvariantCulture);

    internal StringWriter Stderr { get; } = new(CultureInfo.InvariantCulture);

    internal FakeSecretPrompt Prompt { get; } = new();

    internal FakeClipboard Clipboard { get; } = new();

    internal FakeClearStrategy ClearStrategy { get; } = new();

    internal Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

    internal string Out => Stdout.ToString();

    internal string Err => Stderr.ToString();

    internal FakeProcessLauncher ProcessLauncher { get; } = new();

    internal FakeConsoleStyle ConsoleStyle { get; } = new();

    internal FakeClock Clock { get; } = new();

    internal int Run(params string[] args) => CliApp.Run(args, NewContext());

    /// <summary>The context <see cref="Run"/> uses, for tests that call below the verb layer.</summary>
    internal CliContext NewContext() => new()
    {
        Stdout = Stdout,
        Stderr = Stderr,
        Prompt = Prompt,
        Clipboard = Clipboard,
        ClipboardClear = ClearStrategy,
        Environment = new FakeEnvironment(Environment),
        ProcessLauncher = ProcessLauncher,
        ConsoleStyle = ConsoleStyle,
        Clock = Clock,
    };

    /// <summary>Creates a vault with one entry per supplied spec, via the CLI itself.</summary>
    /// <remarks>
    /// Every step is checked. A seeding failure that goes unnoticed here surfaces much later as an
    /// unrelated assertion — "expected 0, got 3" in a test about clipboard timeouts — and sends
    /// whoever reads it looking in entirely the wrong place.
    /// </remarks>
    internal void SeedVault(string masterPassword, params (string Path, string Password)[] entries)
    {
        Prompt.Interactive = false;
        Prompt.Enqueue(masterPassword, masterPassword);
        Check(Run("init", VaultPath) == CliApp.ExitSuccess, "SeedVault: init failed");

        foreach (var (path, password) in entries)
        {
            Prompt.Enqueue(masterPassword, password);
            Check(Run("add", path, "--vault", VaultPath) == CliApp.ExitSuccess, $"SeedVault: add {path} failed");
        }

        Stdout.GetStringBuilder().Clear();
        Stderr.GetStringBuilder().Clear();
    }

    /// <summary>Asserts an exit code, quoting stderr when it does not match.</summary>
    /// <remarks>
    /// <c>Assert.Equal(0, exit)</c> reports "expected 0, actual 2" and nothing else, while the one
    /// line explaining why sits unread in <see cref="Err"/>. That gap cost a CI investigation
    /// once; it should not cost a second one.
    /// </remarks>
    internal void AssertExit(int expected, int actual) =>
        Check(expected == actual, $"expected exit {expected}, got {actual}");

    private void Check(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"{what}.{System.Environment.NewLine}stderr:{System.Environment.NewLine}{Err}");
        }
    }

    public void Dispose()
    {
        Stdout.Dispose();
        Stderr.Dispose();
        System.IO.Directory.Delete(Directory, recursive: true);
    }
}

/// <summary>A scripted prompt. Records what it was asked and hands back queued answers.</summary>
internal sealed class FakeSecretPrompt : ISecretPrompt
{
    private readonly Queue<string> _answers = new();

    public bool IsInteractive { get; set; }

    internal bool Interactive { get => IsInteractive; set => IsInteractive = value; }

    /// <summary>Every prompt string the CLI passed in, in order.</summary>
    internal List<string> PromptsSeen { get; } = [];

    /// <summary>Buffers handed out, kept so a test can assert they were zeroed afterwards.</summary>
    internal List<SecretBuffer> IssuedSecrets { get; } = [];

    /// <summary>Prompt strings for which <see cref="ReadSecret"/> — not ReadLine — was used.</summary>
    internal List<string> SecretPrompts { get; } = [];

    /// <summary>
    /// Runs before each answer is handed back, with the prompt that asked for it.
    /// </summary>
    /// <remarks>
    /// The only seam that lands <em>inside</em> an open vault session. A command's prompts for
    /// entry fields happen between <c>Vault.Open</c> and <c>vault.Save</c>, so this is where a test
    /// can be a second writer — which is the whole scenario
    /// <c>VaultChangedOnDiskException</c> exists for and is otherwise unreachable from a CLI whose
    /// commands open and save within milliseconds.
    /// </remarks>
    internal Action<string>? OnPrompt { get; set; }

    internal void Enqueue(params string[] answers)
    {
        foreach (var answer in answers)
        {
            _answers.Enqueue(answer);
        }
    }

    public SecretBuffer? ReadSecret(string prompt)
    {
        PromptsSeen.Add(prompt);
        SecretPrompts.Add(prompt);
        OnPrompt?.Invoke(prompt);

        if (_answers.Count == 0)
        {
            return null;
        }

        var buffer = new SecretBuffer();
        buffer.Append(_answers.Dequeue());
        IssuedSecrets.Add(buffer);
        return buffer;
    }

    public string? ReadLine(string prompt)
    {
        PromptsSeen.Add(prompt);
        return _answers.Count == 0 ? null : _answers.Dequeue();
    }
}

/// <summary>An in-memory clipboard that counts what happened to it.</summary>
internal sealed class FakeClipboard : IClipboard
{
    internal string? Content { get; private set; }

    internal int SetCount { get; private set; }

    internal int ClearCount { get; private set; }

    /// <summary>Forced failure mode, for the headless and missing-tool paths.</summary>
    internal ClipboardStatus SetStatus { get; set; } = ClipboardStatus.Ok;

    public ClipboardStatus TrySet(string text, out string error)
    {
        error = SetStatus == ClipboardStatus.Ok ? string.Empty : "no clipboard tool found";
        if (SetStatus != ClipboardStatus.Ok)
        {
            return SetStatus;
        }

        Content = text;
        SetCount++;
        return ClipboardStatus.Ok;
    }

    public ClipboardStatus TryReadHash(out byte[] sha256, out string error)
    {
        error = string.Empty;
        sha256 = SHA256.HashData(Encoding.UTF8.GetBytes(Content ?? string.Empty));
        return ClipboardStatus.Ok;
    }

    public ClipboardStatus TryClear(out string error)
    {
        error = string.Empty;
        Content = null;
        ClearCount++;
        return ClipboardStatus.Ok;
    }

    /// <summary>Simulates the user copying something else.</summary>
    internal void ReplaceExternally(string text) => Content = text;
}

/// <summary>Records the clear request and performs it immediately, so no test waits.</summary>
internal sealed class FakeClearStrategy : IClipboardClearStrategy
{
    internal TimeSpan? RequestedDelay { get; private set; }

    internal bool Cleared { get; private set; }

    /// <summary>Runs during the simulated wait, so a test can change the clipboard underneath.</summary>
    internal Action? DuringWait { get; set; }

    public void ClearAfter(IClipboard clipboard, byte[] expectedHash, TimeSpan delay, TextWriter status)
    {
        RequestedDelay = delay;
        DuringWait?.Invoke();

        // Mirrors BlockingClearStrategy's conditional-clear rule so the command-level tests
        // observe the same behaviour without depending on a clock.
        clipboard.TryReadHash(out var current, out _);
        if (!CryptographicOperations.FixedTimeEquals(current, expectedHash))
        {
            status.WriteLine("Clipboard changed since the copy; leaving it alone.");
            return;
        }

        clipboard.TryClear(out _);
        Cleared = true;
        status.WriteLine("Clipboard cleared.");
    }
}

/// <summary>Records what was shouted, and writes it plainly.</summary>
/// <remarks>
/// Plain on purpose. Every assertion in this suite looks for substrings in
/// <see cref="CliHarness.Err"/>, and a fake that emitted real escape sequences would break them all
/// while proving nothing — whether the escapes are correct is
/// <c>ConsoleStyleTests</c>'s job, against the real implementation.
/// </remarks>
internal sealed class FakeConsoleStyle : IConsoleStyle
{
    /// <summary>Every line passed to <see cref="Alarm"/>, in order.</summary>
    internal List<string> Alarms { get; } = [];

    public void Alarm(TextWriter writer, string text)
    {
        Alarms.Add(text);
        writer.WriteLine(text);
    }
}

/// <summary>A clock a test can move, for the commands that take a relative span.</summary>
internal sealed class FakeClock : TimeProvider
{
    /// <summary>What time it is. Fixed by default, so a filter's boundary is assertable.</summary>
    internal DateTimeOffset Now { get; set; } = new(2026, 7, 26, 15, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>An environment backed by a dictionary.</summary>
internal sealed class FakeEnvironment(Dictionary<string, string> values) : IEnvironmentProbe
{
    public string? Get(string name) => values.TryGetValue(name, out var value) ? value : null;

    public IReadOnlyDictionary<string, string> All() => new Dictionary<string, string>(values, EnvironmentMerge.Comparer);
}

/// <summary>
/// Records what would have been started, and hands back whatever the test wants to happen.
/// </summary>
/// <remarks>
/// It writes nothing at all, so anything appearing in the harness's output came from keypaste
/// itself — which is what makes the hygiene sweep over <c>run</c> mean something.
/// </remarks>
internal sealed class FakeProcessLauncher : IProcessLauncher
{
    /// <summary>Every child that was started, in order.</summary>
    internal List<ChildStart> Started { get; } = [];

    /// <summary>What <see cref="Run"/> reports back.</summary>
    internal ChildResult Result { get; set; } = new(ChildOutcome.Exited, 0, string.Empty);

    /// <summary>Runs at the moment the child would start, for assertions about ordering.</summary>
    internal Action? OnRun { get; set; }

    /// <summary>The environment the last child would have been given.</summary>
    /// <remarks>
    /// Throws rather than returning an empty dictionary when no child was started. The empty
    /// version turned "the run failed before reaching the launcher" into
    /// <c>KeyNotFoundException: 'UNRELATED' was not present</c>, which reads like a merge bug and
    /// is not one.
    /// </remarks>
    internal IReadOnlyDictionary<string, string> Environment => Started.Count == 0
        ? throw new InvalidOperationException("no child was ever started; the run failed before that")
        : Started[^1].Environment;

    public ChildResult Run(ChildStart start)
    {
        Started.Add(start);
        OnRun?.Invoke();
        return Result;
    }
}
