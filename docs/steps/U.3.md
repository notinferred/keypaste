# U.3 — Serve current saved vault state

Completed 2026-09-23 on `main` above `d285bb8`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** the authority U.1 establishes serves every read, release and launch from the vault as last saved, not from the snapshot the unlock produced. An app edit, deletion, move or rename, access change or exposure change invalidates the grants and policy-derived releases naming the affected entry before the next release. Before releasing, the authority compares the file on disk with what it last read or wrote; an external change refuses the release, invalidates the vault's grants and requires a reload or re-unlock the person starts. It never overwrites, merges or keeps serving the old snapshot. Traces to PRODUCT §2.

**Verify (V-U.3):** through a real `keypaste-mcp` request against the owning session, a password edited in the app is the next value released; a granted entry deleted, moved or made unexposed is refused on the next request rather than served from its grant; after an access change no earlier grant releases anything. A KeePassXC or CLI save between two requests makes the second refuse and name the change, the file keeps the external writer's bytes, and nothing is released until a reload. A cache cleared only in a view model, or a check made after the value was released, does not pass.

The founder approved the plan as written. One part of it was dropped while building: the plan had `AppVaultSession.Swap` announce a change to every entry after an access change. `Vault.ChangeAccess` already does that before it writes, and a grant given between that write and the swap holds the same value, so the second announcement changed nothing a test could observe and was removed.

There is no launch through the session yet (E.1a), so "launch" had nothing to apply to. Exposure is carried by each request and checked before any grant is looked up, so an exposure change already refused a granted entry; the gate and tests cover it through a move out of `env/**`.

## What changed for users

The process holding a vault, the desktop app or `keypaste agent`, now answers agents only while its open vault holds exactly what the file holds (D-0317). Every listing, resolution and read takes the vault's state lock and checks two things: that no change made in the process is still unsaved, and that the file's SHA-256 is what this vault last read or wrote. Otherwise:

- **Another program saved the file**, such as KeePassXC or the CLI. Credential requests and listings are refused as the new audit method `vault-changed`, whose reason says another program changed the file. Every grant in the session is zeroed at that moment. The refusal lasts until the person locks and unlocks the app or restarts the agent. The agent's terminal says to restart it. Nothing is written: the app's own save was already refused over an external change, and still is.
- **A change made in the process is not saved yet**, or a write is in progress. The request is refused as `vault-changed` with a reason saying so, and grants are kept. The app saves each edit as it makes it, so this lasts only as long as a save, unless the save failed.
- **The file cannot be read.** The request is refused as `failed`, and nothing is taken as unchanged.

Every change to an open vault now reports the entries it touched before it is saved: an edit or history restore, an add, a delete, a restore from the trash, a rename, a move, and a group rename, which reports every entry beneath the group. Moves and renames report both the old and the new name. An access change reports everything. The app's session zeroes every connection's grants for those entries (D-0318). A password changed in the app is therefore the next value an agent receives, after asking again, and a name reused by another entry is asked about again rather than answered from the old grant. Policy releases were never cached and now read the saved file.

The bridge audits a refused listing as `vault-changed` too. A names reply can now carry the refusal's method, an optional field that decodes an unknown value as `failed`. Absent, it still reads as locked, so the protocol version is unchanged.

## Evidence

**Tests**, on real vaults and, for the session, real pipes:

- `VaultSavedStateTests`, 10, new:
  - A vault that was never saved is not read. A saved one and a reopened one are read.
  - A change is not read until it is saved, and an edit whose save was refused over an external change is never read.
  - Another writer's save is `ChangedOnDisk` until the vault is reopened, and a deleted file is `Unreadable`.
  - Each kind of change names what it touched while the vault is still `Unsaved`. A group rename names every entry beneath it and not a sibling group sharing its prefix.
  - Refused changes name nothing and leave the vault current. An access change names everything and leaves the vault current once written.
- `GrantCacheTests`, 2 new: an edit withdraws every connection's grants for the entries it touched and no others, and a change to everything withdraws every grant.
- `SessionAuthorityTests`, 3 new, a real listener and client in front of a real vault:
  - After another program saves, the request and the listing are refused as `vault-changed`, the grant is zeroed, the file keeps the other writer's bytes and the terminal narration says to restart the agent.
  - With the old bytes copied back, the same request is asked again rather than served from the grant.
  - A policy release is refused after an external change.
- `ApproverProtocolTests`, 1 new: a refused listing keeps its method, and an unknown one reads as `failed`.
- `CurrentStateTests`, 6, new. They use a real `AppVaultSession` and `SessionHost` with an approving channel and a real pipe, acting through the entries, detail and access screens:
  - An edit is the next value released, asked again.
  - A delete, a move out of the exposure and a group rename each refuse the next request. Undoing each asks again: a different entry moved onto the deleted name, the move back, and the rename back.
  - An access change leaves no earlier grant usable, with the app still unlocked.
  - An external save refuses as `vault-changed`, an edit in the app is refused and leaves the file with the external bytes, and after lock and unlock the new value is released.
- Tests that served vaults they had never saved now save them first, as every real owner does: `VaultCredentialSourceTests`, three `VaultRecycleBinTests`, and the MCP `SecretHygieneTests`, `LargeCredentialTests` and `LargeVaultListingTests` fixtures.

**Gate**, [verify-current-state.sh](../../scripts/verify-current-state.sh): the shipped Release `keypaste` and `keypaste-mcp` against `Keypaste.AppDriver hold --approving-prompt`, all through one bridge connection so that a grant can be reused:

1. The first request is prompted and releases v1, and the repeat is `grant-cache`.
2. After `edit` on the app's detail screen, the next request is prompted again and releases v2.
3. After `relocate` out of `env/**`, the request is denied `out-of-scope`. After moving the entry back, it is prompted again rather than served from the grant.
4. `keypaste env set` writes v3 while the app holds the vault. The next request is denied `vault-changed`, naming the app's session, with the change named in its audit reason, and the listing is also denied `vault-changed`. An `edit` in the app is refused and the file's SHA-256 is still the CLI's. A further request is still denied, and nobody was asked.
5. After `lock` and `unlock` a new session answers, and the request is prompted and releases v3.

The gate passed on Windows 10 in about 6 s, three consecutive times. It runs in the `desktop` profile, which `app.yml` runs on `ubuntu-24.04`, and `app.yml` now triggers on it. [verify-lock-boundary.sh](../../scripts/verify-lock-boundary.sh) and [verify-session-authority.sh](../../scripts/verify-session-authority.sh) still pass.

**Mutations**, each restored and touched afterwards:

| Mutation | Result |
|---|---|
| `UpdateEntry` reports no entry | 2 fail: the edit in `CurrentStateTests` is served from the old grant, and the saved-state test of what each change names |
| `ReadSaved` skips the digest | 5 fail: the external-change tests in `VaultSavedStateTests`, `SessionAuthorityTests` (3) and `CurrentStateTests` |
| The handler keeps grants on an external change | 2 `SessionAuthorityTests` fail, including the grant served once the old bytes are put back |
| A change does not mark the vault unsaved | 3 `VaultSavedStateTests` fail, including an edit whose save was refused being read |
| An access change reports nothing | 2 fail: `VaultSavedStateTests` and the access change in `CurrentStateTests`, where the earlier grant is reused |

The script touched each restored file so an incremental build could not keep a mutation, the lesson U.2 recorded.

**Verification:** `bash scripts/verify.sh` on the finished tree passed workflows and scripts. It then failed backend and desktop at `dotnet format`, because a private constant in each new test file lacked the `_` prefix. After the rename, `--from backend` passed backend, integration and desktop. Backend ran 1,698 tests with 10 skipped on Windows, and desktop ran 468 app tests and 40 consistency tests, none failing. The desktop profile ran all three session gates, with the agent's SIGTERM step skipped on Windows as before. `compat` needs KeePassXC and was not run: saves write the same bytes as before, and only when a vault counts as saved changed.

## Decisions

[DECISIONS](../../DECISIONS.md) holds D-0317 and D-0318. These rows bind only this step's code:

| id | date | decision | supersedes |
|---|---|---|---|
| D-0319 | 2026-09-23 | An external save and an unsaved change are both audited as `vault-changed`, with reasons that tell them apart. Only the external save zeroes grants. An unreadable file is `failed`, and a names reply carries an optional refusal method rather than a new protocol version | — |
| D-0320 | 2026-09-23 | `Keypaste.AppDriver hold --approving-prompt` passes a channel that approves every request, and its `edit`, `delete` and `relocate` lines act through the entries screen. The shipped app still passes the one with nowhere to ask (D-0311, D-0316) | — |

## Limits and follow-ups

The check is made before anything is read and at every read, not at the pipe write. A save by another program that lands after the read and before the reply leaves is not caught. The next request is refused.

The app does not tell the person that the file changed until one of its own saves is refused. An agent's refusal says to lock and unlock, and the app's status is 4.4b's and 4.3b's to show.

An unsaved change refuses requests only while a save is in progress, unless that save failed. After a failed save the app keeps refusing agents until a later save succeeds or the vault is locked. The app already tells the person to lock and unlock in that case.

`keypaste agent` never edits its vault, so edits there need no notices. Its reload is a restart, and the gate's agent path is covered only by core tests over the same `VaultCredentialSource`.

Launches through the session are E.1a's, which is expected to read through `Vault.ReadSaved`.
