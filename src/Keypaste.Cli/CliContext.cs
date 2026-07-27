using Keypaste.Cli.Clipboard;
using Keypaste.Cli.Execution;
using Keypaste.Cli.Prompting;
using Keypaste.Cli.Styling;

namespace Keypaste.Cli;

/// <summary>
/// Everything a command needs from the outside world.
/// </summary>
/// <remarks>
/// Every field is a seam so the whole CLI runs in-process in tests: no real console, no real
/// clipboard, no real environment variables, and no test that waits twenty seconds.
/// </remarks>
internal sealed class CliContext
{
    /// <summary>Data output. Nothing but data ever goes here.</summary>
    internal required TextWriter Stdout { get; init; }

    /// <summary>Prompts, diagnostics, and errors.</summary>
    internal required TextWriter Stderr { get; init; }

    /// <summary>Reads passwords and other input.</summary>
    internal required ISecretPrompt Prompt { get; init; }

    /// <summary>The system clipboard.</summary>
    internal required IClipboard Clipboard { get; init; }

    /// <summary>How the clipboard is cleared after a copy.</summary>
    internal required IClipboardClearStrategy ClipboardClear { get; init; }

    /// <summary>Environment variable access.</summary>
    internal required IEnvironmentProbe Environment { get; init; }

    /// <summary>How <c>run</c> starts, supervises and reaps a child process.</summary>
    internal required IProcessLauncher ProcessLauncher { get; init; }

    /// <summary>How a warning that must not be missed reaches the terminal.</summary>
    internal required IConsoleStyle ConsoleStyle { get; init; }

    /// <summary>What time it is, for the commands that take a relative span.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Builds the context the real program uses.</summary>
    /// <remarks>
    /// Prompts are wired to <paramref name="stderr"/>, not <paramref name="stdout"/>. That is the
    /// whole reason <c>keypaste get x --show | tr -d '\n'</c> is safe to write, and a test asserts
    /// this wiring rather than trusting the convention.
    /// </remarks>
    internal static CliContext CreateDefault(TextWriter stdout, TextWriter stderr)
    {
        var environment = new SystemEnvironmentProbe();

        return new CliContext
        {
            Stdout = stdout,
            Stderr = stderr,
            Prompt = new ConsoleSecretPrompt(stderr),
            Clipboard = new SystemClipboard(new SystemProcessRunner(), environment),
            ClipboardClear = new BlockingClearStrategy(TimeProvider.System),
            Environment = environment,
            ProcessLauncher = new SystemProcessLauncher(),
            ConsoleStyle = new SystemConsoleStyle(environment),
        };
    }
}
