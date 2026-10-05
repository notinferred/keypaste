# F.49 — Keep a grant when its bridge reads a large listing slowly

Completed 2026-10-05 at `f97ad6f` on `task/f49`. Runs: ci 37387017401 and app 37387020501, dispatched so every job ran. The regression's runs are in the table below.

## Amendments

The row was a discovery. At the founder's direction of 2026-10-05 its repair follows in the same step, as F.44's did, once the regression had reproduced the mechanism.

## Evidence

**Observation.** Dev run 37337588072 at `30e338f`, job `test (windows-2025)`: `LargeVaultListingTests.OnALargeVault_AGrantSurvivesAListing` recorded `prompt` where `grant-cache` was expected, so the human was asked twice.

**Cause.**
- Since U.2 (`d285bb8`, D-0315), `ApproverListener.ServeAsync` gave every reply write one second.
- A full listing frame (about 64 KiB) waits on its reader wherever the pipe holds less than a frame: a Windows named pipe, and on macOS a Unix socket's small send buffer. A bridge reading slowly under load had the write cancelled.
- The listener then ended the connection, `Disconnected` revoked its grants (`GrantCache` is keyed by connection id), and the bridge reconnected and retried the listing (`ApproverConnection`'s single retry).
- The next request arrived on the new connection with no grant.
- Linux's socket buffer takes the whole frame, so the write never waits there. v0.3.0 had no bound, so no published release has this defect.

**Repair.** A delivery has no time limit until the listener's stop token fires, and from then on it has one second, so a lock or quit still ends (D-0392). A write that begins after the stop gets its own second, as before, so withdrawn requests still deliver their denials (D-0313).

| Run | Commit | Result | What it showed |
|---|---|---|---|
| 37385839471 | `d7adfc6` | failed, as intended | `AHugeListing_ReadSlowly_IsDeliveredAndTheConnectionSurvives` read no frame (`Assert.NotNull`, `ApproverListenerTests.cs:224`) on windows-2025 and macos-15, after waiting 1.5 s to read; passed on ubuntu-24.04 |
| 37385839471 | `d7adfc6` | passed | `AReplyNobodyReads_EndsOneSecondAfterTheListenerStops` on all three: a never-read reply did not hold the listener's stop |
| 37386590950 | `fa3efde` | passed | the repair: both tests and the rest of `ApproverListenerTests` on all three runners |
| 37387017401, 37387020501 | `f97ad6f` | passed | every job, including `LargeVaultListingTests`, the lock and lifecycle gates, and F.35's stop tests |

The second test is the repair's control: a delivery with no bound at all would hold the stop, and the test waits ten seconds before failing.

## Decisions

D-0420 replaces D-0315's bound on every delivery with a bound that starts at the stop. D-0392 now cites it.

## Limits and follow-ups

- Grants stay scoped to the connection, because a pipe cannot prove that a reconnecting peer is the same process. A real reconnect still asks again, which is the safe direction.
- `ServeAsync` still swallows what ends a connection, so a future cut leaves no trace beyond its effect.
- `AppClaim`'s answer keeps its one-second bound: it is a few bytes and never waits on a reader.
