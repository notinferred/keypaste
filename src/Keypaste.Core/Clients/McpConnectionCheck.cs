using System.Buffers;
using System.Diagnostics;
using System.Text.Json;

namespace Keypaste.Core.Clients;

/// <summary>An entry the bridge listed, as the check can ask for it.</summary>
/// <param name="Handle">The bridge's opaque handle, which is what the request names.</param>
/// <param name="Name">The group path and title as the bridge sanitized them, for a person to read.</param>
public sealed record McpListedEntry(string Handle, string Name);

/// <summary>What the bridge's listing returned.</summary>
/// <param name="Entries">The exposed entries, empty when the listing was refused or failed.</param>
/// <param name="Problem">Why there is no listing: the bridge's refusal or what went wrong; null when it listed.</param>
public sealed record McpCheckListing(IReadOnlyList<McpListedEntry> Entries, string? Problem);

/// <summary>How the check's credential request ended.</summary>
public enum McpCheckOutcome
{
    /// <summary>The bridge released the field; the check discarded it unread.</summary>
    Granted,

    /// <summary>The bridge answered with a refusal, such as a person pressing Deny.</summary>
    Denied,

    /// <summary>No answer came back: the bridge exited, timed out or spoke something else.</summary>
    Failed,
}

/// <summary>The answer to the check's one credential request, never carrying the value.</summary>
/// <param name="Outcome">Granted, denied or failed.</param>
/// <param name="Said">The first line of the refusal or failure; empty on a grant.</param>
public sealed record McpCheckAnswer(McpCheckOutcome Outcome, string Said);

/// <summary>
/// Starts the bridge exactly as a client was told to, and asks it what a client would.
/// </summary>
/// <remarks>
/// <para>
/// A check that only opened the vault's endpoint would prove the endpoint, not the connection:
/// the bridge's arguments, its exposure, its audit log and the MCP handshake would all go
/// untested. So this starts <see cref="McpServerRegistration.CommandLine"/> as a child process with
/// this process's environment, which is what places its audit records and its endpoint beside the
/// app's, and speaks newline-delimited JSON-RPC over its standard streams.
/// </para>
/// <para>
/// Hand-written over <see cref="System.Text.Json"/> rather than the MCP SDK's client, so the
/// desktop takes no dependency the bridge alone was allowed (D-0019). It speaks the three messages
/// a client needs and nothing else.
/// </para>
/// <para>
/// <b>The released value is never read.</b> A grant's reply is parsed in place from the bytes it
/// arrived in; only the error flag and the lifetime are taken from it, and the buffer is zeroed
/// before it is returned to the pool. No string ever holds the value.
/// </para>
/// </remarks>
public sealed class McpConnectionCheck : IAsyncDisposable
{
    /// <summary>What the check calls itself in <c>initialize</c>, and so in the prompt and the audit log.</summary>
    public const string ClientName = "keypaste-check";

    /// <summary>The reason the prompt shows the person for the check's request.</summary>
    public const string Reason = "Connection check from the keypaste app. The value is discarded unread.";

    /// <summary>The field the check asks for.</summary>
    public const string Field = "password";

    /// <summary>The lifetime the check asks for. Its grant ends with the check's bridge anyway.</summary>
    public const int TtlSeconds = 60;

    /// <summary>How long the handshake and the listing may take.</summary>
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long the credential request may wait: longer than the prompt window gives a person.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private const string _protocolVersion = "2025-11-25";
    private const int _maximumLine = 1024 * 1024;
    private const int _stderrKept = 4096;

    private readonly Stream _toServer;
    private readonly Stream _fromServer;
    private readonly Process? _process;
    private readonly Task<string> _stderr;
    private readonly TimeProvider _clock;
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
    private int _buffered;
    private int _nextId = 1;
    private bool _initialized;
    private bool _disposed;

    /// <summary>Speaks to a bridge over streams somebody else owns.</summary>
    /// <param name="toServer">What the bridge reads.</param>
    /// <param name="fromServer">What the bridge writes.</param>
    /// <param name="clock">What the deadlines are measured by; the system clock when null.</param>
    public McpConnectionCheck(Stream toServer, Stream fromServer, TimeProvider? clock = null)
        : this(toServer, fromServer, process: null, Task.FromResult(string.Empty), clock ?? TimeProvider.System)
    {
    }

    private McpConnectionCheck(Stream toServer, Stream fromServer, Process? process, Task<string> stderr, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(toServer);
        ArgumentNullException.ThrowIfNull(fromServer);

        _toServer = toServer;
        _fromServer = fromServer;
        _process = process;
        _stderr = stderr;
        _clock = clock;
    }

    /// <summary>Starts the bridge <paramref name="registration"/> describes.</summary>
    /// <returns>The check, or null when the executable could not be started.</returns>
    public static McpConnectionCheck? Start(McpServerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var line = registration.CommandLine();
        var info = new ProcessStartInfo(line[0])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in line.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        return process is null
            ? null
            : new McpConnectionCheck(
                process.StandardInput.BaseStream,
                process.StandardOutput.BaseStream,
                process,
                KeepStart(process.StandardError),
                TimeProvider.System);
    }

    /// <summary>Introduces the check and asks the bridge for the names it may list.</summary>
    public async Task<McpCheckListing> ListAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var timeout = new CancellationTokenSource(StepTimeout, _clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            if (!_initialized)
            {
                using var hello = await CallAsync("initialize", Initialize, deadline.Token).ConfigureAwait(false);
                if (Failure(hello) is { } refused)
                {
                    return new McpCheckListing([], refused);
                }

                await SendAsync(writer =>
                {
                    writer.WriteString("jsonrpc", "2.0");
                    writer.WriteString("method", "notifications/initialized");
                }, deadline.Token).ConfigureAwait(false);
                _initialized = true;
            }

            var listed = await CallToolAsync("list_entry_names", _ => { }, deadline.Token).ConfigureAwait(false);
            using (listed)
            {
                if (Failure(listed) is { } failed)
                {
                    return new McpCheckListing([], failed);
                }

                var result = listed.Document!.RootElement.GetProperty("result");
                if (IsError(result))
                {
                    return new McpCheckListing([], ToolText(result));
                }

                return new McpCheckListing(Entries(result), Problem: null);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new McpCheckListing([], $"the bridge did not answer within {StepTimeout.TotalSeconds:0} seconds");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return new McpCheckListing([], await Broken(ex).ConfigureAwait(false));
        }
    }

    /// <summary>Asks for <see cref="Field"/> of <paramref name="entry"/>, which a person decides.</summary>
    public async Task<McpCheckAnswer> RequestAsync(McpListedEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var timeout = new CancellationTokenSource(RequestTimeout, _clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var reply = await CallToolAsync(
                "request_credential",
                writer =>
                {
                    writer.WriteString("entry", entry.Handle);
                    writer.WriteString("field", Field);
                    writer.WriteString("reason", Reason);
                    writer.WriteNumber("ttl_seconds", TtlSeconds);
                },
                deadline.Token).ConfigureAwait(false);

            using (reply)
            {
                if (Failure(reply) is { } failed)
                {
                    return new McpCheckAnswer(McpCheckOutcome.Failed, failed);
                }

                // Only the flag, and on a refusal its words: a grant's text and structured content
                // hold the value, and neither is turned into a string.
                var result = reply.Document!.RootElement.GetProperty("result");
                return IsError(result)
                    ? new McpCheckAnswer(McpCheckOutcome.Denied, ToolText(result))
                    : new McpCheckAnswer(McpCheckOutcome.Granted, string.Empty);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new McpCheckAnswer(
                McpCheckOutcome.Failed,
                $"no answer within {RequestTimeout.TotalSeconds:0} seconds");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return new McpCheckAnswer(McpCheckOutcome.Failed, await Broken(ex).ConfigureAwait(false));
        }
    }

    /// <summary>Ends the bridge: its input closes, and it is killed if it does not exit.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await _toServer.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The bridge already went away.
        }

        if (_process is not null)
        {
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Exited between the wait and here.
                }
            }

            _process.Dispose();
        }

        CryptographicOperations.ZeroMemory(_buffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    private static void Initialize(Utf8JsonWriter writer)
    {
        writer.WriteString("protocolVersion", _protocolVersion);
        writer.WriteStartObject("capabilities");
        writer.WriteEndObject();
        writer.WriteStartObject("clientInfo");
        writer.WriteString("name", ClientName);
        writer.WriteString("version", CoreInfo.Version);
        writer.WriteEndObject();
    }

    private Task<Reply> CallToolAsync(string tool, Action<Utf8JsonWriter> arguments, CancellationToken cancellationToken) =>
        CallAsync(
            "tools/call",
            writer =>
            {
                writer.WriteString("name", tool);
                writer.WriteStartObject("arguments");
                arguments(writer);
                writer.WriteEndObject();
            },
            cancellationToken);

    private async Task<Reply> CallAsync(string method, Action<Utf8JsonWriter> parameters, CancellationToken cancellationToken)
    {
        var id = _nextId++;

        await SendAsync(writer =>
        {
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            writer.WriteStartObject("params");
            parameters(writer);
            writer.WriteEndObject();
        }, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            if (await ReadLineAsync(cancellationToken).ConfigureAwait(false) is not var (line, length))
            {
                return new Reply(null, null, await Exited().ConfigureAwait(false));
            }

            if (length == 0)
            {
                ArrayPool<byte>.Shared.Return(line);
                continue;
            }

            var reply = Parse(line, length);
            if (reply.Document is null)
            {
                return reply;
            }

            if (reply.Document.RootElement.TryGetProperty("id", out var answered)
                && answered.ValueKind == JsonValueKind.Number
                && answered.TryGetInt32(out var number)
                && number == id)
            {
                return reply;
            }

            // A notification or a request of the server's own; the check answers none of them.
            reply.Dispose();
        }
    }

    private async Task SendAsync(Action<Utf8JsonWriter> message, CancellationToken cancellationToken)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            message(writer);
            writer.WriteEndObject();
        }

        output.Write("\n"u8);
        await _toServer.WriteAsync(output.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await _toServer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one line into a pooled array the reply owns, or null at the end of the stream.</summary>
    private async Task<(byte[] Line, int Length)?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var newline = Array.IndexOf(_buffer, (byte)'\n', 0, _buffered);
            if (newline >= 0)
            {
                var line = ArrayPool<byte>.Shared.Rent(Math.Max(newline, 1));
                Array.Copy(_buffer, line, newline);

                var rest = _buffered - newline - 1;
                Array.Copy(_buffer, newline + 1, _buffer, 0, rest);
                CryptographicOperations.ZeroMemory(_buffer.AsSpan(rest, _buffered - rest));
                _buffered = rest;

                return (line, newline);
            }

            if (_buffered == _buffer.Length)
            {
                if (_buffer.Length >= _maximumLine)
                {
                    throw new InvalidDataException("the bridge sent a line longer than the check reads");
                }

                var larger = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
                Array.Copy(_buffer, larger, _buffered);
                CryptographicOperations.ZeroMemory(_buffer);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = larger;
            }

            var read = await _fromServer.ReadAsync(_buffer.AsMemory(_buffered), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            _buffered += read;
        }
    }

    private static Reply Parse(byte[] line, int length)
    {
        try
        {
            return new Reply(JsonDocument.Parse(line.AsMemory(0, length)), line, Problem: null);
        }
        catch (JsonException)
        {
            CryptographicOperations.ZeroMemory(line);
            ArrayPool<byte>.Shared.Return(line);
            return new Reply(null, null, "the bridge sent something that is not JSON-RPC");
        }
    }

    private static string? Failure(Reply reply)
    {
        if (reply.Document is null)
        {
            return reply.Problem;
        }

        var root = reply.Document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            return error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                ? FirstLine(message.GetString())
                : "the bridge answered with an error";
        }

        return root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
            ? null
            : "the bridge answered with no result";
    }

    private static bool IsError(JsonElement result) =>
        result.TryGetProperty("isError", out var flag) && flag.ValueKind == JsonValueKind.True;

    /// <summary>The first line of a refusal's text. Only ever called on a result that released nothing.</summary>
    private static string ToolText(JsonElement result)
    {
        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return FirstLine(text.GetString());
                }
            }
        }

        return "the bridge refused without saying why";
    }

    private static List<McpListedEntry> Entries(JsonElement result)
    {
        List<McpListedEntry> entries = [];

        if (result.TryGetProperty("structuredContent", out var structured)
            && structured.TryGetProperty("entries", out var listed)
            && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in listed.EnumerateArray())
            {
                var handle = Text(entry, "handle");
                var name = Text(entry, "name");
                if (handle.Length == 0 || name.Length == 0)
                {
                    continue;
                }

                var group = Text(entry, "group");
                entries.Add(new McpListedEntry(handle, group.Length == 0 ? name : group + "/" + name));
            }
        }

        return entries;
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string FirstLine(string? text) =>
        (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? string.Empty;

    /// <summary>A write to a bridge that has gone, or a line too long to be an answer.</summary>
    private async Task<string> Broken(Exception ex) =>
        ex is InvalidDataException ? ex.Message : await Exited().ConfigureAwait(false);

    private async Task<string> Exited()
    {
        var said = FirstLine(await _stderr.ConfigureAwait(false));
        return said.Length > 0 ? $"the bridge exited: {said}" : "the bridge exited without answering";
    }

    /// <summary>Keeps the start of what the bridge writes to stderr, and drains the rest.</summary>
    private static async Task<string> KeepStart(StreamReader stderr)
    {
        var kept = new StringBuilder();
        var chunk = new char[1024];
        int read;
        while ((read = await stderr.ReadAsync(chunk).ConfigureAwait(false)) > 0)
        {
            if (kept.Length < _stderrKept)
            {
                kept.Append(chunk, 0, Math.Min(read, _stderrKept - kept.Length));
            }
        }

        return kept.ToString();
    }

    /// <summary>A parsed reply over the pooled line it was read into, zeroed when disposed.</summary>
    private sealed record Reply(JsonDocument? Document, byte[]? Line, string? Problem) : IDisposable
    {
        public void Dispose()
        {
            Document?.Dispose();
            if (Line is not null)
            {
                CryptographicOperations.ZeroMemory(Line);
                ArrayPool<byte>.Shared.Return(Line);
            }
        }
    }
}
