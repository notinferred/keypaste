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
              "description": "The handle from list_entry_names (preferred), or the item's full path."
            },
            "field": {
              "type": "string",
              "anyOf": [
                { "enum": ["password", "username", "url", "notes", "totp"] },
                { "pattern": "^[A-Z][A-Z0-9_]{0,127}$" }
              ],
              "description": "Which single field to release: password, username, url or notes; totp for the item's current TOTP code, never its seed; or one custom field of the item named like an environment variable, such as OPENAI_API_KEY. Never more than one."
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
              "description": "How long you would like it for. The person chooses once or an allowance for a set time; a release under an Automatic mode honours this number. A TOTP code lasts only until it changes, whatever you ask."
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
              "description": "The project's environment. Defaults to dev."
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

    /// <summary>The arguments of <c>request_access_change</c>: what to change, and why. No agent's name, since it is always this bridge's own.</summary>
    /// <remarks>The bounds are <c>AccessChangeRules</c>'s, which the bridge and the owner apply again.</remarks>
    internal const string AccessChangeInputJson = """
        {
          "type": "object",
          "properties": {
            "mode": {
              "type": "string",
              "enum": ["ask", "allow-for-a-while", "automatic"],
              "description": "ask: the person is asked every time. allow-for-a-while: asked, and they may allow for an hour. automatic: released without asking, within until and per_hour. With project, for that project or environment only."
            },
            "project": {
              "type": "string",
              "minLength": 1,
              "description": "<project> or <project>:<environment>, as an item's project tag names it after env:. Holds only a mode and its limits."
            },
            "until": {
              "type": "string",
              "minLength": 1,
              "description": "When automatic ends, after which the person is asked again: a time such as 2026-12-31T18:00:00+02:00, on this computer's clock when it has no offset, or a date such as 2026-12-31, which ends at the close of that day on this computer. Ask for the shortest time your task needs."
            },
            "per_hour": {
              "type": "integer",
              "minimum": 1,
              "maximum": 1000,
              "description": "At most this many values released without asking in an hour, after which the person is asked again."
            },
            "see": {
              "type": "array",
              "items": { "type": "string", "minLength": 1, "maxLength": 128 },
              "minItems": 1,
              "maxItems": 16,
              "description": "Everything this agent may see, in place of what it sees now: a place such as env/**, or a project's variables such as tag:env:billing or tag:env:billing:staging."
            },
            "values": {
              "type": "string",
              "enum": ["yes", "no"],
              "description": "yes: request_credential may hand this agent values. no: values go only into commands it runs."
            },
            "run": {
              "type": "string",
              "enum": ["on", "off"],
              "description": "on: this agent may ask to run commands with secrets, each approved by the person. off: it may not."
            },
            "reason": {
              "type": "string",
              "minLength": 1,
              "maxLength": 2000,
              "description": "Shown verbatim to the person who confirms or refuses a change that gives you more. Be specific and honest."
            }
          },
          "required": ["reason"],
          "additionalProperties": false
        }
        """;

    /// <summary>The parsed schema for <c>request_access_change</c>.</summary>
    internal static readonly JsonElement AccessChangeInput = Parse(AccessChangeInputJson);

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
