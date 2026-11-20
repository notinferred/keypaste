using System.Text.Json;

namespace Keypaste.Mcp.Tools;

/// <summary>
/// The agent-facing input contracts, written out rather than generated.
/// </summary>
/// <remarks>
/// <para>
/// The SDK can build a schema by reflecting over a C# method signature, and it compiles perfectly
/// well under this repository's trim and AOT analysers — that was checked, not assumed.
/// It is still not used here, because of what it generates: no <c>additionalProperties: false</c>,
/// no enum on <c>field</c>, no bounds on <c>reason</c>, no behaviour hints at all, and names taken
/// from whatever the parameter was
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

    /// <summary>The arguments of <c>find_access</c>, each optional and at least one given; the bounds are <c>AccessQuery</c>'s, which the owner applies again.</summary>
    internal const string FindInputJson = """
        {
          "type": "object",
          "properties": {
            "text": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "Text found, ignoring case, in an item's title, group, host, username, field names or projects."
            },
            "host": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "A host or URL, such as github.com; items on the same site match, its own host first."
            },
            "kind": {
              "type": "string",
              "enum": ["none", "login", "api_key", "database", "server", "secure_note", "card", "identity", "ssh_key", "wifi", "environment"],
              "description": "The kind of item; none is an item with no kind set."
            },
            "related_to": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "An item's handle; finds the items linked to it by a site, a project, an account or a reference."
            },
            "project": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "A project's name, exactly; finds its items in every stage."
            },
            "vault": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "Only this vault, by the name find_access gives it; every vault this agent can reach when left out."
            }
          },
          "minProperties": 1,
          "additionalProperties": false
        }
        """;

    /// <summary>The structured result of <c>find_access</c>: names, origins, usernames, links and projects, never a value.</summary>
    internal const string FindOutputJson = """
        {
          "type": "object",
          "properties": {
            "vault": { "type": "string" },
            "vaults": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "state": { "type": "string" }
                },
                "required": ["name", "state"]
              }
            },
            "truncated": { "type": "boolean" },
            "count": { "type": "integer" },
            "projects": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "vault": { "type": "string" },
                  "environments": { "type": "array", "items": { "type": "string" } },
                  "items": { "type": "integer" }
                },
                "required": ["name", "environments", "items"]
              }
            },
            "items": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "handle": { "type": "string" },
                  "vault": { "type": "string" },
                  "group": { "type": "string" },
                  "name": { "type": "string" },
                  "kind": { "type": "string" },
                  "origin": { "type": ["string", "null"] },
                  "username": { "type": ["string", "null"] },
                  "fields": { "type": "array", "items": { "type": "string" } },
                  "projects": { "type": "array", "items": { "type": "string" } },
                  "links": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "handle": { "type": "string" },
                        "path": { "type": "string" },
                        "site": { "type": ["string", "null"] },
                        "projects": { "type": "array", "items": { "type": "string" } },
                        "same_account": { "type": "boolean" },
                        "references": { "type": "boolean" },
                        "referenced_by": { "type": "boolean" }
                      },
                      "required": ["handle", "path", "site", "projects", "same_account", "references", "referenced_by"]
                    }
                  },
                  "altered": { "type": "boolean" }
                },
                "required": ["handle", "group", "name", "kind", "origin", "username", "fields", "projects", "links", "altered"]
              }
            }
          },
          "required": ["vault", "truncated", "count", "projects", "items"]
        }
        """;

    /// <summary>The four arguments the build prompt specifies for <c>request_credential</c>.</summary>
    /// <remarks><c>field</c> refuses the names <c>CredentialRequestRules.Check</c> refuses, with no lookahead, which clients using RE2 cannot compile.</remarks>
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
              "minLength": 1,
              "maxLength": 128,
              "anyOf": [
                { "enum": ["password", "username", "url", "notes", "totp"] },
                {
                  "not": {
                    "anyOf": [
                      { "pattern": "^([Tt][Ii][Tt][Ll][Ee]|[Uu][Ss][Ee][Rr][Nn][Aa][Mm][Ee]|[Pp][Aa][Ss][Ss][Ww][Oo][Rr][Dd]|[Uu][Rr][Ll]|[Nn][Oo][Tt][Ee][Ss])$" },
                      { "enum": ["otp", "TOTP Seed", "TOTP Settings", "attachment"] },
                      { "pattern": "^TimeOtp-" }
                    ]
                  }
                }
              ],
              "description": "Which single field to release: password, username, url or notes; totp for the item's current TOTP code, never its seed; or the exact name of one of the item's custom fields, as list_entry_names gives it, such as OPENAI_API_KEY. Never more than one."
            },
            "vault": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "The vault holding the item, by the name find_access gives it; needed only when its name is in more than one vault this agent can reach."
            },
            "reason": {
              "type": "string",
              "minLength": 1,
              "maxLength": 2000,
              "description": "Shown verbatim to the human who approves or denies. Be specific and honest."
            }
          },
          "required": ["entry", "field", "reason"],
          "additionalProperties": false
        }
        """;

    /// <summary>The arguments of <c>request_linked_set</c>: two to four fields, the first item the anchor, and why; the bounds are <c>SetRequestRules</c>'s.</summary>
    internal const string SetInputJson = """
        {
          "type": "object",
          "properties": {
            "items": {
              "type": "array",
              "minItems": 2,
              "maxItems": 4,
              "items": {
                "type": "object",
                "properties": {
                  "entry": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 512,
                    "description": "The handle from find_access or list_entry_names (preferred), or the item's full path."
                  },
                  "field": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 128,
                    "description": "One field of that item, as request_credential takes it; never an attachment."
                  }
                },
                "required": ["entry", "field"],
                "additionalProperties": false
              },
              "description": "The fields to release together. The first item is the anchor; every other item is the anchor itself or one find_access links to it."
            },
            "vault": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "The vault holding every item, by the name find_access gives it; needed only when their names are in more than one vault this agent can reach."
            },
            "reason": {
              "type": "string",
              "minLength": 1,
              "maxLength": 2000,
              "description": "Shown verbatim to the human who approves or denies the whole set. Be specific and honest."
            }
          },
          "required": ["items", "reason"],
          "additionalProperties": false
        }
        """;

    /// <summary>The arguments of <c>request_save</c>: an item and a field, or a project, an environment and a key, and why.</summary>
    /// <remarks>The bounds are the owner's, which it applies again; which of the two places is named it checks itself.</remarks>
    internal const string SaveInputJson = """
        {
          "type": "object",
          "properties": {
            "item": {
              "type": "string",
              "minLength": 1,
              "maxLength": 512,
              "description": "The item's full path, such as work/github; it is created when no item has that path. Give it with field."
            },
            "field": {
              "type": "string",
              "minLength": 1,
              "maxLength": 128,
              "description": "With item: password, or the name of a custom field, such as OPENAI_API_KEY."
            },
            "project": {
              "type": "string",
              "minLength": 1,
              "maxLength": 128,
              "description": "A project, for a variable of one of its stages. Give it with environment and key instead of item and field."
            },
            "environment": {
              "type": "string",
              "minLength": 1,
              "maxLength": 128,
              "description": "With project: the stage, such as dev or prod."
            },
            "key": {
              "type": "string",
              "minLength": 1,
              "maxLength": 128,
              "description": "With project: the variable's name, such as DATABASE_URL."
            },
            "vault": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "The vault the value goes in, by the name find_access gives it; needed only when this agent can reach several vaults and the item is in none of them or more than one."
            },
            "reason": {
              "type": "string",
              "minLength": 1,
              "maxLength": 2000,
              "description": "Shown verbatim to the human, who enters the value. Say what the value is and where they will find it."
            }
          },
          "required": ["reason"],
          "additionalProperties": false
        }
        """;

    /// <summary>The arguments of <c>request_item_change</c>: which item, which change, the day an expiry names, and why.</summary>
    /// <remarks>The bounds are <c>ItemChangeRules</c>'s, which the owner applies again.</remarks>
    internal const string ItemChangeInputJson = """
        {
          "type": "object",
          "properties": {
            "entry": {
              "type": "string",
              "minLength": 1,
              "maxLength": 512,
              "description": "The item: its handle from list_entry_names or find_access, or its full path, such as work/github."
            },
            "change": {
              "type": "string",
              "enum": ["move_to_recycle_bin", "mark_expired", "mark_needs_update", "clear_needs_update", "delete"],
              "description": "move_to_recycle_bin, mark_expired, or delete for an item you created through request_save, each asked; mark_needs_update or clear_needs_update, made without asking."
            },
            "expires": {
              "type": "string",
              "pattern": "^[0-9]{4}-[0-9]{2}-[0-9]{2}$",
              "description": "With mark_expired: the day it expires, as yyyy-MM-dd. Leave it out to mark it expired now."
            },
            "vault": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "The vault holding the item, by the name find_access gives it; needed only when its name is in more than one vault this agent can reach."
            },
            "reason": {
              "type": "string",
              "minLength": 1,
              "maxLength": 2000,
              "description": "Shown verbatim to the human, who allows or denies an asked change, and recorded with every change. Say why this item needs it."
            }
          },
          "required": ["entry", "change", "reason"],
          "additionalProperties": false
        }
        """;

    /// <summary>The arguments of <c>request_attachment</c>: which item, which attachment by name, and why.</summary>
    /// <remarks>The bounds are <c>CredentialRequestRules</c>'s, which the owner applies again.</remarks>
    internal const string AttachmentInputJson = """
        {
          "type": "object",
          "properties": {
            "entry": {
              "type": "string",
              "minLength": 1,
              "maxLength": 512,
              "description": "The handle from list_entry_names (preferred), or the item's full path."
            },
            "name": {
              "type": "string",
              "minLength": 1,
              "maxLength": 512,
              "description": "The attachment's exact name, as the item holds it."
            },
            "vault": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "The vault holding the item, by the name find_access gives it; needed only when its name is in more than one vault this agent can reach."
            },
            "reason": {
              "type": "string",
              "minLength": 1,
              "maxLength": 2000,
              "description": "Shown verbatim to the human who approves or denies. Be specific and honest."
            }
          },
          "required": ["entry", "name", "reason"],
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
              "description": "The project's stage. Defaults to dev."
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
            "vault": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256,
              "description": "The vault holding the project or the items env names, by the name find_access gives it; needed only when they are in more than one vault this agent can reach."
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

    /// <summary><see cref="FindInputJson"/>, parsed once.</summary>
    internal static readonly JsonElement FindInput = Parse(FindInputJson);

    /// <summary><see cref="FindOutputJson"/>, parsed once.</summary>
    internal static readonly JsonElement FindOutput = Parse(FindOutputJson);

    /// <summary>The parsed schema for <c>request_credential</c>.</summary>
    internal static readonly JsonElement CredentialInput = Parse(CredentialInputJson);

    /// <summary><see cref="SetInputJson"/>, parsed once.</summary>
    internal static readonly JsonElement SetInput = Parse(SetInputJson);

    /// <summary><see cref="SaveInputJson"/>, parsed once.</summary>
    internal static readonly JsonElement SaveInput = Parse(SaveInputJson);

    /// <summary><see cref="ItemChangeInputJson"/>, parsed once.</summary>
    internal static readonly JsonElement ItemChangeInput = Parse(ItemChangeInputJson);

    /// <summary><see cref="AttachmentInputJson"/>, parsed once.</summary>
    internal static readonly JsonElement AttachmentInput = Parse(AttachmentInputJson);

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
