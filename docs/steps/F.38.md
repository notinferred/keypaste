# F.38 — Reopen the running app instead of starting a second one

Completed 2026-10-01 on `task/f38` above `ccb7cc1`, source only; after the review, dev run 36898158867 passed every lane the change selects on Windows at `bba53ec`, gates included, and 36890502653 on Linux and 36890506526 the Core and App tests on macOS at `507961d`, which differs from `bba53ec` only in the time the acknowledgement's write is given; the squashed commit is `bba53ec`'s tree with this record's runs filled in; final dev runs 36904588065 and 36904592702 at `5377d8d`; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners).

## Amendments

The row as selected is in Git at `ccb7cc1`. On 2026-09-30 the founder directed that rows left open that day be finished with no new steps; each choice below fills what the row left open, and the last five are the review's findings, fixed as directed on 2026-10-01.

- **Every platform.** `Program.Run` takes the claim on macOS too, where a reopen through Launch Services never starts a second process; a copy started directly, or by the LaunchAgent while the app already runs, now hands its start over as on Windows and Linux.
- **The process check is that the running app lives on and each second start exits by itself**, since no desktop gate has a display. The running app is `Keypaste.AppDriver launch`, which runs `keypaste-app`'s own `Program.Run` and `App.Launch` on a headless display, and the second starts are the shipped `keypaste-app`, which exits before Avalonia starts; one that kept running would meet the gate's 60-second limit and fail it. The review dropped the gate's process counts, which ran after the second start had exited.
- **A second start waits for an answer.** The running app writes one byte back only after its window is shown on its own thread, and stops answering as soon as it begins to quit, so a start during quitting or a hang is not told it was shown. Until answered, the start retries the claim and runs as the app once the first has quit; after ten seconds unanswered, or at once when the runtime refuses the endpoint, it writes that keypaste is already running and exits 1. A home where `app.lock` cannot be written at all gives a claim that holds nothing, so the app starts there as every start did before this step, and its unlock then fails to take the vault's claim and says why.
- **Socket errors.** A squatted Unix socket file, which another user can leave under the default umask, made a second start's connect throw `SocketException`, which crashed it, and made the listener's bind fault unseen. Both catch it; the listener writes why no start can reach it on standard error, and T-29 says a refused start fails at once.
- **Identification only.** Both pipe clients, the app's and `ApproverClient`'s, open the pipe at `TokenImpersonationLevel.Identification`, so on Windows the pipe's creator cannot act as the user who connects; T-10 says so.
- **Foreground.** On Windows the second start calls `AllowSetForegroundWindow(ASFW_ANY)` before it connects, so the window the running app shows comes to the front.
- **Standard error only.** Both messages reach standard error, which a launch from a desktop shell does not show; T-29 states it, which is smaller than a dialog, since Avalonia has not started at that point.
- **T-10 corrected**: T-10 and `ApproverEndpoint`'s comment said .NET creates the Unix socket owner-only, but .NET 10's `NamedPipeServerStream.Unix.cs` sets no mode on it, so the umask decides. T-10 is rewritten whole to rest the restriction on `CurrentUserOnly`'s peer-user check on accept and on connect, which the app's pipe relies on too, and the comment says the same.

## Evidence

- `AppClaimTests` (10), in Core.Tests: a second claim on one home is refused until the first is released; two homes do not contend and have their own endpoints; a holder that is not listening answers no start; a start is answered only after the holder's show has run; a holder whose show never completes is not taken for an answer; a holder that quits while a start waits leaves it the home well inside the wait; 64 KiB a peer writes is never read and the next start is still answered; a stopped listener leaves nothing answering; a name taken first, a directory at the socket path on Unix and a pipe made first on Windows, is reported by the listener; on Linux and macOS a socket file with no permissions ends a start at once.
- `SecondStartTests` (6), in App.Tests, with the tray on and the app launched at login by `App.Launch` into Avalonia's lifetime holding the claim: `Program.Run` with no arguments, off the UI thread, returns 0 with the window already shown as the lifetime's main window and one request made; with `--background` it returns 0, the window stays hidden and no request arrives, and a person's start afterwards still shows it; after the tray's Quit a start is not answered; a holder that never answers, or a squatted socket file on Linux and macOS, gets exit 1 and the message, the latter at once; a holder that quits while a start waits leaves the home to that start well inside the wait. Each second start is given a start that fails the test if it is ever asked to run an app.
- `scripts/verify-session-lifecycle.sh` gained the process check, after its 4.4b checks, in a home of its own whose `app.toml` turns the tray on: the driver's `launch --background` prints its process and endpoint and `window hidden`; the shipped `keypaste-app --background` exits 0 and two seconds later the window is still hidden; the shipped `keypaste-app` exits 0 with `window shown` already printed and the first still running; a killed app leaves the home, and on Linux its socket file, to a new `launch --background`, which a second start then reaches in the same way before it shuts down when its input closes.

Dev runs, each a dispatch of `dev.yml` with `dev.sh`'s inputs, `auto` selecting every backend and desktop project and the integration and compat gates:

| Target, runners, commit | Run | Result |
|---|---|---|
| `auto`, Linux and Windows, `8492fb7` | 36866683886 | Build refused CA2000 in the claim's accept loop and in `AppClaimTests`; Windows cancelled by the next dispatch |
| `auto`, Linux and Windows, `56f4dd6` | 36867278594 | Build refused CA2000 in the loop again; Windows cancelled by the next dispatch |
| `auto`, Linux and Windows, `513808f` | 36867573284 | Windows green: backend 3,260 with 3,249 passed and 11 skipped, App 866 with 863 and 3, consistency 44, every desktop gate with the new check, integration and compat. Linux passed backend 3,258, App 866 and consistency 44 and failed the new check, whose fixture wrote `stay_in_tray` outside `[[settings]]`, which `app.toml` ignores, so the tray was off by Linux's default and the start at login showed its window |
| `auto`, Linux, `04f6aba` | 36872078213 | Green: backend 3,258 with 3,239 passed and 19 skipped, App 866 with 862 and 4, consistency 44, every desktop gate with the new check, integration and compat |
| `core,app`, macOS, `04f6aba` | 36872082087 | Green: Core 2,325 with 2,310 passed and 15 skipped, App 866 with 862 and 4 |
| `auto`, Linux and Windows, `e498860`, squashed | 36875138373 | Green on both, with the counts above for each |
| `core,app`, macOS, `e498860`, squashed | 36875142896 | Green, with the counts above |
| `auto`, Linux and Windows, `507961d`, after review | 36890502653 | Linux green: backend 3,262 with 3,243 passed and 19 skipped, App 868 with 864 and 4, consistency 44, every desktop gate, integration and compat. Windows reached `dev.yml`'s 50-minute limit in Core.Tests, where the acknowledgement written to the peer that writes and never reads waited on a Windows pipe for its reader; that write is now given a second |
| `core,app`, macOS, `507961d` | 36890506526 | Green: Core 2,329 with 2,314 passed and 15 skipped, App 868 with 864 and 4 |
| `auto`, Windows, `bba53ec` | 36898158867 | Green: backend 3,264 with 3,252 passed and 12 skipped, App 868 with 864 and 4, consistency 44, every desktop gate, integration and compat |

## Decisions

D-0397.

## Limits and follow-ups

- **No native display observed.** How the window comes up from the Start menu, a Linux launcher or the Dock is R.1a's manual check.
- **Another `KEYPASTE_HOME` is another app**, and runs beside this one, as its claims already did (T-29).
- **Squatting is denial of service only.** Another local user who takes the pipe name or the Unix socket path first leaves the app with no endpoint; a second start then exits 1, at once or after ten seconds, with its message on standard error only, and the tray or menu bar icon still opens the window (T-29).
- **Cross-user refusal is the runtime's**, as for the approver pipe, and is not tested.
