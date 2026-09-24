# Approving an agent's request

A person approves credential requests unless a live approval or a matching policy rule covers them. This guide describes the published terminal workflow and, in source, [approving in the desktop app](#approving-in-the-desktop-app); [policy rules](policy.md) allow matching requests without a prompt at `keypaste agent`.

In source, one vault has one owner: while the app has a vault unlocked, `keypaste agent` on that vault is refused with a message naming the app, and the app asks about that vault's credential requests itself. The desktop and a terminal approver on another vault still unlock independently, and launching env projects through the app's session is unfinished; [STEPS](STEPS.md) owns the remaining work.

<a id="the-short-version"></a>

```
  your terminal                     your MCP client
       │                                   │
  keypaste agent  ◄──── local pipe ────  keypaste-mcp
   • unlocked vault                       • validates the request
   • asks you                             • refuses anything out of scope
   • holds live grants                    • writes the audit line
                                          • returns one field
```

`keypaste-mcp` is started by the MCP client and forwards requests without holding the vault. You start `keypaste agent`, which holds the vault and decides whether to prompt, reuse an approval or apply a policy rule.

An agent cannot trigger a master-password prompt. You enter the password only in a terminal you opened, after running `keypaste agent`. This avoids teaching you to trust password windows that another program could imitate.

## Starting the approver

```sh
keypaste agent --vault ~/vaults/personal.kdbx
```

It asks for your master password, then waits:

```
Master password:
keypaste: watching /home/you/vaults/personal.kdbx
keypaste: policy: no file at /home/you/.keypaste/policy.toml, so every request is shown to you.
keypaste: listening on keypaste-vault-9f3a1c02b7d54e60 for session 5d0c8e1a4b7f2c936e0a1d4b8c7f3e21, 45 seconds to answer, grants last at most 300 seconds
keypaste: nothing is released without you saying yes. Press Ctrl+C to stop.
```

The third line reports what the policy file says before anything can use it; with no such file, as above, every request comes to you. Leave it running in its own terminal. Ctrl+C locks the vault again and zeroes every live grant. In source, so do SIGTERM and closing the terminal, and a request waiting at the prompt is withdrawn and denied as `vault-locked`.

| Option | What it does |
|---|---|
| `--vault <path>` | Which vault to unlock. Or set `KEYPASTE_VAULT`. |
| `--approval-timeout <seconds>` | How long you get to answer. Default 45, range 5–55. |
| `--max-ttl <seconds>` | The longest grant it will ever issue, however long an agent asks for. Default 300. |
| `--approver <name>` | Which pipe to listen on. Or set `KEYPASTE_APPROVER`. You need this only if you run two. |

Both processes derive the same per-user pipe name, so [MCP configuration](mcp-setup.md) usually needs no change. In source the name is derived from the vault as well, so the bridge's `--vault` must name the vault the approver holds; `v0.3.0` names the pipe `keypaste-agent-…` and prints no session.

## What you see

```
────────────────────────────────────────────────────────────
keypaste: an agent is asking for a credential.

  client   claude-code
  entry    env/dev/STRIPE_KEY
  field    password
  for      300 seconds

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

Approve? [y/N]
```

`client` is the connecting program's unauthenticated name. Any process that can start `keypaste-mcp` can claim it; keypaste displays it but does not authorize from it.

`entry` and `field` identify the requested value in your vault. A `/` inside an entry title is displayed as a space so a title such as `../../prod/ROOT_TOKEN` cannot impersonate a different group path.

`for` is the grant lifetime after the approver applies `--max-ttl`.

The reason is text written by the agent. keypaste removes control characters, line breaks, invisible characters and right-to-left controls, then truncates it to 400 characters. It cannot alter the dialog, default answer or deadline.

Anything that is not `y` or `yes` is a no, including pressing Enter. Answering nothing for 45 seconds is a no.

## Repeat requests

If the agent asks for the same field of the same entry again, and the grant has not expired, you are not asked twice:

```
keypaste: reused an approval for env/dev/STRIPE_KEY (238s left)
```

The grant belongs to that one connection. If the client restarts, the grant is gone. A different field of the same entry is a different question and you are asked again.

Reused approvals do not show the new reason in the terminal. The audit log records it for later review. Set `--max-ttl 60` to shorten reuse or `--max-ttl 1` to effectively disable it; THREATS.md T-12 explains the limit.

## When you say no

An explicit refusal tells the agent not to retry. The same request is refused for one minute without prompting again. A timeout allows retry because nobody made a decision.

Only one request is displayed at a time. Additional requests on that connection, including entry listings, receive `BUSY` immediately. They are not queued, and the response does not identify the call already in progress.

In source, a request whose client gives up, or whose `keypaste-mcp` goes away, is withdrawn from the prompt rather than waiting out its 45 seconds.

## Approving in the desktop app

In source, when the desktop app has a vault unlocked, a credential request for that vault opens a keypaste prompt window over whatever you are doing. It shows who is asking (the name the client gave itself, which is not verified), the client label from its configuration, the entry, the field, how long a grant would last, and the agent's reason, under a line saying the agent wrote it.

Approve releases that one field. It works a second after the prompt appears, so a click meant for another window cannot approve. Deny, Escape and closing the window refuse, and focus starts on Deny, so Enter refuses too. Nobody answering for 45 seconds refuses. Locking the app, quitting it and the client giving up each refuse the request and take the prompt down. The one-prompt-at-a-time rule, the one-minute refusal cooldown and connection-scoped grants apply as they do at `keypaste agent`. The app's grants last at most 300 seconds.

The app does not read `policy.toml`: every release from the app needs a press of Approve.

Agent Activity lists the request in front of you and the grants in force, each with the client, its label, the entry, the field and the seconds left, and counts them down. Revoke ends one grant and Revoke all ends every one, so the next request for them opens the prompt again; a revoke is not recorded in the audit log. Below the lists is this session's history: the audit records naming the app's current session, as `keypaste log` prints them. It says when the log is missing or cannot be read rather than showing an empty history, and the Log screen shows the whole file.

## When no agent is running

Everything is denied, and the agent is told exactly how to fix it:

```
keypaste: DENIED. Nobody can approve this right now: nothing holds this vault unlocked, neither
the keypaste desktop app nor a keypaste agent. keypaste never releases a credential without a
person saying yes to that specific request.

Ask the person you are working with to unlock this vault in the keypaste desktop app, or to
run `keypaste agent --vault <their vault>` in a terminal, and then try again.
```

That is the source wording; `v0.3.0` says "No keypaste agent is running, so there is nobody to approve this."

The MCP client may start its bridge before you start an approver. Calls are refused until an approver is available.

On `0.2.0`, this refusal can also occur under load while the approver is running: the bridge's half-second connection deadline can expire before it tries the pipe. No credential or entry name is released. Retrying is appropriate. This is F.9, repaired in `0.3.0`.

## What is written down

`keypaste-mcp` appends one line to `~/.keypaste/audit.jsonl` for every call, including granted, denied, malformed and abandoned requests. It records the entry, field, client, stated reason and decision.

It does not add the returned field value to the log. Names and reason excerpts are logged metadata, so do not put secret values in them. See [docs/mcp-setup.md](mcp-setup.md) for the format.

<a id="the-honest-limits"></a>

## Limits

The vault stays unlocked while `keypaste agent` runs; it has no idle auto-lock. Stop that process to lock it. The desktop app holds a separate session, so locking the desktop does not lock the approver; in source the two cannot hold the same vault at once. In `v0.3.0` approval prompts appear only in the approver terminal; in source the desktop asks in its own window for the vault it has unlocked. An already-open approver also retains its in-memory vault snapshot: reopen it after a desktop or external edit to use the updated values. In source, once another program changes the file it refuses every request as `vault-changed` instead, until you restart it.

TTL limits cached approval reuse. Expiry clears the cache buffer but cannot erase strings or copies retained by clients, transcripts or session files. Stopping the approver cannot revoke these copies; rotate the credential at its provider when needed. [SECURITY.md](../SECURITY.md) describes the memory limits.

## Verifying it yourself

`scripts/verify-approval-e2e.sh` runs real CLI and MCP processes against a test vault in CI on Linux, macOS and Windows. It checks that approval returns the secret, refusal does not, and neither writes it to the audit log.

`scripts/verify-demo.sh` checks the corresponding dialog in [Claude asks for a key, you approve, the deploy runs](demo.md) and four other public pages against the built binaries. It does not read this page, so edits to the examples above need review against that verified demo.
