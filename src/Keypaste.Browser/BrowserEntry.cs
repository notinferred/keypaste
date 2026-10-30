using System.Runtime.InteropServices;

namespace Keypaste.Browser;

/// <summary>The one way into keypaste's browser code from <c>keypaste</c>.</summary>
public static class BrowserEntry
{
    /// <summary>
    /// Whether <paramref name="args"/> are a browser starting keypaste as its native messaging host, decided from the arguments
    /// alone so no vault code runs first (D-0418, D-0624).
    /// </summary>
    /// <param name="args">The arguments, without the program.</param>
    /// <returns>True for a Chrome-family caller's origin, with the window it was called from on Windows, or Firefox's manifest path and add-on ID.</returns>
    public static bool StartsRelay(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return BrowserCaller.Of(args, OperatingSystem.IsWindows()) is not null;
    }

    /// <summary>
    /// The relay a browser started: KeePassXC-Browser's host over standard input and output until the browser closes standard
    /// input or sends SIGTERM, or ends its job object on Windows (D-0625).
    /// </summary>
    /// <param name="args">The arguments the browser passed.</param>
    /// <returns>0 once the browser is done; 1, before standard input is read, for a caller keypaste does not answer or a malformed setting, and for a message too large to read.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var stop = new CancellationTokenSource();
        using var terminated = OnTerminate(stop);
        await using var stdout = Console.OpenStandardOutput();

        return await RunAsync(
            args,
            Console.OpenStandardInput,
            stdout,
            Console.Error,
            Environment.GetEnvironmentVariable,
            OperatingSystem.IsWindows(),
            stop.Token).ConfigureAwait(false);
    }

    /// <summary>Stops the relay on SIGTERM, which a browser sends to end its host, so it ends as when standard input closes.</summary>
    private static PosixSignalRegistration? OnTerminate(CancellationTokenSource stop)
    {
        try
        {
            return PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                stop.Cancel();
            });
        }
        catch (PlatformNotSupportedException)
        {
            // Where there is no SIGTERM, the browser ends the relay's job object instead.
            return null;
        }
    }

    /// <summary>The relay with its streams, environment and platform given, so a test can tell what it read.</summary>
    /// <param name="args">The arguments the browser passed.</param>
    /// <param name="input">Opens standard input, which happens only once the caller and the settings are known good.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="windows">Whether this is Windows.</param>
    /// <param name="stop">Cancelled to stop the relay.</param>
    /// <returns>The relay's exit code.</returns>
    internal static async Task<int> RunAsync(
        string[] args,
        Func<Stream> input,
        Stream output,
        TextWriter error,
        Func<string, string?> environment,
        bool windows,
        CancellationToken stop)
    {
        if (!BrowserCaller.TryRead(args, windows, out var identity, out var problem)
            || !RelayOptions.TryResolve(identity, environment, out var options, out problem))
        {
            await error.WriteLineAsync($"keypaste: {problem}").ConfigureAwait(false);
            return 1;
        }

        await using var stdin = input();

        return await Relay.RunAsync(options, stdin, output, error, stop).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>keypaste --selftest</c>: the box's known answers, in this process. CI requires them of each NativeAOT <c>keypaste</c>
    /// it builds, the one binary libsodium is linked into (D-0613).
    /// </summary>
    /// <returns>0 when the box gave every answer, or 1 when it gave a wrong one or libsodium could not be reached.</returns>
    public static int SelfTest(TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        string? disagreement;

        try
        {
            disagreement = KnownAnswers.Disagreement();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or CryptographicException)
        {
            disagreement = exception.Message;
        }

        if (disagreement is not null)
        {
            stderr.WriteLine($"keypaste: selftest failed: {disagreement}");
            return 1;
        }

        stdout.WriteLine(
            "keypaste: selftest ok: libsodium's NaCl box gave its known answers, opened and sealed the box TweetNaCl.js sealed, "
            + "and refused changed boxes and a public key of small order");
        return 0;
    }
}
