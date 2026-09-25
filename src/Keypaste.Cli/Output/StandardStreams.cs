using System.Text;

namespace Keypaste.Cli.Output;

/// <summary>The writers keypaste gives its stdout and stderr.</summary>
/// <remarks>
/// Redirected on Windows, .NET encodes in the console code page, while the reader of a pipe, such as
/// Git Bash's terminal, decodes UTF-8 and shows <c>·</c> as <c>�</c>. A redirected stream there is
/// written as UTF-8 without a byte order mark instead. A console keeps .NET's writer, whose code page
/// is what the console draws, and every stream elsewhere follows the locale.
/// </remarks>
internal static class StandardStreams
{
    internal static TextWriter Output() =>
        For(OperatingSystem.IsWindows() && Console.IsOutputRedirected, Console.OpenStandardOutput, () => Console.Out);

    internal static TextWriter Error() =>
        For(OperatingSystem.IsWindows() && Console.IsErrorRedirected, Console.OpenStandardError, () => Console.Error);

    internal static TextWriter For(bool utf8, Func<Stream> open, Func<TextWriter> console)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(console);

        return utf8 ? Utf8(open()) : console();
    }

    /// <summary>Synchronized and flushed on every write, as .NET's own console writers are.</summary>
    internal static TextWriter Utf8(Stream stream)
    {
        // The writer lives for the process, as the console's own writers do, so nothing disposes it.
#pragma warning disable CA2000
        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
#pragma warning restore CA2000
        return TextWriter.Synchronized(writer);
    }
}
