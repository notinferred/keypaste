using Keypaste.Cli.Prompting;

namespace Keypaste.Cli.Styling;

/// <summary>Red on a terminal, plain text everywhere else.</summary>
/// <remarks>
/// <para>
/// <b>Whether to colour is decided once, at construction.</b> Colour goes to stderr, so the test is
/// whether <em>stderr</em> is a terminal — not stdout, which <c>env export --stdout</c> pipes on
/// purpose. <c>NO_COLOR</c> and <c>TERM=dumb</c> are both honoured, so a build log or a CI
/// annotation never has escape sequences in it.
/// </para>
/// <para>
/// <b>The two platforms reach the terminal differently, and the split is not cosmetic.</b> On Unix
/// the escape is written straight into the target writer, because .NET's own
/// <see cref="Console.ForegroundColor"/> emits its escape to <em>stdout</em> — which would inject
/// escape codes into a pipe while trying to colour the terminal beside it. On Windows the reverse
/// is true: raw escapes render only when <c>ENABLE_VIRTUAL_TERMINAL_PROCESSING</c> is set on the
/// console, which conhost does not do for us, and <see cref="Console.ForegroundColor"/> is the
/// console attribute API that works whether or not it is. Two <c>kernel32</c> P/Invokes to force
/// the mode would be a dependency on the secret path bought for a warning colour (docs/PRODUCT.md law 3.9).
/// </para>
/// </remarks>
internal sealed class SystemConsoleStyle : IConsoleStyle
{
    /// <summary>Bold red.</summary>
    internal const string Red = "\u001b[1;31m";

    /// <summary>Back to whatever the terminal was doing before.</summary>
    internal const string Reset = "\u001b[0m";

    private readonly bool _enabled;

    internal SystemConsoleStyle(IEnvironmentProbe environment)
        : this(environment, Console.IsErrorRedirected)
    {
    }

    internal SystemConsoleStyle(IEnvironmentProbe environment, bool isErrorRedirected)
    {
        ArgumentNullException.ThrowIfNull(environment);

        _enabled = !isErrorRedirected
            && environment.Get("NO_COLOR") is not { Length: > 0 }
            && !string.Equals(environment.Get("TERM"), "dumb", StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public void Alarm(TextWriter writer, string text)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (!_enabled)
        {
            writer.WriteLine(text);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            try
            {
                writer.WriteLine(text);
                writer.Flush();
            }
            finally
            {
                Console.ForegroundColor = previous;
            }

            return;
        }

        writer.WriteLine($"{Red}{text}{Reset}");
    }
}
