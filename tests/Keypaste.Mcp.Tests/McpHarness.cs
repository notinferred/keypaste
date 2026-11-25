using System.Text;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// Runs the real MCP server in this process and talks to it with a real MCP client.
/// </summary>
/// <remarks>
/// <para>
/// Two anonymous pipes rather than a child process. That keeps the real JSON-RPC framing, the real
/// <c>initialize</c> handshake, the real serializer and the real tool dispatch — everything except
/// <c>StdioServerTransport</c> and <c>BridgeEntry.RunAsync</c>, which is what
/// <c>scripts/verify-mcp-stdio.sh</c> covers.
/// </para>
/// <para>
/// The client-side transport is in <c>ModelContextProtocol.Core</c> too, so the tests take no
/// dependency the shipped binary does not already have.
/// </para>
/// </remarks>
internal sealed class McpHarness : IAsyncDisposable
{
    internal const string ClientName = "test-client";
    internal const string ClientVersion = "9.9.9";

    /// <summary>
    /// What the operator called this bridge. Deliberately different from <see cref="ClientName"/>,
    /// so a test that confuses the asserted name with the label the operator wrote fails rather
    /// than passing by coincidence.
    /// </summary>
    internal const string ClientLabel = "test-label";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-bridge-tests-").FullName;
    private readonly List<IDisposable> _owned = [];

    private readonly ApproverConnection _approver;
    private readonly CancellationTokenSource _inputEnded = new();
    private readonly List<(string Name, FakeApprover Owner, ApproverConnection Connection)> _others = [];
    private BridgeVaults? _vaults;
    private TeeStream? _transcript;

    private AuditLog? _audit;
    private ServerOptions? _options;
    private McpServerPrimitiveCollection<McpServerTool>? _tools;
    private McpServer? _server;
    private McpClient? _client;
    private StreamServerTransport? _transport;
    private Task? _serving;

    /// <summary>The <c>--client-label</c> the server is started with, or null to start it with none.</summary>
    internal string? Label { get; init; } = ClientLabel;

    /// <summary>The vault the server will be pointed at. Never opened: nothing opens a vault here.</summary>
    internal string VaultPath => _vaultPath ?? Path.Combine(_directory, "vault.kdbx");

    /// <summary>Whether the server is started with this harness's directory as its <c>KEYPASTE_HOME</c>, whose log <see cref="AuditPath"/> is.</summary>
    internal bool InItsOwnHome { get; init; }

    /// <summary>The clock and time zone the bridge reads an agent's end time on.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Where this bridge records the calls no owner recorded.</summary>
    internal string AuditPath => KeypasteHome.AuditPath(_directory);

    /// <summary>Where the started bridge looks for the app open and locked on its vault, or null before it starts.</summary>
    internal string? LockedAppName => _options is { Vaults.Count: > 0 } options ? options.Vaults[0].LockedAppName : null;

    /// <summary>Every byte the server has sent the client, as text.</summary>
    /// <remarks>
    /// The raw frames rather than the parsed results. A claim that a secret did not leak is only
    /// worth making against what actually left the process.
    /// </remarks>
    internal string Transcript => _transcript is null
        ? string.Empty
        : Encoding.UTF8.GetString(_transcript.Written);

    /// <summary>What the listing tool will be told the vault contains.</summary>
    internal FakeEntryNameSource Source { get; } = new();

    /// <summary>
    /// A real <c>keypaste approve</c> listener, in this process, answering over a real named pipe.
    /// </summary>
    /// <remarks>
    /// A fake handler rather than a fake connection, so the bridge under test builds a genuine
    /// request, sends it over the wire and decodes a genuine reply. Stubbing the connection would
    /// have skipped the protocol, which is where a credential actually crosses a process boundary.
    /// </remarks>
    internal FakeApprover Approver { get; }

    internal McpHarness()
    {
        Approver = new FakeApprover();
        _approver = new ApproverConnection(Approver.PipeName, VaultPath) { InputEnded = _inputEnded.Token };
    }

    /// <summary>
    /// Points the bridge at an approver somebody else is running, rather than at
    /// <see cref="Approver"/>.
    /// </summary>
    /// <remarks>
    /// For the tests that stand up a real approver over a real vault. The harness's own fake is
    /// still built and still disposed; it is simply never started, which is also the state a bridge
    /// finds the world in most of the time.
    /// </remarks>
    /// <param name="approverPipeName">Where that approver listens.</param>
    /// <param name="vaultPath">The vault it holds, which the bridge names when it attaches, or null for a fake.</param>
    internal McpHarness(string approverPipeName, string? vaultPath = null)
    {
        _vaultPath = vaultPath;
        Approver = new FakeApprover();
        _approver = new ApproverConnection(approverPipeName, VaultPath) { InputEnded = _inputEnded.Token };
        _approverPipeName = approverPipeName;
    }

    /// <summary>A bridge serving several vaults, the first <paramref name="names"/> on <see cref="Approver"/> and each other on a fake owner of its own.</summary>
    private McpHarness(IReadOnlyList<string> names)
    {
        _vaultPath = Path.Combine(_directory, names[0] + ".kdbx");
        Approver = new FakeApprover();
        _approver = new ApproverConnection(Approver.PipeName, VaultPath) { InputEnded = _inputEnded.Token };

        foreach (var name in names.Skip(1))
        {
            var owner = new FakeApprover();
            _others.Add((name, owner, new ApproverConnection(owner.PipeName, Path.Combine(_directory, name + ".kdbx")) { InputEnded = _inputEnded.Token }));
        }
    }

    /// <summary>A bridge serving a vault for each of <paramref name="names"/>, in that order, each with its own fake owner.</summary>
    internal static McpHarness OfVaults(params string[] names) => new(names);

    /// <summary>The fake owner of the vault named <paramref name="name"/>.</summary>
    internal FakeApprover OwnerOf(string name) =>
        name == Path.GetFileNameWithoutExtension(VaultPath) ? Approver : _others.Single(other => other.Name == name).Owner;

    private readonly string? _approverPipeName;
    private readonly string? _vaultPath;

    /// <summary>Starts the server with the given arguments and connects a client to it.</summary>
    internal async Task<McpClient> StartAsync(params string[] argv)
    {
        var channels = Serve(argv);

        _client = await McpClient.CreateAsync(
            new StreamClientTransport(channels.ClientWrites, channels.ClientReads),
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = ClientName, Version = ClientVersion },
            },
            loggerFactory: null,
            CancellationToken.None);

        return _client;
    }

    /// <summary>
    /// Starts the server with the given arguments and hands back its streams, for a client other
    /// than the SDK's to speak over.
    /// </summary>
    /// <remarks>
    /// Arguments naming no <c>--expose</c> get <c>env/**</c>, the place these tests' entries sit in;
    /// a test of the default exposure names it.
    /// </remarks>
    internal HarnessChannels Serve(params string[] argv)
    {
        string[] label = Label is null ? [] : ["--client-label", Label];
        var arguments = (argv.Contains("--expose") ? argv : ["--expose", "env/**", .. argv])
            .Concat(["--vault", VaultPath, "--audit-log", AuditPath, .. label])
            .ToArray();

        if (!ServerOptions.TryParse(arguments, null, InItsOwnHome ? _directory : null, _approverPipeName ?? Approver.PipeName, null, out var options, out var error))
        {
            throw new InvalidOperationException($"the harness could not start the server: {error}");
        }

        _options = options;

        if (!AuditLog.TryOpen(options.AuditPath, TimeProvider.System, out _audit, out var auditError))
        {
            throw new InvalidOperationException($"the harness could not open the audit log: {auditError}");
        }

        var channels = HarnessChannels.Open();
        _owned.Add(channels);

        var first = options.Vaults.Count > 0 ? options.Vaults[0] : null;
        List<VaultRoute> routes = [new VaultRoute(first, _approver, new ApproverEntryNameSource(_approver, options, first))];

        foreach (var (name, owner, connection) in _others)
        {
            var path = Path.Combine(_directory, name + ".kdbx");
            var identity = VaultIdentity.Of(KeypasteHome.Resolve(InItsOwnHome ? _directory : null), path);
            var vault = new BridgeVault(name, path, identity.Key, owner.PipeName, LockedAppEndpoint.NameFor(identity));
            routes.Add(new VaultRoute(vault, connection, new ApproverEntryNameSource(connection, options, vault)));
        }

        _vaults = new BridgeVaults(routes, _inputEnded.Token);

        // Pointed at a real approver, or serving several vaults, the listing goes through the real source too: the point
        // of that constructor is to leave nothing faked but the human.
#pragma warning disable CA2000 // Its one connection is the harness's own, which DisposeAsync disposes.
        var listing = _approverPipeName is null && _others.Count == 0
            ? new BridgeVaults([routes[0] with { Names = Source }], _inputEnded.Token)
            : _vaults;
#pragma warning restore CA2000

        // The bridge's own tools and instructions, without its adopting orphans: after each run that kills every child the process still has, and this one has children that are not runs.
        var serverOptions = BridgeEntry.McpOptionsFor(options, _vaults, _audit, listing);

        _tools = serverOptions.ToolCollection;
        _transcript = new TeeStream(channels.ServerWrites);
        _owned.Add(_transcript);

        _transport = new StreamServerTransport(channels.ServerReads, _transcript, "keypaste");
        _server = McpServer.Create(_transport, serverOptions, loggerFactory: null, serviceProvider: null);
        var server = _server;
        _vaults.Describe(() => McpAudit.AttachIdentity(server.ClientInfo, options), options.Exposure.Globs);
        _serving = _server.RunAsync();

        return channels;
    }

    /// <summary>Has the bridge attach to its owner as the client <paramref name="server"/> names, for a test that calls a tool on a server of its own.</summary>
    internal void AttachAs(McpServer server)
    {
        var options = _options ?? throw new InvalidOperationException("the harness is not serving");
        _vaults!.Describe(() => McpAudit.AttachIdentity(server.ClientInfo, options), options.Exposure.Globs);
    }

    /// <summary>Ends the bridge's input as a client closing its standard input does, as <c>BridgeEntry</c> tells the connection.</summary>
    internal Task EndInputAsync() => _inputEnded.CancelAsync();

    /// <summary>A tool the server is serving, for a test that calls it as the server's dispatch would.</summary>
    internal McpServerTool Tool(string name) =>
        _tools is not null && _tools.TryGetPrimitive(name, out var tool)
            ? tool
            : throw new InvalidOperationException($"the harness is not serving {name}");

    /// <summary>
    /// The audit trail so far, opened the way a reader has to while the server still holds it.
    /// </summary>
    internal string[] AuditLines()
    {
        if (!File.Exists(AuditPath))
        {
            return [];
        }

        using var stream = new FileStream(AuditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// The audit log's raw text, opened the way a reader has to while the server still holds it.
    /// </summary>
    /// <remarks>
    /// Not <see cref="File.ReadAllText(string)"/>: that asks for <see cref="FileShare.Read"/>, which
    /// denies other <em>writers</em>, so it fails outright while a server has the log open. The same
    /// constraint <c>keypaste log</c> is under.
    /// </remarks>
    internal string AuditText => string.Join('\n', AuditLines());

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        if (_serving is not null)
        {
            try
            {
                await _serving;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // Tearing down the pipes under a running server is how it is meant to stop.
            }
        }

        if (_transport is not null)
        {
            await _transport.DisposeAsync();
        }

        await _approver.DisposeAsync();

        foreach (var (_, owner, connection) in _others)
        {
            await connection.DisposeAsync();
            await owner.DisposeAsync();
        }

        _inputEnded.Dispose();
        await Approver.DisposeAsync();

        _audit?.Dispose();
        Source.Dispose();

        foreach (var owned in _owned)
        {
            owned.Dispose();
        }

        Directory.Delete(_directory, recursive: true);
    }
}

/// <summary>
/// Stands in for the vault's entry names, and counts its callers.
/// </summary>
/// <remarks>
/// The call count is what makes "the locked path never touched the vault" directly assertable
/// rather than inferred from the absence of names in the reply.
/// </remarks>
internal sealed class FakeEntryNameSource : IEntryNameSource, IDisposable
{
    private readonly List<ListedEntry> _names = [];

    /// <summary>How the fake vault answers. Locked by default, like the real one.</summary>
    internal VaultAvailability Availability { get; set; } = VaultAvailability.Locked;

    /// <summary>Whether the fake vault claims it gave every name it had.</summary>
    internal bool Complete { get; set; } = true;

    /// <summary>Whether the owner says it recorded each listing, so the bridge writes no line. True by default, as an owner does.</summary>
    internal bool Recorded { get; set; } = true;

    /// <summary>How many times the tool asked.</summary>
    internal int Calls { get; private set; }

    /// <summary>Set to make a call park inside the tool, so a second call can be raced against it.</summary>
    internal ManualResetEventSlim? Held { get; } = new(initialState: false);

    /// <summary>Signalled once a call has genuinely reached the tool, rather than merely been sent.</summary>
    internal ManualResetEventSlim Entered { get; } = new(initialState: false);

    /// <summary>Whether calls should park on <see cref="Held"/> until it is released.</summary>
    internal bool Hold { get; set; }

    /// <summary>
    /// Set to make the source throw something other than cancellation, the way a vault on a failing
    /// disk or a share that went away would.
    /// </summary>
    internal bool Throw { get; set; }

    internal FakeEntryNameSource With(string groupPath, string title) => With(groupPath, title, ["password"], []);

    internal FakeEntryNameSource With(string groupPath, string title, string[] fields, string[] tags)
    {
        _names.Add(new ListedEntry(new EntryName(groupPath, title), fields, tags));
        Availability = VaultAvailability.Available;
        return this;
    }

    public ValueTask<EntryNameListing> ListAsync(CancellationToken cancellationToken)
    {
        Calls++;
        Entered.Set();

        if (Hold)
        {
            Held!.Wait(cancellationToken);
        }

        if (Throw)
        {
            throw new IOException("the vault file went away");
        }

        return ValueTask.FromResult(
            Availability == VaultAvailability.Available
                ? new EntryNameListing(VaultAvailability.Available, _names, string.Empty, Complete) { Recorded = Recorded }
                : new EntryNameListing(Availability, [], ToolText.VaultLocked, true) { Recorded = Recorded });
    }

    /// <summary>Searches are answered by <see cref="FakeApprover"/> over its pipe, so this source is never asked one.</summary>
    public ValueTask<EntryNameListing> FindAsync(AccessQuery query, CancellationToken cancellationToken) =>
        throw new NotSupportedException("the harness's searches go through its FakeApprover");

    public void Dispose()
    {
        Held?.Dispose();
        Entered.Dispose();
    }
}
