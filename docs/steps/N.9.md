# N.9 — Give each CLI concept one name

Completed 2026-10-21 on `main` at `134e92d`, source only: ci 39614028030 and app 39614028190.

## Amendments

On 2026-10-21 the founder selected N.9 with N.8 and L.3, moved N.8 and N.9 into 0.5.0, and approved the plan's recommendations. A rename after 0.5.0 would break published scripts, and D-0416 drops old names without aliases only before 0.5.0. The approved plan also settled what the row left open (D-0470):

- **Other duplicates.** `keypaste mcp setup` is removed, leaving `keypaste setup`. `get --show` is removed, leaving `--reveal`, as N.13 had deferred.
- **Removed spellings are refused.** Each exits 1 with one line naming its replacement and runs nothing, so it is not an alias. `mcp serve`, `mcp setup` and `mcp policy` still never start the bridge.
- **The audit log.** Lines `keypaste clients` writes from now on name that client. Old lines keep what they say. The `.owner` claim keeps its kind, and only its display text became `keypaste approve`.

## What changed

- **`keypaste approve`** is the terminal approver. It replaces `keypaste agent` and `keypaste mcp serve`, and lives in `Commands/ApproveCommand.cs`.
- **`keypaste clients`** shows and changes each agent's settings. It replaces `keypaste mcp policy`, and lives in `Commands/ClientsCommand.cs`.
- **The help** lists `setup`, `mcp`, `approve` and `clients` under AGENTS, 26 verbs in all.
- **Every current text, script and document that named an old spelling** was updated:
  - texts in the CLI, Core, the bridge and the app, including what agents are told;
  - the gates, including `verify-demo.sh`'s approver;
  - the guides, THREATS, SECURITY and current DECISIONS rows.

  Left as they were:
  - `site/` is untouched, since a change there deploys keypaste.com, and L.1 rewrites it for 0.5.0;
  - step records and CHANGELOG's released sections stay as history.

## Evidence

ci 39614028030 and app 39614028190 at `134e92d`, every job green:

- **`CliAppTests`** covers the help with 26 verbs, `approve` and `clients` dispatching, and each removed spelling refused with its replacement while running nothing.
- **Other tests:**
  - `ProgramDispatchTests`: `mcp serve`, `setup` and `policy` never start the bridge.
  - `ClientsVerbTests` and `ApproveCommandTests`, renamed from the old tests.
  - `PlainWordsTests`, whose list of allowed command names now takes the `clients` verb.
  - `SessionAuthorityAccessTests`: the new audit name.
  - `ServerOptionsTests`: the bridge's usage names no subcommand.
- **The gates** drive `keypaste approve` and `keypaste clients` on all three runners.

**What failed on the way:**

- The workflow's final fix agent changed nothing, because plan mode was switched on during it.
- Its eight review findings were applied by hand before the compile check:
  - five test names cited in THREATS;
  - the approver's stop line and the two gates that read it;
  - a README sentence;
  - the allowed-names pattern.

## Decisions

- D-0470 in [DECISIONS](../../DECISIONS.md).
- D-0444's wording is amended to name `keypaste clients`. The other current rows that named `keypaste agent` now name `keypaste approve`.

## Limits and follow-ups

- **The tools jobs** ran against N.9's setup output in ci 39634014306, the tools-only run at `c208ad6`, and passed.
- **keypaste.com** still names `keypaste agent`, until L.1 rewrites it.
- **For 1.0.0.** The class and type names `AgentConsole` and `OwnerKind.TerminalAgent` keep the name D-0470 retired, and the claim file writes and parses the enum's name. Rename them `ApproveConsole` and `OwnerKind.TerminalApprover`, with `VaultClaim` mapping the kind through a fixed table that keeps `kind=TerminalAgent` on disk, as D-0470 requires. `VaultClaimTests` must show a terminal approver's claim written as `TerminalAgent` and a file holding `kind=TerminalAgent` read back as the terminal approver.
