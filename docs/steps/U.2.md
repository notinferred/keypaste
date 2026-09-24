# U.2 — Enforce the common lock boundary

Completed 2026-09-23 on `main` above `62eca07`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** in the authority U.1 establishes, one lock transition serves a manual lock, the idle timeout, an expired sleep/resume and shutdown. It cancels every pending approval, clears cached and policy-derived grants, and refuses any read, release or launch that has not already delivered, including one racing the lock. Agent requests are not activity for the idle deadline. A later unlock starts with no request, grant or capability from before it. Values already delivered stay outside control, and the guides say so. Traces to PRODUCT §2.

**Verify (V-U.2):** with a real `keypaste-mcp` request pending at the owning session, each kind of lock denies it and the audit log records the denial. A grant and a matching policy rule in force before a lock release nothing after re-unlock until asked again. A request arriving during the lock transition is refused rather than answered from the old vault. A steady stream of agent requests does not move the idle lock. A lock flag checked in a view model, or a lock taken before any request exists, does not pass.

The founder approved the plan with two choices, recorded here because the Build text left them open:

- **The agent's lock is shutdown only.** `keypaste agent` gets no idle lock. Ctrl+C, SIGTERM and SIGHUP take the common transition.
- **A driver-held prompt stands in for approval in the app.** Until 4.4 the app refuses every credential request at once, so no request can wait at it. `SessionHost` now takes an approval channel. The app passes the one with nowhere to ask, so D-0311 stands. `Keypaste.AppDriver hold --held-prompt` passes one that never answers, so a real request waits at the real app session while the gate locks it.

## What changed for users

Every lock of the process holding a vault is now one transition (D-0313). On the desktop that means:

- manual lock, idle lock, minimize lock and a lock after an access change;
- a vault replaced by another unlock;
- quitting.

For `keypaste agent` it means Ctrl+C, SIGTERM and closing its terminal.

The transition ends the unlock's lifetime before the vault is disposed. A request waiting for a person is withdrawn, and the agent's terminal prints its "withdrawn before you answered" notice. The bridge receives a `vault-locked` denial naming the session it reached, and audits it as such. Grants belong to the lifetime and are zeroed with it. A release that had not committed when the lock came is refused, whether it was prompted, from a grant or from a policy rule, and its value is never sent. The next unlock is a new lifetime with no grant, cooldown or waiting request from before.

Before this step, a lock stopped the app's listener and cancelled the connection. The bridge got no reply and audited whatever its retry met. The agent's Ctrl+C stopped the listener without denying anything waiting, and SIGTERM killed it outright.

An agent's request is not activity: it never moves the idle deadline. The app now checks that deadline on every agent request. After the machine slept past the timeout, a request that arrives before anybody activates the window is refused as locked, and it locks the app. Activation and input still re-check the deadline as before. There is still no platform power hook.

Quitting the app locks the session before its endpoint stops, so a waiting request is answered as a lock rather than dropped.

The listener now delivers a reply it has already computed within one second, even when it is stopping, so a lock's denial reaches the bridge.

Values already delivered to a client, a child process or a clipboard stay outside keypaste's control. FEATURES, the desktop guide and THREATS say so.

## Evidence

**Tests**, over real pipes:

- `SessionLifetimeTests`, 4: ending a lifetime refuses commits, cancels `Ended` and zeroes the grants it owns; ending twice is harmless; what an ended lifetime is given keeps nothing; each lifetime has its own session.
- `SessionAuthorityTests`, 5 new, a real listener and client in front of the core handler:
  - A request waiting at a held prompt is withdrawn and answered `vault-locked` naming its session, with nothing read.
  - An approval whose read is interrupted by the lock releases nothing and leaves no grant.
  - A policy release racing the lock is refused, with nobody asked.
  - A grant given before a lock is gone afterwards, and the same request is asked again under the next session.
  - A listing racing the lock is refused.

  The existing nine were adapted from a session string to a lifetime and still pass.
- `LockBoundaryTests`, 8, a real `AppVaultSession` on a manual clock, a real `SessionHost` and a real pipe:
  - Manual, minimize, idle and shutdown each deny a waiting request as `vault-locked` and withdraw its prompt.
  - A request after an expired sleep is refused, nobody is asked, and the app locks.
  - Four minutes of listings and requests do not stop the five-minute idle lock.
  - A re-unlock asks again where a grant had been reused.
  - An ended lifetime cannot reach the vault a later unlock opened.
- `ApproverFixture`'s fakes gained a held prompt and a hook that runs inside a read or a listing. The MCP tests' owner now holds a lifetime.

**Gate**, [verify-lock-boundary.sh](../../scripts/verify-lock-boundary.sh), shipped Release `keypaste` and `keypaste-mcp` against `Keypaste.AppDriver hold --held-prompt`:

1. A `request_credential` waits at the app's session. `lock` makes the client's result an error with no value. The audit line is `denied`, `vault-locked`, naming the app's session, and the driver reports the prompt withdrawn.
2. After `unlock`, the same request waits again under a new session. Closing the driver's input, which is quitting, answers it the same way under that session.
3. Where a signal reaches a native process, `keypaste agent` is sent SIGTERM with a request waiting at its prompt. The request is audited as a `vault-locked` denial of the agent's session, and the agent prints the withdrawal and its closing line and exits 0.

The gate passed on Windows 10 in about 5 s, three consecutive times after the final build, with step 3 skipped and logged. Git Bash cannot deliver a signal to a native Windows process. The whole gate, step 3 included, passed on Linux in the `scripts/container` Ubuntu 24.04 image, with non-AOT self-contained linux-x64 builds of this tree. It runs in the `desktop` profile, which `app.yml` runs on `ubuntu-24.04`. [verify-session-authority.sh](../../scripts/verify-session-authority.sh) still passes.

**Mutations**, each restored afterwards:

| Mutation | Result |
|---|---|
| The authority skips the commit check | 3 `SessionAuthorityTests` fail: the waiting request, the approval racing the lock and the policy release racing it |
| The handler's token is not linked to the lifetime's end | `ARequestWaitingForAPerson…` fails. The app tests still pass, because the app also stops its listener on lock, which withdraws the request through the connection |
| `Lock` never ends the lifetime | The 4 lock kinds in `LockBoundaryTests` and the ended-lifetime test fail |
| Reading the lifetime for a request counts as activity | `Agent_requests_do_not_move_the_idle_deadline` fails |
| The lifetime is served past the idle deadline | `A_request_after_sleeping_past_the_deadline…` fails |
| The listener writes a reply on its stop token | The gate fails at the manual lock, 5 of 5 runs: the waiting request is not audited as `vault-locked` |

The plan named "`Lock` ends the lifetime after disposing the vault" as a mutation. No test tells that order apart, because nothing reads the vault between the two calls. "`Lock` never ends the lifetime" was run instead.

The first mutation pass restored each file with an older timestamp than its mutated build. Incremental builds then kept one mutation in the next. The script now touches what it restores, and every result above comes from the rerun. One stale-binary gate run that failed unmutated was diagnosed the same way, and it passed after a rebuild.

**Verification:** `./scripts/verify.ps1` on the finished tree passed workflows and scripts. It then failed backend and desktop at `dotnet format`: a private constant lacked the `_` prefix. After the rename, `--from backend` passed backend, integration and desktop. Backend ran 1,682 tests with 10 skipped on Windows, and desktop ran 462 app tests and 40 consistency tests, none failing. The desktop profile ran both session gates. `compat` needs KeePassXC and was not run; this step changes no vault write.

## Decisions

[DECISIONS](../../DECISIONS.md) holds D-0313. These rows bind only this step's code:

| id | date | decision | supersedes |
|---|---|---|---|
| D-0314 | 2026-09-23 | The app honours an idle deadline that has passed when an agent's request reads its lifetime: the request is refused as locked and the lock runs on the thread pool, not the listener's thread; there is no platform sleep/resume hook | — |
| D-0315 | 2026-09-23 | A release commits under the lock that ends its lifetime; one that committed before the lock is delivered, and the listener delivers a computed reply within one second even while stopping | — |
| D-0316 | 2026-09-23 | `SessionHost` takes an approval channel per unlock; the shipped app passes the one with nowhere to ask (D-0311), and only `Keypaste.AppDriver hold --held-prompt` passes another | — |

## Limits and follow-ups

The linearization point is the commit check, not the pipe write. A release that committed a moment before the lock is still delivered. That counts as "already delivered", and nothing can recall it once sent.

A read can still happen for a request the lock is withdrawing: its token is checked just before reading, and the commit check then drops the value. What the lock guarantees is that nothing is released.

`keypaste agent` still has no idle lock, by founder choice. Its SIGTERM step is exercised on Linux only. On Windows, Ctrl+C and console close go through the same registration, but no gate delivers them.

The app still answers from the snapshot it opened (U.3), and has no approval of its own (4.4). The status read from the authority and crash takeover are 4.4b's. Launches through the session do not exist yet. E.1a is expected to commit through the same lifetime.

From reading `TerminalApprovalChannel`, not reproduced in this step: standard input closing at the agent's prompt is answered as a person's denial. It is audited as `prompt` and starts the refusal cooldown. The gate keeps the agent's input open, so it does not meet this.
