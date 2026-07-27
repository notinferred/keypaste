using Keypaste.Core;
using Keypaste.Core.Audit;

namespace Keypaste.Cli.Commands;

/// <summary><c>keypaste log</c> — what agents asked for, and what happened.</summary>
/// <remarks>
/// <para>
/// <b>The bare verb shows the log</b>, rather than printing usage the way <c>keypaste policy</c>
/// does. <c>policy</c> has nothing to show without a subcommand; this one has the thing the operator
/// came for, and making them type a second word to see it would be a toll on the command they reach
/// for when something looks wrong.
/// </para>
/// <para>
/// <b>It needs no vault, and <c>--vault</c> is not one of its options.</b> The audit log is
/// plaintext by design — it is the record that survives the vault being locked — so asking for a
/// master password to read it would be theatre, in exactly the way <c>keypaste policy ls</c>
/// already argues.
/// </para>
/// <para>
/// <b>It checks the chain on every run.</b> Not as a separate concern the user has to remember:
/// somebody reading this table is reading it to find out what happened, and a table drawn from a
/// file that has been edited must not look like a table drawn from one that has not. A break is an
/// alarm on stderr and a non-zero exit, and the records are still printed, because refusing to show
/// a tampered log would hand an attacker a way to make it unreadable.
/// </para>
/// </remarks>
internal static class LogCommand
{
    internal const string LogOption = "audit-log";
    internal const string DeniedOption = "denied";
    internal const string ClientOption = "client";
    internal const string SinceOption = "since";

    private static readonly OptionSpec[] _options =
    [
        new(LogOption, TakesValue: true),
        new(DeniedOption, TakesValue: false),
        new(ClientOption, TakesValue: true),
        new(SinceOption, TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (args.Length >= 2)
        {
            switch (args[1])
            {
                case "verify":
                    return LogVerifyCommand.Execute(args, context);

                case "help":
                    WriteUsage(context.Stdout);
                    return CliApp.ExitSuccess;

                default:
                    // Anything that is not a subcommand falls through to the listing, which is what
                    // parses options. An unknown word lands there as an unexpected operand.
                    break;
            }
        }

        return List(args, context);
    }

    internal static void WriteUsage(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("usage: keypaste log [--denied] [--client <text>] [--since <when>]");
        writer.WriteLine("       keypaste log verify [--expect <hash>]");
        writer.WriteLine();
        writer.WriteLine("Shows every call an AI agent made through the bridge: when, which client,");
        writer.WriteLine("which entry, whether it was allowed, and how that was decided.");
        writer.WriteLine();
        writer.WriteLine("filters:");
        writer.WriteLine("  --denied            only the calls that were refused");
        writer.WriteLine("  --client <text>     only clients whose label or name contains this");
        writer.WriteLine($"  --since <when>      {AuditSince.Expected}");
        writer.WriteLine();
        writer.WriteLine("the file:");
        writer.WriteLine($"  --audit-log <path>  which log to read, or set {KeypasteHome.EnvironmentVariable}");
        writer.WriteLine($"                      (default ~/{KeypasteHome.DirectoryName}/{KeypasteHome.AuditFileName})");
        writer.WriteLine();
        writer.WriteLine("Every record is linked to the one before it by a hash, so an edit to the file");
        writer.WriteLine("is detectable. Both commands check that; `verify` reports it in full.");
        writer.WriteLine($"A broken chain exits {CliApp.ExitTamperDetected}.");
    }

    /// <summary>Which log to read: the option if given, otherwise the one keypaste writes.</summary>
    internal static string Resolve(string? option, CliContext context) =>
        option ?? KeypasteHome.AuditPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));

    private static int List(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var parseError))
        {
            context.Stderr.WriteLine($"keypaste log: {parseError}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 0)
        {
            context.Stderr.WriteLine($"keypaste log: unexpected argument '{line.Operands[0]}'");
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        // Before looking at the file, so that a mistyped filter is a usage error whether or not a
        // log happens to exist yet.
        if (!TryFilters(line, context, out var filters, out var wanted))
        {
            return CliApp.ExitUsageError;
        }

        var path = Resolve(line.Value(LogOption), context);

        // An absent log is not a failure: it is what a machine looks like before any agent has
        // asked for anything. `verify` treats the same absence as an error, because there it is the
        // difference between "checked and fine" and "nothing was checked".
        if (!File.Exists(path))
        {
            context.Stdout.WriteLine($"No audit log at {path} yet.");
            context.Stdout.WriteLine("Nothing has asked keypaste for a credential on this machine.");
            return CliApp.ExitSuccess;
        }

        if (!AuditReader.TryRead(path, out var entries, out var unreadable, out var readError))
        {
            context.Stderr.WriteLine($"keypaste log: {path} could not be read: {readError}");
            return CliApp.ExitInternalError;
        }

        var shown = new List<AuditEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (wanted(entry))
            {
                shown.Add(entry);
            }
        }

        var report = AuditChainVerifier.Verify(path);

        // A table drawn from a file nothing checked must not be handed over as though something
        // had. This is the same reasoning as `verify` refusing to call a missing file intact.
        if (report.Verdict == AuditChainVerdict.Unreadable)
        {
            context.Stderr.WriteLine($"keypaste log: {path} could not be checked: {report.Error}");
            context.Stderr.WriteLine("keypaste log: nothing below has been verified against the chain.");
            return CliApp.ExitInternalError;
        }

        Alarm(report, context);
        Render(path, shown, entries.Count, unreadable, filters, report, context);

        return report.Verdict == AuditChainVerdict.Broken
            ? CliApp.ExitTamperDetected
            : CliApp.ExitSuccess;
    }

    private static void Render(
        string path,
        List<AuditEntry> shown,
        int total,
        int unreadable,
        IReadOnlyList<string> filters,
        AuditChainReport report,
        CliContext context)
    {
        context.Stdout.WriteLine(AuditText.Heading(path, shown.Count, total, filters));
        context.Stdout.WriteLine();

        if (shown.Count == 0)
        {
            // Said rather than left to an empty screen: silence after a filter reads as "nothing
            // happened", which is the one conclusion an audit tool must never invite by accident.
            context.Stdout.WriteLine(
                total == 0 ? "No records yet." : "No records matched.");

            return;
        }

        var unverified = report.Unverified;

        foreach (var written in AuditText.Table(shown, unverified))
        {
            context.Stdout.WriteLine(written);
        }

        var notes = AuditText.Notes(shown, unreadable, unverified);
        if (notes.Count == 0)
        {
            return;
        }

        context.Stdout.WriteLine();
        foreach (var written in notes)
        {
            context.Stdout.WriteLine(written);
        }
    }

    private static void Alarm(AuditChainReport report, CliContext context)
    {
        if (report.Verdict != AuditChainVerdict.Broken)
        {
            return;
        }

        context.ConsoleStyle.Alarm(
            context.Stderr,
            "This log has been tampered with. What follows may not be what keypaste recorded.");

        context.Stderr.WriteLine("Run 'keypaste log verify' for where, and read THREATS.md T-5.");
        context.Stderr.WriteLine();
    }

    /// <summary>Turns the filter options into a predicate, and into words for the heading.</summary>
    private static bool TryFilters(
        CommandLine line,
        CliContext context,
        out IReadOnlyList<string> filters,
        out Func<AuditEntry, bool> wanted)
    {
        var described = new List<string>();
        var denied = line.HasFlag(DeniedOption);
        var client = line.Value(ClientOption);
        DateTimeOffset? since = null;

        if (denied)
        {
            described.Add("refused calls only");
        }

        if (client is not null)
        {
            // Sanitized on the way back out even though the user typed it: the heading sits directly
            // above a table whose whole purpose is to be trustworthy, and text echoed into it should
            // arrive on the same terms as the text read out of the file.
            described.Add($"client containing '{EntryNameSanitizer.Sanitize(client, 40).Text}'");
        }

        if (line.Value(SinceOption) is { } text)
        {
            if (!AuditSince.TryParse(text, context.Clock.GetUtcNow(), out var from, out var error))
            {
                context.Stderr.WriteLine($"keypaste log: --since {error}");
                filters = described;
                wanted = _ => true;
                return false;
            }

            since = from;
            described.Add($"since {from.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z");
        }

        filters = described;
        wanted = entry =>
            (!denied || !entry.Granted)
            && (client is null || Matches(entry, client))
            && (since is null || (entry.At is { } at && at >= since));

        return true;
    }

    /// <summary>
    /// Whether a record's client is the one asked for.
    /// </summary>
    /// <remarks>
    /// A case-insensitive substring, over both the label the operator set and the name the client
    /// asserted. Exact matching would let somebody type <c>claude</c>, see an empty table, and
    /// conclude nothing happened while <c>claude-code</c> was reading credentials all morning. In a
    /// tool for finding out what happened, matching too much costs noise and matching too little
    /// costs the answer.
    /// </remarks>
    private static bool Matches(AuditEntry entry, string text) =>
        entry.Label.Contains(text, StringComparison.OrdinalIgnoreCase)
        || entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
}
