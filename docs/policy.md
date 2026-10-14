# Agent settings and modes

In source, for the next release, each agent has settings that say what it can see, whether it is given values, whether it may run commands and how its requests are answered. They are kept in the vault, so they travel with it, and they follow the name the agent's bridge was given with `--client-label`, such as `claude-code`, never the name the agent calls itself. An agent with no settings of its own gets the defaults, which ask you before anything is released.

`v0.3.0` has no settings: it reads standing rules from `~/.keypaste/policy.toml`, which [its guide](https://github.com/notinferred/keypaste/blob/v0.3.0/docs/policy.md) describes. In source that file is no longer read and nothing carries its rules over; `keypaste agent` says so when it starts and finds one, and `keypaste policy` and `keypaste agent --policy` are gone.

```sh
keypaste mcp policy                                            # every agent's settings
keypaste mcp policy claude-code --run on                       # waits for your confirmation
keypaste mcp policy claude-code --project billing:dev --mode automatic --per-hour 20
keypaste mcp policy claude-code --mode ask                     # gives less, so applies at once
```

## What each agent can be given

| Setting | Default | What it does |
|---|---|---|
| Can see | Every project's variables (`tag:env:*`) | Which items the agent can name, list and ask for, in [`--expose`'s selectors](mcp-setup.md#what-the-agent-may-see): `tag:env:billing` is every environment of billing's variables, `tag:env:billing:staging` one environment, and a place pattern such as `services/**` reaches whole items. In the app, All projects or a project or environment picked from the vault's tags, with the patterns under Advanced. |
| Give values | Yes | No keeps values to commands the agent runs: a request for a value is refused before anything in the vault is looked up. |
| May run commands | Off | On lets the agent ask to run a command with secrets in its environment; every command is still put to you. The bridge must also offer `run`, which `keypaste setup` registers. |
| Mode | Ask, allow for a while | How a request for a value is answered: Ask every time offers Allow once only; Ask, allow for a while also offers Allow for 1 hour; Automatic releases what the agent can see without asking you, and each release is still recorded. |
| Project rows | None | A mode, and a limit, for one project (`billing`, every environment of it) or one environment (`billing:staging`), which replaces the agent's mode for what it covers. |
| Limits | None | On Automatic only: at most so many releases an hour, from 1 to 1000, and Automatic until a time, which you give and read on this computer's clock and the vault keeps in UTC. When either is reached, keypaste asks you instead, with Allow for 1 hour offered. |

Commands are never Automatic, whatever the mode. An item in a protected environment, one whose own tag names `prod`, `production`, `prod-…` or `production-…` in any case, is asked about every time with Allow once only, and no mode, row, limit or Always for releases it unasked.

`*` is every agent without settings of its own, a bridge started with no `--client-label` included. Its settings can only take away from the defaults, so it offers no Automatic, no commands and nothing wider to see, and a bridge with no name is never given more than the defaults.

## Which settings apply to a request

What an agent gets is the least of three things: its settings in the vault, this computer's ceilings in `~/.keypaste/clients.toml`, and the bridge's own arguments (`--expose`, `--allow-run`, `--max-ttl`). None of them can raise another. A bridge that `keypaste setup` or the app's Connect registered has `--allow-run --expose '**'`, so the settings alone decide; an `--expose` you add only narrows them.

For an item, the mode is its environment's row, else its project's row, else the agent's mode. An item tagged into several projects or environments takes the strictest of them, and an item tagged into none takes the agent's mode. A row names its scope as `billing` or `billing:staging`, so `billing` covers every environment of billing, while an item tagged `env:billing` is in billing's `dev` environment alone.

An Automatic release lasts what the agent asked for, at most `--max-ttl`, and keeps no grant, so each later request is decided again and spends the hourly limit again. The hourly count belongs to the app or `keypaste agent` holding the vault: it survives a lock and starts again when that process restarts.

## Changing them

A change that gives an agent nothing more, such as a stricter mode, values off, commands off, seeing less or an end or hourly cap on Automatic, applies at once. A change lifts what this computer's ceilings hold back only where it names it: turning commands off leaves a ceiling on values as it is, while turning values on asks to lift it. Anything else, including turning a limit off or a reset to the defaults that gives more, is a widening, and it takes effect only once you confirm it where the vault is unlocked: in the app's confirmation window, which opens with Deny focused and lets Allow work only after a second on screen, or in `keypaste agent`'s terminal, where `y` allows and every other key, Enter included, denies. The confirmation names the agent, exactly what it would gain, including the limit an Automatic it gives would have, such as `billing:dev: Automatic until 2026-12-31 23:59 +02:00, at most 20 an hour`, and where the request says it came from, which is a claim, and it shows an agent's reason as the agent's own words; a change you made in the app says instead where you chose it. You confirm it once, and the agent gets it from its next request. After you refuse a widening asked for from a terminal, the same one is refused for a minute without asking you again; one you refuse in the app's settings or through Always for can be tried again at once.

There are four ways to ask for a change:

- **The app.** On the Agents screen, each agent's row has Settings, which opens its settings on the row: its mode, values, commands, what it can see, project rows and limits. What it can see is All projects or Only these: under Only these you pick projects and environments and Apply sends them as one change, so moving to a few projects never passes through seeing nothing, and with nothing picked it says that applying lets the agent see no items; Advanced takes `--expose`'s patterns, which a change of picks keeps. Automatic's end is a day picked from a calendar and an optional time of day such as `18:00`, or the close of that day, on this computer's clock, and the panel shows the limit in force with the clock's offset from UTC. Each other choice applies on its own, and one that gives more opens the confirmation. When this computer holds the agent back from what the vault gives it, the panel says so and Use the vault's settings asks to lift it, which the audit log records as that, with what it lifted.
- **Always for.** A credential prompt for an item tagged into one project's environment, from a named agent, has an Always for button (`a` at `keypaste agent`) beside the allows, captioned "Allows once, then asks to make it Automatic". It releases this one request and then opens the same confirmation to make that environment Automatic for the agent; deny that and it stays one release. When the change does not take effect, because the settings cannot be read or take another row, another request was already in front of you, nobody answered, the vault could not be saved or this computer's ceilings could not be lifted, a notice in the app, or a line at `keypaste agent`, says the environment was not made Automatic and why.
- **`keypaste mcp policy <agent>`**, with `--project <project>[:<environment>]`, `--mode ask|allow-for-a-while|automatic`, `--see <selector>` (repeated), `--values yes|no`, `--run on|off`, `--per-hour <n>|none`, `--until <time>|none` and `--reset`. `--until` takes a date, which ends at the close of that day, or a time such as `2026-12-31 18:00`, both on this computer's clock unless the time ends in `Z` or an offset such as `+02:00`; it is sent and kept in UTC, the audit line keeps it so, as `keypaste log --json` shows, and the list, the confirmation and the app's History show it on this computer's clock with its offset, such as `2026-12-31 23:59 +02:00`, with its seconds when they are not zero. With `--project` only the mode and limit apply, and `--reset` removes that row; without it, `--reset` puts the agent back to the defaults. Any program you run can type this command, so the confirmation says it was asked from a terminal and never that you asked.
- **The agent itself**, through its `request_access_change` tool, for its own settings only, with a reason. It may ask for Automatic only until a time (`until`, read on this computer's clock unless it names its offset) or at most so many an hour (`per_hour`), and its tool asks it to ask for the shortest time its task needs; it cannot ask for a limit to be removed or for a reset. A change that gives it less applies at once on this computer, even while nothing holds the vault; one that gives it more is put to you, and after you refuse a request for more from it, another agent or a terminal, or leave one unanswered, every request for more from any of them is refused for a minute without asking you and told when to try again, while one for less still applies and the app's agent settings never wait.

With nothing holding the vault unlocked, `keypaste mcp policy` can only lower this computer's ceilings, and says so for anything else; the listing then shows the ceilings alone. An agent's bridge then makes the agent's own request for less the same way and refuses one for more. `keypaste setup` and the app's Connect register a bridge for a home other than `~/.keypaste` with that home's `clients.toml` as `--clients` beside its `--audit-log`, so the AI tool need not pass `KEYPASTE_HOME` on. A bridge with an `--audit-log` outside the keypaste home it runs under and no `--clients` cannot tell which `clients.toml` applies, refuses both and says to run `keypaste setup` again. Every change, made or refused, is one line in the audit log.

Nothing an agent can do, and no keypaste command, gives an agent more without your confirmation in the app or `keypaste agent`. The one other way is editing the vault itself, which takes its master password ([below](#where-they-are-kept)).

## Seeing them

```
$ keypaste mcp policy
  AGENT          PROJECT      MODE                           VALUES  RUN  SEES
  claude-code                 allow-for-a-while              yes     on   tag:env:*
                 billing:dev  automatic, at most 20 an hour
  *   (default)               allow-for-a-while              yes     off  tag:env:*

  this computer's ceilings, in clients.toml: nothing above gives more than these
  AGENT  PROJECT  MODE  VALUES  RUN  SEES
  codex           ask   no
```

The vault's settings come from the app or `keypaste agent` holding it; `--json` prints the same as one array of `agent` and `ceiling` objects. `keypaste agent` names, when it starts, each agent released to without asking and where, beneath this computer's ceilings, and prints a line for each release it makes without asking; the app's Agents screen names them in its status, such as `except what these agents are Automatic for: claude-code (billing:dev)`. When a tag change you are making in the app or with `keypaste env tag` would let an Automatic agent get the item without asking, the change names that agent first, before you confirm it, without blocking it; while `clients.toml` cannot be read, it names the agents the vault's settings would release the item to once the file reads again. A tag added in KeePassXC or by a merge says nothing.

## This computer's ceilings

`~/.keypaste/clients.toml` holds only ceilings, keyed by `--client-label`: an agent with rows of its own is held to them, and every other agent to the `*` rows. Each row can only take away, so no value written there, by hand or by a script, gives an agent more than the vault does. A narrowing made in the app or from a terminal is written there as well as in the vault, so restoring the vault or merging it with an older copy cannot undo it on this computer; a narrowing an agent asks for, an end or hourly cap on its Automatic included, is written there alone. When one of the two cannot take a narrowing you make, the other still does and keypaste says which did not: the file holds at most 2048 rows in 256 KiB, and an agent's record at most 256 project rows. A confirmed widening lifts this computer's ceilings exactly as far as it gives.

```toml
[[client]]
label = "codex"
mode = "ask"
values = "no"

[[client]]
label = "claude-code"
project = "billing:prod-eu"
mode = "ask"
```

`mode` is a ceiling on the mode, `values = "no"` keeps values to commands, `run = "off"` refuses commands and `see` is a list of selectors; `values = "yes"` and `run = "on"` set no ceiling. `per_hour = 20` and `until = "2026-12-31T21:59:59Z"`, a time in UTC, limit Automatic where the row's mode applies, as a limit in the vault does, and reaching either asks you. A row with `project` holds only `mode`, `per_hour` and `until`, and an environment's row beats its project's, which beats the agent's row: a project row's mode and limit replace the agent row's for that project, so lowering only the agent row by hand leaves that project as its row says. Lower both, or use `keypaste mcp policy`, which does.

The app or `keypaste agent` reads the file at every request, so an edit applies to the next one. A file it cannot read or parse refuses every agent request, naming its line, until it is fixed or deleted; the earlier `policy = …` form is such a file. The app's Agents screen offers Reset for a file it cannot parse: it shows what each agent's settings in the vault then give beyond the defaults, and only once you confirm does it set the file aside as `clients.toml.unreadable`, or `clients.toml.unreadable-2` and on when an earlier reset's copy is there, so no copy is ever replaced, and start an empty one, recorded in the audit log under `*` with the copy's name. A file it could not read at all is left as it is, since it may be well formed.

## Where they are kept

Each agent's settings are one entry in the vault's `.keypaste/agents` group, titled with the agent's name, holding an [ACAF v1](../site/web/src/content/docs/acaf/v1.mdx) record, the format keypaste publishes so other KeePass apps can read and keep it. The vault stays KDBX 4.0. keypaste hides the group from agents, listings, search and export, as it hides tokens and share links; KeePassXC shows it.

Anyone who can open the vault can change a record there, in KeePassXC for example, and keypaste applies it without a confirmation of its own: the master password is the boundary. A record only applies as the vault's file holds it: while the open vault has an unsaved change, or another program has changed the file, and for any record keypaste does not wholly understand, the settings apply only beneath the defaults, so they can take away and never give more. A change made in keypaste rewrites only what it names and keeps the rest of the record as it is, including anything keypaste does not understand, such as another program's settings or a typo from a hand edit; when such a part remains, the confirmation of a change that gives more says the agent gets no more than the defaults until the record is fixed. Such a change is judged by what the agent can then do: one that only takes away, such as turning values off, applies at once even when the record holds a setting that gives more, which stays inactive in it, and a later change that leaves the record wholly understood asks for everything the agent then gets, that setting included. Two copies of the vault that each changed an agent merge, in KeePass and KeePassXC, to the newer change with the older kept in the entry's history, since each agent's entry has a UUID derived from its name.

## What is written down

| `method` | What happened |
|---|---|
| `automatic` | Released without asking, because the agent is Automatic for the item. The reason says so. |
| `inject-only` | A request for a value from an agent whose settings give it no values, refused before anything was looked up. |
| `run-off` | A command from an agent whose settings do not let it run commands, refused before anything was looked up. |
| `narrowing` | A change to an agent's settings that gave it nothing more, applied at once. |
| `prompt` | With a change to settings, a widening you confirmed or refused. |

A change to settings is recorded under the tool `access_change`, naming the agent and the change. History in the app shows an Automatic release as Automatic, and a change to settings as Changed settings when it was made and Asked to change settings when it was not. A line written by `v0.3.0` keeps its `policy` or `policy-limit`.

## Limits

An Automatic release has nobody reading the agent's reason, and the agent keeps what it was given: an hourly limit and an end time bound how much leaves, not what is done with it. The settings follow a label, which is whatever the client's configuration says, so another program running as you can start a bridge with an Automatic agent's label and receive its releases ([THREATS](../THREATS.md) T-14, T-36). A row covers what is in its project now, including an item tagged into it later, by the app, KeePassXC or a merge (T-13). A narrowing is kept on the computer that made it; another computer the vault syncs to applies the vault's record, which a restored backup or an older copy can bring back there, as it can here for a narrowing this computer's file could not keep (T-15). An agent can tell an Automatic release from a prompted one by how fast it comes back (T-17).

## Verifying it yourself

`scripts/verify-policy-e2e.sh` runs the shipped `keypaste agent` and bridge on Linux, macOS and Windows: an agent with no settings is asked, a change confirmed in the agent's terminal makes it Automatic for one project and releases there with no prompt and an `automatic` line, a refused change applies nothing, the agent's own request waits for the person, a protected environment and other projects still ask, `*` cannot be made Automatic, a ceiling or an unreadable `clients.toml` takes Automatic away, a spent hourly limit asks, and an old `policy.toml` grants nothing. `scripts/verify-keepassxc-projects.sh` has KeePassXC edit, save and merge a vault holding agents' settings and keypaste apply what it reads.
