namespace Keypaste.Cli.Styling;

/// <summary>One of the process's two standard output streams, numbered as <c>GetStdHandle</c> takes them.</summary>
internal enum StandardStream
{
    Output = -11,
    Error = -12,
}

/// <summary>Whether a console shows escape sequences, and how to colour one that does not.</summary>
internal interface IVirtualTerminal
{
    /// <summary>Whether a terminal that takes escapes takes 24-bit colour without <c>COLORTERM</c> saying so.</summary>
    bool Truecolor { get; }

    /// <summary>Asks the console behind <paramref name="stream"/> to interpret escape sequences.</summary>
    /// <returns><see langword="false"/> when it cannot, so text written there stays plain.</returns>
    bool TryEnable(StandardStream stream);

    /// <summary>Writes one line in a console colour without escapes, for a terminal that refused them.</summary>
    void WriteLine(TextWriter writer, string text, ConsoleColor colour);
}
