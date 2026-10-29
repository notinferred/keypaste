using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Keypaste.AppDriver;

/// <summary>
/// A stand-in for keypaste.com's <c>/api/crash</c> on loopback, which <c>scripts/verify-crash-report.sh</c> points
/// <c>KEYPASTE_CRASH_URL</c> at (4.11c): it answers each request 204 and keeps its request line and headers as
/// <c>&lt;n&gt;.head</c> and its body as <c>&lt;n&gt;.json</c> in a folder, or, with <c>--silent</c>, takes each
/// connection and never answers, as an unreachable server that holds the line open would.
/// </summary>
/// <remarks>
/// It prints <c>listening on http://127.0.0.1:&lt;port&gt;</c>, then <c>connected</c> for each connection and
/// <c>received &lt;n&gt;</c> for each request it answered, and ends once standard input closes. A plain HTTP/1.1 reader
/// over a socket, since the app sends one small request a connection and needs nothing more.
/// </remarks>
internal static class CrashEndpoint
{
    private const int _largestHead = 16 * 1024;
    private const int _largestBody = 64 * 1024;

    internal static int Run(string folder, bool silent)
    {
        Directory.CreateDirectory(folder);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var served = 0;
        List<TcpClient> held = [];

        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"listening on http://127.0.0.1:{port}"));

        var accepting = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                Console.Out.WriteLine("connected");

                if (silent)
                {
                    held.Add(client);
                    continue;
                }

                using (client)
                {
                    await ServeAsync(client, folder, Interlocked.Increment(ref served)).ConfigureAwait(false);
                }
            }
        });

        while (Console.In.ReadLine() is not null)
        {
        }

        stop.Cancel();
        accepting.GetAwaiter().GetResult();

        foreach (var client in held)
        {
            client.Dispose();
        }

        return 0;
    }

    private static async Task ServeAsync(TcpClient client, string folder, int n)
    {
        try
        {
            var stream = client.GetStream();
            var head = await ReadHeadAsync(stream).ConfigureAwait(false);
            var length = ContentLength(head);

            if (length is null or > _largestBody)
            {
                await AnswerAsync(stream, "400 Bad Request").ConfigureAwait(false);
                return;
            }

            var body = new byte[length.Value];
            await stream.ReadExactlyAsync(body).ConfigureAwait(false);

            var name = n.ToString(CultureInfo.InvariantCulture);
            await File.WriteAllTextAsync(Path.Combine(folder, name + ".head"), head).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(folder, name + ".json"), body).ConfigureAwait(false);
            await AnswerAsync(stream, "204 No Content").ConfigureAwait(false);
            Console.Out.WriteLine("received " + name);
        }
        catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException)
        {
            Console.Error.WriteLine($"crash endpoint: a request ended early: {ex.GetType().Name}");
        }
    }

    private static async Task<string> ReadHeadAsync(NetworkStream stream)
    {
        var head = new List<byte>();
        var one = new byte[1];

        while (head.Count < _largestHead)
        {
            if (await stream.ReadAsync(one).ConfigureAwait(false) == 0)
            {
                throw new EndOfStreamException();
            }

            head.Add(one[0]);

            if (head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n')
            {
                return Encoding.ASCII.GetString(head.ToArray());
            }
        }

        throw new IOException("the request's head is too long");
    }

    private static int? ContentLength(string head)
    {
        foreach (var line in head.Split("\r\n"))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon > 0
                && line[..colon].Trim().Equals("content-length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(colon + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
            {
                return length;
            }
        }

        return null;
    }

    private static Task AnswerAsync(NetworkStream stream, string status) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")).AsTask();
}
