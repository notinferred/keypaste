# Agent settings

In source, for the next release, what every agent gets is decided by the kind of value it asks for, under rules this computer keeps. `v0.3.0` reads standing rules from `~/.keypaste/policy.toml` instead, which [its guide](https://github.com/notinferred/keypaste/blob/v0.3.0/docs/policy.md) describes; in source that file is no longer read and nothing carries its rules over, and `keypaste approve` says so when it starts and finds one.

```sh
keypaste clients                      # the rules and the agents this computer knows
keypaste clients claude-code --run on # let claude-code ask to run commands, each run asked
keypaste clients claude-code --set notes=block     # its own rule for notes; ask, block or settings
keypaste clients claude-code --forget 54380d03ad11 # forget an Always allow or pick, by the key the list shows
keypaste clients claude-code --remove # forget it, which turns its run off
```

## What an agent sees

An agent sees each item its bridge's `--expose` reaches ([mcp-setup](mcp-setup.md#what-the-agent-may-see)): its title, group, tags and URL, and its username unless Agents › What agents get turns Agents see usernames off, which hides the username alone, so the agent then asks for it. Listing works either way.

## How each value is answered

Every value an agent asks for falls in one category, and each category is Ask, Allow or Block for every agent, Ask by default:

| Category | What it holds |
|---|---|
| Passwords | An item's password |
| Custom fields | Any custom field of an item that is not in a project and not a card's or an identity's protected field, protected or not |
| Project variables | Any custom field of an item tagged into a project, `env:<project>` or `env:<project>:<environment>` |
| Card values | A protected custom field of an item tagged `Credit Card`, such as its number or security code |
| Identity values | A protected custom field of an item tagged `Identity`, such as a Social Security or passport number |
| TOTP codes | An item's current code, never its seed |
| Notes | An item's notes |
| Attachments | One attachment of an item, asked for by name, at most 5 MiB |

Ask puts each request to you, and the prompt offers Deny and Allow once, and Always allow and Always block for an agent whose bridge has a label ([approvals](approvals.md)). Allow releases without asking you and records each release; it takes effect only once you confirm, in Settings, that every agent will receive every value of that kind without asking. Block refuses without asking you. A passkey's fields are always blocked, since its private key signs in wherever it is registered. Whether a custom field is a project variable, a card value or an identity value depends only on its item's tags and whether the field is protected, never on the field's name. keypaste's Bitwarden and 1Password 1PUX imports tag cards `Credit Card` and identities `Identity`, and its LastPass import tags a card `Credit Card`; tag a card or identity you made yourself the same way.

After you refuse a request, the same field is refused for a minute without asking you again; changing that field's category in Settings ends the wait at once.

## Commands

An agent may ask to run a command with values in its environment only once you turn run on for it, on its row in Agents or with `keypaste clients <agent> --run on`, and the bridge offers `run`, which `keypaste setup` registers. Each run is put to you, with Allow once only, whatever the categories say, and a run whose set holds a blocked value is refused without asking. Run follows the name the agent's bridge was given with `--client-label`, such as `claude-code`, never the name the agent calls itself.

## Where they are kept

The rules are kept on this computer in `~/.keypaste/agents.toml`, never in the vault, so a synced or edited vault gives an agent nothing more. Any program you run can write that file, so the app or `keypaste approve` holding the vault seals each change with a key the vault holds, bound to this computer's keypaste home and to the vault's count of this home's changes; a file whose seal does not hold, because it was edited outside keypaste, copied from another computer or put back from an earlier copy, is ignored and left as it is, every category is asked and no agent may run commands, and Settings says the rules were changed outside keypaste and are ignored until you set them again. From a terminal, which proves no person agreed, `keypaste clients` makes only changes that release nothing more without asking: an Allow, setting a kind of value back to Settings' Allow, and removing an Always block are made on the agent's page in the app. With nothing holding the vault it can only turn run off, set Ask or Block, forget an Always allow or a pick, or forget an agent, which it writes unsealed, so the defaults hold until you set the rules again in the app. Agent records an earlier version kept in the vault's `.keypaste/agents` group are ignored and left in place.

A vault synced to two computers keeps a count for each, so each computer keeps its own rules.

## What is written down

| `method` | What happened |
|---|---|
| `prompt` | You answered the request |
| `allowed` | Released without asking, because its category is Allow, or because it is an item's URL, or its username while Agents see usernames is on; the reason says which |
| `blocked` | Refused without asking, because its category is Block or it is a passkey's field |
| `run-off` | A command from an agent whose run is off, refused before anything was looked up |
| `rules-changed` | A change to the rules, on an `access_change` line naming the agent it was about |

An attachment's line is a `request_credential` line whose field is `attachment` and whose reason ends with the attachment's name.

## Limits

An allowed release has nobody reading the agent's reason, and the agent keeps what it was given. Another program running as you that starts a bridge gets whatever is allowed for every agent, and under an agent's label what is allowed or always allowed for that agent ([THREATS](../THREATS.md) T-14), and an Allow covers values added later, such as an item tagged into a project afterwards (T-13).

## Verifying it yourself

`scripts/verify-policy-e2e.sh` runs the shipped `keypaste approve`, a bridge and `keypaste clients`: an agent with no rules is asked, a hand-edited Allow in `agents.toml`, sealed before the edit or not, releases nothing unasked, `keypaste clients` turns run on only through the owner, which seals and records it, and an old `policy.toml` grants nothing.
