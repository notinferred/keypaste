using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Mcp.Tools;

namespace Keypaste.Mcp;

/// <summary>One vault as the bridge reaches it: the vault, the link to its owner and what lists and searches it.</summary>
/// <param name="Vault">The vault, or null for the one route a bridge started without a vault has, whose every call is refused as naming none.</param>
/// <param name="Connection">The link to the vault's owner, which forwards each request to that owner alone.</param>
/// <param name="Names">What lists and searches the vault.</param>
internal sealed record VaultRoute(BridgeVault? Vault, ApproverConnection Connection, IEntryNameSource Names)
{
    /// <summary>The vault's identity key for an audit line, or null without a vault.</summary>
    internal string? Key => Vault?.Key;
}

/// <summary>One vault's answer to a search or listing that asked every vault it names.</summary>
/// <param name="Route">The vault asked.</param>
/// <param name="Listing">What it answered.</param>
internal sealed record VaultAnswer(VaultRoute Route, EntryNameListing Listing)
{
    /// <summary>The word a result gives this vault's state: <c>open</c> when it answered, otherwise why it did not.</summary>
    internal string State => Listing.Availability switch
    {
        VaultAvailability.Available => "open",
        VaultAvailability.Locked => "locked",
        VaultAvailability.Busy => "busy",
        VaultAvailability.Changed => "changed",
        VaultAvailability.Unrecorded => "not recorded",
        VaultAvailability.NoSession => "not reached",
        _ => "not answering",
    };
}

/// <summary>Where a request goes, or why it goes nowhere.</summary>
/// <param name="Route">The vault the request goes to, or null when it is refused.</param>
/// <param name="Method">How a refusal is recorded.</param>
/// <param name="Reason">keypaste's sentence for the log.</param>
/// <param name="Refusal">What the agent is told.</param>
internal readonly record struct Routing(VaultRoute? Route, AuditMethod Method = AuditMethod.InvalidRequest, string Reason = "", string Refusal = "");

/// <summary>
/// The vaults a bridge serves, each through its own owner's pipe, and which one a request goes to.
/// </summary>
/// <remarks>
/// <para>
/// Each vault's own owner decides each request for it, under that vault's rules and prompts, so a locked vault answers
/// as locked without holding up another. A request naming its vault goes there and only there, and one naming a vault
/// this bridge does not serve is refused before any owner hears of it (THREATS.md T-4).
/// </para>
/// <para>
/// A request that names no vault goes to the one vault holding the item it names: the one the bridge's own searches
/// and listings showed it in, or, when they showed it nowhere, the one a listing of every vault finds it in. An item
/// more than one vault could hold is refused rather than guessed at, as a path more than one item has is.
/// </para>
/// </remarks>
internal sealed class BridgeVaults : IAsyncDisposable
{
    private readonly Lock _seenLock = new();
    private readonly Dictionary<string, HashSet<VaultRoute>> _seen = new(StringComparer.Ordinal);

    /// <param name="routes">At least one route: one per vault, or the one with no vault.</param>
    /// <param name="inputEnded">Cancelled once the client has closed the bridge's standard input.</param>
    internal BridgeVaults(IReadOnlyList<VaultRoute> routes, CancellationToken inputEnded = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(routes.Count);

        Routes = routes;
        InputEnded = inputEnded;
    }

    /// <summary>Every route, in the order the vaults were named.</summary>
    internal IReadOnlyList<VaultRoute> Routes { get; }

    /// <summary>Whether the bridge serves more than one vault, so that results and refusals name each one's.</summary>
    internal bool Several => Routes.Count > 1;

    /// <summary>Cancelled once the client has closed the bridge's standard input, which withdraws every exchange still in flight.</summary>
    internal CancellationToken InputEnded { get; }

    /// <summary>The vault key of a line about a call no vault was chosen for: the one vault's, or null for several.</summary>
    internal string? SoleKey => Several ? null : Routes[0].Key;

    /// <summary>A route to each vault <paramref name="options"/> names, or the one with no vault when it names none.</summary>
    internal static BridgeVaults Connect(ServerOptions options, CancellationToken inputEnded)
    {
        ArgumentNullException.ThrowIfNull(options);

#pragma warning disable CA2000 // Each connection goes into its route, and DisposeAsync disposes every route's.
        VaultRoute Route(BridgeVault? vault)
        {
            var connection = new ApproverConnection(vault?.PipeName, vault?.Path ?? string.Empty) { InputEnded = inputEnded };
            return new VaultRoute(vault, connection, new ApproverEntryNameSource(connection, options, vault));
        }
#pragma warning restore CA2000

        return new BridgeVaults(options.Vaults.Count == 0 ? [Route(null)] : [.. options.Vaults.Select(Route)], inputEnded);
    }

    /// <summary>Has every attach say who the client is and what this bridge exposes, for the owners' display alone (THREATS.md T-3).</summary>
    internal void Describe(Func<AttachClient?> identity, IReadOnlyList<string> exposure)
    {
        foreach (var route in Routes)
        {
            route.Connection.Identity = identity;
            route.Connection.Exposure = exposure;
        }
    }

    /// <summary>The routes a search or listing naming <paramref name="vault"/> reaches: that vault's, or every one when it names none.</summary>
    /// <returns>Null with <paramref name="refusal"/> when it names a vault this bridge does not serve.</returns>
    internal IReadOnlyList<VaultRoute>? Reached(string? vault, out Routing refusal)
    {
        refusal = default;

        if (string.IsNullOrEmpty(vault))
        {
            return Routes;
        }

        if (Named(vault) is { } route)
        {
            return [route];
        }

        refusal = NotServed();
        return null;
    }

    /// <summary>Asks each of <paramref name="routes"/> at once, so a vault that is slow or locked holds up no other's answer.</summary>
    /// <param name="routes">The vaults to ask.</param>
    /// <param name="ask">The listing or search, asked of one vault's names.</param>
    /// <returns>Each vault's answer, in the order of <paramref name="routes"/>; one whose source threw is answered as failed, and still recorded as asked.</returns>
    internal static async Task<IReadOnlyList<VaultAnswer>> EachAsync(
        IReadOnlyList<VaultRoute> routes,
        Func<IEntryNameSource, ValueTask<EntryNameListing>> ask)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(ask);

        return await Task.WhenAll(routes.Select(async route =>
        {
            try
            {
                return new VaultAnswer(route, await ask(route.Names).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                return new VaultAnswer(route, new EntryNameListing(VaultAvailability.Cancelled, [], ToolText.Cancelled, true));
            }
            catch (Exception)
            {
                // Anything a source throws is still a call that was attempted, and is recorded as one.
                return new VaultAnswer(route, new EntryNameListing(VaultAvailability.Failed, [], ToolText.ApproverFailed, true));
            }
        })).ConfigureAwait(false);
    }

    /// <summary>Notes the items and projects a search or listing showed in a vault, so a request naming one of them finds its vault.</summary>
    internal void Saw(VaultRoute route, IEnumerable<EntryName> items, IEnumerable<string> projects)
    {
        ArgumentNullException.ThrowIfNull(route);

        lock (_seenLock)
        {
            foreach (var key in items.SelectMany(Keys).Concat(projects.Select(ProjectKey)))
            {
                if (!_seen.TryGetValue(key, out var routes))
                {
                    _seen[key] = routes = [];
                }

                routes.Add(route);
            }
        }
    }

    /// <summary>The vault a request goes to.</summary>
    /// <param name="vault">The vault the agent named, or null.</param>
    /// <param name="entries">Each item the request names, by handle or path.</param>
    /// <param name="projects">Each project the request names.</param>
    /// <param name="cancellationToken">Cancelled when the call is no longer wanted.</param>
    internal async ValueTask<Routing> RouteAsync(
        string? vault,
        IReadOnlyList<string> entries,
        IReadOnlyList<string> projects,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(vault))
        {
            return Named(vault) is { } route ? new Routing(route) : NotServed();
        }

        if (!Several)
        {
            return new Routing(Routes[0]);
        }

        List<string> keys = [.. entries.Where(entry => entry.Length > 0), .. projects.Where(project => project.Length > 0).Select(ProjectKey)];

        if (keys.Count == 0)
        {
            return Ambiguous(Routes);
        }

        var holding = Holding(keys);
        List<VaultRoute> unread = [];

        if (holding.Count == 0)
        {
            unread = await LookAsync(cancellationToken).ConfigureAwait(false);
            holding = Holding(keys);
        }

        if (holding.Count == 1)
        {
            return new Routing(holding[0]);
        }

        if (holding.Count > 1)
        {
            return Ambiguous(holding);
        }

        // Only a vault that could not be read may hold it, and its owner's answer says why it cannot be asked; with none
        // read, the first vault's does.
        if (unread.Count == 1 || unread.Count == Routes.Count)
        {
            return new Routing(unread[0]);
        }

        return unread.Count > 1
            ? Ambiguous(unread)
            : new Routing(null, AuditMethod.OutOfScope, "no vault this bridge serves holds the item within what it can see", ToolText.InNoVault);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var route in Routes)
        {
            await route.Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Lists every vault, noting what each showed, and returns those that could not be read whole.</summary>
    private async Task<List<VaultRoute>> LookAsync(CancellationToken cancellationToken)
    {
        var listings = await EachAsync(Routes, names => names.ListAsync(cancellationToken)).ConfigureAwait(false);

        foreach (var (route, listing) in listings.Where(each => each.Listing.Availability == VaultAvailability.Available))
        {
            Saw(
                route,
                listing.Names.Select(listed => listed.Name),
                listing.Names.SelectMany(listed => new IndexedEntry(listed, ItemKind.None, null, null).Projects));
        }

        return [.. listings.Where(each => each.Listing is not { Availability: VaultAvailability.Available, Complete: true }).Select(each => each.Route)];
    }

    /// <summary>The routes every key was seen in.</summary>
    private List<VaultRoute> Holding(List<string> keys)
    {
        lock (_seenLock)
        {
            return [.. Routes.Where(route => keys.All(key => _seen.TryGetValue(key, out var routes) && routes.Contains(route)))];
        }
    }

    private VaultRoute? Named(string vault) =>
        Routes.FirstOrDefault(route => string.Equals(route.Vault?.Name, vault, StringComparison.OrdinalIgnoreCase));

    private Routing NotServed() => new(
        null,
        AuditMethod.OutOfScope,
        "the request named a vault this bridge does not serve",
        ToolText.VaultNotServed(Routes.Select(route => route.Vault?.Name).OfType<string>()));

    private static Routing Ambiguous(IEnumerable<VaultRoute> routes) => new(
        null,
        AuditMethod.InvalidRequest,
        "more than one vault this bridge serves could hold the item",
        ToolText.VaultAmbiguous(routes.Select(route => route.Vault?.Name).OfType<string>()));

    /// <summary>How a request may name an item: by its handle or by its path.</summary>
    private static IEnumerable<string> Keys(EntryName name) => [EntryHandle.For(name), PathOf(name)];

    /// <summary>An item's path as a request gives it: its group and title, or its title alone in the root group.</summary>
    internal static string PathOf(EntryName name) => name.GroupPath.Length == 0 ? name.Title : $"{name.GroupPath}/{name.Title}";

    private static string ProjectKey(string project) => "\0project\0" + project;
}
