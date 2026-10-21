# N.8 — `keypaste` alone shows where things stand

Completed 2026-10-21, source only; runs RUN_IDS.

## Amendments

The founder approved the design before the step started: on a terminal, bare `keypaste` prints the version, the vault and what chose it, who holds it, the connected AI tools and one next step, exits 0, reads what `keypaste doctor` reads but starts no process, and stays within 80 columns; redirected, it keeps printing the help on stderr with exit 1. Under the founder's decision rule of 2026-10-21, these choices the row and the design left open were taken as recommended (founder-delegated):

- **What is a terminal.** A new `CliContext.StdoutIsTerminal`, `!Console.IsOutputRedirected` in the real program and set by `CliHarness` in tests, decides; `NO_COLOR` and `TERM=dumb` change only the colour.
- **The next step for a vault that is not a KeePass database** is `keypaste use <path>`, as for no vault or a missing one, because doctor's fix for it is the same command.
- **A vault held locked** gets "unlock your vault in the keypaste app" rather than "open the keypaste app, or run `keypaste approve`", which reads wrong when the app is already open.
- **"Something doctor would fix"** means a fix among the three readings status makes: the vault, each AI tool and the owner. A version mismatch, an unreadable settings file and PATH stay doctor's alone, since finding them needs a process started or files status does not read.
- **Wording for states the design did not spell out:** `none chosen` (with `; app.toml could not be read` when it cannot be parsed), `missing`, `not a KeePass database` and `unreadable` after the vault's path; `nothing, as no vault is chosen`, `<holder>, locked`, `a keypaste process holding another vault`, `<holder>, not answering` and `cannot tell: KEYPASTE_APPROVER is not a pipe name keypaste accepts` for who holds it; `none installed`, and an `unknown` group for a tool whose file cannot be read, after the `connected` and `not connected` groups.
- **Width.** A row breaks between its pieces, never inside a tool's name, and continues under its first value; a path longer than the 68 columns left is shortened in the middle around `…`, or `...` where the terminal's encoding cannot carry it.
- **Colour.** Only the row labels (grey) and the next step's `›` (amber) are coloured, as in the help and doctor.
- **"chosen in the app"** names the choice in `app.toml` whether the app or `keypaste use` made it, since both write the one choice the app shows.
- **Reuse.** `OwnerReading.Holder` became public and `DoctorCommand.Shown` internal, leaving doctor's output unchanged.

## Evidence

Not built or run on the founder's machine; the integrator's compile check and the branch's `ci` and `app` runs are the evidence.

**Tests.** `StatusTests` (new, 11 tests, 13 cases): nothing set up prints the version, the three rows and the `keypaste use` step exactly; the chosen vault with Cursor connected and nothing holding it says to open the app or run `keypaste approve`; a missing vault from `KEYPASTE_VAULT` says so and says to choose one; tools read connected, not connected and unknown in that order, and none connected says `keypaste setup`; the app, `keypaste approve` and another keypaste process holding the vault unlocked are each named, and with nothing to fix the step is the help, with no agent counted by the owner; a locked holder says to unlock it in the app; a registration that is not a full path says `keypaste doctor`; with an owner, two registrations (one a keypaste doctor would start) and an unreadable `clients.toml`, it asks nothing, starts no process, launches nothing, writes no audit file or claim and changes no byte under the harness; a vault path holding ESC, BEL, a bidi override and a line break prints as text in one row (skipped on Windows, whose file names cannot hold them); a 125-character file name fits 80 columns, keeping the path's start and end; and a row too wide breaks between pieces under its first value. `CliAppTests`: the bare call redirected prints the help on stderr with exit 1, and on a terminal prints the status with exit 0 and nothing on stderr. `DoctorSourceRulesTests` scans `StatusCommand.cs` with doctor's forbidden names. `PlainWordsTests` reads the status as well as every help. `SecretHygieneTests` runs bare `keypaste` on a terminal against the seeded vault and finds no field value. `HostileNameRenderingTests` runs it against Cursor's file registering an escape and a forged line, and finds neither printed.

**Gate.** [verify-connect-client.sh](../../scripts/verify-connect-client.sh), while the app holds the vault after doctor's check, runs bare `keypaste` under a pseudo-terminal from `script` (`script -qec` on Linux, `script -q /dev/null` on macOS) with `NO_COLOR=1` and `TERM=dumb`, so colour is off and the terminal's own escapes stay out of the output, and requires exit 0, the version, the vault row naming `vault.kdbx` as chosen in the app, `the keypaste app, unlocked`, Claude Code connected and the help as the next step, with no byte under the gate's home and no audit line changed. Windows prints a note and runs no such check.

## Decisions

D-0471.

## Limits and follow-ups

- Status does not compare versions; a holder or registration older than this keypaste reaches the person through `keypaste doctor`.
- Width is counted in UTF-16 code units, so a path with wide or combining characters can pass 80 columns on screen.
- `keypaste --vault <path>` with no verb is still an unknown command; the status always shows the vault keypaste finds without a flag.
- The pseudo-terminal check does not run on Windows.
