using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Clients;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Which audit lines the person's browser made, as the lines the owner writes read back: every pairing, check, lookup and
/// fill, never an agent's request or a pairing the person removed in keypaste, and a lookup only where the browser asked
/// which logins match a page. The Agents screen counts none of them as an agent.
/// </summary>
public sealed class BrowserAttributionTests : IDisposable
{
    private const string _vault = "0123456789abcdef";
    private const string _session = "session-one";
    private const string _pairing = "Chrome on studio";
    private const string _origin = "https://github.com";

    private static readonly DateTimeOffset _now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly BrowserIdentity _chrome = BrowserIdentity.ChromeWebStore;
    private static readonly AuditClient _claude = new("claude-code", "1.2.3", "cc");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-browser-attribution-").FullName;

    private string LogPath => Path.Combine(_directory, "audit.jsonl");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Writes the records through the real writer and reads them back as a screen does.</summary>
    private IReadOnlyList<AuditEntry> Written(params AuditRecord[] records)
    {
        Assert.True(AuditLog.TryOpen(LogPath, new ManualClock(_now), out var log, out var error), error);

        using (log)
        {
            foreach (var record in records)
            {
                Assert.True(log.TryAppend(record, out var failure), failure);
            }
        }

        Assert.True(AuditReader.TryRead(LogPath, out var entries, out _, out error), error);
        Assert.Equal(records.Length, entries.Count);
        return entries;
    }

    private static AuditRecord Fill(AuditMethod method) =>
        RequestRecords.BrowserFill(_chrome, _pairing, null, _origin, AuditDecision.Granted, method, "filled", _session, _vault);

    private static AuditRecord Lookup() =>
        RequestRecords.Browser(_chrome, _pairing, BrowserAct.List, AuditDecision.Granted, AuditMethod.Exposure, $"listed 2 login(s) by name, no password, on {_origin}", _session, _vault);

    [Fact]
    public void Every_line_the_browser_made_is_the_browsers_and_only_a_pages_list_is_a_lookup()
    {
        var entries = Written(
            Lookup(),
            Fill(AuditMethod.Prompt),
            Fill(AuditMethod.Allowed),
            Fill(AuditMethod.Picked),
            RequestRecords.Browser(_chrome, _pairing, BrowserAct.Pair, AuditDecision.Granted, AuditMethod.Prompt, "a person allowed it", _session, _vault),
            RequestRecords.Browser(_chrome, null, BrowserAct.Check, AuditDecision.Denied, AuditMethod.NotPaired, "no pairing of that name", _session, _vault));

        Assert.All(entries, entry => Assert.True(entry.ByBrowser, entry.Field));
        Assert.Equal([true, false, false, false, false, false], entries.Select(entry => entry.IsBrowserLookup));
    }

    /// <summary>A pairing removed in keypaste is the person's act there, and an agent's request, a listing of names included, is never the browser's.</summary>
    [Fact]
    public void A_removed_pairing_and_an_agents_requests_are_not_the_browsers()
    {
        var entries = Written(
            RequestRecords.Browser(_chrome, _pairing, BrowserAct.Remove, AuditDecision.Granted, AuditMethod.Removed, "the person removed the pairing in keypaste", _session, _vault),
            RequestRecords.Listing(_claude, AuditDecision.Granted, AuditMethod.Exposure, "listed 3 item names", ["**"], _session, _vault),
            RequestRecords.Credential(_claude, "Work/github", "password", "sign in", AuditDecision.Granted, AuditMethod.Prompt, "a person approved this request", "Work/github", ["**"], _session, _vault));

        Assert.All(entries, entry => Assert.False(entry.ByBrowser, entry.Tool));
        Assert.All(entries, entry => Assert.False(entry.IsBrowserLookup, entry.Tool));
    }

    /// <summary>A browser's lines carry its pairing as their label, yet the Agents screen makes no card of it and moves no agent's last request.</summary>
    [Fact]
    public void The_agents_screen_counts_no_browser_line_as_an_agent()
    {
        var entries = Written(
            RequestRecords.Credential(_claude, "Work/github", "password", "sign in", AuditDecision.Granted, AuditMethod.Prompt, "a person approved this request", "Work/github", ["**"], _session, _vault),
            Lookup(),
            Fill(AuditMethod.Prompt));

        var cards = McpClientCards.Build([], entries, _vault, AgentRules.Defaults, [], _now);

        Assert.Equal(["cc", AgentNames.Any], cards.Select(card => card.Key));
        Assert.Equal(entries[0].At, cards[0].LastSeen);
    }
}
