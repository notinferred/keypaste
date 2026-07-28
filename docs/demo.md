# Claude asks for a key, you approve, the deploy runs

Two terminals. A deploy that refuses because it has no API key, an agent that goes looking for one, a question on your screen, one keystroke, and a deploy that works — then the log line proving it happened. About sixty seconds.

The two processes are not interchangeable, and the split is the whole design. **`keypaste agent`** holds your vault and asks you; **`keypaste-mcp`** holds no vault and decides nothing. Your MCP client starts the second one, which means software starts it. You start the first. [**Approving an agent's request**](approvals.md) is the guide to what you are agreeing to.

**Everything Claude does on this page is Claude's choice, not a script.** keypaste's half is deterministic and every block below was pasted out of a real terminal. Claude's half is a model deciding what to do, and it will word things differently every time you run it. Where that shows, this page says so.

## Before you start

```sh
dotnet build keypaste.slnx -c Release
```

The two binaries land at `artifacts/bin/Keypaste.Cli/release/keypaste` and `artifacts/bin/Keypaste.Mcp/release/keypaste-mcp` (`.exe` on Windows). Note both absolute paths; you need them below. You also need Claude Code, and two terminals you can see at once.

**Use a throwaway vault for this.** A released credential is returned to the agent twice — once as text and once as structured data, so that a client reading either half works — which means it is rendered in Claude's transcript and stored in its session file. That is inherent to the protocol, not something keypaste can wrap. The vault built below holds a value that is worth nothing.

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

**That value is deliberate nonsense.** It is shaped like a Stripe test key so the masked line at the end of the demo looks like the real thing, and it is worth nothing to anybody who sees it.

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

`--scope local` keeps this to your machine. There is no `--expose` here, so the default applies and `env/**` is the only part of the vault an agent can even name. There is nowhere to put a master password, and there never will be — see [**Connecting keypaste to Claude**](mcp-setup.md).

Work in a small scratch project rather than a real one. Copy `scripts/demo/deploy.sh` from this repository into it; that is the deploy Claude will run.

## The sixty seconds

### 0:00 — start the approver

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

There is a short pause after you press Enter: that is Argon2 deriving your key, and it is the only thing in keypaste that is deliberately slow. The tail of the pipe name is derived from your home directory, so yours will differ; everything else is exact.

Leave it running. Ctrl+C locks the vault again.

### 0:08 — ask for the deploy

Right terminal, in your scratch project:

```sh
claude
```

Then type:

```
Deploy the billing service to staging with ./deploy.sh. It needs a Stripe key — get it from
my keypaste vault rather than asking me to paste one.
```

Both halves of that sentence earn their place. Naming the script removes the guesswork about what "deploy" means. The second clause heads off the ordinary failure, which is not that the agent does something dangerous — it is that it politely asks *you* to paste a secret into a chat window.

### 0:15 — the deploy refuses

Claude runs `./deploy.sh` and gets nothing:

```
deploy: STRIPE_KEY is not set.
deploy: the billing service will not deploy without it.
deploy: nothing was deployed.
```

Exit 1, and no mention of keypaste anywhere in it. That refusal is the whole reason the agent goes looking, and it has to be the agent's idea.

### 0:22 — the question

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

**Exactly one line of that will read differently on your screen, and it is the indented one.** Claude writes that sentence itself, so it changes every run. Everything above it is keypaste's: `client` is the `--client-label` *you* put in your own config, not a name the agent chose; `entry` and `field` came out of your vault; and `for` is what will actually apply — Claude asked for 900 seconds and 300 is the ceiling the approver was started with.

Claude may call `list_entry_names` first to find the entry, or go straight to the credential. Either is fine, and both appear in the log.

You have 45 seconds. Anything that is not `y` or `yes` is a no, including pressing Enter, and so is saying nothing.

### 0:30 — say yes

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

**Look at the second line.** The one program in this story with a legitimate reason to hold the credential still does not print it. keypaste cannot make an agent behave that way — but a deploy script you control can, and this is what it costs: eight characters and a length.

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

Asking again for the same thing inside a minute is refused without reaching you at all. A request that *timed out* is told something different and deliberately softer — nobody decided anything, you may simply have been away from the keyboard — so the agent is not told to give up.

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

Two calls, because Claude listed the names before it asked for one. `exposure` means a listing allowed by your `--expose` globs; `prompt` means a person was shown that exact request and answered it. **The value is not in the file and no field of a record can hold one.**

A filtered view always says it is filtered, with the count it is showing out of the count in the file, so a narrow view can never be mistaken for the whole log.

```sh
keypaste log verify
```

```
2 records verified in /home/you/.keypaste/audit.jsonl.
Latest: seq 2, hash d1845344153201c850ac949d108d24d4243931aa38c82f909558910aac78e8ae
```

It then prints, on every pass rather than only on a failure, the two things it cannot see: a rewrite that recomputed the chain, and records deleted from the end. `--expect <hash>` closes the second — [**Connecting keypaste to Claude**](mcp-setup.md) has the detail.

Run the demo twice inside five minutes and the second release reads `grant-cache (!)` instead of `prompt`. That mark means the credential was served from the approval you already gave, under a reason nobody read. [THREATS.md](../THREATS.md) T-12 is the argument about what that costs.

## When it does not go like this

| What you see | What it is |
|---|---|
| `DENIED. No keypaste agent is running` | The left terminal is not running, or the two are on different pipes. Same vault, and pass the same `--approver` to both if you set one. |
| Claude asks *you* to paste the key | It did not reach for the tool. Say `use the keypaste MCP server to read env/demo/STRIPE_KEY`. |
| `DENIED. That entry is outside what this server was configured to expose` | The entry is not under `env/`. The default exposure is `env/**` and approval cannot widen it. |
| The dialog never appears | Your MCP client is running somewhere you are not looking. There is no native dialog yet; the approval prompt is that terminal. |
| The server shows as failed to start | Check the path is absolute and executable, then check `~/.keypaste` is writable — an unwritable audit log stops the bridge on purpose. |

## The honest limits

- **The credential is in Claude's context, twice.** `request_credential` returns it as text and as structured data so a client reading either half works, so your MCP client renders it and stores it in its session file. That is why this page uses a fake value.
- **And then the agent puts it on a command line.** To run the deploy it has to set the variable for a child process. keypaste's guarantee ends at the moment of release; what happens next belongs to the agent and to the machine it runs on. The TTL and the audit log are what keypaste offers instead of a promise it could not keep.
- **The reason is always a claim.** keypaste can strip the control characters, cap the length, label whose words they are, and put the entry name beside it from a source the agent does not control. It cannot tell you whether the sentence is true.
- **Nothing here proves Claude will behave this way.** It is a model, not a script. It may pick a different entry, ask a clarifying question first, or read the deploy script before running it.
- **The vault stays unlocked while the approver runs.** There is no idle auto-lock in this version; Ctrl+C is the lock.
- **A policy rule would skip the dialog entirely.** There is no policy file unless you wrote one, and [**Pre-approving with a policy file**](policy.md) is honest about what writing one costs.

## Verifying it yourself

`scripts/verify-demo.sh` runs this page in CI on Linux, macOS and Windows. It builds a vault, starts a real `keypaste agent`, drives a real `keypaste-mcp` from a separate process, and diffs the approval dialog the agent actually draws against the block on this page — character for character — so a transcript here cannot drift from what the binaries print. It runs `scripts/demo/deploy.sh` down both paths and asserts the key never appears in what it prints or in the audit log, and it checks the refused path returns nothing.

**It does not run Claude, and it never will.** What a model chooses to do is not a thing a gate can hold, and a check this project asks strangers to trust should not depend on a paid, networked, non-reproducible service. The stand-in sends exactly the calls this page says Claude sends; whether Claude sends them is the part you are watching for when you run the demo yourself. [DECISIONS.md](../DECISIONS.md) D-0034 is the full argument, including the two lines the harness cannot see and does not pretend to.

The recording of this page is made by `scripts/demo/`, and [its README](../scripts/demo/README.md) explains how — and why the `y` in it was pressed by a person.
