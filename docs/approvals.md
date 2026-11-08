# Approving an agent's request

A person approves credential requests unless, in source, this computer's rules allow or block that kind of value for every agent. This guide describes the terminal workflow in source, for the next release, and, in source, [approving in the desktop app](#approving-in-the-desktop-app); notes give `v0.3.0`'s differences: its approver is `keypaste agent`, where policy rules allow matching requests without a prompt. [Agent settings](policy.md) explains what an agent sees, how each kind of value is answered and when it may run commands.

In source, one vault has one owner: while the app has a vault unlocked, `keypaste approve` on that vault is refused with a message naming the app, and the app asks about that vault's credential requests itself. The desktop and a terminal approver on another vault still unlock independently, and the app launches a project's terminal through its own session; [ROADMAP](../ROADMAP.md) has the work ahead.

<a id="the-short-version"></a>

```
  your terminal                     your MCP client
       │                                   │
  keypaste approve ◄─── local pipe ────  keypaste mcp
   • unlocked vault                       • validates the request
   • asks you                             • refuses anything out of scope
                                          • returns one field
   • writes the audit line
```

`keypaste mcp` is started by the MCP client and forwards requests without holding the vault. You start `keypaste approve`, which holds the vault and decides, under this computer's rules, whether to prompt, release without asking or refuse.

An agent cannot trigger a master-password prompt. You enter the password only in a terminal you opened, after running `keypaste approve`. This avoids teaching you to trust password windows that another program could imitate.

## Starting the approver

```sh
keypaste approve --vault ~/vaults/personal.kdbx
```

It asks for your master password, then waits:

```
Master password:
keypaste: watching /home/you/vaults/personal.kdbx
keypaste: listening on keypaste-vault-9f3a1c02b7d54e60 for unlock 5d0c8e1a4b7f2c936e0a1d4b8c7f3e21, 45 seconds to answer.
keypaste: every value waits for you to say yes; agents see each item's URL, and its username unless Settings hides it. Press Ctrl+C to stop.
```

When the rules allow a kind of value, a line before `listening` names each one released to every agent without asking, and the last line says every other value waits for you; a `~/.keypaste/policy.toml` left from `v0.3.0` is named as no longer read. Leave it running in its own terminal. Ctrl+C locks the vault again. In source, so do SIGTERM and closing the terminal, and a request waiting at the prompt is withdrawn and denied as `vault-locked`.

| Option | What it does |
|---|---|
| `--vault <path>` | Which vault to unlock. Or set `KEYPASTE_VAULT`. |
| `--approval-timeout <seconds>` | How long you get to answer. Default 45, range 5–55. |
| `--approver <name>` | Which pipe to listen on. Or set `KEYPASTE_APPROVER`. You need this only if you run two. |

Both processes derive the same per-user pipe name, so [MCP configuration](mcp-setup.md) usually needs no change. In source the name is derived from the vault as well, so the vault the bridge names with `--vault`, or the chosen one it uses without it, must be the vault the approver holds; `v0.3.0` names the pipe `keypaste-agent-…` and prints no session.

## What you see

```
────────────────────────────────────────────────────────────
keypaste: an agent is asking for a credential.

  agent    claude-code (claude-code)
  item     env/billing/.env
  field    STRIPE_KEY

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

[d] deny  [o] once  45s ›
```

`v0.3.0` asks `Approve? [y/N]`; `y` releases the value and keeps a grant up to its `--max-ttl`, and anything else is a no.

`agent` is the name the connecting program gave itself, which nothing checks, then in brackets the bridge's `--client-label` when it was started with one. Any process that can start `keypaste mcp` can claim the name; keypaste displays it but does not authorize from it. Whether an agent may run commands follows the label instead.

`item` and `field` identify the requested value in your vault. A `/` inside an item title is displayed as a space so a title such as `../../prod/ROOT_TOKEN` cannot impersonate a different group path.

The reason is text written by the agent. keypaste removes control characters, line breaks, invisible characters and right-to-left controls, then truncates it to 400 characters. It cannot alter the dialog, default answer or deadline.

`o` releases it for this request only and keeps nothing. It counts only once the choice has been on screen for a second, and keys typed before it appeared are discarded, so a key meant for the last prompt cannot answer this one. Anything else, including Enter, is a no, and so is saying nothing for 45 seconds. The countdown shows the seconds left.

In source, an entry whose own KeePass tag puts it in a protected environment, one named `prod`, `production`, `prod-…` or `production-…` such as `env:billing:prod`, is refused to a scoped token's run and asked about at every run that names it. A tag keypaste cannot read as a project tag still counts when it names one in any case, such as `env:billing:Prod`, and a group's name protects nothing (D-0416).

## Repeat requests

Each request is asked about unless this computer's rules allow or block its kind of value, or it is an item's URL, or its username while Agents see usernames is on. A release without asking does not show the request's reason in the terminal; the audit log records it for later review, and THREATS.md T-12 explains the limit.

## Releases without asking

In source, a request whose kind of value the rules allow is released without a prompt, within what the agent can see, and `keypaste approve` prints a line for it:

```
keypaste: released env/billing/.env to claude-code without asking (project variables allowed)
```

The audit line records it as `allowed`, never as a person's approval, and the next request is decided again. A request whose kind of value the rules block, or a passkey's field, is refused without a prompt and recorded as `blocked`. The rules are set in the app's Settings, and an Allow takes effect only once you confirm it there ([policy](policy.md)).

## When you say no

An explicit refusal tells the agent not to retry. The same request is refused for one minute without prompting again. A timeout allows retry because nobody made a decision.

Only one request is displayed at a time. Additional requests on that connection, including entry listings, receive `BUSY` immediately. They are not queued, and the response does not identify the call already in progress.

There is no pause button for an agent that keeps asking. The rules are the pause: block a kind of value in Settings and every agent's requests for it are refused before anything in the vault is read, with no prompt, and `keypaste clients <agent> --run off` stops its commands the same way. Changing a field's kind in Settings also ends the minute a refusal holds it back ([policy](policy.md)).

In source, a request whose client gives up, or whose `keypaste mcp` goes away, is withdrawn from the prompt rather than waiting out its 45 seconds.

## Approving in the desktop app

In source, when the desktop app has a vault unlocked, a credential request for that vault opens a keypaste prompt window over whatever you are doing. It shows who is asking (the name the client gave itself, which is not verified), the client label from its configuration, the entry, the field, what you can allow, a countdown, and the agent's reason, under a line saying the agent wrote it.

The prompt offers Deny and Allow once, and for an agent whose bridge has a label, Always allow and Always block. Allow once releases that one field and keeps nothing. Always allow and Always block each ask you to confirm in the same window, then keep that answer for that agent and that item's field until you remove it on the agent's page in Agents. Allow once works from a second after the prompt has focus, so a click meant for another window cannot approve. While another window has focus the prompt is drawn inactive and Allow once stays disarmed however long it waits, one that opened behind another window included; click into it or press Show the prompt on Agents, and it works a second later, with focus back on Deny. Deny works at any time. Deny, Escape and closing the window refuse, and focus starts on Deny, so Enter refuses too. Nobody answering for 45 seconds refuses. Locking the app, quitting it and the client giving up each refuse the request and take the prompt down. The one-prompt-at-a-time rule and the one-minute refusal cooldown apply as they do at `keypaste approve`.

In source, a request for an attachment, which an agent makes by the attachment's name with `request_attachment`, shows `attachment` and that name as its field, and Allow once releases that one attachment, at most 5 MiB.

The app does not read `policy.toml`. A credential it releases without a press of Allow once is an item's URL, its username while Agents see usernames is on, or a kind of value you allowed in Settings and confirmed, and History shows it as allowed. A run under a scoped token is released without a prompt unless its profile is protected ([T-32](../THREATS.md#t-32--a-scoped-token-is-a-bearer-credential)).

The Agents screen lists the request in front of you under Waiting for you, with the agent, the item and field it asks for and the seconds left, never its reason, and Show the prompt, which brings the prompt window forward with focus on Deny and its allows disarmed for another second; you answer only in that window. History, at the top of the screen, opens what agents and tokens asked for, and the activity log in Settings › Advanced shows the whole file.

## Runs that ask for a project's variables

In source, `keypaste run --session <project> -- <command>` asks the process holding the vault for the project's whole set instead of opening the vault itself ([replace-dotenv](replace-dotenv.md#run-your-app)). The app shows it in a prompt window of its own and `keypaste approve` in its terminal, naming the project, the variable names, the entry each value comes from, the command and the directory the run was started in, never a value. Allow once, or `o` at the terminal, starts the command with the set. It is offered Deny and Allow once only and is asked about each time, since a run names no agent whose mode could apply ([THREATS.md](../THREATS.md) T-34). The same rules as a credential request apply otherwise: Allow once works from a second after the prompt has focus, Deny, Escape, closing, `d`, `n`, Enter, a lock and 45 seconds without an answer refuse, one prompt is shown at a time, and refusing a run refuses the same project, command and directory for a minute. The command shown is what the run says it will start, and a program running as you could claim one and start another, so approve only a run you started ([THREATS.md](../THREATS.md) T-30). No agent setting releases a set. The process holding the vault writes each release and each refusal of a valid run to the audit log, and refuses a run whose line it cannot write.

## Commands an agent asks to run

In source, a bridge started with `--allow-run` lets an agent ask to run a command with secrets in its environment ([mcp-setup](mcp-setup.md#running-a-command-with-secrets---allow-run)). The prompt, in the app's window or `keypaste approve`'s terminal, shows `tool keypaste.run`, the client and its label, the exact program and command line, the resolved directory, the project and profile, each variable with the entry it comes from and `inject only`, and the agent's reason, and a loader such as `NODE_OPTIONS` is tagged as changing how programs start. The prompt offers Deny and Allow once only; Allow once, or `o`, starts that command once. The app or `keypaste approve` writes every run to the audit log before it answers, and the bridge starts nothing the owner did not record. An agent runs nothing until you turn run on for it, which is off by default, every run is asked, and a run whose set holds a blocked value is refused without asking. The output the agent gets back has each literal or escaped occurrence of a value replaced, but a command can reveal a value in another form, so approve only a command whose behaviour you know ([THREATS.md](../THREATS.md) T-35).

## When no approver is running

Everything is denied, and the agent is told exactly how to fix it:

```
keypaste: DENIED. Nobody can approve this right now: nothing holds this vault unlocked, neither
the keypaste desktop app nor keypaste approve, and keypaste releases nothing until one does,
whatever this agent's settings say.

Ask the person you are working with to unlock this vault in the keypaste desktop app, or to
run `keypaste approve --vault <their vault>` in a terminal, and then try again.
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

In source, on macOS and Linux the first such request after the app locks also posts a system notification, "An agent asked for a locked vault", which names no vault because your lock screen and other apps can read it; its Open keypaste shows the unlock screen with the vault asked about selected, whether it is the one the unlock screen shows or the one you chose. On Windows, and where the system cannot post one, the app shows a small notice of its own instead, which names the vault. It has no password field and does not take the keyboard from the window you are typing in. Close is its default, and Open keypaste works a second after the notice appears. No other notice appears until you have unlocked and the app has locked again. An agent asking for item names is likewise told that the app is open with the vault locked, and can raise that one notice too.

The MCP client may start its bridge before you start an approver. Calls are refused until an approver is available.

On `0.2.0`, this refusal can also occur under load while the approver is running: the bridge's half-second connection deadline can expire before it tries the pipe. No credential or entry name is released. Retrying is appropriate. This is F.9, repaired in `0.3.0`.

## What is written down

The app or `keypaste approve` appends one line to `~/.keypaste/audit.jsonl` for every call it answers, granted, denied or abandoned, and `keypaste mcp` one for every call it refuses itself, a malformed one included, or that nothing answered. Each line records the entry, field, client, stated reason and decision.

It does not add the returned field value to the log. Names and reason excerpts are logged metadata, so do not put secret values in them. See [docs/mcp-setup.md](mcp-setup.md) for the format.

<a id="the-honest-limits"></a>

## Limits

The vault stays unlocked while `keypaste approve` runs; it has no idle auto-lock. Stop that process to lock it. The desktop app holds a separate session, so locking the desktop does not lock the approver; in source the two cannot hold the same vault at once. In `v0.3.0` approval prompts appear only in the approver terminal; in source the desktop asks in its own window for the vault it has unlocked. An already-open approver also retains its in-memory vault snapshot: reopen it after a desktop or external edit to use the updated values. In source, once another program changes the file it refuses every request as `vault-changed` instead, until you restart it.

keypaste keeps no value between requests. It cannot erase strings or copies retained by clients, transcripts or session files. Stopping the approver cannot revoke these copies; rotate the credential at its provider when needed. [SECURITY.md](../SECURITY.md) describes the memory limits.

## Verifying it yourself

`scripts/verify-approval-e2e.sh` runs real CLI and MCP processes against a test vault in CI on Linux, macOS and Windows. It checks that approval returns the secret, refusal does not, and neither writes it to the audit log.

`scripts/verify-demo.sh` checks the corresponding dialog in [Claude asks for a key, you approve, the deploy runs](demo.md) and four other public pages against the built binaries. It does not read this page, so edits to the examples above need review against that verified demo.
