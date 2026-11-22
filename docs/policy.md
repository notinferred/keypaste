# Agent settings

Agent settings decide what each agent gets without asking you: Ask, Allow or Block for each kind of value, for every agent or for one, and whether an agent may run commands. A request that reaches you is answered as [Approvals](approvals.md) describes.

This guide describes the unpublished 1.0.0 source build. The published v0.3.0 reads standing rules from `~/.keypaste/policy.toml` instead ([its guide](https://github.com/notinferred/keypaste/blob/v0.3.0/docs/policy.md)); 1.0.0 does not read that file or carry its rules over ([FEATURES](FEATURES.md)).

## Change the settings

In the app, open Agents: the general settings apply to every agent, and each agent's page sets its own. From a terminal:

```sh
keypaste clients                                   # the rules and the agents this computer knows
keypaste clients claude-code --run on              # let claude-code ask to run commands, each run asked
keypaste clients claude-code --set notes=block     # its own rule for notes: ask, block or settings
keypaste clients claude-code --forget 54380d03ad11 # forget an Always allow or a pick, by the key the list shows
keypaste clients claude-code --remove              # forget the agent, which turns its run off
```

A terminal proves no person agreed, so `keypaste clients` makes only changes that release nothing more. Make an Allow, or remove an Always block, in the app. Each change is recorded in the audit log as a `rules-changed` line ([Approvals](approvals.md#the-audit-log)).

## What an agent sees

An agent sees each item its bridge's `--expose` reaches ([what it can see](mcp-setup.md#what-the-agent-can-see)): title, group, tags, URL and, unless Settings › Agents hides it, username. Hosts and references, off by default, also shows Host fields and which items reference others; those then reach the agent's AI provider too.

## How each value is answered

Each kind is Ask, Allow or Block for every agent. Ask is the default.

| Kind | What it holds |
|---|---|
| Passwords | An item's password |
| Custom fields | A custom field of an item in no project, other than a card's or identity's protected field |
| Project variables | A custom field of an item tagged `env:<project>` or `env:<project>:<stage>` |
| Card values | A protected custom field of an item tagged `Credit Card` |
| Identity values | A protected custom field of an item tagged `Identity` |
| One-time codes | An item's current code, never its seed |
| Notes | An item's notes |
| Attachments | One attachment, by name, at most 5 MiB |

| Setting | What it does |
|---|---|
| Ask | Puts each request to you ([Approvals](approvals.md#the-prompt)). |
| Allow | Releases without asking and records each release, once you confirm in the app. |
| Block | Refuses without asking. |

A field's kind depends on its item's tags and whether it is protected, never on its name. A passkey's fields are always blocked.

<a id="protected-stages"></a>

### Protected stages

A stage named `prod`, `production`, `prod-…` or `production-…`, such as `env:billing:prod`, is protected. Its values and runs are asked every time, even under Allow, with no Always allow. A scoped token's run is refused them. Block still refuses without asking.

## One agent's own rules

An agent's page in Agents can set any kind differently for that agent, and lists its kept Always allow answers with Remove. The most specific setting decides: a kept answer, then the agent's rule, then the general setting. Rules follow the bridge's `--client-label`, never the name the agent calls itself.

## Running commands

An agent may run a command with values in its environment only once you turn run on for it, in Agents or with `keypaste clients <agent> --run on`, and only through a bridge that offers `run` ([mcp-setup](mcp-setup.md#running-a-command-with-secrets---allow-run)). Every run is put to you ([Approvals](approvals.md#runs)). A run needing a blocked value is refused.

## Where they are kept

Each vault's settings are kept in `~/.keypaste/agents/` on this computer, never in the vault, so a synced or edited vault gives an agent nothing more. The approver seals each change with a key the vault holds. A file edited outside keypaste, copied or restored is ignored: every kind is asked and no agent may run commands until you set the rules again in the app.

## Limits

- Nobody reads the agent's reason for a release under Allow or Always allow, and the agent keeps what it got ([THREATS](../THREATS.md) T-12).
- An Allow covers values added later, such as an item tagged into a project afterwards (T-13).
- Another program running as you that starts a bridge gets whatever is allowed, for every agent or for the label it uses (T-14).

## Checking it yourself

`scripts/verify-policy-e2e.sh` runs the shipped `keypaste approve`, a bridge and `keypaste clients`: an agent with no rules is asked, a hand-edited Allow releases nothing, `keypaste clients` turns run on only through the process holding the vault, and an old `policy.toml` grants nothing.
