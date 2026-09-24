# E.1a — Resolve usable project environments through the session

Completed 2026-09-24 on `main` above `d01299b`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** one core resolution reads a selected env set as the vault's owner holds it saved (`Vault.ReadSaved`, D-0317) and commits only while the requesting lifetime is live (D-0313). It checks every entry under `env/<project>` before any value leaves: a name `EnvConvention` accepts, an entry that has not expired, and one outside the recycle bin. A set with any unusable entry is refused whole, naming each key and why without its value; nothing is ever injected partially. Standalone `keypaste run` uses the same resolution and refusal with its own unlock, and its exit codes and signal behaviour stay as documented. Expiry is read through `Internal/KeePassInterop.cs`, since `VaultEntry` does not carry it yet. Traces to PRODUCT §§1.4, 2 and 3.4.

**Verify (V-E.1a):** `keypaste run` against a vault KeePassXC made, holding an expired entry, a recycled entry and an invalid name in one set, exits non-zero naming each, starts no child and prints no value. Removing them lets the next run inject exactly the set. Through the app's session, a resolution waiting when a lock comes, and one after another program saves the file, both release nothing, and an edit made in the app is the next value resolved. A check made only in the CLI or the view, or one made after values were read into the child's environment, does not pass.

**Founder amendment, made while planning:** a recycled entry is left out of the set, not refused. The recycle bin is already outside every traversal (D-0248), so an entry KeePassXC deleted from `env/<project>` is no longer part of it, and refusing the set because the bin holds a former member would make deleting a variable break its project until the bin was emptied. The gate proves the recycled value never reaches the child and is never named; the refusal names the expired entry and the invalid name.

## What changed for users

- **`keypaste run` refuses an expired entry.** A set holding an entry whose expiry is at or before now exits 2 with `keypaste run: 'env/<project>' cannot be used, so nothing was started:`, then one line per entry, such as `OLD expired 2020-01-02 03:04:05Z` or `BAD-NAME is not a valid environment variable name: '-' is not allowed`, and `Fix or remove them in KeePassXC or the app, then run again.` No child starts and no value is printed. An expiry in the future does not refuse.
- **One list of everything wrong.** Unexportable names, names differing only in case, a name more than one entry has and an untitled entry are listed together with expired entries, each with its reason. An untitled entry used to be skipped and now refuses the set, because the Build requires every entry to have a name `EnvConvention` accepts. A duplicated name used to end the run with the vault's own error before the other checks ran.
- **Unchanged:** exit 3 for a project that does not exist, an empty project still runs with nothing added, the PATH warning, the child's exit code, 126 and 127, and signal forwarding. A recycled entry is still left out.
- **The app's session can resolve a set.** `AppVaultSession.Environments` resolves a project from the vault as its file holds it and releases it only while the unlock that asked is live. A confirmation it is given sees names only; after it answers the set is read again, so an edit saved meanwhile is what leaves, a file another program saved is refused, a set whose names changed is refused, and a lock while it waits or before the commit releases nothing. No screen or command uses it yet: E.1b and E.1c do.

## Evidence

**Tests:**

- `EnvResolutionTests`, 7 new, in core: a usable set is released whole and sorted; an entry expiring now refuses and one a second later does not, until the clock passes it; an untitled entry, an invalid name, a duplicate and a case pair are each named with their reason and no value appears in the refusal; a recycled entry, itself expired and misnamed, is not in the set; a missing project is told apart from an empty one; an unsaved edit is refused, a saved one is resolved and another program's save is refused; expiry round-trips through the file and an update leaves it as it was.
- `SessionEnvResolverTests`, 8 new, in core: a live lifetime releases; a lock while the confirmation waits, a lock inside the confirmation and a lock between the second read and the commit each give `Locked` with no variables; another program's save while asked is refused; an edit saved while asked is what leaves and a new name is refused as `ChangedWhileAsked`; an unusable set is refused before the confirmation is called and a no releases nothing; no live lifetime is `Locked`.
- `EnvThroughSessionTests`, 4 new, in the app, through `AppVaultSession.Environments` and the entries screen: a resolution waiting when the app locks releases nothing; after another program saves the file nothing is released until a re-unlock, which then resolves the other program's value; an edit saved on the entries screen is the next value resolved; a session past its idle deadline on the wall clock, and a locked one, release nothing.
- `RunCommandTests`, 1 new: an expired entry and an invalid name exit 2, name both with their reasons, print none of the three values and start no child. The existing name and case refusals still pass.

**Gate**, [verify-keepassxc-run.sh](../../scripts/verify-keepassxc-run.sh), new, in the `compat` profile and `ci.yml`'s compatibility job. `keepassxc-cli import` makes the vault from KeePass XML, since keepassxc-cli cannot set an expiry: `env/gate` holds VALID (expiring in 2999), EXPIRED (2020-01-02), BAD-NAME and RECYCLED, and `keepassxc-cli rm` then moves RECYCLED to KeePassXC's recycle bin. The shipped Release `keypaste run gate` exits 2, names EXPIRED with its expiry and BAD-NAME with its reason, does not name RECYCLED, starts no child and prints none of the four values. After `keepassxc-cli rm` of the two, the child reports `VALID=… EXPIRED=unset RECYCLED=unset`. The negative control checks that a corrupted expectation fails and that VALID still carries the expiry the run ignored. It passed locally on Windows 10 with KeePassXC 2.7.10, and failed against the pre-E.1a `keypaste` with "the expired entry was not named with its expiry".

**Mutations**, each restored afterwards:

| Mutation | Result |
|---|---|
| The expiry check is skipped | 1 core test fails |
| `SessionEnvResolver` returns without `TryCommit` | 1 core test fails (it survived until `A_lock_after_the_set_was_read_again_commits_nothing` was added) |
| The set is not read again after the confirmation | 3 core tests fail |
| Changed names after the confirmation are not refused | 1 core test fails |
| The app composes the resolver from its raw lifetime and vault, bypassing `Lifetime` and `UnlockedFor` | 1 app test fails |

**Verification:** `./scripts/verify.ps1` on the finished code and documents, before this record was written, ran workflows, scripts, backend, integration and desktop, and passed first time in 457 s. Backend ran 1,788 tests, 10 of them skipped on Windows, and none failed; desktop ran 513 app tests and 40 consistency tests, none failing. `compat`, run by name against KeePassXC 2.7.10, passed every gate, the new one included. macOS and Linux runs come from CI, where `ci.yml`'s compatibility job now runs the new gate on each platform it covers.

## Decisions

[DECISIONS](../../DECISIONS.md) holds D-0337. This row binds only this step's code:

| id | date | decision | supersedes |
|---|---|---|---|
| D-0338 | 2026-09-24 | `VaultEntry.Expires` is read from the file as UTC and never written by `AddEntry` or `UpdateEntry`, so an update keeps the expiry KeePassXC set; the internal `SetExpiryUnchecked` test seam is keypaste's only writer of it | `VaultEntry` carrying no expiry |

## Limits and follow-ups

Expiry is judged against the clock of the process resolving: `keypaste run` uses the machine's, the app its session's. An entry that expires after a child starts stays in that child's environment.

`keypaste env export` still checks names only through `EnvNameRules` and does not refuse an expired entry; the Build scoped expiry to what leaves for a child.

The app shows no expiry, so a person learns of one from the refusal and fixes it in KeePassXC or by recreating the entry; nothing in keypaste clears an expiry.

`SessionEnvResolver` has no consumer outside tests until E.1b (the app's launch) and E.1c (`keypaste run --session`), which fill its confirmation with their own prompt.
