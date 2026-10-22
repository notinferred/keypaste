# V.4b — Restore and export an encrypted vault from the app

Completed 2026-09-20 in `00882fe`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Verify (V-V.4b):** change a credential, create its backup through a real save, restore it through the desktop and reopen the restored vault. Cancel, a wrong unlock secret, corrupt backup and destination failure preserve the live file. The encrypted export opens independently in KeePassXC; a file chooser or backup reader without the save-to-restore journey does not pass.

## Evidence

Locked restore over a healthy, damaged or missing vault, and an export: [core](../../tests/Keypaste.Core.Tests/VaultRestoreTests.cs) 39, screens 17, export 9, D-0099 3, cli 1; dropping keep fails 6, dedupe 3, digest 1, re-check 3; gate 2.7.10 locally; D-0264 to D-0270

## Decisions

Ledger rows from this step, which constrain later work, stay in [DECISIONS](../../DECISIONS.md): D-0264, D-0265.

## Limits and follow-ups

The CLI has no restore or export verb, kept by founder decision 2026-10-22. A missing vault is reachable only from the recent list. The KeePassXC gate ran locally when this was recorded; `ci.yml`'s compat job runs it on Ubuntu 24.04, Windows 2025 and macOS 15, and passed in ci 39646134010 at `2073f24` ([9.2b](9.2b.md)). A restore takes the keyfile a backup was made under since [V.1b](V.1b.md).
