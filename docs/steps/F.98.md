# F.98 — KeePassXC's own browser server answers its pairing after keypaste's saves

Completed 2026-11-09 on `backlog-2026-11`, source only; the gate has not run, so there are no run IDs and the question stays open in BACKLOG until it passes.

## Amendments

The founder selected BACKLOG's browser-pairing row for one CI check. A real `associate` needs a person's confirmation in KeePassXC's window, so the check presents the pairing the seed already holds and proves KeePassXC reads it, rather than making a new one.

## Evidence

The check that left 8.1d returns to `verify-keepassxc-workflows.sh`, in app's `keepassxc workflows` jobs on `ubuntu-24.04` (apt's 2.7.6) and `windows-2025` (the pinned 2.8.0). KeePassXC opens the vault unlocked in its window with browser integration on, and `tests/Keypaste.FakeBrowser` asks through `keepassxc-proxy`: `change-public-keys`, `get-databasehash`, then `test-associate` for the seeded pairing, which must succeed, and for another identification key, which must be refused with error 8. It does this first on a vault `keepassxc-cli` made that keypaste never touched, then on the vault keypaste's CLI and app saved; the two database hashes must match, and differ from keypaste's own.

8.1d's Windows failure was the fake browser's: it sent a new `clientID` with each message and KeePassXC keeps a client's session keys under its `clientID` (`BrowserService::processClientMessage`), so every message after the key exchange could not be opened, error 4, whatever vault was open. It now keeps one for the connection, as the extension does. 8.1d's Linux segfault under Qt's offscreen platform came before any answer and the source does not explain it, so the Linux leg runs under Xvfb.

Here, on macOS, only: `verify.sh compile`, `bash -n` under bash 3.2, and the new function run with KeePassXC and the fake browser stubbed, through its answered, not-refused, exited and silent paths. That exercises the script's flow, and no KeePassXC.

## Limits and follow-ups

It does not show a new `associate` through KeePassXC's dialog, a page's logins being filled, or a version other than these two. KeePassXC's offscreen segfault stays unexplained; if the check fails on the control vault the method is at fault and it proves nothing about keypaste.
