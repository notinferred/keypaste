# Claude asks for a key, you approve, the deploy runs

> This demo runs the source build headed for keypaste 1.0.0, which is not yet published. The published `v0.3.0` has `keypaste agent` and a separate `keypaste-mcp` binary instead of `keypaste approve` and `keypaste mcp`, asks `Approve? [y/N]`, and keeps a variable as its own item, so its transcript differs. [FEATURES](FEATURES.md) lists what each version has.

This demo uses two terminals to show a failed deploy, a credential request, approval and a successful retry, followed by its audit record. The recorded flow takes about sixty seconds.

`keypaste approve` holds the vault and asks for approval. Your MCP client starts `keypaste mcp`, which forwards requests without holding a vault. You start the approver yourself. The desktop app approves the same way while it holds the vault unlocked; this demo uses the terminal. [Approvals](approvals.md) explains what you authorize.

The terminal output below was captured from real keypaste processes. Claude's actions and wording can vary between runs.

## Before you start

Until 1.0.0 is published, the demo needs `keypaste` [built from source](../README.md#building-from-source). Put its build directory, `artifacts/bin/Keypaste.Cli/release/`, first on `PATH`, or use its full path below; an older installed `keypaste` would otherwise run. You also need Claude Code, and two terminals you can see at once.

Use a disposable vault. The tool returns credentials as text and structured data, which Claude can display and retain in transcripts or session files. This demo uses a fake value.

## Building the demo vault

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

The key is a variable of the project `demo`: a protected field named `STRIPE_KEY` on the item `env/demo/.env`, which the tag `env:demo` puts in the project's `dev` environment. That is the field the request below names, and one an agent with no settings of its own can see. Nothing is echoed at the prompts. When it asks for the value, paste this:

```
sk_test_EXAMPLE_ONLY_not_a_real_key_0000
```

This is a fake value shaped like a Stripe test key so the masked output is recognizable.

## Wiring it into Claude Code

```sh
claude mcp add --transport stdio --scope local keypaste \
  -- /absolute/path/to/keypaste mcp \
     --vault /absolute/path/to/keypaste-demo.kdbx \
     --client-label claude-code
```

The equivalent by hand, in `.mcp.json`:

```json
{
  "mcpServers": {
    "keypaste": {
      "command": "/absolute/path/to/keypaste",
      "args": ["mcp", "--vault", "/absolute/path/to/keypaste-demo.kdbx", "--client-label", "claude-code"]
    }
  }
}
```

`--scope local` applies only to this machine. A bridge without `--expose` reaches only the project variables of tagged items (`tag:env:*`), and so does an agent with no settings of its own, which is where `env set` put the key; an `--expose` you add can only narrow that further, and the agent's settings say what it gets ([agent settings](policy.md)). `--client-label claude-code` is the name those settings follow. The master password is entered in the approver, never in client configuration; see [Connecting keypaste to Claude](mcp-setup.md).

Work in a small scratch project rather than a real one. Copy `scripts/demo/deploy.sh` from this repository into it; that is the deploy Claude will run.

## The sixty seconds

<a id="000--start-the-approver"></a>

### Start the approver

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

Unlocking pauses for Argon2 key derivation. The pipe suffix is derived from the home directory and will differ on your machine.

Leave it running. Ctrl+C stops this approver and closes its vault; it does not control another process's unlocked session or erase credentials already released.

<a id="008--ask-for-the-deploy"></a>

### Request the deploy

Right terminal, in your scratch project:

```sh
claude
```

Then type:

```
Deploy the billing service to staging with ./deploy.sh. It needs a Stripe key — get it from
my keypaste vault rather than asking me to paste one.
```

The prompt names the deploy script and directs Claude to the MCP tool for its credential.

<a id="015--the-deploy-refuses"></a>

### Observe the refusal

Claude runs `./deploy.sh` and gets nothing:

```
deploy: STRIPE_KEY is not set.
deploy: the billing service will not deploy without it.
deploy: nothing was deployed.
```

The deploy exits 1 without naming keypaste. Claude must choose how to obtain the credential.

<a id="022--the-question"></a>

### Review the request

Claude calls `request_credential`, and your left terminal stops being idle:

```
────────────────────────────────────────────────────────────
keypaste: an agent is asking for a credential.

  agent    claude-code (claude-code)
  item     env/demo/.env
  field    STRIPE_KEY

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

[d] deny  [o] once  [a] always  [b] block  45s ›
```

Your run may use different arguments. Claude writes the reason. `agent` is the sanitized name the AI tool gave itself, which nothing checks; it can differ from `--client-label`, the name the audit log records and the agent's settings follow. `item` is the resolved vault item, and `field` is the requested allowed field.

`o` releases it for this request only. Anything else, including Enter, is a no, and so is saying nothing for 45 seconds.

Claude may call `list_entry_names` first to find the item, or go straight to the credential. Either is fine, and both appear in the log.

<a id="030--say-yes"></a>

### Approve

Press `o`:

```
keypaste: allowed once.
────────────────────────────────────────────────────────────
keypaste: released env/demo/.env to claude-code once
```

Claude sets the variable for the child process and runs the deploy again:

```
deploy: building billing-service
deploy: STRIPE_KEY is set: sk_test_...0000 (40 characters)
deploy: deployed billing-service to staging
deploy: this is a demo. Nothing was built and nothing left this machine.
```

The deploy script prints only a masked prefix, suffix and length. keypaste cannot require the agent to mask its own output.

## If you say no

Press `d`, Enter or Escape:

```
keypaste: denied. Nothing was released.
────────────────────────────────────────────────────────────
```

What Claude reads back:

```
keypaste: DENIED. A person read this request and said no.

Do not retry: asking again immediately is refused without troubling them, and asking
repeatedly is treated as pressure rather than as a question. Ask them directly what they want
you to do instead. This call was recorded in the audit log as denied.
```

The same request is refused for a minute after an explicit denial without prompting again. Timeouts receive a different response because nobody made a decision, and the agent is allowed to retry.

## What the log says

```sh
keypaste log --since 5m
```

```
2 records of 2 in /home/you/.keypaste/audit.jsonl, since 2026-07-27 09:53:07Z

  time (UTC)           agent        item           decision  method
  2026-07-27 09:57:42  claude-code  -              granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/.env  granted   prompt
```

This run logged a listing (`exposure`) followed by an approved credential request (`prompt`). The returned field value is excluded from the log; names and reason excerpts remain logged metadata and should not contain secrets.

Filtered views report the selected and total record counts.

```sh
keypaste log verify
```

```
2 records verified in /home/you/.keypaste/audit.jsonl.
Latest: seq 2, hash d1845344153201c850ac949d108d24d4243931aa38c82f909558910aac78e8ae
```

Every verification reports the limits of the chain: complete recomputation and deletion of final records can escape detection. `--expect <hash>` detects loss of a previously observed record; [Approvals](approvals.md#knowing-it-has-not-been-edited) explains its use.

Repeat the credential request and it asks again, since Allow once keeps nothing. Had you allowed project variables for every agent in Agents, the second release would read `allowed` instead of `prompt`: the credential was released under a reason nobody read. [THREATS.md](../THREATS.md) T-12 explains the limit.

## When it does not go like this

| What you see | What it is |
|---|---|
| `DENIED. Nobody can approve this right now` (`v0.3.0`: `No keypaste agent is running`) | The left terminal is not running, or the two are on different pipes. Same vault, and pass the same `--approver` to both if you set one. An app with this vault unlocked keeps `keypaste approve` from starting on it; lock the vault in the app first. |
| Claude asks you to paste the key | It did not reach for the tool. Say `use the keypaste MCP server to read STRIPE_KEY from env/demo/.env`. |
| `DENIED. That item is outside what this agent can see` (`v0.3.0`: `outside what this server was configured to expose`) | The request named something other than `STRIPE_KEY` on `env/demo/.env`, the key was stored some other way than `env set`, or an `--expose` was added to the bridge. Approval cannot widen it. |
| The dialog never appears | Look at the terminal running `keypaste approve`: that is where this demo's prompt appears. |
| The server shows as failed to start | Check the absolute executable path and permissions, then whether `~/.keypaste` is writable for auditing. |

<a id="the-honest-limits"></a>

## Limits

The tool returns the credential as both text and structured data. Claude can retain either in context and session files, which is why this demo uses a fake value. Claude may place the value on a command line when starting a child process. keypaste does not control that use or retention. keypaste cannot erase returned copies or revoke credentials at their provider. keypaste sanitizes, truncates and labels the reason beside the resolved item name. It cannot verify whether the claim is true. Claude may choose another item, ask for clarification or inspect the script first. This recording does not establish behavior in future runs. The approver keeps its vault unlocked until stopped with Ctrl+C; it has no idle auto-lock. A kind of value you allowed for every agent is not asked about ([agent settings](policy.md)); the demo's rules allow nothing, so every request comes to you.

## Verifying it yourself

`scripts/verify-demo.sh` runs real CLI and MCP processes on Linux, macOS and Windows. It compares the approval dialog byte for byte, exercises both deploy paths, checks that refusal returns no credential, and checks that the credential stays out of deploy output and the audit log.

The harness sends fixed MCP calls; it does not run Claude. Observing the model remains part of the manual demo.
