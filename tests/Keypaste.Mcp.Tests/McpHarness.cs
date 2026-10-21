using System.Text;
using Keypaste.Core;
using Keypaste.Core.Audit;
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

    /// <summary>The ceilings a server in its own home lowers.</summary>
    internal string ClientsPath => KeypasteHome.ClientsPath(_directory);

    /// <summary>Where the started bridge looks for the app open and locked on its vault, or null before it starts.</summary>
    internal string? LockedAppName => _options?.LockedAppName;

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
        _approver = new ApproverConnection(Approver.PipeName, VaultPath);
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
        _approver = new ApproverConnection(approverPipeName, VaultPath);
        _approverPipeName = approverPipeName;
    }

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

        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "keypaste", Version = CoreInfo.Version },
            ServerInstructions = ToolText.ServerInstructions,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            ToolCollection = [],
        };

        // Pointed at a real approver, the listing goes through the real source too: the point of
        // that constructor is to leave nothing faked but the human.
        serverOptions.ToolCollection.Add(
            _approverPipeName is null
                ? new ListEntryNamesTool(Source, options, _audit)
                : new ListEntryNamesTool(new ApproverEntryNameSource(_approver, options), options, _audit));
        serverOptions.ToolCollection.Add(new RequestCredentialTool(options, _approver, _audit));
        serverOptions.ToolCollection.Add(new RequestAccessChangeTool(options, _approver, _audit, Clock));

        if (options.AllowRun)
        {
            serverOptions.ToolCollection.Add(new RunTool(options, _approver, _audit));
            serverOptions.ServerInstructions += ToolText.RunInstructions;
        }

        _tools = serverOptions.ToolCollection;
        _transcript = new TeeStream(channels.ServerWrites);
        _owned.Add(_transcript);

        _transport = new StreamServerTransport(channels.ServerReads, _transcript, "keypaste");
        _server = McpServer.Create(_transport, serverOptions, loggerFactory: null, serviceProvider: null);
        var server = _server;
        _approver.Identity = () => McpAudit.AttachIdentity(server.ClientInfo, options);
        _approver.Exposure = options.Exposure.Globs;
        _serving = _server.RunAsync();

        return channels;
    }

    /// <summary>Has the bridge attach to its owner as the client <paramref name="server"/> names, for a test that calls a tool on a server of its own.</summary>
    internal void AttachAs(McpServer server) =>
        _approver.Identity = () => McpAudit.AttachIdentity(server.ClientInfo, _options ?? throw new InvalidOperationException("the harness is not serving"));

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
/// Stands in for the unlocked vault Stage 2.2 will provide, and counts its callers.
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

    public void Dispose()
    {
        Held?.Dispose();
        Entered.Dispose();
    }
}
