using System.Text.Json;
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

    /// <summary>The one result in keypaste that carries a credential.</summary>
    /// <param name="field">Which field was released.</param>
    /// <param name="value">Its value, as a person approved it.</param>
    /// <param name="ttlSeconds">How long the grant lasts.</param>
    /// <returns>A success result carrying exactly one field value and nothing else.</returns>
    /// <remarks>
    /// <para>
    /// The value goes in both the text content and the structured content, on purpose: a client is
    /// free to read either, and one that read only the half we left empty would turn a working
    /// approval into a silent failure. It does mean the credential appears twice in one message,
    /// which is the honest cost of the protocol having two ways to say the same thing.
    /// </para>
    /// <para>
    /// Nothing else is in here. No entry name, no user name, no URL, no notes — not even the entry
    /// the agent asked about, which it already knows. The audit log is where the context lives; this
    /// message is the credential and the terms it came with.
    /// </para>
    /// </remarks>
    internal static CallToolResult Release(string field, string value, int ttlSeconds) => new()
    {
        IsError = false,
        Content = [new TextContentBlock { Text = ToolText.Released(field, ttlSeconds) + value }],
        StructuredContent = Structured(field, value, ttlSeconds),
    };

    private static JsonElement Structured(string field, string value, int ttlSeconds)
    {
        using var buffer = new MemoryStream(256);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("field", field);
            writer.WriteString("value", value);
            writer.WriteNumber("expires_in_seconds", ttlSeconds);
            writer.WriteEndObject();
        }

        using var parsed = JsonDocument.Parse(buffer.ToArray());
        return parsed.RootElement.Clone();
    }
}
