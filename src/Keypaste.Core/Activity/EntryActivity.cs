using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tokens;

namespace Keypaste.Core.Activity;

/// <summary>How recently an entry left the vault.</summary>
public enum EntryUseState
{
    /// <summary>Not in use and not used within <see cref="EntryActivity.RecentWindow"/>.</summary>
    Idle = 0,

    /// <summary>Released within <see cref="EntryActivity.RecentWindow"/>.</summary>
    Recent = 1,

    /// <summary>A grant in force covers it, or a waiting request names it.</summary>
    InUse = 2,
}

/// <summary>An entry's state and when it was last released.</summary>
/// <param name="State">Its state.</param>
/// <param name="LastUsed">When it was last released, or null when never as far as keypaste knows.</param>
public sealed record EntryUse(EntryUseState State, DateTimeOffset? LastUsed);

/// <summary>One client that received an entry.</summary>
/// <param name="Client">Its label, else its name; a token as <c>&lt;name&gt; · token</c>.</param>
/// <param name="LastAt">When it last received it.</param>
/// <param name="Releases">How many releases keypaste knows of.</param>
/// <param name="How">How it last received it.</param>
public sealed record ClientAccess(string Client, DateTimeOffset LastAt, int Releases, string How);

/// <summary>Everything the Agent access card says about one entry: names and times, never a value.</summary>
/// <param name="Use">Its state and last use.</param>
/// <param name="Clients">Who received it, newest first.</param>
/// <param name="Grants">The grants in force that cover it.</param>
/// <param name="Waiting">Whether a waiting request names it.</param>
public sealed record EntryAgentAccess(EntryUse Use, IReadOnlyList<ClientAccess> Clients, IReadOnlyList<GrantSummary> Grants, bool Waiting);

/// <summary>
/// Which entries agents use, from what is already recorded: this vault's granted audit lines, the
/// owner's release ledger for this session and its live grants and waiting prompts (D-0361).
/// </summary>
/// <remarks>
/// A line written before lines named their vault counts for whichever vault is open, a stated limit.
/// Every source names an entry the way prompts and audit lines do, so <see cref="KeyOf"/> is the one key.
/// </remarks>
public sealed class EntryActivity
{
    /// <summary>How long after its last release an entry reads as recent.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromHours(2);

    private readonly DateTimeOffset _now;
    private readonly Dictionary<string, Dictionary<string, ClientAccess>> _released = new(StringComparer.Ordinal);
    private readonly HashSet<string> _waiting = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<GrantSummary>> _envGrants = new(StringComparer.Ordinal);
    private readonly ApproverActivity _live;

    private EntryActivity(ApproverActivity live, DateTimeOffset now)
    {
        _live = live;
        _now = now;
    }

    /// <summary>Builds the picture from everything recorded.</summary>
    /// <param name="audit">Audit entries read so far.</param>
    /// <param name="vaultKey">The open vault's identity key.</param>
    /// <param name="live">What the owner holds now.</param>
    /// <param name="session">What the owner released this session.</param>
    /// <param name="now">The time the recent window is measured from.</param>
    /// <returns>The picture.</returns>
    public static EntryActivity Build(
        IReadOnlyList<AuditEntry> audit,
        string vaultKey,
        ApproverActivity live,
        IReadOnlyList<ReleaseSeen> session,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(vaultKey);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(session);

        var activity = new EntryActivity(live, now);

        foreach (var entry in audit)
        {
            if (!entry.Granted
                || entry.At is not { } at
                || (entry.Vault.Length > 0 && !string.Equals(entry.Vault, vaultKey, StringComparison.Ordinal)))
            {
                continue;
            }

            var client = TokenAuditReason.TryName(entry.Reason, out var token)
                ? $"{token} · token"
                : entry.Client.Length > 0 ? entry.Client : "an unnamed client";
            var how = string.Equals(entry.Tool, "run", StringComparison.Ordinal) && entry.Method != "token" ? "run" : entry.Method;

            foreach (var named in entry.Entries.Count > 0 ? entry.Entries : entry.Entry.Length > 0 ? [entry.Entry] : [])
            {
                activity.Released(Key(named), client, how, at, counted: true);
            }
        }

        // Every other release the ledger holds is also an audit line: it moves the time, not the count.
        foreach (var seen in session)
        {
            activity.Released(Key(seen.Entry), seen.Client, seen.How, seen.At, counted: seen.How == "run --session");
        }

        foreach (var grant in live.EnvGrants)
        {
            foreach (var named in grant.Entries)
            {
                var key = Key(named);

                if (!activity._envGrants.TryGetValue(key, out var rows))
                {
                    rows = [];
                    activity._envGrants[key] = rows;
                }

                rows.Add(GrantSummary.From(grant));
            }
        }

        foreach (var waiting in live.Waiting)
        {
            activity._waiting.Add(Key(waiting.Prompt.Entry));
        }

        foreach (var run in live.WaitingRuns)
        {
            activity._waiting.UnionWith(run.Prompt.Variables.Select(variable => Key(variable.Entry)));
        }

        foreach (var env in live.WaitingEnvs)
        {
            activity._waiting.UnionWith(env.Prompt.Keys.Select(name =>
                KeyOf(new EntryName(EnvProfileNames.GroupPath(env.Prompt.Project, env.Prompt.Profile), name))));
        }

        return activity;
    }

    /// <summary>An entry as every prompt, grant and audit line names it.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>Its display path, capped as an audit line caps it.</returns>
    public static string KeyOf(EntryName entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return Key(ApprovalPrompt.Shown(entry));
    }

    /// <summary>An entry's state and last use.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>In use, recent or idle.</returns>
    public EntryUse Use(EntryName entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var key = KeyOf(entry);
        DateTimeOffset? last = _released.TryGetValue(key, out var clients) ? clients.Values.Max(access => access.LastAt) : null;

        var state = InUse(entry, key)
            ? EntryUseState.InUse
            : last is { } at && _now - at <= RecentWindow ? EntryUseState.Recent : EntryUseState.Idle;

        return new EntryUse(state, last);
    }

    /// <summary>What the Agent access card shows for an entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>Its use, its clients newest first, its grants and whether a request waits on it.</returns>
    public EntryAgentAccess Access(EntryName entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var key = KeyOf(entry);
        var handle = EntryHandle.For(entry);

        IReadOnlyList<ClientAccess> clients = _released.TryGetValue(key, out var byClient)
            ? [.. byClient.Values.OrderByDescending(access => access.LastAt).ThenBy(access => access.Client, StringComparer.Ordinal)]
            : [];

        IReadOnlyList<GrantSummary> grants =
        [
            .. _live.Grants.Where(grant => string.Equals(grant.Key.Handle, handle, StringComparison.Ordinal)).Select(GrantSummary.From),
            .. _envGrants.GetValueOrDefault(key) ?? [],
        ];

        return new EntryAgentAccess(Use(entry), clients, grants, _waiting.Contains(key));
    }

    private bool InUse(EntryName entry, string key)
    {
        var handle = EntryHandle.For(entry);

        return _waiting.Contains(key)
            || _envGrants.ContainsKey(key)
            || _live.Grants.Any(grant => string.Equals(grant.Key.Handle, handle, StringComparison.Ordinal));
    }

    private void Released(string key, string client, string how, DateTimeOffset at, bool counted)
    {
        if (!_released.TryGetValue(key, out var byClient))
        {
            byClient = new Dictionary<string, ClientAccess>(StringComparer.Ordinal);
            _released[key] = byClient;
        }

        byClient[client] = byClient.TryGetValue(client, out var known)
            ? known with
            {
                Releases = known.Releases + (counted ? 1 : 0),
                LastAt = at > known.LastAt ? at : known.LastAt,
                How = at > known.LastAt ? how : known.How,
            }
            : new ClientAccess(client, at, 1, how);
    }

    private static string Key(string shown) =>
        EntryNameSanitizer.SanitizePath(shown, maximumLength: AuditArgs.EntryLength).Text;
}
