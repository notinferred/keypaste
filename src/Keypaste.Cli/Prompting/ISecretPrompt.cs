namespace Keypaste.Cli.Prompting;

/// <summary>
/// Reads values from the user. Prompts are written to stderr so stdout stays data-only.
/// </summary>
/// <remarks>
/// A seam rather than direct <see cref="Console"/> use because <c>Console.SetIn</c> provably does
/// not intercept <c>Console.ReadKey</c>, so there is no way to drive the real prompt from a test.
/// Every password-handling path in the CLI would otherwise be untestable, which CORE.md law 4.5
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
}
