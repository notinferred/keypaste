# Demo: Claude asks for a key, you approve, the deploy runs

In about sixty seconds, a deploy fails for want of a key, Claude asks keypaste for it, you allow it once, the deploy runs, and the log shows what happened. The terminal output is captured from real keypaste processes; Claude's actions and wording vary between runs.

> This demo runs the source build headed for 1.0.0. The published `v0.3.0` differs ([FEATURES](FEATURES.md)).

## Before you start

- `keypaste` [built from source](install.md#building-from-source), with `artifacts/bin/Keypaste.Cli/release/` first on `PATH`, so an older installed `keypaste` does not run.
- Claude Code.
- Two terminals you can see at once.
- A scratch project holding a copy of this repository's `scripts/demo/deploy.sh`.

`keypaste approve`, in the left terminal, holds the vault and asks you. Claude Code starts `keypaste mcp`, the bridge, which holds no vault and forwards requests ([Approvals](approvals.md)).

## 1. Make a demo vault

Use a disposable vault and a fake value: Claude can keep a released credential in its transcripts.

```sh
keypaste init ~/keypaste-demo.kdbx
export KEYPASTE_VAULT=~/keypaste-demo.kdbx
keypaste env set demo STRIPE_KEY
```

```
New master password:
Confirm master password:
Created /home/you/keypaste-demo.kdbx
Master password:
Value for STRIPE_KEY:
Set STRIPE_KEY on env/demo/.env, created and tagged env:demo
```

At the value prompt, paste this fake key:

```
sk_test_EXAMPLE_ONLY_not_a_real_key_0000
```

The key is now a variable of the project `demo`, which an agent with no settings of its own can see.

## 2. Connect Claude Code

```sh
claude mcp add --transport stdio --scope local keypaste \
  -- /absolute/path/to/keypaste mcp \
     --vault /absolute/path/to/keypaste-demo.kdbx \
     --client-label claude-code
```

- `--client-label claude-code` is the name the log records and the agent's [settings](policy.md) follow.
- With no `--expose`, the bridge reaches only project variables, where `env set` put the key.
- The master password goes into the approver, never into client configuration ([Connect an AI tool](mcp-setup.md)).

<a id="000--start-the-approver"></a>

## 3. Start the approver

Left terminal:

```sh
keypaste approve --vault ~/keypaste-demo.kdbx
```

```
Master password:
keypaste: watching /home/you/keypaste-demo.kdbx
keypaste: listening on keypaste-vault-9f3a1c02b7d54e60 for unlock 5d0c8e1a4b7f2c936e0a1d4b8c7f3e21, 45 seconds to answer.
keypaste: every value waits for you to say yes; agents see each item's URL, and its username unless Settings hides it. Press Ctrl+C to stop.
```

Leave it running. Ctrl+C closes the vault, but cannot take back a released credential.

<a id="008--ask-for-the-deploy"></a>

## 4. Ask Claude to deploy

Right terminal, in your scratch project, run `claude` and type:

```
Deploy the billing service to staging with ./deploy.sh. It needs a Stripe key — get it from
my keypaste vault rather than asking me to paste one.
```

<a id="015--the-deploy-refuses"></a>

## 5. The deploy fails

```
deploy: STRIPE_KEY is not set.
deploy: the billing service will not deploy without it.
deploy: nothing was deployed.
```

The script never names keypaste. Claude has to decide how to get the key.

<a id="022--the-question"></a>
<a id="review-the-request"></a>

## 6. Review the request

Claude calls `request_credential`, and your left terminal asks:

```
────────────────────────────────────────────────────────────
keypaste: an agent is asking for a credential.

  agent    claude-code (claude-code)
  item     .env
  group    env/demo
  field    STRIPE_KEY

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

[d] deny  [o] once  [a] always  45s ›
```

- `agent` is the name the AI tool gave itself. Nothing checks it; the log records `--client-label`.
- `item`, `group` and `field` are what the request resolved to.
- The reason is Claude's, and its wording varies.

<a id="030--say-yes"></a>

## 7. Approve

Press `o` to allow this request only. Any other key, or 45 seconds of silence, is a no.

```
keypaste: allowed once.
────────────────────────────────────────────────────────────
keypaste: released env/demo/.env to claude-code once
```

Claude runs the deploy again with the key:

```
deploy: building billing-service
deploy: STRIPE_KEY is set: sk_test_...0000 (40 characters)
deploy: deployed billing-service to staging
deploy: this is a demo. Nothing was built and nothing left this machine.
```

The script masks the key. keypaste cannot make the agent mask its own output.

## 8. Read the log

```sh
keypaste log --since 5m
```

```
2 records of 2 in /home/you/.keypaste/audit.jsonl, since 2026-07-27 09:53:07Z

  time (UTC)           agent        item           decision  method
  2026-07-27 09:57:42  claude-code  -              granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/.env  granted   prompt
```

`exposure` is a listing, and `prompt` the request you approved. The value is never logged; item names and the agent's reason are. `keypaste log verify` checks the hash chain ([Approvals](approvals.md#knowing-it-has-not-been-edited)).

Ask again and it asks again: Allow once keeps nothing.

## If you say no

Press `d`, Enter or Escape:

```
keypaste: denied. Nothing was released.
────────────────────────────────────────────────────────────
```

Claude reads:

```
keypaste: DENIED. A person read this request and said no.

Do not retry: asking again immediately is refused without troubling them, and asking
repeatedly is treated as pressure rather than as a question. Ask them directly what they want
you to do instead. This call was recorded in the audit log as denied.
```

For a minute, the same request is refused without asking you.

## When it goes differently

| What you see | What to do |
|---|---|
| `DENIED. Nobody can approve this right now` | Start the approver on the same vault, with the same `--approver` if you set one. Lock the vault in the desktop app first if it holds it. |
| Claude asks you to paste the key | Say `use the keypaste MCP server to read STRIPE_KEY from env/demo/.env`. |
| `DENIED. That item is outside what this agent can see` | The request named another item, or the key was not stored with `env set`. |
| No dialog appears | It appears in the terminal running `keypaste approve`. |
| The server fails to start | Check the absolute path to `keypaste` and that `~/.keypaste` is writable. |

<a id="the-honest-limits"></a>

## Limits

- Claude can keep a released credential in its context and session files, or put it on a command line. keypaste cannot erase those copies or revoke the key.
- keypaste cannot tell whether the agent's reason is true.
- Claude's behavior varies, so one recording says nothing about future runs.
- The approver keeps its vault unlocked until Ctrl+C.
- A kind of value you allowed for every agent is not asked about ([agent settings](policy.md)).

`scripts/verify-demo.sh` checks this page's terminal output against the real CLI and bridge on Linux, macOS and Windows. Claude's half is checked only by running the demo.
