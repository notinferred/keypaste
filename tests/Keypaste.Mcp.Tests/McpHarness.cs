using System.IO.Pipes;
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
/// <c>StdioServerTransport</c> and <c>Main</c>, which is what
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

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-mcp-tests-").FullName;
    private readonly List<IDisposable> _owned = [];

    private readonly ApproverConnection _approver;

    private AuditLog? _audit;
    private McpServer? _server;
    private McpClient? _client;
    private StreamServerTransport? _transport;
    private Task? _serving;

    /// <summary>The vault the server will be pointed at. Never opened: nothing opens a vault here.</summary>
    internal string VaultPath => Path.Combine(_directory, "vault.kdbx");

    internal string AuditPath => Path.Combine(_directory, "audit.jsonl");

    /// <summary>What the listing tool will be told the vault contains.</summary>
    internal FakeEntryNameSource Source { get; } = new();

    /// <summary>
    /// A real <c>keypaste agent</c>, in this process, answering over a real named pipe.
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
        _approver = new ApproverConnection(Approver.PipeName);
    }

    /// <summary>Starts the server with the given arguments and connects a client to it.</summary>
    internal async Task<McpClient> StartAsync(params string[] argv)
    {
        var arguments = argv.Concat(["--vault", VaultPath, "--audit-log", AuditPath]).ToArray();

        if (!ServerOptions.TryParse(arguments, null, null, Approver.PipeName, out var options, out var error))
        {
            throw new InvalidOperationException($"the harness could not start the server: {error}");
        }

        if (!AuditLog.TryOpen(options.AuditPath, TimeProvider.System, out _audit, out var auditError))
        {
            throw new InvalidOperationException($"the harness could not open the audit log: {auditError}");
        }

        // Two one-way pipes: one carrying the client's requests, one carrying the server's replies.
        var toServer = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var toServerRead = new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle);
        var toClient = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var toClientRead = new AnonymousPipeClientStream(PipeDirection.In, toClient.ClientSafePipeHandle);

        _owned.Add(toServer);
        _owned.Add(toServerRead);
        _owned.Add(toClient);
        _owned.Add(toClientRead);

        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "keypaste", Version = CoreInfo.Version },
            ServerInstructions = ToolText.ServerInstructions,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            ToolCollection = [],
        };

        serverOptions.ToolCollection.Add(new ListEntryNamesTool(Source, options, _audit));
        serverOptions.ToolCollection.Add(new RequestCredentialTool(options, _approver, _audit));

        _transport = new StreamServerTransport(toServerRead, toClient, "keypaste");
        _server = McpServer.Create(_transport, serverOptions, loggerFactory: null, serviceProvider: null);
        _serving = _server.RunAsync();

        _client = await McpClient.CreateAsync(
            new StreamClientTransport(toServer, toClientRead),
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = ClientName, Version = ClientVersion },
            },
            loggerFactory: null,
            CancellationToken.None);

        return _client;
    }

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
    private readonly List<EntryName> _names = [];

    /// <summary>How the fake vault answers. Locked by default, like the real one.</summary>
    internal VaultAvailability Availability { get; set; } = VaultAvailability.Locked;

    /// <summary>How many times the tool asked.</summary>
    internal int Calls { get; private set; }

    /// <summary>Set to make a call park inside the tool, so a second call can be raced against it.</summary>
    internal ManualResetEventSlim? Held { get; } = new(initialState: false);

    /// <summary>Signalled once a call has genuinely reached the tool, rather than merely been sent.</summary>
    internal ManualResetEventSlim Entered { get; } = new(initialState: false);

    /// <summary>Whether calls should park on <see cref="Held"/> until it is released.</summary>
    internal bool Hold { get; set; }

    internal FakeEntryNameSource With(string groupPath, string title)
    {
        _names.Add(new EntryName(groupPath, title));
        Availability = VaultAvailability.Available;
        return this;
    }

    public EntryNameListing List()
    {
        Calls++;
        Entered.Set();

        if (Hold)
        {
            Held!.Wait();
        }

        return Availability == VaultAvailability.Available
            ? new EntryNameListing(VaultAvailability.Available, _names, string.Empty)
            : new EntryNameListing(Availability, [], ToolText.VaultLocked);
    }

    public void Dispose()
    {
        Held?.Dispose();
        Entered.Dispose();
    }
}
