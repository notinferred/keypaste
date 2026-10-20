# Approving an agent's request

A person approves credential requests unless a live approval covers them or, in source, the agent's settings make it Automatic for the entry. This guide describes the terminal workflow in source, for the next release, and, in source, [approving in the desktop app](#approving-in-the-desktop-app); notes give `v0.3.0`'s differences, where policy rules allow matching requests without a prompt at `keypaste agent`. [Agent settings and modes](policy.md) explains what each agent can see, receive and run, and how its requests are answered.

In source, one vault has one owner: while the app has a vault unlocked, `keypaste agent` on that vault is refused with a message naming the app, and the app asks about that vault's credential requests itself. The desktop and a terminal approver on another vault still unlock independently, and the app launches a project's terminal through its own session; [ROADMAP](../ROADMAP.md) has the work ahead.

<a id="the-short-version"></a>

```
  your terminal                     your MCP client
       │                                   │
  keypaste agent  ◄──── local pipe ────  keypaste mcp
   • unlocked vault                       • validates the request
   • asks you                             • refuses anything out of scope
   • holds live grants                    • returns one field
   • writes the audit line
```

`keypaste mcp` is started by the MCP client and forwards requests without holding the vault. You start `keypaste agent`, which holds the vault and decides, under each agent's settings, whether to prompt, reuse an approval or release without asking.

An agent cannot trigger a master-password prompt. You enter the password only in a terminal you opened, after running `keypaste agent`. This avoids teaching you to trust password windows that another program could imitate.

## Starting the approver

```sh
keypaste agent --vault ~/vaults/personal.kdbx
```

It asks for your master password, then waits:

```
Master password:
keypaste: watching /home/you/vaults/personal.kdbx
keypaste: listening on keypaste-vault-9f3a1c02b7d54e60 for unlock 5d0c8e1a4b7f2c936e0a1d4b8c7f3e21, 45 seconds to answer, an allowance lasts at most 1 hour
keypaste: nothing is released without you saying yes. Press Ctrl+C to stop.
```

When the vault sets an agent to Automatic, a line before `listening` names each such agent and where, and the last line says that what they are Automatic for is released without you; a `~/.keypaste/policy.toml` left from `v0.3.0` is named as no longer read. Leave it running in its own terminal. Ctrl+C locks the vault again and zeroes every live grant. In source, so do SIGTERM and closing the terminal, and a request waiting at the prompt is withdrawn and denied as `vault-locked`.

| Option | What it does |
|---|---|
| `--vault <path>` | Which vault to unlock. Or set `KEYPASTE_VAULT`. |
| `--approval-timeout <seconds>` | How long you get to answer. Default 45, range 5–55. |
| `--max-ttl <seconds>` | How long `h` lets the same connection reuse an approval, whatever the agent asks for. Default 3600 (300 in `v0.3.0`). An Automatic release lasts what the agent asked for, at most this. |
| `--approver <name>` | Which pipe to listen on. Or set `KEYPASTE_APPROVER`. You need this only if you run two. |

Both processes derive the same per-user pipe name, so [MCP configuration](mcp-setup.md) usually needs no change. In source the name is derived from the vault as well, so the vault the bridge names with `--vault`, or the chosen one it uses without it, must be the vault the approver holds; `v0.3.0` names the pipe `keypaste-agent-…` and prints no session.

## What you see

```
────────────────────────────────────────────────────────────
keypaste: an agent is asking for a credential.

  agent    claude-code
  item     env/billing/.env
  field    STRIPE_KEY

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

  [a] allows this once, then asks you to make billing:dev Automatic for claude-code:
      released without asking you from then on, and recorded in history.
[d] deny  [o] once  [h] 1 hour  [a] always for billing:dev  45s ›
```

`v0.3.0` asks `Approve? [y/N]`; `y` releases the value and keeps a grant up to `--max-ttl`, and anything else is a no.

`agent` is the name the connecting program gave itself, which nothing checks. Any process that can start `keypaste mcp` can claim it; keypaste displays it but does not authorize from it. The agent's settings follow the bridge's `--client-label` instead, which is what `[a]` names.

`item` and `field` identify the requested value in your vault. A `/` inside an item title is displayed as a space so a title such as `../../prod/ROOT_TOKEN` cannot impersonate a different group path.

The reason is text written by the agent. keypaste removes control characters, line breaks, invisible characters and right-to-left controls, then truncates it to 400 characters. It cannot alter the dialog, default answer or deadline.

`o` releases it for this request only. `h` also lets the same client ask for the same field again for an hour without asking you; an agent set to Ask every time is offered `o` alone, with a line saying so. In source, `a` releases it once and then asks you to confirm making the entry's environment Automatic for that agent, so that what it can see there is released without asking from then on and still recorded; deny that confirmation and it stays one release. `a` is offered only for a named agent asking for an entry tagged into exactly one project's environment. All three count only once the choice has been on screen for a second, and keys typed before it appeared are discarded, so a key meant for the last prompt cannot answer this one. Anything else, including Enter, is a no, and so is saying nothing for 45 seconds. The countdown shows the seconds left.

In source, an entry whose own KeePass tag puts it in a protected environment, one named `prod`, `production`, `prod-…` or `production-…` such as `env:billing:prod`, offers only `o`: it is asked about every time, no grant is kept, `a` is not offered and no mode releases it unasked. A tag keypaste cannot read as a project tag still counts when it names one in any case, such as `env:billing:Prod`, and a group's name protects nothing (D-0416).

## Repeat requests

If you pressed `h` and the agent asks for the same field of the same entry again before the grant expires, you are not asked twice:

```
keypaste: reused an approval for env/billing/.env (238s left)
```

The grant belongs to that one connection. If the client restarts, the grant is gone. A different field of the same entry is a different question and you are asked again.

Reused approvals do not show the new reason in the terminal. The audit log records it for later review. Press `o` to keep nothing, or set `--max-ttl 300` to shorten what `h` keeps; THREATS.md T-12 explains the limit.

## Automatic releases

In source, a request from an agent whose settings make it Automatic for the entry is released without a prompt, within what the agent can see, and `keypaste agent` prints a line for it:

```
keypaste: released env/billing/.env to claude-code without asking (Automatic)
```

The audit line records it as `automatic`, never as a person's approval, and it keeps no grant, so the next request is decided again. When the agent's hourly limit or its end time is reached, the request comes to you instead, with `h` offered. Commands and entries in a protected environment are always asked.

## Confirming a change to an agent's settings

In source, a change that would give an agent more, whether you asked for it with `keypaste mcp policy`, the agent asked through its keypaste tool or `a` asked for it, waits for you where the vault is unlocked:

```
────────────────────────────────────────────────────────────
keypaste: a change would give an agent more.

  agent    claude-code
  asked    asked from a terminal; any program you run could have typed it
  gains    billing:dev: Automatic: released without asking, recorded in history

  [d] deny: nothing changes.
  [y] allow: keypaste saves it in the vault, and the agent gets it from its next request.
[d] deny  [y] allow  45s ›
```

`asked` is where the request says it came from, which keypaste cannot check, and an agent's own request adds its reason as its own words. When the agent's saved settings hold something keypaste does not wholly understand, such as another program's setting or a typo from a hand edit, the `[y]` line says instead that the agent gets no more than the defaults until they are fixed, and so does the app's confirmation. `y` allows; every other key, Enter included, denies, and so does silence. Once you refuse a request for more from a terminal or an agent, or leave it unanswered, the next request for more from any terminal or any agent, under any agent name, is refused for a minute without asking you, and is told when to try again; the one `a` asked for is not held back, since only you can ask for it, and neither are the app's own agent settings. A change that gives an agent less applies at once and asks nobody, even while its saved settings hold something keypaste does not understand, and a change that leaves them wholly understood asks for everything they then give.

When the change `a` asked for does not take effect, for any reason but your `d`, a line after the release says so and why, and gives the command that asks for it again, naming this terminal's vault and pipe when `keypaste mcp policy` would not find them by itself:

```
keypaste: allowed once. billing:dev was not made Automatic for claude-code: another request was already in front of a person. To ask for it again: keypaste mcp policy claude-code --project billing:dev --mode automatic
```

When a name in that command is one a shell would act on, or would read as an option, the line says only that `keypaste mcp policy` can ask for it again.

## When you say no

An explicit refusal tells the agent not to retry. The same request is refused for one minute without prompting again. A timeout allows retry because nobody made a decision.

Only one request is displayed at a time. Additional requests on that connection, including entry listings, receive `BUSY` immediately. They are not queued, and the response does not identify the call already in progress.

In source, a request whose client gives up, or whose `keypaste mcp` goes away, is withdrawn from the prompt rather than waiting out its 45 seconds.

## Approving in the desktop app

In source, when the desktop app has a vault unlocked, a credential request for that vault opens a keypaste prompt window over whatever you are doing. It shows who is asking (the name the client gave itself, which is not verified), the client label from its configuration, the entry, the field, what you can allow, a countdown, and the agent's reason, under a line saying the agent wrote it.

Allow once releases that one field and keeps nothing. Allow for 1 hour also lets the same connection ask for the same field again for an hour without asking you, and is not offered for an entry tagged into a protected environment. Both work a second after the prompt appears, so a click meant for another window cannot approve. Deny, Escape and closing the window refuse, and focus starts on Deny, so Enter refuses too. Nobody answering for 45 seconds refuses. Locking the app, quitting it and the client giving up each refuse the request and take the prompt down. The one-prompt-at-a-time rule, the one-minute refusal cooldown and connection-scoped grants apply as they do at `keypaste agent`.

In source, a prompt the terminal would offer `a` for also has an Always for button, labelled with the entry's environment and captioned "Allows once, then asks to make it Automatic", in a place of its own above the other buttons, so none of them moves whether it shows or not; it works when the allows do, releases the request once and then opens the confirmation of the change. That confirmation is a window of its own, with the same fixed layout: it names the agent, what it would gain and where the request came from, and in the reason's box says that you chose it with Always for, focus starts on Deny, Allow works a second after it appears, and Deny, Escape, closing it, a lock and 45 seconds without an answer change nothing. When the change does not take effect for any reason but your Deny, a notice window says, after the release, which environment was not made Automatic and why; Close is its default, and its button that names the agent's settings brings the main window forward with that agent's settings open on Agents, where any change still asks you to confirm it.

The app does not read `policy.toml`. A credential it releases without a press of Allow once or Allow for 1 hour, or a grant one of them kept, is one the agent's settings make Automatic, which you confirmed, and History shows it as Automatic. A run under a scoped token is released without a prompt unless its profile is protected ([T-32](../THREATS.md#t-32--a-scoped-token-is-a-bearer-credential)).

The Agents screen lists the request in front of you under Waiting for you, with the agent, the item and field it asks for and the seconds left, never its reason, and Show the prompt, which brings the prompt window forward with focus on Deny and its allows disarmed for another second; you answer only in that window. Allowances lists the grants in force the same way with the time left, and End ends one and End all every one, so the next request for them opens the prompt again; ending one is not recorded in the audit log. Below the lists is the history since you unlocked: the audit records naming the app's current session, as `keypaste log` prints them. It says when the log is missing, cannot be read or has no records from this session yet rather than showing an empty history, and the activity log in Settings › Advanced shows the whole file.

## Runs that ask for a project's variables

In source, `keypaste run --session <project> -- <command>` asks the process holding the vault for the project's whole set instead of opening the vault itself ([replace-dotenv](replace-dotenv.md#run-your-app)). The app shows it in a prompt window of its own and `keypaste agent` in its terminal, naming the project, the variable names, the entry each value comes from, the command and the directory the run was started in, never a value. Allow once, or `o` at the agent, starts the command with the set. Allow this command for 15 minutes, or `h`, also lets any program of yours run exactly that command in that directory again for 15 minutes without asking, reading the latest values each time while the variable names stay the same; it is never offered for a protected profile or a set holding an entry tagged into one, such as `env:billing:prod`, and it ends on a lock or a revoke ([THREATS.md](../THREATS.md) T-34). `keypaste agent` prints a line for each run such a grant lets through, naming the project, profile and command and the seconds left. The same rules as a credential request apply otherwise: both allows work after a second, Deny, Escape, closing, `d`, `n`, Enter, a lock and 45 seconds without an answer refuse, one prompt is shown at a time, and refusing a run refuses the same project, command and directory for a minute. The command shown is what the run says it will start, and a program running as you could claim one and start another, so approve only a run you started ([THREATS.md](../THREATS.md) T-30). No agent setting releases a set. The process holding the vault writes each release and each refusal of a valid run to the audit log, and refuses a run whose line it cannot write.

## Commands an agent asks to run

In source, a bridge started with `--allow-run` lets an agent ask to run a command with secrets in its environment ([mcp-setup](mcp-setup.md#running-a-command-with-secrets---allow-run)). The prompt, in the app's window or the agent's terminal, shows `tool keypaste.run`, the client and its label, the exact program and command line, the resolved directory, the project and profile, each variable with the entry it comes from and `inject only`, and the agent's reason, and a loader such as `NODE_OPTIONS` is tagged as changing how programs start. Allow once, or `o`, starts that command once. The timed choice, or `h`, lets that client run the same command line in the same directory on the same connection again for up to 15 minutes without asking, and the prompt says it can change the files that command runs in that time; it is never offered for a protected entry or an agent set to Ask every time, and it ends when the bridge disconnects, on a lock, a revoke or an edit of an entry it names. `keypaste agent` prints a line for each run such a grant lets through. The app or `keypaste agent` writes every run to the audit log before it answers, and the bridge starts nothing the owner did not record. An agent runs nothing until its settings let it, which is off by default, and no mode releases a run without asking. The output the agent gets back has each literal or escaped occurrence of a value replaced, but a command can reveal a value in another form, so approve only a command whose behaviour you know ([THREATS.md](../THREATS.md) T-35).

## When no agent is running

Everything is denied, and the agent is told exactly how to fix it:

```
keypaste: DENIED. Nobody can approve this right now: nothing holds this vault unlocked, neither
the keypaste desktop app nor a keypaste agent, and keypaste releases nothing until one does,
whatever this agent's settings say.

Ask the person you are working with to unlock this vault in the keypaste desktop app, or to
run `keypaste agent --vault <their vault>` in a terminal, and then try again.
```

That is the source wording; `v0.3.0` says "No keypaste agent is running, so there is nobody to approve this."

In source, when the keypaste app is open with this vault locked, the request is still denied and recorded the same way, and the agent is told where to unlock it:

```
keypaste: DENIED. Nobody can approve this right now: the keypaste app is open on this computer,
but this vault is locked in it, and keypaste releases nothing until it is unlocked, whatever
this agent's settings say.

Ask the person you are working with to unlock this vault in the keypaste app, and then try
again.
```

The first such request after the app locks also shows a small notice naming the vault, whether it is the one the unlock screen shows or the one you chose. It has no password field and does not take the keyboard from the window you are typing in. Close is its default, and Open keypaste, which works a second after the notice appears, shows the unlock screen with that vault selected. No other notice appears until you have unlocked and the app has locked again. An agent asking for item names, or for a change to its settings that needs your confirmation, is likewise told that the app is open with the vault locked, and can raise that one notice too.

The MCP client may start its bridge before you start an approver. Calls are refused until an approver is available.

On `0.2.0`, this refusal can also occur under load while the approver is running: the bridge's half-second connection deadline can expire before it tries the pipe. No credential or entry name is released. Retrying is appropriate. This is F.9, repaired in `0.3.0`.

## What is written down

The app or `keypaste agent` appends one line to `~/.keypaste/audit.jsonl` for every call it answers, granted, denied or abandoned, and `keypaste mcp` one for every call it refuses itself, a malformed one included, or that nothing answered. Each line records the entry, field, client, stated reason and decision.

It does not add the returned field value to the log. Names and reason excerpts are logged metadata, so do not put secret values in them. See [docs/mcp-setup.md](mcp-setup.md) for the format.

<a id="the-honest-limits"></a>

## Limits

The vault stays unlocked while `keypaste agent` runs; it has no idle auto-lock. Stop that process to lock it. The desktop app holds a separate session, so locking the desktop does not lock the approver; in source the two cannot hold the same vault at once. In `v0.3.0` approval prompts appear only in the approver terminal; in source the desktop asks in its own window for the vault it has unlocked. An already-open approver also retains its in-memory vault snapshot: reopen it after a desktop or external edit to use the updated values. In source, once another program changes the file it refuses every request as `vault-changed` instead, until you restart it.

TTL limits cached approval reuse. Expiry clears the cache buffer but cannot erase strings or copies retained by clients, transcripts or session files. Stopping the approver cannot revoke these copies; rotate the credential at its provider when needed. [SECURITY.md](../SECURITY.md) describes the memory limits.

## Verifying it yourself

`scripts/verify-approval-e2e.sh` runs real CLI and MCP processes against a test vault in CI on Linux, macOS and Windows. It checks that approval returns the secret, refusal does not, and neither writes it to the audit log.

`scripts/verify-demo.sh` checks the corresponding dialog in [Claude asks for a key, you approve, the deploy runs](demo.md) and four other public pages against the built binaries. It does not read this page, so edits to the examples above need review against that verified demo.
