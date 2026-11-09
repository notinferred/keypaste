# F.80 — Let a request wait its turn

Completed 2026-11-08 on `f76-picker`, source only and not merged, with F.76 to F.79. Runs on Linux at `dff90d3b`: ci fast tier 42542381818, dev desktop 42542943103 and dev desktop-gates 42542974487, all green at the first attempt.

## Amendments

The founder settled the bound on 2026-11-08: three requests may wait for each agent behind the prompt on screen; each prompt's window starts when it shows, not when it arrives; and an agent's parallel calls through one bridge wait too, not only separate bridges.

## Judgment calls

- **An agent is its bridge's label, or its connection** when it gives none, the same key its always answers use.
- **Only agents' requests wait**: a credential request, a picker, a save and an agent's run. A terminal's edit, a pairing and `keypaste run --session` still take the one prompt now or are refused as busy, as nothing asks them to wait.
- **A request is checked against the refusal cooldown again** when its turn comes, so a person who refused the one in front is not shown the same request again.
- **The bridge opens up to four connections**, one exchange each, the one in front and the three waiting, and refuses a call beyond them as busy at once; a connection stays open for the next exchange once its own ends.
- **The bridge withdraws a credential request or a run after 235 seconds**, four of the longest windows and 15, so a request at the back of its agent's queue is not withdrawn while the prompts ahead of it are answered; a request behind other agents' prompts as well can still be.
- **`BUSY` now says** the agent has as many requests waiting as keypaste lets one have.

## Evidence

Run locally, each class or project once: Keypaste.Core.Tests, the whole project, 4165 passed, with `WaitingTurnTests` (6, new) for two requests prompted in order, a withdrawn one leaving the queue unshown, a prompt timing out and handing the turn on, a lock emptying the queue, the bound per agent and a request naming no agent refused, and a refusal holding back the same request waiting behind it; Keypaste.Cli.Tests, the whole project, 952 passed; Keypaste.Mcp.Tests `ConcurrentRequestsTests`, rewritten for a second call reaching the owner and answered, a listing answered beside a prompt, and every lane busy refusing the next at once and coming back; Keypaste.App.Tests `DesktopApprovalTests`, with a second request waiting its turn behind the window on screen. `bash scripts/verify.sh compile` passed.

## Limits and follow-ups

- **The Agents screen shows the prompt on screen**, not how many requests wait behind it.
- **A request waiting behind other agents' prompts** can reach the bridge's 235 seconds and be withdrawn, which the agent reads as a failed exchange.
