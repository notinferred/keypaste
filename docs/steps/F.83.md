# F.83 — Agents opens on who is connected

Completed 2026-11-09 on `agents-and-recommendations`, source only.

## Amendments

The founder selected this on 2026-11-09: **Add an agent** with a copied prompt, a specific app or MCP by hand; connections on a tab of their own; each live session and its folder; the key file warning moved out of the banner into Recommendations; and Recommendations under Items, with its count off Settings. There is no STEPS row.

## Judgment calls

- **Agents opens on Connections until something is connected**, and on Agents afterwards, unless the person has chosen a tab.
- **A session's folder is its last path segment only**, sent with the bridge's identity, left out when longer than 64 characters, shown sanitized, never audited and deciding nothing (T-3).
- **Sessions of one tool stay one agent**, with one set of rules and one queue, as the founder chose.
- **The added MCP block runs `keypaste` from PATH**, under a `my-agent` label the person changes.

## Evidence

`McpClientCardsTests`, `RunToolTests`, `AgentsFirstLevelTests`, `AgentActivityViewTests`, `UnlockKeyfileTests` and `EachFactOnceTests` pass locally, and `ScreenRenderer` drew both tabs.

## Limits and follow-ups

The folder is what the agent's process reports and cannot be verified. Recommendations has no rail icon in a narrow window.
