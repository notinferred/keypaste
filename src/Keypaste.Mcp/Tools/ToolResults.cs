using System.Text.Json;
using Keypaste.Core.Audit;
using Keypaste.Core.Launch;
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

    /// <summary>A change to the agent's settings that was made.</summary>
    /// <param name="explanation">What to tell the agent. Trusted text written by keypaste.</param>
    /// <returns>A result carrying the explanation and nothing else.</returns>
    internal static CallToolResult Changed(string explanation) => new()
    {
        IsError = false,
        Content = [new TextContentBlock { Text = explanation }],
    };

    /// <summary>A value the person saved, which the result says and never carries.</summary>
    /// <param name="where">Where it was saved.</param>
    /// <returns>A success result saying so, with <c>saved</c> in its structured content.</returns>
    internal static CallToolResult Saved(string where) => new()
    {
        IsError = false,
        Content = [new TextContentBlock { Text = ToolText.Saved(where) }],
        StructuredContent = StructuredSaved(where),
    };

    /// <summary>The one result in keypaste that carries a credential.</summary>
    /// <param name="field">Which field was released.</param>
    /// <param name="value">Its value, as a person approved it.</param>
    /// <param name="codeSeconds">How long a TOTP code stays current; zero for any other field.</param>
    /// <param name="method">Whether a person answered this request or an Allow rule covered it.</param>
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
    internal static CallToolResult Release(string field, string value, int codeSeconds, AuditMethod method) => new()
    {
        IsError = false,
        Content =
        [
            new TextContentBlock
            {
                // Keyed on the method the approver actually returned, so the sentence an agent reads
                // and the word written to the log cannot disagree about whether a person was
                // involved. Telling a model a human approved something no human saw is exactly the
                // claim keypaste asks to be trusted on.
                Text = (method switch
                {
                    AuditMethod.Allowed => ToolText.ReleasedUnasked(field),
                    _ => ToolText.ReleasedOnce(field),
                }) + (codeSeconds > 0 ? ToolText.CodeLasts(codeSeconds) : string.Empty) + value,
            },
        ],
        StructuredContent = Structured(field, value, codeSeconds),
    };

    /// <summary>The result carrying one attachment, as base64, under the same terms as <see cref="Release"/>.</summary>
    /// <param name="name">The attachment's name as the agent asked for it.</param>
    /// <param name="base64">Its bytes as base64.</param>
    /// <param name="method">Whether a person answered this request or an Allow rule covered it.</param>
    /// <returns>A success result carrying exactly one attachment and nothing else.</returns>
    internal static CallToolResult ReleaseAttachment(string name, string base64, AuditMethod method) => new()
    {
        IsError = false,
        Content =
        [
            new TextContentBlock
            {
                Text = (method switch
                {
                    AuditMethod.Allowed => ToolText.ReleasedUnasked(ToolText.AttachmentShown(name)),
                    _ => ToolText.ReleasedOnce(ToolText.AttachmentShown(name)),
                }) + ToolText.Base64Follows + base64,
            },
        ],
        StructuredContent = StructuredAttachment(name, base64),
    };

    /// <summary>A run's result: how it ended and its scrubbed output. It carries no value.</summary>
    /// <param name="names">The variables injected.</param>
    /// <param name="exitCode">The exit code, or null when it was stopped.</param>
    /// <param name="timeoutSeconds">The run's timeout.</param>
    /// <param name="stdout">Standard output, scrubbed.</param>
    /// <param name="stderr">Standard error, scrubbed.</param>
    /// <returns>A success result, even for a non-zero exit: the tool worked and the command did not.</returns>
    internal static CallToolResult Ran(
        IReadOnlyList<string> names,
        int? exitCode,
        int timeoutSeconds,
        ScrubbedText stdout,
        ScrubbedText stderr)
    {
        var replacements = stdout.Replacements + stderr.Replacements;
        var header = ToolText.Ran(names, exitCode, timeoutSeconds, replacements);

        using var buffer = new MemoryStream(1024);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            if (exitCode is { } code)
            {
                writer.WriteNumber("exit_code", code);
            }
            else
            {
                writer.WriteNull("exit_code");
            }

            writer.WriteBoolean("timed_out", exitCode is null);
            writer.WriteStartArray("injected");
            foreach (var name in names)
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
            writer.WriteString("stdout", stdout.Text);
            writer.WriteString("stderr", stderr.Text);
            writer.WriteBoolean("stdout_truncated", stdout.Truncated);
            writer.WriteBoolean("stderr_truncated", stderr.Truncated);
            writer.WriteNumber("replacements", replacements);
            writer.WriteEndObject();
        }

        using var parsed = JsonDocument.Parse(buffer.ToArray());

        return new CallToolResult
        {
            IsError = false,
            Content =
            [
                new TextContentBlock
                {
                    Text = $"{header}\n--- stdout ---\n{stdout.Text}\n--- stderr ---\n{stderr.Text}",
                },
            ],
            StructuredContent = parsed.RootElement.Clone(),
        };
    }

    private static JsonElement StructuredAttachment(string name, string base64)
    {
        using var buffer = new MemoryStream(base64.Length + 128);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("attachment", name);
            writer.WriteString("base64", base64);
            writer.WriteEndObject();
        }

        using var parsed = JsonDocument.Parse(buffer.ToArray());
        return parsed.RootElement.Clone();
    }

    private static JsonElement Structured(string field, string value, int codeSeconds)
    {
        using var buffer = new MemoryStream(256);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("field", field);
            writer.WriteString("value", value);
            writer.WriteNumber("expires_in_seconds", codeSeconds);
            writer.WriteEndObject();
        }

        using var parsed = JsonDocument.Parse(buffer.ToArray());
        return parsed.RootElement.Clone();
    }

    private static JsonElement StructuredSaved(string where)
    {
        using var buffer = new MemoryStream(128);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("saved", true);
            writer.WriteString("where", where);
            writer.WriteEndObject();
        }

        using var parsed = JsonDocument.Parse(buffer.ToArray());
        return parsed.RootElement.Clone();
    }
}
