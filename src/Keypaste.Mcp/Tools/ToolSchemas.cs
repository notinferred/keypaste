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

    /// <summary>The four arguments the build prompt specifies for <c>request_credential</c>.</summary>
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
              "description": "How long you would like it for. The person chooses once or a timed grant; standing rules honour this number."
            }
          },
          "required": ["entry", "field", "reason", "ttl_seconds"],
          "additionalProperties": false
        }
        """;

    /// <summary>The arguments of <c>run</c>: the command, where, which variables and why.</summary>
    /// <remarks>The bounds are <c>RunRequestRules</c>'s, which the bridge and the owner apply again.</remarks>
    internal const string RunInputJson = """
        {
          "type": "object",
          "properties": {
            "command": {
              "type": "array",
              "items": { "type": "string", "maxLength": 4096 },
              "minItems": 1,
              "maxItems": 256,
              "description": "The program and its arguments, one per item. No shell; the person approves exactly this."
            },
            "directory": {
              "type": "string",
              "minLength": 1,
              "maxLength": 1024,
              "description": "Absolute path of an existing directory to run in."
            },
            "project": {
              "type": "string",
              "minLength": 1,
              "maxLength": 128,
              "description": "The env project whose set to inject. Give this or env."
            },
            "profile": {
              "type": "string",
              "pattern": "^[a-z0-9][a-z0-9-]{0,31}$",
              "description": "The project's profile. Defaults to dev."
            },
            "keys": {
              "type": "array",
              "items": { "type": "string", "maxLength": 128 },
              "minItems": 1,
              "maxItems": 32,
              "uniqueItems": true,
              "description": "Only these keys of the set. Omit for the whole set."
            },
            "env": {
              "type": "object",
              "minProperties": 1,
              "maxProperties": 32,
              "additionalProperties": { "type": "string", "maxLength": 2048, "pattern": "^kp://" },
              "description": "Variable name to kp:// reference. Give this or project."
            },
            "reason": {
              "type": "string",
              "minLength": 1,
              "maxLength": 2000,
              "description": "Shown verbatim to the person who approves or denies. Be specific and honest."
            },
            "timeout_seconds": {
              "type": "integer",
              "minimum": 1,
              "maximum": 600,
              "description": "How long the command may run once started. Defaults to 120."
            }
          },
          "required": ["command", "directory", "reason"],
          "additionalProperties": false
        }
        """;

    /// <summary>The parsed schema for <c>run</c>.</summary>
    internal static readonly JsonElement RunInput = Parse(RunInputJson);

    /// <summary>The parsed schema for <c>list_entry_names</c>.</summary>
    internal static readonly JsonElement ListInput = Parse(ListInputJson);

    /// <summary>The parsed schema for <c>request_credential</c>.</summary>
    internal static readonly JsonElement CredentialInput = Parse(CredentialInputJson);

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
