# Claude asks for a key, you approve, the deploy runs

This demo uses two terminals to show a failed deploy, a credential request, approval and a successful retry, followed by its audit record. The recorded flow takes about sixty seconds.

`keypaste agent` holds the vault and asks for approval. Your MCP client starts `keypaste-mcp`, which forwards requests without holding a vault. You start the approver yourself. [Approvals](approvals.md) explains what you authorize.

The terminal output below was captured from real keypaste processes. Claude's actions and wording can vary between runs.

## Before you start

```sh
dotnet build keypaste.slnx -c Release
```

The two binaries land at `artifacts/bin/Keypaste.Cli/release/keypaste` and `artifacts/bin/Keypaste.Mcp/release/keypaste-mcp` (`.exe` on Windows). Make these build directories available on `PATH`, or replace the commands below with their full paths; an older installed `keypaste` will otherwise still run. You also need Claude Code, and two terminals you can see at once. This page verifies the source build; [RELEASE](RELEASE.md) owns published availability.

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
Set env/demo/STRIPE_KEY
```

Nothing is echoed at either prompt. When it asks for the value, paste this:

```
sk_test_EXAMPLE_ONLY_not_a_real_key_0000
```

This is a fake value shaped like a Stripe test key so the masked output is recognizable.

## Wiring it into Claude Code

```sh
claude mcp add --transport stdio --scope local keypaste \
  -- /absolute/path/to/keypaste-mcp \
     --vault /absolute/path/to/keypaste-demo.kdbx \
     --client-label claude-code
```

The equivalent by hand, in `.mcp.json`:

```json
{
  "mcpServers": {
    "keypaste": {
      "command": "/absolute/path/to/keypaste-mcp",
      "args": ["--vault", "/absolute/path/to/keypaste-demo.kdbx", "--client-label", "claude-code"]
    }
  }
}
```

`--scope local` applies only to this machine. Without explicit `--expose`, only `env/**` is available. The master password is entered in the approver, never in client configuration; see [Connecting keypaste to Claude](mcp-setup.md).

Work in a small scratch project rather than a real one. Copy `scripts/demo/deploy.sh` from this repository into it; that is the deploy Claude will run.

## The sixty seconds

<a id="000--start-the-approver"></a>

### Start the approver

Left terminal:

```sh
keypaste agent --vault ~/keypaste-demo.kdbx
```

```
Master password:
keypaste: watching /home/you/keypaste-demo.kdbx
keypaste: policy: no file at /home/you/.keypaste/policy.toml, so every request is shown to you.
keypaste: listening on keypaste-agent-9f3a1c02b7d54e60, 45 seconds to answer, grants last at most 300 seconds
keypaste: nothing is released without you saying yes. Press Ctrl+C to stop.
```

Unlocking pauses for Argon2 key derivation. The pipe suffix is derived from the home directory and will differ on your machine.

Leave it running. Ctrl+C locks the vault again.

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

  client   claude-code
  entry    env/demo/STRIPE_KEY
  field    password
  for      300 seconds

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

Approve? [y/N]
```

Your run may use different arguments. Claude writes the reason. `client` is the sanitized, unauthenticated MCP handshake name; it can differ from the configured audit and policy label. `entry` is the resolved vault entry, `field` is the requested allowed field, and `for` is the capped lifetime. This request asked for 900 seconds and received 300.

Claude may call `list_entry_names` first to find the entry, or go straight to the credential. Either is fine, and both appear in the log.

You have 45 seconds. Anything that is not `y` or `yes` is a no, including pressing Enter, and so is saying nothing.

<a id="030--say-yes"></a>

### Approve

Type `y`:

```
keypaste: approved.
────────────────────────────────────────────────────────────
keypaste: released env/demo/STRIPE_KEY to claude-code for 300s
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

Press Enter, or `n`:

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

  time (UTC)           client       entry                decision  method
  2026-07-27 09:57:42  claude-code  -                    granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/STRIPE_KEY  granted   prompt
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

Every verification reports the limits of the chain: complete recomputation and deletion of final records can escape detection. `--expect <hash>` detects loss of a previously observed record; [Connecting keypaste to Claude](mcp-setup.md) explains its use.

Repeat the credential request on the same MCP connection while its approval remains live and the second release reads `grant-cache (!)` instead of `prompt`. Restarting the client creates a new connection and requires a new approval. That mark means the credential was served from the approval you already gave, under a reason nobody read. [THREATS.md](../THREATS.md) T-12 explains the limit.

## When it does not go like this

| What you see | What it is |
|---|---|
| `DENIED. No keypaste agent is running` | The left terminal is not running, or the two are on different pipes. Same vault, and pass the same `--approver` to both if you set one. |
| Claude asks you to paste the key | It did not reach for the tool. Say `use the keypaste MCP server to read env/demo/STRIPE_KEY`. |
| `DENIED. That entry is outside what this server was configured to expose` | The entry is not under `env/`. The default exposure is `env/**` and approval cannot widen it. |
| The dialog never appears | Your MCP client is running somewhere you are not looking. There is no native dialog yet; the approval prompt is that terminal. |
| The server shows as failed to start | Check the absolute executable path and permissions, then whether `~/.keypaste` is writable for auditing. |

<a id="the-honest-limits"></a>

## Limits

The tool returns the credential as both text and structured data. Claude can retain either in context and session files, which is why this demo uses a fake value. Claude may place the value on a command line when starting a child process. keypaste does not control that use or retention. TTL limits cached approval reuse; it cannot erase returned copies or revoke credentials at their provider. keypaste sanitizes, truncates and labels the reason beside the resolved entry name. It cannot verify whether the claim is true. Claude may choose another entry, ask for clarification or inspect the script first. This recording does not establish behavior in future runs. The approver keeps its vault unlocked until stopped with Ctrl+C; it has no idle auto-lock. A matching [policy rule](policy.md) bypasses the dialog. This demo assumes no policy file.

## Verifying it yourself

`scripts/verify-demo.sh` runs real CLI and MCP processes on Linux, macOS and Windows. It compares the approval dialog byte for byte, exercises both deploy paths, checks that refusal returns no credential, and checks that the credential stays out of deploy output and the audit log.

The harness sends fixed MCP calls; it does not run Claude. Observing the model remains part of the manual demo. [DECISIONS.md](../DECISIONS.md) D-0034 explains the reproducibility requirement and the two prompt lines the harness cannot observe.
