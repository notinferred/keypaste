# F.98 — KeePassXC's own browser server answers its pairing after keypaste's saves

Completed 2026-11-09 on `backlog-2026-11` and repaired 2026-11-10 on `backlog-ci`, whose gate ran at `72936674`; runs app 42819316329 (jobs `keepassxc workflows` on `ubuntu-24.04` and `windows-2025`, both green), dev 42810833630, 42811913260, 42812628953, 42813322153, 42814922694 and 42816855771 (probes on that branch, removed), and app 42727503590 (the failure repaired).

## Amendments

The founder selected BACKLOG's browser-pairing row for one CI check. A real `associate` needs a person's confirmation in KeePassXC's window, so the check presents the pairing the seed already holds and proves KeePassXC reads it, rather than making a new one.

After app 42727503590's Linux job failed on the control vault, the founder's item 14 had the method diagnosed and fixed, with at most three dispatches of `app.yml`; two were used.

## Evidence

The check that left 8.1d returns to `verify-keepassxc-workflows.sh`, in app's `keepassxc workflows` jobs on `ubuntu-24.04` (apt's 2.7.6) and `windows-2025` (the pinned 2.8.0). KeePassXC opens the vault unlocked in its window with browser integration on, and `tests/Keypaste.FakeBrowser` asks through `keepassxc-proxy`: `change-public-keys`, `get-databasehash`, then `test-associate` for the seeded pairing, which must succeed, and for another identification key, which must be refused with error 8. It does this first on a vault `keepassxc-cli` made that keypaste never touched, then on the vault keypaste's CLI and app saved; the two database hashes must match, and differ from keypaste's own. In app 42819316329 KeePassXC answered `kp81d-keepassxc` on both vaults under one hash and refused another key with error 8, on both runners.

8.1d's Windows failure was the fake browser's: it sent a new `clientID` with each message and KeePassXC keeps a client's session keys under its `clientID` (`BrowserService::processClientMessage`), so every message after the key exchange could not be opened, error 4, whatever vault was open. It now keeps one for the connection, as the extension does.

The Linux leg had three separate faults in the method, none in keypaste:

- **The socket's path was too long.** The gate pointed `XDG_RUNTIME_DIR` under the checkout, and 2.7.6 puts its socket at `$XDG_RUNTIME_DIR/app/org.keepassxc.KeePassXC/org.keepassxc.KeePassXC.BrowserServer` (`BrowserShared::localServerPath`), 148 bytes there against a Unix socket's limit of 107. KeePassXC could not listen, and `keepassxc-proxy` drops what it cannot send, so in dev 42814922694 every start that lived still gave the fake browser no answer within 8 s. The runtime directory is now a short `mktemp -d` under `/tmp`. With it, every live start answered (dev 42816855771, 17 of 17).
- **2.7.6 segfaults in some starts, with browser integration on.** A core dump (dev 42813322153, KeePassXC's own core-dump block shimmed off in the probe) shows it in `QWidgetPrivate::showChildren` on a dead child, under `MainWindow::bringToFront` at `main.cpp:209`, before the event loop runs and so before it reads any browser message. Rates: 6 of 12 starts with browser integration on and the long path (dev 42814922694, on the full seed and a one-entry vault, under Xvfb and offscreen), 3 of 20 with the short path and 0 of 10 with browser integration off (dev 42816855771). What frees the child is not identified, and D-Bus, a window manager, the packages `--no-install-recommends` leaves out and `--pw-stdin` were not varied. app 42727503590's exit 139 is this crash on the same line of the gate; 8.1d's under offscreen fits it but left no core. The gate now starts KeePassXC again on exit 139 with no answer, at most five times, and logs each one; any other exit fails as before. What it asserts is unchanged.
- **`get-databasehash` could be answered before the database opened.** The gate required only the pairing's success, so a `Database not opened` hash beside a good pairing passed the loop and failed later (Windows, app 42818276013). The hash must now be present before the attempt counts.

Here, on macOS, only `bash -n`; the runners exercised everything else.

## Limits and follow-ups

It does not show a new `associate` through KeePassXC's dialog, a page's logins being filled, or a version other than these two. In the two dispatched runs the segfault landed on attempt 1 of keypaste's vault and not the control's; the probes and app 42727503590 show it on the control vault and a vault keypaste never saved, so it is KeePassXC's, but a crash confined to keypaste's vault would need five in a row to show. If the check fails on the control vault the method is at fault and it proves nothing about keypaste.
