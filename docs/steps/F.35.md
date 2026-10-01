# F.35 — Answer what a lock or quit owes before the endpoint stops

Completed 2026-10-01 on `task/f35` above `ccb7cc1`, source only; this record also closes F.29 and F.33. The probe measured in dev runs 36866319873, 36866352048 and 36866382820 at `bd874eb` and ran the repair in 36867342409, 36867688304 and 36867715659 at `5648535`; dev run 36868273325 failed the regression without the repair at `4a91ccd`; dev runs 36871700715 and 36871786212 passed this commit's code at `a4e0313` and `737cd7d`, which differ from it in this record, D-0392's wording, the comment on the wait, and the name and disposal of the test of a client that neither reads nor hangs up. final dev runs 36883702296 and 36883790010 at `be26d84`; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners).

## Amendments

- F.35 was a diagnosis whose repair was to be a later row. The founder directed on 2026-09-30 that the rows left open that day be finished with no new steps, so this step repairs the cause it measured, and F.29 and F.33 close here as two outcomes of that cause.
- The probe ran at `bd874eb`, and with the repair at `5648535`, in commits squashed out of this one, and was deleted once the diagnosis closed; its results are below.

## Evidence

**What the grace cut off.** `SessionHost`'s hosted endpoint stopped by cancelling its listener, waiting at most two seconds for the listener's run, then disposing its spare server instance, the approval gate and the audit log and returning from `Lock`. The probe noted every step of every stop in the desktop suite against its pipe, under the identical `--target app` command, and added two arranged cases: a prompt that comes down 2.5 s after its withdrawal, four times per leg, and the pool held across one stop of each of the three cases the rows cite, by queueing twice its threads plus eight items that each sleep three seconds.

| At `bd874eb` | ubuntu-24.04, three legs | macos-15 | windows-2025 |
|---|---|---|---|
| Prompt down 2.5 s after the lock, 4 per leg | `Lock` returned at 2.0 s; reply null; the old endpoint accepted a connection | the same, at 2.0 to 2.1 s | `Lock` at 2.0 s; reply null; connection refused |
| Pool held, lock, quit and closed connection | the wait timed out at 2.0 s in all 9 and the endpoint accepted; the lock's and the quit's replies were `vault-locked` | the same in all 3 | the wait finished in 0 to 684 ms; refused |

Each null reply has one trace: the withdrawn prompt returned after the grace, `ApprovalGate.AskThroughAsync` released its slot on the semaphore the stop had disposed, the `ObjectDisposedException` ended `ServeAsync`, and the connection closed with no reply, which the client reads as the end of the stream. Each accepted connection had a server instance still undisposed when `Lock` returned: the one serving the request, or the one accepting, whose cancellation had not yet run. .NET's Unix pipe server shares one listening socket among a name's instances and closes it with the last of them, so the endpoint kept accepting; on Windows only a listening instance accepts, and the loop had disposed it.

**Why the wait ran out.** The wait lasts as long as the thread pool takes to run the listener's cancellations. In the 714 other stops of the three Linux legs, no wait finished later than 936 ms, and in 11 of the 12 slowest, from 124 to 936 ms, an item queued on the pool as the stop began ran within 5 ms of the wait's end. Windows' slowest were 760 and 687 ms, with that item not yet run; macOS's was 265 ms. The three failing runs took 2.16, 2.19 and 2.22 s, the two-second grace and the test's remaining work, so in each the pool ran the listener's continuations later than two seconds: in dev run 36726998011 and app run 36728150916 before the denial was computed, giving F.35's and F.29's null replies, and in dev run 36726742125 before the accepting instance was disposed, giving F.33's accepted connection. With the repair, two stops waited 1.12 and 1.15 s, each as long as such an item took to run. No natural stall over two seconds recurred under the probe.

**Repair.** `Hosted.Dispose` waits for the listener's run with no time limit and then disposes what it owns (D-0392). The stop token ends every read and each delivery keeps its one-second bound (D-0315), so the wait lasts as long as the answers owed take, provided every handler finishes once withdrawn or stopped, and every server instance has closed before the lock that ended the session returns. A second `Lock` that finds the vault already closed by one still stopping returns at once. `keypaste agent` already waited this way.

- `LockBoundaryTests.A_lock_answers_a_request_whose_prompt_comes_down_late_before_the_endpoint_stops` has a prompt come down three seconds after its withdrawal and requires a `vault-locked` reply and a refused connection after `Lock`. In dev run 36868273325 at `4a91ccd`, these tests over `main`'s `SessionHost`, it failed at line 84 because the old endpoint accepted, and the class's other 8 passed.
- `LockBoundaryTests.A_lock_does_not_wait_for_a_client_that_neither_reads_nor_hangs_up` asks over a raw pipe that does neither, and requires `Lock` to return within ten seconds, failing rather than hanging when it does not, and the endpoint to refuse. It holds that the stop ends the connection's read; it passes without the repair too.
- With the repair and the probe, at `5648535`, in five legs, three on ubuntu-24.04 in dev runs 36867342409, 36867688304 and 36867715659 and one each on windows-2025 and macos-15 in the latter two: the late prompt got `vault-locked` 20 of 20, with the endpoint refusing and `Lock` taking 2.5 to 2.7 s; with the pool held, every case answered `vault-locked` and refused, `Lock` taking 3.2 to 18.5 s on Linux and macOS and 50 to 832 ms on Windows; none of the 1,220 stops timed out. The three tests the rows cite and 50 repetitions of each of their cases passed in every leg, the slowest a quit whose stop waited 1.12 s for the pool and answered `vault-locked`.
- Without the probe, dev run 36871700715 at `a4e0313` passed `Keypaste.App.Tests`, 860 with 4 skipped on ubuntu-24.04 and macos-15 and 861 with 3 skipped on windows-2025, and the 44 consistency tests on all three, and the nine desktop process gates on Linux and Windows; on macOS the first gate stopped under the runner's bash 3.2, which is F.40. Dev run 36871786212 at `737cd7d` passed the same on ubuntu-24.04. Both left `appcompat`, `markers` and `package` to `app.yml`.

## Decisions

D-0392.

## Limits and follow-ups

- A lock or quit now lasts as long as the pool takes to deliver what it owes, and the app's window does not respond meanwhile. With the pool held, one took 18.5 s; under the suite's own load the slowest took 1.15 s.
- A handler that ignored its withdrawal would now hold the lock rather than lose its answer; the app's prompt window acknowledges a withdrawal as it happens. N.11 would put saves on this path, and `_saveGate.Wait()` cannot be cancelled, so a lock would wait for a save already queued there.
- No test blocks a write at a lock: a denial fits every platform's pipe buffer, and a larger reply is cut by D-0315's bound.
- No CHANGELOG entry: the desktop session is unreleased, its Unreleased entry already says a lock answers a waiting request as `vault-locked`, and a lock that freezes the window needs a starved pool.
