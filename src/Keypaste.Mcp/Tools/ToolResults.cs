using ModelContextProtocol.Protocol;

namespace Keypaste.Mcp.Tools;

/// <summary>
/// Builds the protocol shapes for an answer and for a refusal.
/// </summary>
/// <remarks>
/// <para>
/// <b>A refusal is returned, never thrown.</b> The SDK catches an exception out of a tool and
/// replaces its message with a generic one unless it derives from the SDK's own exception type — so
/// a thrown refusal would silently lose every word of the explanation, and the human would see "an
/// error occurred" where keypaste meant to say "the vault is locked, and here is why it cannot be
/// unlocked". Every deliberate no goes through here.
/// </para>
/// <para>
/// One file, so that if the result shape changes in a future SDK version there is one place to
/// change (D-0019).
/// </para>
/// </remarks>
internal static class ToolResults
{
    /// <summary>A deliberate no, with the reason intact.</summary>
    /// <param name="explanation">What to tell the agent. Trusted text written by keypaste.</param>
    /// <returns>An error result carrying the explanation.</returns>
    /// <remarks>
    /// <c>IsError</c> is set because the MCP specification says a client should feed tool errors
    /// back to the model for self-correction, which is exactly what should happen: the agent needs
    /// to stop asking and tell the person what it needs.
    /// </remarks>
    internal static CallToolResult Refuse(string explanation) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = explanation }],
    };
}
