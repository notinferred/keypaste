# Approving an agent's request

When an AI agent asks keypaste for a credential, a person decides — unless that person already wrote
a rule saying yes to exactly this. This page is about who that person is, what they see, and what
happens when nobody is there. [Pre-approving with a policy file](policy.md) is the other half, and
it is the only path in keypaste where nobody is asked at all.

## The short version

```
  your terminal                     your MCP client
       │                                   │
  keypaste agent  ◄──── local pipe ────  keypaste-mcp
   • unlocked vault                       • validates the request
   • asks you                             • refuses anything out of scope
   • holds live grants                    • writes the audit line
                                          • returns one field
```

Two processes, on purpose. **`keypaste-mcp` never holds your vault and never decides anything** —
including whether a policy rule covers a request, which is decided in `keypaste agent` where the
vault is. It is started by your MCP client, which means it is started by software. `keypaste agent`
is started by you.

That split exists for one reason above all the others: **nothing an agent does can make keypaste ask
for your master password.** You type it in a terminal you opened, in answer to a command you typed.
Any program on your machine can pop up a window that looks like keypaste asking for your master
password — so keypaste never gives you a reason to expect one.

## Starting the approver

```sh
keypaste agent --vault ~/vaults/personal.kdbx
```

It asks for your master password, then waits:

```
Master password:
keypaste: watching /home/you/vaults/personal.kdbx
keypaste: policy: no file at /home/you/.keypaste/policy.toml, so every request is shown to you.
keypaste: listening on keypaste-agent-9f3a1c02b7d54e60, 45 seconds to answer, grants last at most 300 seconds
keypaste: nothing is released without you saying yes. Press Ctrl+C to stop.
```

The third line reports what the policy file says before anything can use it; with no such file, as
above, every request comes to you. Leave it running in its own terminal. Ctrl+C locks the vault
again and zeroes every live grant.

| Option | What it does |
|---|---|
| `--vault <path>` | Which vault to unlock. Or set `KEYPASTE_VAULT`. |
| `--approval-timeout <seconds>` | How long you get to answer. Default 45, range 5–55. |
| `--max-ttl <seconds>` | The longest grant it will ever issue, however long an agent asks for. Default 300. |
| `--approver <name>` | Which pipe to listen on. Or set `KEYPASTE_APPROVER`. You need this only if you run two. |

`keypaste-mcp` finds it automatically — both sides derive the same per-user default name — so
[the MCP client config](mcp-setup.md) usually needs no change.

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

Four things, and each is there for a reason.

**client** — what the connecting program calls itself. **This is not proof of anything.** Nothing
authenticates it; any process that can start `keypaste-mcp` can call itself `claude-code`. keypaste
shows it because it is usually true and always worth knowing, and never makes a decision from it.

**entry** and **field** — where the value would come from, and which one. These come from your
vault, so they are the trustworthy half of the screen. A `/` inside an entry's *title* is shown as a
space, so an entry cunningly named `../../prod/ROOT_TOKEN` cannot render as though it lived somewhere
it does not.

**for** — how long the grant will last. This is the number that will *actually* apply after
`--max-ttl`, not the number the agent asked for.

**the reason** — free text the agent wrote, whose entire purpose is to persuade you. keypaste
sanitizes it (no control characters, no line breaks, no invisible or right-to-left trickery) and cuts
it at 400 characters, so it cannot draw a fake dialog inside the real one or push the question off
your screen. It cannot change the default answer or the deadline, because the thing that renders it
has nowhere to put either.

Anything that is not `y` or `yes` is a no, including pressing Enter. Answering nothing for 45 seconds
is a no.

## Repeat requests

If the agent asks for **the same field of the same entry** again, and the grant has not expired, you
are not asked twice:

```
keypaste: reused an approval for env/dev/STRIPE_KEY (238s left)
```

The grant belongs to that one connection. If the client restarts, the grant is gone. A different
field of the same entry is a different question and you are asked again.

**Worth knowing:** on a reuse, keypaste does *not* show you what the agent said its reason was that
time. It is recorded in the audit log — so you can see afterwards whether it changed — but nobody
reads it in the moment. If that bothers you for a particular vault, `--max-ttl 60` is the control,
and `--max-ttl 1` effectively turns reuse off. THREATS.md T-12 has the full argument.

## When you say no

The agent is told plainly, and told not to ask again. Asking again for the same thing inside a minute
is refused without bothering you at all. If a request times out because you were away from the
keyboard, the agent is *not* told to give up — nobody decided anything.

Only one request is ever in front of you at a time. A second one arriving while you are reading the
first is refused immediately rather than queued behind it, so a misbehaving agent cannot build a
stack of prompts you have to clear.

## When no agent is running

Everything is denied, and the agent is told exactly how to fix it:

```
keypaste: DENIED. No keypaste agent is running, so there is nobody to approve this.
Ask the person you are working with to run `keypaste agent --vault <their vault>` in a
terminal, and then try again.
```

This is the ordinary state of things — your MCP client starts `keypaste-mcp` when it launches,
probably long before you start an approver. Nothing breaks; you just get refusals until you start
one.

## What is written down

`keypaste-mcp` — not the approver — appends one line to `~/.keypaste/audit.jsonl` for **every** call,
granted or denied, including the ones that were malformed and the ones nobody waited for. It records
which entry, which field, who asked, what they said their reason was, and what was decided.

**It never records the value.** See [docs/mcp-setup.md](mcp-setup.md) for the format.

## The honest limits

- **The vault stays unlocked while the agent runs.** There is no idle auto-lock in this version;
  closing the terminal is the lock. Auto-locking arrives with the desktop app.
- **A released value lives in memory until its grant expires**, and it existed as an ordinary string
  before that. keypaste narrows the window; it does not claim in-memory secrecy, and SECURITY.md
  says so.
- **There is no native dialog yet.** The approval prompt is your terminal. If your MCP client runs
  somewhere you are not looking, you will not see the request until you look.
- **keypaste cannot tell whether the agent's reason is true.** It can only make sure the sentence is
  inert, that it is labelled as the agent's words, and that the entry name beside it came from your
  vault instead.
- **A policy rule skips this page entirely.** If you wrote one, requests it covers are released with
  no prompt and nobody reads the reason at all. The agent prints a line per release and the audit log
  records which rule did it; that is the whole of the signal. [policy.md](policy.md) is honest about
  what that costs.

## Verifying it yourself

`scripts/verify-approval-e2e.sh` runs the whole thing in CI on Linux, macOS and Windows: a real
vault, a real `keypaste agent`, a real `keypaste-mcp` in a separate process, one approval and one
refusal — asserting the approved request returns the secret, the refused one does not, and neither
puts it in the audit log.

`scripts/verify-demo.sh` additionally holds the dialog above to what the shipped binary draws,
character for character, so the block on this page cannot drift from the one on your screen.
[**Claude asks for a key, you approve, the deploy runs**](demo.md) is that flow end to end.
