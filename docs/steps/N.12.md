# N.12 — Mark copied secrets as concealed on macOS and Linux

Completed 2026-09-29 on `main` above `bee0d1c`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** a secret the desktop copies carries three more markers in the same clipboard item as the Windows formats:
- `org.nspasteboard.ConcealedType` and `org.nspasteboard.TransientType`, which macOS pasteboard managers honour;
- `x-kde-passwordManagerHint` holding `secret`, which KDE's Klipper honours.

No marker's content is the secret. A copy that is not a secret, such as a `keypaste run` command, carries none. T-19 and SECURITY say what each marker asks and that none stops a process reading the clipboard, and O-0019 is answered. The CLI's `pbcopy`, `wl-copy` and `xclip` writes are unchanged, and that limit is stated. Traces to PRODUCT §3.

**Verify (V-N.12):** `AvaloniaClipboard` hands the platform one item holding the text and every marker for a secret, and no marker for plain text. On a macOS runner, after the app's composition copies a secret, the general pasteboard's item lists both nspasteboard types beside the text, and none after the clear. The bundled `keypaste.app` on a Mac gives the same read. On a Linux runner with an X11 clipboard, `xclip -selection clipboard -o -t TARGETS` lists `x-kde-passwordManagerHint`, whose content reads `secret`. A marker shown only in the source, with no platform read of what the clipboard offered, does not pass.

**Amendment, 2026-09-29.** The bundled app's read on a Mac was added to the Verify in `8192987`, without a founder request, when the founder said a Mac was available. Asked about Mac chores, the founder did not take it up.
- **Where the check went.** The read moves to docs/desktop.md's checklist as item 54, where the Windows Clipboard History check already is. There it is observed with R.1a's installed macOS package, which a person installs on a Mac anyway.
- **Why the runner stands in.** The runner's read goes through the same Avalonia native pasteboard code the bundle uses. An undeclared type is still written, because Avalonia asks macOS for an exported type by name.
- **What the founder can do.** Restore the clause, and N.12's record stays as it is; the check becomes a new row.

## What changed for users

- **A copied secret asks macOS pasteboard managers to skip it.** A password, env value, history password, custom field or token the desktop copies is marked concealed and transient, which asks managers that follow nspasteboard.org, such as Maccy, to leave it out of their history. The markers hold no content, so a manager that stores what it skips does not keep the value either.
- **A copied secret asks KDE's Klipper to skip it.** The same item carries `x-kde-passwordManagerHint` holding `secret`.
- **A copied run command is left for them to keep.** It holds no secret, and it may be useful in history.
- **Not the CLI.** `keypaste get` on macOS and Linux copies through `pbcopy`, `wl-copy` or `xclip`, one type per call, and carries none of these markers.

None of this is in a download: the desktop has no public release.

## Evidence

Local, Windows 10 Pro 19045, on `main` above `bee0d1c`, and GitHub-hosted runners at `bf67177` on the probe branch `n12-clipboard-markers`, whose code is this step's.

**What the app hands the platform.** [SecretMarkerTests](../../tests/Keypaste.App.Tests/Clipboard/SecretMarkerTests.cs), on the headless platform, whose clipboard keeps the data as given:
- A secret is one item holding the text and exactly the six markers. The Windows three are four zero bytes each, the two nspasteboard types are empty, and the KDE hint is `secret`, none containing the secret.
- A run command is one item holding the text alone.

`ClipboardSourceRulesTests` spells all six names exactly, and `ClipboardCountdownTests` and `SecretCopyParityTests` pass unchanged: 36 of 36 with `SecretMarkerTests`.

**What the platform then offers.** [verify-clipboard-markers.sh](../../scripts/verify-clipboard-markers.sh) runs the app's own composition through `Keypaste.MinimizeObserver --scenario markers` on a real display. It copies a secret through the shell's clipboard, waits for the twenty-second clear, then copies a run command plainly. After each, it reads type names only:
- on macOS, each general-pasteboard item's types, through `osascript` and AppKit;
- on Linux under Xvfb, the CLIPBOARD selection's TARGETS and the hint's content, through `xclip`.

Its `--selftest` judges fourteen canned readings as expected, and runs in the scripts profile. The `clipboard-markers` job in `app.yml` runs it on `macos-15` and `ubuntu-24.04`.

In `app.yml` run 36640987154, dispatched on the probe branch at `bf67177`, both jobs passed, and each kept its `readings.jsonl` as an artifact.

| Runner | After the secret copy | After the clear | After the run command |
|---|---|---|---|
| `macos-15`, macOS 15.7.9 | one item: `public.utf8-plain-text`, `org.nspasteboard.ConcealedType`, `org.nspasteboard.TransientType` and Avalonia's own in-process type | no item | one item: the text and Avalonia's type, no marker |
| `ubuntu-24.04`, Ubuntu 24.04.5 under Xvfb | TARGETS lists the text types, the three Windows names, both nspasteboard types and `x-kde-passwordManagerHint`, which reads `secret` | no owner, so no targets | the text types alone |

- **The clear came on time.** It came about twenty seconds after the observer reported the copy on each runner.
- **macOS does not list every name.** The Windows names and the KDE hint are not on its item; why was not looked into. The two types macOS managers read are there.

The row's `xclip -o -t TARGETS` reads the PRIMARY selection. The app writes CLIPBOARD, so the script reads `-selection clipboard`.

**The command.** `bash scripts/verify.sh` selected every profile but `compat`.
- **Passed:** backend, integration and desktop.
- **Failed:** scripts. `verify-release-matrix.sh` holds that nothing in `app.yml` uploads before the installer is signed, and requires every upload to carry the signing rehearsal's guard. The probe's artifact upload broke both.
- **The fix:** the upload was removed, and the script prints its readings to the job's log instead. That step changed after run 36640987154.
- **The founder's direction:** build all three steps before testing further, so the tree is verified whole with N.10 ([N.10](N.10.md)).

## Decisions

[D-0381](../../DECISIONS.md) answers O-0019. These bind only this step's code:

- **The nspasteboard types are empty.** nspasteboard.org reads them for presence. KeePassXC puts the secret itself in ConcealedType, which a manager that stores the items it skips would keep.
- **The markers are one list,** each name with the bytes it holds, set on the one item in the one `SetDataAsync` call that already carried the Windows formats.
- **The platform read uses the minimize observer,** the one helper that already runs the app's composition on a real backend. Its new scenario reads nothing: the script, outside the process, reads the clipboard.
- **The job runs with every desktop push,** since an Avalonia upgrade is what would silently drop a marker. It uploads nothing: its readings are in its log.

## Limits and follow-ups

- **No manager was observed honouring the markers.** That is docs/desktop.md's item 54, with the manager, its version and the OS recorded.
- **Wayland is not read.** Avalonia's X11 backend was read under Xvfb; a Wayland session reaches the app through XWayland, whose bridge to the Wayland clipboard is unverified.
- **The CLI carries no marker on macOS or Linux.**
- **The bundled app's own read on a Mac** is docs/desktop.md's item 54, under the amendment above.
