using Keypaste.Core;

namespace Keypaste.Cli.Prompting;

/// <summary>
/// Reads values from the user. Prompts are written to stderr so stdout stays data-only.
/// </summary>
/// <remarks>
/// A seam rather than direct <see cref="Console"/> use because <c>Console.SetIn</c> provably does
/// not intercept <c>Console.ReadKey</c>, so there is no way to drive the real prompt from a test.
/// Every password-handling path in the CLI would otherwise be untestable, which docs/PRODUCT.md law 4.5
/// does not allow on the secret path.
/// </remarks>
internal interface ISecretPrompt
{
    /// <summary>
    /// Whether input is coming from a terminal. When false, prompts are not written and each
    /// read consumes exactly one line of stdin, in a fixed order per verb.
    /// </summary>
    bool IsInteractive { get; }

    /// <summary>Reads a secret without echoing it. Returns null at end of input.</summary>
    /// <param name="prompt">Shown to the user when interactive.</param>
    SecretBuffer? ReadSecret(string prompt);

    /// <summary>Reads an ordinary, echoed line. Returns null at end of input.</summary>
    /// <param name="prompt">Shown to the user when interactive.</param>
    string? ReadLine(string prompt);

    /// <summary>
    /// Reads one choice. Interactive: single keypresses without echo, redrawing
    /// <paramref name="prompt"/> about once a second until a key in <paramref name="choices"/>, a
    /// deny key or cancellation. Redirected: one line, its trimmed lowercase first word.
    /// </summary>
    /// <param name="prompt">The choice line as it should read now; called again for each redraw.</param>
    /// <param name="choices">The lowercase keys that choose something other than no.</param>
    /// <param name="cancellationToken">Stops waiting for a key.</param>
    /// <returns>The chosen key, <c>'d'</c> for a deny key or anything else a line said, or null at end of input or cancellation.</returns>
    char? ReadChoice(Func<string> prompt, string choices, CancellationToken cancellationToken);
}
