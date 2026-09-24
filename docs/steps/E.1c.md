# E.1c — Use the unlocked session from the CLI runner

Completed 2026-09-24 on `main` above `4701350`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** `keypaste run` takes an explicit `--session` that attaches to the process holding the vault its `--vault` names, over the endpoint the bridge uses (D-0309, D-0310), and never opens the vault or asks for a password itself. The owner resolves the project through E.1a's resolution and asks the person before anything leaves: the app in its prompt window, `keypaste agent` in its terminal, each showing the project, its key names, the command and the working directory, never a value. Only an explicit approval releases the set, which travels on the endpoint to the runner and goes only into the child's environment. No owner, a locked or different session, a refusal, the timeout and a lock before release each exit non-zero naming why, start no child and never fall back to a password prompt. Without `--session`, `keypaste run` keeps its standalone unlock, exit codes and signal behaviour as documented. Traces to PRODUCT §§1.4, 2 and 3.4.

**Verify (V-E.1c):** with the app holding the vault, `keypaste run --session <project> -- <child>` raises the app's prompt naming the project and command; after Approve a real child reports the set's values from its own environment, and the runner printed no value and asked for no password. Deny, the timeout, a lock while it waits and no owner running each exit non-zero with no child started and no password prompt, and a set E.1a refuses is refused before anyone is asked. Standalone `keypaste run` passes the existing injection and signal gates unchanged. A runner that opens the vault itself, or a check made only against a fake owner, does not pass.

## What changed for users

- **`keypaste run --session <project> -- <command>`** resolves the vault from `--vault` or `KEYPASTE_VAULT`, connects to that vault's endpoint (or the one `--approver` or `KEYPASTE_APPROVER` names), attaches as `keypaste-mcp` does, and says on stderr `keypaste run: asking the keypaste process holding <vault> to release '<project>'; answer in its prompt`. It sends the project, the command as its arguments and the directory it was started in. It never reads a password: `--session` with `--keyfile` is a usage error (exit 1), and `--approver` without `--session` is one too. `KEYPASTE_KEYFILE` is ignored under `--session`, since nothing is unlocked there.
- **The owner asks first.** The app shows a new prompt window, and `keypaste agent` prints a block in its terminal. Each names the project, the variable names, the command and the directory, with no value, and says the command is what the run claims it will start. Approve (armed after a second) or `y` releases the whole set. Deny, Escape, closing the window, Enter (focus starts on Deny), `n`, 45 seconds without an answer, a lock and the runner going away all refuse. The prompt shares the one-at-a-time slot with agents' prompts, so a run while an agent is being asked is refused as busy, and a refused run refuses the same project, command and directory for a minute, even from a new connection.
- **What is refused before anyone is asked:** no connection attached to the current session, another vault, an ended session, a locked vault, a set E.1a refuses (listed per entry exactly as standalone `run` lists it), an unknown project (exit 3), a command that is empty or longer than 4096 characters once joined, and a directory over 1024 characters.
- **What the run prints on a refusal** is `keypaste run: <why>, so nothing was started`, exit 2: "nothing holds <vault> unlocked; unlock it in the keypaste app or start `keypaste agent`", the owner's reason for an attachment it refused, "the person asked said no", "nobody answered the prompt in time", "another request is waiting for an answer", "the same request was refused a moment ago", "the vault was locked before the set was released", or "no answer came in time" when the owner never replied within 70 seconds. The owner's words are sanitized before they reach the terminal.
- **On release** the set goes through the same `Start` as standalone `run`: the PATH warning, 126 and 127, the child's exit code and signal forwarding are unchanged.
- **Unchanged:** `keypaste run` without `--session`, and every existing exit code and signal behaviour.

## Evidence

**Tests:**

- `ApproverEnvProtocolTests`, 12 cases in core: a request round-trips with its command as arguments; a released set round-trips whole and neither reply's description holds a value; a refusal round-trips its problems and words; a set over one frame is encoded as `TooLarge` with no value in the frame; five malformed replies are refused, including a refusal carrying values, a release with problems or no list, and an unknown outcome; three malformed requests are refused.
- `SessionAuthorityEnvTests`, 11 in core, over a real pipe with a real vault. Approve releases the whole set after a prompt naming the project, names, command and directory with no value. Deny releases nothing, and the same run on a new connection is then refused as cooled down, unasked. The timeout, a lock while asked and a runner hanging up each release nothing and withdraw the prompt. No attachment, another vault, an ended session and a locked vault are refused unasked. An unusable set and a command that cannot be shown whole are refused before anyone is asked. A run while an agent is being asked is refused as busy. An owner composed without environments refuses.
- `EnvReleasePromptTests`, 7 cases in core: arguments with spaces or quotes are quoted and an ordinary command, with `\`, `|` and `>`, is not marked altered; a bidi override, a line break and a zero-width space are scrubbed and marked; the project, command and directory rules and the 4096-character edge.
- `RunSessionTests`, 8 cases in the CLI, against a real `ApproverListener` and `SessionAuthority` over the vault: an approved run starts the child with the set, reads no password and prints no value; Deny, the timeout and a prompt that could not be shown start nothing and say why; no owner, a locked owner, an unknown project (exit 3) and an unusable set; the two usage errors.
- `TerminalApprovalChannelTests`, 2 new: the terminal shows the project, names, command and directory and only `y` approves; a line break in a command cannot draw a line of its own under the prompt.
- `DesktopEnvApprovalTests`, 9 cases in the app, through `AppAuthority`'s real endpoint and the prompt window drawn by Skia and clicked: Approve releases the set and the window exposes no value to the automation tree; Deny, Escape, closing, Enter, the timeout and a lock release nothing and take the window down; Approve is not armed for a second; a command of 90 long arguments, 200 names and a 1,000-character directory leave the window and its buttons where an ordinary run puts them.
- The existing `DesktopApprovalTests`, `SessionAuthorityTests`, `ApproverListenerTests`, `ApproverClientTests`, `RunCommandTests` and the MCP tests pass unchanged apart from the fakes gaining the new method.

**Gate**, [verify-run-session.sh](../../scripts/verify-run-session.sh), new, in the `desktop` profile and so in `app.yml`. It runs the shipped Release `keypaste`, `Keypaste.AppDriver hold` with the app's prompt window drawn headless and clicked, and `keypaste agent`, with bash as the child printing `DEPLOY_KEY` and `DB_URL` from its own environment. Every run gets the master password on its standard input. No owner: exit non-zero, "nothing holds". With the app: the drawn prompt reads `env-prompt project=ci command=… case-approve directory=…project-e1c keys=DB_URL,DEPLOY_KEY`, and Approve prints both values from the child. Deny, a lock while the prompt waits and the 45-second timeout each exit non-zero with no child. A set holding `BAD-NAME` is refused naming it, with no prompt drawn. With `keypaste agent`: `y` prints the set and `n` refuses. Neither the runner's stderr nor the app's output nor the agent's terminal holds a value, and the runner's stderr never mentions a password. It passed first time on Windows 10 in 54 s.

**Mutations**, each restored afterwards:

| Mutation | Result |
|---|---|
| The owner resolves with no confirmation | 6 of 11 `SessionAuthorityEnvTests` fail |
| The owner admits a request from any connection while a session is live | 1 core test fails |
| The runner opens the vault itself after any refusal | 6 of 8 `RunSessionTests` fail, and the gate fails at "no owner: the run exited 0", because the password on its standard input opened the vault |
| The prompt keeps line breaks in the command | 1 core test and 1 terminal test fail |

**Verification:** `./scripts/verify.ps1` on the finished code and documents first failed in backend on the naming rule: private constants in three new test files lacked the `_` prefix. Workflows, scripts and desktop, including the new gate, passed in that run. After the constants were renamed, the run resumed with `--from backend` and passed backend in 82 s, integration in 118 s and desktop in 298 s. Backend ran 1,852 tests, 10 of them skipped on Windows, and none failed. Desktop ran 531 app tests and 41 consistency tests, none failing, then `verify-run-session.sh`. Integration ran the unchanged `verify-run-injection.sh`; `verify-run-signals.sh` does not run on Windows and comes from CI. `compat` was not run: this step changes no KDBX read or write. macOS and Linux runs come from CI.

## Decisions

[DECISIONS](../../DECISIONS.md) holds D-0341, and [THREATS](../../THREATS.md) T-30 describes what a run's prompt protects against. Nothing else binds only this step's code.

## Limits and follow-ups

- **Not audited.** A session release writes no audit line. The audit log belongs to `keypaste-mcp` (D-0020), and neither standalone `run` nor the app's launches are audited. An agent with a shell can start a run, and its only record is the prompt it raised.
- **The command is the run's claim.** A program running as the user can show one command and start another. The prompt protects against a run the person can see is wrong (T-30).
- **One frame.** A set larger than 64 KiB once encoded is refused whole as too large; standalone `keypaste run` still starts it.
- **Agent Activity** lists agents' requests only. A run's prompt is in its own window and is not listed there, though it holds the slot an agent's request would take.
- **The copied command.** Env Sets still copies `keypaste run <project> -- `, without `--session`.
- **The real app was not clicked through.** [desktop.md](../desktop.md#checking-a-build-by-hand) item 51 was not run in this session, and no real Linux desktop observed the prompt window; the gate draws it headless.
- **Ctrl+C while waiting** ends the runner by the default handler, and the closed pipe withdraws the prompt at the owner, as `SessionAuthorityEnvTests` shows for a runner that gives up. This was not observed with a real console signal.
