using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// A temporary <c>KEYPASTE_HOME</c>, with a real audit log written into it the way keypaste-mcp
/// writes one.
/// </summary>
/// <remarks>
/// A real log rather than a hand-written file, because what the activity view is judged on is
/// whether it says the same thing as <c>keypaste log</c> about the same bytes — and a fixture whose
/// hash chain was faked would be a fixture that cannot be verified, which is the state this view is
/// supposed to be able to tell apart from an intact one.
/// </remarks>
internal sealed class TempAuditHome : IDisposable
{
    /// <summary>The reason keypaste records against every fixture record.</summary>
    /// <remarks>
    /// Named because <see cref="Alter"/> edits it: it is text the log carries verbatim, so changing
    /// it changes the bytes the record's hash covers without changing anything else.
    /// </remarks>
    internal const string Reason = "approved by the person at the keyboard";

    internal TempAuditHome() =>
        Home = Directory.CreateTempSubdirectory("keypaste-app-log-tests-").FullName;

    /// <summary>The directory <c>KEYPASTE_HOME</c> would name.</summary>
    internal string Home { get; }

    /// <summary>Where the log is, resolved the way the app resolves it.</summary>
    internal string LogPath => KeypasteHome.AuditPath(Home);

    /// <summary>Appends one granted record per entry named.</summary>
    internal void Append(params string[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Assert.True(AuditLog.TryOpen(LogPath, new ManualClock(), out var log, out var error), error);

        using (log)
        {
            foreach (var entry in entries)
            {
                Assert.True(log.TryAppend(Record(entry), out var failure), failure);
            }
        }
    }

    /// <summary>
    /// Changes the first record's text without touching its hash — a careless edit, which is the
    /// thing the chain exists to detect.
    /// </summary>
    /// <remarks>
    /// The line endings are rewritten by hand rather than left to
    /// <see cref="File.WriteAllLines(string, string[])"/>, which would use CRLF on Windows and have
    /// the file reported as re-saved as well as edited.
    /// </remarks>
    internal void Alter()
    {
        var lines = File.ReadAllText(LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0] = lines[0].Replace(Reason, Reason + ".", StringComparison.Ordinal);

        File.WriteAllText(LogPath, string.Join('\n', lines) + '\n');
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (IOException)
        {
            // A test that cannot clean up its temporary directory has still made its point.
        }
    }

    private static AuditRecord Record(string entry) => new()
    {
        Tool = "get_credential",
        Client = new AuditClient("claude-code", "1.2.3", "work-laptop"),
        Args = AuditArgs.ForCredentialRequest(entry, "password", ttlSeconds: 60, "deploying the staging site"),
        Decision = AuditDecision.Granted,
        Method = AuditMethod.Prompt,
        Reason = Reason,
        Exposure = ["env/**"],
    };
}
