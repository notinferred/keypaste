# Recording the demo

[`docs/demo.md`](../../docs/demo.md) is the demo. This directory is how the GIF of it gets made.

The recording is a **real Claude Code session** driving a **real `keypaste-mcp`** against a **real
`keypaste agent`**, with a person pressing `y`. It is not a scripted terminal, and the reasoning is
in [`DECISIONS.md`](../../DECISIONS.md) D-0033: a demo whose approval keystroke came from a script
is a demo of a mock, and the difference is invisible in the output.

Everything here runs **inside Linux — WSL is fine, Windows is not**. The approver channel is a .NET
named pipe: `\\.\pipe\<name>` on Windows, a socket under the temporary directory on Unix. A Windows
Claude Code spawns a Windows `keypaste-mcp`, which cannot reach a Linux `keypaste agent`, and every
request in your recording would come back `no keypaste agent is running`.

## Once per machine

```sh
scripts/demo/install-recording-tools.sh   # asks for sudo once, and says what it will install
scripts/demo/build-demo-binaries.sh       # clones at HEAD, publishes Linux binaries
scripts/demo/make-demo-fixture.sh         # vault, project, .mcp.json, empty audit log

cd ~/kp/billing && claude                 # then: /login, trust the folder, approve the MCP server
```

**Do not skip that last line.** Without it the first thing your demo shows is a consent dialog for
the very tool it is demonstrating.

## Per take

```sh
scripts/demo/make-demo-fixture.sh   # empties the audit log, so the closing table is this take only
scripts/demo/record-demo.sh
scripts/demo/render-demo-gif.sh
```

`record-demo.sh` opens the panes, starts the approver, types the master password, waits for it to
be listening, starts Claude and types the prompt — then stops and hands you the keyboard. You press
`y`, run `keypaste log`, and Ctrl+C the approver. `--auto-approve` exists for rehearsals only.

## What you should expect to go wrong

**Claude is a model, not a script. Budget three to eight takes.** It may plan before acting, read
`deploy.sh` first, pick a different entry, or ask a clarifying question. `CLAUDE.md` in the fixture
is the one legitimate lever and it is deliberately vague about *which* entry — the agent still has
to look. Abandon a bad take early rather than editing it later.

**You have 45 seconds to answer.** A dialog nobody answers is a denial and a lost take.

**Never point any of this at a real vault.** A released credential is returned to the agent twice,
so it lands in Claude's transcript, in its session file, and in a `.cast` this repository commits to
git forever. `make-demo-fixture.sh` refuses to build a vault whose sentinel does not look like an
obvious fake, and `record-demo.sh` refuses to hand over a cast containing the master password or
anything shaped like a real key.

## Why the cast is committed too

`docs/demo/keypaste-demo.cast` is JSON lines. Anyone can `grep` it and check that the master
password never appears, that the released value is a sentinel, that the dialog on screen is the one
`TerminalApprovalChannel` actually emits, and that the audit table was not retouched. A GIF is a
megabyte of pixels nobody can check. It is also what lets the asset be re-rendered at another size
or theme without re-shooting.

Same instinct as `keypaste log verify`: the artefact that makes a claim should carry the means to
check it.

## What is not automated, and will not be

None of this runs in CI. A gate must be able to be green, and a step needing a live model, a paid
API and a human keystroke cannot be. What *is* gated is
[`scripts/verify-demo.sh`](../verify-demo.sh), which holds every transcript in `docs/demo.md` to
what the shipped binaries print — including diffing the approval dialog character for character —
on Linux, macOS and Windows.
