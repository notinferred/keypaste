namespace Keypaste.Cli.Styling;

/// <summary>Output that must not be missed.</summary>
/// <remarks>
/// The only place keypaste uses colour. It takes the writer rather than returning a decorated
/// string so that the two platforms can reach the terminal differently — which they must — without
/// any caller learning about it.
/// </remarks>
internal interface IConsoleStyle
{
    /// <summary>Writes one line loudly, or plainly when the terminal cannot show it.</summary>
    void Alarm(TextWriter writer, string text);
}
