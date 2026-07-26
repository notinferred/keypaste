using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Keypaste.Cli.Clipboard;
using Keypaste.Cli.Prompting;

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

    internal int Run(params string[] args)
    {
        var context = new CliContext
        {
            Stdout = Stdout,
            Stderr = Stderr,
            Prompt = Prompt,
            Clipboard = Clipboard,
            ClipboardClear = ClearStrategy,
            Environment = new FakeEnvironment(Environment),
        };

        return CliApp.Run(args, context);
    }

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

/// <summary>An environment backed by a dictionary.</summary>
internal sealed class FakeEnvironment(Dictionary<string, string> values) : IEnvironmentProbe
{
    public string? Get(string name) => values.TryGetValue(name, out var value) ? value : null;
}
