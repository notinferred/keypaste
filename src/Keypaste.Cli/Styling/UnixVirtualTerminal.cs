namespace Keypaste.Cli.Styling;

/// <summary>A Unix terminal, which interprets escapes without being asked.</summary>
internal sealed class UnixVirtualTerminal : IVirtualTerminal
{
    /// <inheritdoc/>
    public bool Truecolor => false;

    /// <inheritdoc/>
    public bool TryEnable(StandardStream stream) => true;

    /// <inheritdoc/>
    public void WriteLine(TextWriter writer, string text, ConsoleColor colour)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine(text);
    }
}
