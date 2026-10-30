# F.53 — Save the vault in one rename and stamp what was written

Completed 2026-10-07 at `01eedc0` on `task/f53`: ci 37627277189 (its Windows test job passed on a rerun, see Limits) and app 37627277202 ran every job green on its pull request. The integrated commit differs from that tree in this record and in `scripts/dev.sh`, which no CI job runs beyond its syntax check.

It had no STEPS row: the founder asked on 2026-10-07 for keypaste to be audited against the founder's engineering references and the findings fixed, and chose this group first (D-0424).

## Amendments

- The audit proposed treating a vault file that cannot be read as changed. A missing file was tried first, as the narrower case, and six save tests failed on all three runners (ci 37625445450). They simulate a transient failure by removing the vault's directory, which D-0017 deliberately retries through, so a vanished file stays unchanged. The window the audit cited, another writer between deleting the vault and renaming its save over it, no longer comes from keypaste, which now renames in one step, nor from KeePassXC, which saves through an atomic replace.
- The claim and the audit log refuse while .NET's file locking is off, as planned; that check has a unit test of its reading, not a test of the refusal, because the switch is read once per process.

## What changed

- `KEYPASTE_ATOMIC_REPLACE` in the vendored KeePassLib (`FileTransactionEx.CommitWriteTransaction`): where Transactional NTFS is not used, which is always off Windows, a save moves its temporary file over the vault with `File.Move(temp, vault, overwrite: true)` instead of deleting the vault and then renaming. The Windows Transactional NTFS path is unchanged. `UPSTREAM.md` lists the guard.
- A vault's stamp after a save is the SHA-256 KeePassLib computed over the bytes it wrote (`PwDatabase.HashOfFileOnDisk`, or the verified bytes of an access change), not a read of the file afterwards.
- `Vault.Open` digests the file before the key is derived, as `Reload` already did.
- `FileLocking` reads `System.IO.DisableFileLocking` and `DOTNET_SYSTEM_IO_DISABLEFILELOCKING` as the runtime does; `VaultClaim.TryAcquire` and `AuditLog.TryAppend` refuse while it is off.
- `scripts/dev.sh` waits up to 30 s for a failed job's log, which GitHub publishes a few seconds after the job ends; the pull request's first failure printed an empty log.

## Evidence

- Regressions, each run on a control branch carrying the new tests over the old behaviour (`5fb8772`, since deleted) and failing there, then passing on the fix:

| Test | Control (old behaviour) | Fix |
|---|---|---|
| `VaultConcurrentWriteTests.A_write_landing_straight_after_a_save_is_not_taken_for_its_own` | dev 37625491527: failed, `Assert.True` on the changed-on-disk check | passed on all three runners |
| `AtomicReplaceTests.A_commit_whose_move_fails_leaves_the_vault_in_place` | dev 37627012867: failed, the vault was gone after the failed move | passed on Linux and macOS; skipped on Windows, which commits through Transactional NTFS |
| `HardwareKeyVaultTests.A_save_landing_while_the_key_is_derived_is_seen` | dev 37627202115: failed, the write during the key derivation was taken as read | passed on Linux and macOS; skipped on Windows, which refuses to rename over a file open for reading |

- `FileLockingTests` holds the switch-then-variable reading in nine cases, including a set switch winning over the variable and Windows never counting as off.
- The KeePassXC compat gates (write-back, history, recycle bin, backup, organize, import, fields, projects, keyfile) passed on all three runners, so the files KeePassXC reads are unchanged.

## Decisions

D-0424.

## Limits and follow-ups

- **For 1.0.0.** `HardwareKeyVaultTests.Adding_a_hardware_key_makes_it_required_and_asks_it_once` failed once on `windows-2025` (ci 37627277189, first attempt: the key was asked twice) and passed on the rerun; it has not failed in the 25 ci runs before. An access change writes and verifies on every save attempt, so one transient retry asks the key twice while the test asserts exactly one challenge; nothing ties the failure to this change, whose code Windows reaches only when Transactional NTFS refuses. No product change: make that mechanism a regression. `HardwareKeyVaultTests` must show one challenge per attempt when a transient refusal is thrown on the first attempt through `Vault.ChangeAccess`'s `duringAttempt` seam, and the existing test must assert as many challenges as attempts. Fixed in [P.1a](P.1a.md): a throw from the hook lands before the attempt serialises the vault and asks nothing, so `A_write_refused_after_the_key_answered_asks_it_again_on_the_retry` stages the refusal where the write meets it, a directory at `<vault>.tmp` off Windows and another transaction holding the vault's name on Windows, and sees the key asked once by each of two attempts; the existing test, now `..._asks_it_once_per_attempt`, counts the attempts through the hook (D-0554; dev 40193149893 on `ubuntu-24.04` and 40193157583 on `windows-2025`).
- A vault file that has been moved or deleted is still written fresh at its path on the next save (D-0017).
- **After 1.0.0.** Whether the claim really refuses with file locking off is shown by reading the code, not by a test: `VaultClaim` and the audit log refuse while `FileLocking.IsDisabled`, but `FileLockingTests` covers only the parsing and nothing sets `DOTNET_SYSTEM_IO_DISABLEFILELOCKING`. `verify-session-authority.sh` should start `keypaste approve` and the driver's hold with that variable set on Linux and macOS, and must find each refusing with `FileLocking.Refusal`'s text before any password is read, with no claim file written.
