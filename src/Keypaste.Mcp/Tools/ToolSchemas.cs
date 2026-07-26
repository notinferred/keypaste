using System.Text.Json;

namespace Keypaste.Mcp.Tools;

/// <summary>
/// The agent-facing input contracts, written out rather than generated.
/// </summary>
/// <remarks>
/// <para>
/// The SDK can build a schema by reflecting over a C# method signature, and it compiles perfectly
/// well under this repository's trim and AOT analysers — that was checked, not assumed (D-0019).
/// It is still not used here, because of what it generates: no <c>additionalProperties: false</c>,
/// no enum on <c>field</c>, no bounds on <c>reason</c> or <c>ttl_seconds</c>, no behaviour hints at
/// all, and <c>ttl_seconds</c> renamed to <c>ttlSeconds</c> because that is what the parameter was
/// called.
/// </para>
/// <para>
/// On a credential bridge the schema <em>is</em> the contract with the agent. A contract that is a
/// byproduct of a method signature changes when somebody renames a parameter. This one changes when
/// somebody edits it, in a diff, under review.
/// </para>
/// <para>
/// The schema is also only advisory — MCP clients are not obliged to validate against it — so the
/// server re-checks every argument itself and records <c>invalid</c> where it cannot.
/// </para>
/// </remarks>
internal static class ToolSchemas
{
    /// <summary>The longest <c>entry</c> argument accepted.</summary>
    internal const int MaximumEntryLength = 512;

    /// <summary>The longest <c>reason</c> accepted.</summary>
    internal const int MaximumReasonLength = 2000;

    /// <summary>The longest lifetime an agent may ask for, in seconds.</summary>
    /// <remarks>Mirrors the cap Stage 2.3's policy file will enforce, so the two never disagree.</remarks>
    internal const int MaximumTtlSeconds = 3600;

    /// <summary>
    /// <c>list_entry_names</c> takes nothing at all.
    /// </summary>
    /// <remarks>
    /// No <c>group</c>, no <c>prefix</c>, no <c>limit</c>. Nothing agent-controlled to validate, and
    /// — the actual reason — no parameter that could ever be talked into widening what this server
    /// exposes. Scope is set in the client's configuration, by a human.
    /// </remarks>
    internal const string ListInputJson =
        """{"type":"object","properties":{},"additionalProperties":false}""";

    /// <summary>The four arguments prompts.md specifies for <c>request_credential</c>.</summary>
    internal const string CredentialInputJson = """
        {
          "type": "object",
          "properties": {
            "entry": {
              "type": "string",
              "minLength": 1,
              "maxLength": 512,
              "description": "The handle from list_entry_names (preferred), or the entry's full path."
            },
            "field": {
              "type": "string",
              "enum": ["password", "username", "url", "notes"],
              "description": "Which single field to release. Never more than one."
            },
            "reason": {
              "type": "string",
              "minLength": 1,
              "maxLength": 2000,
              "description": "Shown verbatim to the human who approves or denies. Be specific and honest."
            },
            "ttl_seconds": {
              "type": "integer",
              "minimum": 1,
              "maximum": 3600,
              "description": "How long the grant should last, in seconds."
            }
          },
          "required": ["entry", "field", "reason", "ttl_seconds"],
          "additionalProperties": false
        }
        """;

    /// <summary>The parsed schema for <c>list_entry_names</c>.</summary>
    internal static readonly JsonElement ListInput = Parse(ListInputJson);

    /// <summary>The parsed schema for <c>request_credential</c>.</summary>
    internal static readonly JsonElement CredentialInput = Parse(CredentialInputJson);

    /// <summary>The field names <c>request_credential</c> will accept.</summary>
    /// <remarks>
    /// A field rather than an inline collection because a constant array passed as an argument is a
    /// build error in this repository (CA1861).
    /// </remarks>
    internal static readonly string[] AllowedFields = ["password", "username", "url", "notes"];

    /// <summary>
    /// Detaches the element from its document, so the parsed schema outlives the parse without
    /// leaving a <see cref="JsonDocument"/> undisposed.
    /// </summary>
    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
