# Agent settings

> This guide describes the source build headed for keypaste 1.0.0, which is not yet published. The published v0.3.0 has no agent settings: it reads standing rules from `~/.keypaste/policy.toml`, which [its own guide](https://github.com/notinferred/keypaste/blob/v0.3.0/docs/policy.md) describes. [FEATURES](FEATURES.md) lists what each version has.

Agent settings decide what each agent gets without asking you. They live on this computer, not in the vault. How a request that does reach you is asked, and what is recorded, is in [Approvals](approvals.md).

```sh
keypaste clients                                   # the rules and the agents this computer knows
keypaste clients claude-code --run on              # let claude-code ask to run commands, each run asked
keypaste clients claude-code --set notes=block     # its own rule for notes: ask, block or settings
keypaste clients claude-code --forget 54380d03ad11 # forget an Always allow or a pick, by the key the list shows
keypaste clients claude-code --remove              # forget the agent, which turns its run off
```

The 1.0.0 build does not read `policy.toml` and carries none of its rules over; `keypaste approve` names the file when it finds one.

## What an agent sees

An agent sees each item that its bridge's `--expose` reaches ([what it can see](mcp-setup.md#what-the-agent-can-see)): the item's title, group, tags and URL, and its username unless Agents see usernames is turned off under Every other agent in Agents. That switch hides the username alone, so the agent then asks for it. Listing works either way.

## How each value is answered

Every value an agent asks for falls in one kind, and each kind is Ask, Allow or Block for every agent. Ask is the default.

| Kind | What it holds |
|---|---|
| Passwords | An item's password |
| Custom fields | A custom field of an item that is in no project and is not a card's or an identity's protected field |
| Project variables | A custom field of an item tagged into a project, `env:<project>` or `env:<project>:<environment>` |
| Card values | A protected custom field of an item tagged `Credit Card`, such as its number or security code |
| Identity values | A protected custom field of an item tagged `Identity`, such as a passport number |
| TOTP codes | An item's current code, never its seed |
| Notes | An item's notes |
| Attachments | One attachment of an item, asked for by name, at most 5 MiB |

- **Ask** puts each request to you ([Approvals](approvals.md#the-prompt)).
- **Allow** releases without asking you and records each release. It takes effect only once you confirm, in the app, that every agent will receive every value of that kind without asking.
- **Block** refuses without asking you.

A passkey's fields are always blocked, since its private key signs in wherever it is registered. Whether a custom field is a project variable, a card value or an identity value depends only on its item's tags and on whether the field is protected, never on the field's name. keypaste's Bitwarden and 1Password 1PUX imports tag cards `Credit Card` and identities `Identity`, and its LastPass import tags a card `Credit Card`; tag one you made yourself the same way.

After you refuse a request, the same field is refused for a minute without asking you again. Changing that field's kind ends the wait at once.

## One agent's own rules

An agent's page in Agents can set any kind differently for that agent, and lists the answers kept for it with Remove. An answer is kept when you choose Always allow or Always block at a prompt, for that agent and that item's field. The most specific setting decides: a kept answer for the item's field, then the agent's own rule, then the setting for every agent. Passkeys stay blocked whatever is set.

An agent's own rules and kept answers follow the label its bridge was started with (`--client-label`, such as `claude-code`), never the name the agent calls itself.

## Running commands

An agent may ask to run a command with values in its environment only once you turn run on for it, on its row in Agents or with `keypaste clients <agent> --run on`, and only through a bridge that offers `run` ([mcp-setup](mcp-setup.md#running-a-command-with-secrets---allow-run)). Each run is put to you, with Allow once only, whatever the kinds say. A run whose set holds a blocked value is refused without asking.

## Where they are kept

The settings are kept in `~/.keypaste/agents.toml` on this computer, never in the vault, so a synced or edited vault gives an agent nothing more. The app or `keypaste approve` holding the vault seals each change with a key the vault holds. A file whose seal does not hold, because it was edited outside keypaste, copied from another computer or put back from an earlier copy, is ignored and left as it is: every kind is asked, no agent may run commands, and the app says the rules were changed outside keypaste until you set them again. A vault synced to two computers keeps a count for each, so each computer keeps its own settings.

From a terminal, which proves no person agreed, `keypaste clients` makes only changes that release nothing more without asking. An Allow, setting an agent's rule back to the general setting where that one is Allow, and removing an Always block are made on the agent's page in the app. With nothing holding the vault, `keypaste clients` can only turn run off, set Ask or Block, forget a kept answer or a pick, or forget an agent; it writes that unsealed, so the defaults hold until you set the rules again in the app. Agent records an earlier source build kept in the vault's `.keypaste/agents` group are ignored and left in place.

Each change is recorded in the audit log as a `rules-changed` line ([Approvals](approvals.md#the-audit-log)).

## Limits

A release under Allow or Always allow has nobody reading the agent's reason, and the agent keeps what it was given. Another program running as you that starts a bridge gets whatever is allowed for every agent, and under an agent's label whatever is allowed for that agent ([THREATS](../THREATS.md) T-14). An Allow covers values added later, such as an item tagged into a project afterwards (T-13).

## Checking it yourself

`scripts/verify-policy-e2e.sh` runs the shipped `keypaste approve`, a bridge and `keypaste clients`: an agent with no rules is asked, a hand-edited Allow in `agents.toml`, sealed before the edit or not, releases nothing unasked, `keypaste clients` turns run on only through the process holding the vault, which seals and records it, and an old `policy.toml` grants nothing.
