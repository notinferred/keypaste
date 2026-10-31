using System.CommandLine;
using Keypaste.Core.Audit;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary><c>keypaste log verify</c> — whether the log is the log keypaste wrote.</summary>
/// <remarks>
/// <para>
/// <b>An absent file is an error here, and is not one for <c>keypaste log</c>.</b> Listing nothing
/// is a true answer about a machine no agent has used; verifying nothing is not an answer at all,
/// and a script that took exit 0 from a missing file as "the log is intact" would be reading a
/// reassurance out of an absence.
/// </para>
/// <para>
/// <b><c>--expect</c> is the only thing that can catch a truncation.</b> Deleting records from the
/// end leaves a chain that is internally perfect — there is no later record left to notice that
/// anything is gone — so no amount of checking inside the file can see it. Passing back a hash
/// recorded earlier turns that into something answerable: either it is still in the file, and
/// everything up to it is accounted for, or it is not. keypaste keeps no copy of it, on purpose:
/// an anchor stored beside the thing it anchors is worth nothing (THREATS.md T-5).
/// </para>
/// </remarks>
internal static class LogVerifyCommand
{
    private static readonly Option<string> _expect = Syntax.Value("--expect", "hash", "a hash from an earlier run, which the chain must still hold");

    internal static CliVerb Command { get; } = new("verify", "check the whole chain and report it in full", Execute)
    {
        _expect, CliOptions.AuditLog,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        var expected = line.GetValue(_expect);
        if (expected is not null && !AuditChainVerifier.IsHash(expected))
        {
            context.Stderr.WriteLine(
                "keypaste log verify: --expect takes a hash from an earlier run - 64 lowercase hex characters");

            return CliApp.ExitUsageError;
        }

        var path = LogCommand.Resolve(line.GetValue(CliOptions.AuditLog), context);

        if (!File.Exists(path))
        {
            context.Stderr.WriteLine($"keypaste log verify: there is no log at {path}");
            context.Stderr.WriteLine("keypaste log verify: nothing was checked, which is not the same as nothing being wrong");
            return CliApp.ExitNotFound;
        }

        // The anchor is answered by the same pass that checks the chain, because only a record whose
        // own bytes still hash to it can answer for it. Searching the file's text would accept a
        // hash sitting in any field — including an entry name, which the agent writes.
        var report = AuditChainVerifier.Verify(path, expected);

        if (report.Verdict == AuditChainVerdict.Unreadable)
        {
            context.Stderr.WriteLine($"keypaste log verify: {path} could not be read: {report.Error}");
            return CliApp.ExitInternalError;
        }

        var lost = report.Anchored == false;
        var broken = report.Verdict == AuditChainVerdict.Broken;

        if (broken)
        {
            context.ConsoleStyle.Alarm(context.Stderr, "This log has been tampered with.");
        }
        else if (lost)
        {
            context.ConsoleStyle.Alarm(context.Stderr, "The record you anchored to is gone.");
        }

        foreach (var written in AuditText.Verdict(report))
        {
            context.Stdout.WriteLine(written);
        }

        return broken || lost ? CliApp.ExitTamperDetected : CliApp.ExitSuccess;
    }
}
