using System.Globalization;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;

namespace Keypaste.App.ViewModels;

/// <summary>
/// A request waiting for a person, or a grant in force, as Agent Activity lists it: who, what and
/// for how long, never a value.
/// </summary>
/// <remarks>
/// Every string comes from the <see cref="ApprovalPrompt"/> the person was shown, which sanitized
/// the client's name, its label and the entry once, so the list says exactly what the prompt said.
/// </remarks>
internal sealed class ActivityRow
{
    private ActivityRow(int number, ApprovalPrompt prompt, TimeSpan remaining, string lifetime, GrantKey? grant)
        : this(number, prompt.Client, prompt.Label ?? "none configured", prompt.Entry, prompt.Field, lifetime + " " + Seconds(remaining))
    {
        Grant = grant;
        Id = grant is { } key ? GrantId.Of(key) : null;
    }

    private ActivityRow(int number, string client, string label, string entry, string field, string left)
    {
        Number = number;
        Client = client;
        Label = label;
        Entry = entry;
        Field = field;
        Left = left;
    }

    /// <summary>The row's place in its list, from 1, which is how the driver names one to revoke.</summary>
    internal int Number { get; }

    internal string Client { get; }

    internal string Label { get; }

    internal string Entry { get; }

    internal string Field { get; }

    /// <summary>How long is left, and of what.</summary>
    internal string Left { get; }

    /// <summary>The agent's grant to revoke, or null for a waiting request or a run's grant.</summary>
    internal GrantKey? Grant { get; }

    /// <summary>The grant's id, as <c>keypaste grants</c> shows it, or null for a waiting request.</summary>
    internal string? Id { get; private init; }

    /// <summary>The client and its label on one line.</summary>
    internal string Who => $"{Client} · label {Label}";

    /// <summary>The entry and field on one line.</summary>
    internal string What => $"{Entry} · {Field}";

    internal static ActivityRow Waiting(int number, WaitingRequest waiting) =>
        new(number, waiting.Prompt, waiting.Remaining, "answered for you in", grant: null);

    internal static ActivityRow Granted(int number, GrantInForce grant) =>
        new(number, grant.Approved, grant.Remaining, "ends in", grant.Key);

    /// <summary>A timed grant a person gave a repeated <c>keypaste run --session</c>; its names were sanitized for the prompt that gave it.</summary>
    internal static ActivityRow EnvGranted(int number, EnvGrantInForce grant) =>
        new(number, GrantSummary.EnvClient, "none configured", $"{grant.Project} · {grant.Profile} · {grant.Command}", "set", "ends in " + Seconds(grant.Remaining))
        {
            Id = GrantId.OfEnv(grant.Key),
        };

    // Rounded up, so a grant with half a second left does not read as over.
    private static string Seconds(TimeSpan remaining) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds))} s");
}
