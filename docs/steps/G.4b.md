# G.4b — Tell the person when a locked app is asked

Completed 2026-10-20 on `main` at `413afc3`, source only: ci 39460137047 and app 39460137950. The step was built directly on `main` alongside G.3, in several commits, each pushed and run; this record and the STEPS, ROADMAP and CHANGELOG edits follow in one more.

## Amendments

The founder selected G.4b on 2026-10-19 with P.3b1 and G.3 and moved all three into 0.5.0. The approved plan settled what the row left open (D-0466):

- **The pipe.** A new same-user pipe per vault, live only while the app runs locked. A connection is the whole request.
- **Which vaults.** The app listens for both the vault its unlock screen shows and the chosen vault.
- **How often.** One notice per locked spell: the next can show only after an unlock and a new lock.
- **What the agent reads.** When the pipe answered, the refusal says the app is open but locked. The request is still denied and recorded `no-approver`.

During the build the founder settled two more points:

- The app follows a vault chosen with `keypaste use` while it stays locked, by watching `app.toml`, rather than reading the choice only at each lock.
- An approver name starting `keypaste-locked-`, in any case, is refused everywhere one is read, so a bridge or a setup pin cannot be pointed at the locked app's pipe.

## What changed

- **Core.** `Ownership/KnockListener.cs` holds the accept loop `AppClaim` already used, and `Ownership/LockedAppEndpoint.cs` listens and knocks. The name is `keypaste-locked-` and the vault's key, whatever `--approver` or `KEYPASTE_APPROVER` says. The bridge connects within 200 ms and hangs up at once. The app uses nothing sent on the pipe, writes nothing, and ends a connection still open after 100 ms.
- **The bridge.** `Mcp/LockedApp.cs` knocks only where an agent is about to be told that nothing holds the vault unlocked: `request_credential`, `run`, `list_entry_names` and an access change only an owner can confirm. Each then reads `ToolText.AppLocked`, `AppLockedForListing` or `AccessAppLocked`. The audit line keeps `denied`, `no-approver` and today's reason.
- **The app.** `Session/LockedNotice.cs` listens while the app holds its home's claim and is locked, for the unlock screen's vault and the chosen vault, and listens anew when either changes. `Views/LockedNoticeWindow.axaml` is topmost and not activated, and has no field. Close is the default and is focused once the person brings it forward. Open keypaste answers after a second, puts the asked vault on the unlock screen and shows the window. An unlock closes the notice and starts a new spell.
- **The driver.** `AppLaunch` prints `locked-notice shown` and `closed`, and presses Open on `notice open`.

## Evidence

ci 39460137047 and app 39460137950, at `413afc3`:

- **Core.** `LockedAppEndpointTests` runs over a real pipe on all three runners. It checks that each vault's name differs from its approver's and the app's, that a bridge reaches the app and is heard, and that with nothing listening it is told no within a second. It also checks that nothing a peer sends is used, that a peer that never hangs up keeps out neither the next bridge nor stopping, that a stopped listener leaves nothing, that a name taken first is reported, and that a refused socket file ends a knock at once.
- **MCP.** `LockedAppTests` covers each of the four texts with the app locked, recorded as before. It also checks that a change for less does not knock, that an answering owner is never knocked, and that another vault gets today's text. `ServerOptionsTests` checks that the name ignores both pins, that there is none without a vault, and that a `keypaste-locked-` approver is refused in any case. `McpClientSetupTests` refuses it as a setup pin.
- **App.** `LockedNoticeTests` checks:
  - one notice with no window and `AppClaim` untouched;
  - only an armed Open shows the unlock screen;
  - no second notice until an unlock and a lock;
  - both vaults are heard, and Open offers the one asked about;
  - a vault chosen while locked replaces the one before;
  - an unlock takes the notice down, and quitting stops listening.

  `LockedNoticeWindowTests`, `PlainWordsTests` and `ScreenRenderer` cover the window.
- **Gate.** `scripts/verify-session-lifecycle.sh` starts the app locked with a remembered vault and a chosen one, then checks:
  - another vault gets today's text and no notice;
  - both of the app's vaults get the locked text and exactly one notice, with no window shown;
  - `notice open` shows the window with that vault and closes the notice;
  - later requests are still refused without another notice.

**What failed on the way, all on `main`:**

- `1746ced`, this step's code, and `84183f7`, G.3's, were pushed together. ci 39423606611 and app 39423606604 at `84183f7` failed to build Core on IDE0055 in G.3's reading of the attach reply, which `930469d` reformatted.
- At `1c60e88`, which added the founder's two follow-ups, ci 39441726536 passed. In app 39441726707, `verify-session-lifecycle.sh` failed on Windows in its run and its rerun: for the vault on the unlock screen, the listing read "the vault is locked". `LockedAppTests` failed the same way on Windows in the real-tools run 39444475403, whose `tools` jobs passed.
  - `ListEntryNamesTool` turns an exception from a listing into that text. A probe on a branch, since deleted, printed it: `InvalidOperationException: The pipe has been disconnected`. It is thrown by the runtime's check, after connecting, of who made the pipe (`ValidateRemotePipeUser`), because the app had hung up as soon as it heard.
  - `244721c` had the bridge hang up first, with the app ending a connection still open after 100 ms.
- At `244721c`, ci 39455038909 and the real-tools run 39455105318 failed `LockedAppEndpointTests.APeerThatNeverHangsUp_HoldsNothingUp` on Linux and macOS. There, a bridge that connects while the app holds a silent peer is queued on the endpoint and dropped when the app makes the endpoint anew, so it was never heard. `8d0e7a1` keeps the wait for the bridge's hang-up to Windows, and elsewhere the app hangs up at once, as before.
- In app 39455039123 at `244721c`, `verify-run-session.sh` printed its `ok` lines on Windows and still exited 1, with nothing on standard error, which its cleanup silences. The only command after those lines that can fail is the removal of its work folder, which on Windows fails while a process just killed still holds a file in it. `413afc3` makes `kill_process` wait, on Windows, up to five seconds for the process to exit.
- At `1c60e88`, `DrawnRevealTests` failed once on Ubuntu for the env surface: while the value was held, the window's automation surface gained a popup layer. Its rerun passed. Diagnosed and fixed: the env cell sits in a panel with a tooltip whose 400 ms show timer starts at the test's hover, so where frames drew slowly it opened before the press; the press closed it, and Avalonia never removes the popup layer it made, which run 39645914815 at `cca84524` shows empty in the held surface alone. The test now turns the cell's tooltip off before its hover, its assertions unchanged (`1b16a9ca`; ci 39785584625, app 39825609666 at `d57aa9b7`).

## Decisions

D-0466 in [DECISIONS](../../DECISIONS.md). T-7, T-10, T-11 and T-29 in [THREATS](../../THREATS.md).

## Limits and follow-ups

- Whether the notice stays above other windows and leaves the keyboard alone on each desktop cannot be seen headless; R.1a checks it by hand.
- A process of the same user that takes the name first makes agents be told the app is locked when it is not (T-29), and a second start by the same user can still show the unlock screen (D-0397).
- The notice opens at the top-right of its screen's working area, where system notifications sit, clear of the unlock form at the centre of the main window; on a screen with little room above that window it can still cover the window's top-right corner (`4f19fbdc`; ci 39850506100, app 39850506098).
- Deleting `app.toml` while the app is locked is not followed until the next lock. Where the system refuses the file watch, such as at Linux's limit on watches, the chosen vault is read again only at the next lock or change of the unlock screen.
- With no app running, each refusal for want of an owner spends up to 200 ms on the knock.
- Knocks in quick succession could go unheard. On Windows, a bridge that hung up before the app waited again was lost. On Unix, making the endpoint anew dropped a bridge queued behind the one being answered. [F.66](F.66.md) found both and fixed them.
- The listing's audit line keeps today's reason while its agent reads the locked text.
- Source only.
