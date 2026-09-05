using System.Text;
using Keypaste.Cli.Clipboard;

namespace Keypaste.Cli.Tests;

/// <summary>
/// Stands in for the external tools <c>setup</c> calls, and records every call.
/// </summary>
/// <remarks>
/// The point of this fake is that no test ever runs a real <c>claude</c> or <c>codex</c>, so no
/// test can reach the developer's own client configuration. That is not a tidiness concern: a
/// suite that wired keypaste into the machine running it would be discovered by someone wondering
/// why their editor had grown an MCP server.
/// </remarks>
internal sealed class FakeProcessRunner : IProcessRunner
{
    /// <summary>Executables this machine is pretending to have.</summary>
    internal HashSet<string> Installed { get; } = new(StringComparer.Ordinal);

    /// <summary>Executables that exist but fail every call, with this on stderr.</summary>
    internal Dictionary<string, string> Refuses { get; } = new(StringComparer.Ordinal);

    /// <summary>Every invocation, in order, as <c>executable arg arg</c>.</summary>
    internal List<string> Calls { get; } = [];

    /// <inheritdoc/>
    public ProcessResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string? stdin,
        Encoding stdinEncoding,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        Calls.Add(arguments.Count == 0 ? fileName : fileName + " " + string.Join(' ', arguments));

        if (!Installed.Contains(fileName))
        {
            return new ProcessResult(ToolFound: false, ExitCode: -1, string.Empty, string.Empty);
        }

        return Refuses.TryGetValue(fileName, out var complaint)
            ? new ProcessResult(ToolFound: true, ExitCode: 1, string.Empty, complaint)
            : new ProcessResult(ToolFound: true, ExitCode: 0, string.Empty, string.Empty);
    }

    /// <summary>The calls that were not the "are you installed" probe.</summary>
    internal IReadOnlyList<string> RealCalls =>
        [.. Calls.Where(call => !call.EndsWith(" --version", StringComparison.Ordinal))];
}
