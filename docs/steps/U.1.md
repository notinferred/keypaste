# U.1 — Establish one vault session authority

Completed 2026-09-23 on `main` above `e8c6bf3`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** choose the process that owns the unlocked vault in the desktop journey and record the choice as a decision, then implement its authenticated local attachment. The vault-free `keypaste-mcp` bridge reaches the owner only over a per-user endpoint that names the vault and session on every operation, and no master password or key crosses it. A second owner of the same vault, and a terminal `keypaste agent` and the desktop both claiming it, are refused by name rather than resolved silently. Standalone CLI verbs keep an explicit unlock of their own outside the desktop journey rather than a hidden second session. Traces to PRODUCT §2.

**Verify (V-U.1):** a real `keypaste-mcp` request against the unlocked desktop reaches the owning process, is answered from its vault and names the session it reached. The same request with the app locked, with a stale session identifier and from a client presenting no valid attachment is refused. Starting `keypaste agent` on the vault the app owns, and a second app instance on it, are each refused with a message naming the owner, and neither opens the vault. A capture of the attachment traffic carries no password or key. A mocked endpoint, or a listener that only accepts connections, does not pass.

The plan was approved by the founder as written; there was no amendment.

## What changed for users

The desktop app owns the vault it unlocks (D-0309). While a vault is unlocked, the app serves it on a pipe derived from the user, keypaste's home and the vault, so a `keypaste-mcp` configured with `--vault` for that vault reaches the app. The app answers `list_entry_names` under the bridge's exposure. It refuses every credential request, because it has nowhere to ask a person until STEPS 4.4, and it consults no policy rule. Locking the app stops serving the vault. Agent Activity now says which of those is true, read from what the app serves rather than from whether some pipe accepts a connection.

One vault has one owner. The app and `keypaste agent` each take a claim on the vault before the password is read, and a second owner is refused with "this vault is already unlocked in the keypaste desktop app (process N). Lock it there first", or the same naming `keypaste agent`. It never prompts and never opens the vault. The claim is an open file handle under `~/.keypaste/sessions`, so a killed holder leaves the vault free. `keypaste run`, `get` and the other CLI verbs take no claim and unlock explicitly as before.

The approver protocol is now version 2 (D-0310). A bridge attaches before every request, naming its vault; the owner answers with the session it minted at unlock; and each request names both. The owner refuses, as `no-session`, a request from an unattached connection, one naming another vault and one from a session that has ended. The audit line of a request that reached a session names it in a new `session` field. A request whose reply was lost is retried under the session it was first sent in, never a later one. A bridge with no `--vault` is refused, and a v0.3.0 bridge and this approver, or the reverse, cannot talk.

The agent-facing refusals changed where they had become untrue. "No keypaste agent is running" now reads "Nobody can approve this right now: no keypaste agent holds this vault unlocked" and says the app cannot approve yet. The locked refusal no longer says an agent is running. There are two new refusals, for `no-session` and for a bridge with no vault. `keypaste agent` prints the session it serves on its `listening on` line.

## Evidence

**Preflight**, Windows 10, before the claim existed: two `keypaste agent` processes given one `--approver` name on two vaults both printed `listening on`. A second .NET named-pipe server on an existing name is accepted rather than refused on Windows, because `ApproverListener` asks for the maximum number of instances. Before this step the shared per-user default name made that the ordinary case for two agents of one user. The claim now prevents it for one vault, and the per-vault name for two; THREATS T-29 keeps the explicit-name residual.

**Tests**, all over real pipes and real vaults:

- `VaultClaimTests`, 6: a second claim is refused naming the holder's kind and process; release; two spellings of one vault meet at one claim; two vaults do not contend; a vault not yet created can be claimed.
- `SessionAuthorityTests`, 9, a real `ApproverListener` and `ApproverClient` in front of the core handler: an attached listing and request are answered and name the session. These are refused unasked, with nothing read from the source and nobody asked: an unattached connection, an attachment naming another vault, a request naming another vault than its attachment, a request from the ended session after a re-unlock, the new session presented on the old attachment, and a locked owner.
- `ApproverProtocolTests`: attachment round trips, a request without vault or session and a v1 frame are refused, and an attach reply that both attaches and refuses, or does neither, is not an attachment. The test that the wire version stayed 1 was removed, because D-0310 bumps it.
- `SessionOwnershipTests`, 6: a second app session gets `HeldElsewhere` with the right and the wrong password alike; lock frees the vault and the next unlock is a new session; a wrong password holds nothing; the unlock screen names the holder. `SessionHost` serves the unlocked vault and stops on lock. A relay pipe recorded every byte of an attachment, a listing and a credential request both ways, and neither the master password, as UTF-8 or UTF-16, nor the keyfile's bytes, hex or base64 appear.
- `SessionAttachmentTests`, 8, the shipped bridge code against a fake owner behind a real listener: every request names the configured vault and the attached session, and the audit line names the session that answered. A refused attachment is audited as `no-session` or `vault-locked` with the matching refusal. A bridge with no vault attaches nothing. A retried request keeps its first session.
- Existing suites were adjusted for attachment and still pass: `FakeApprover` attaches and stamps a session; the real-vault MCP fixtures sit behind a `SessionAuthority`.

**Gate**, [verify-session-authority.sh](../../scripts/verify-session-authority.sh), shipped Release `keypaste` and `keypaste-mcp` against `Keypaste.AppDriver hold`, which unlocks through the app's unlock screen and serves through `SessionHost`. It checks:

1. The listing returns the vault's entry and the credential request is refused with no value. Both audit lines name the session the app printed.
2. `keypaste agent` on that vault exits non-zero, naming the app's process, without a password prompt or a `listening on` line. A second app exits 1 naming it.
3. After `lock`, both calls are refused and audited as denials reaching no session.
4. After `unlock`, a new session answers.
5. After the app is killed, a new app opens the vault.

The script passed locally on Windows 10 in 19 s. It runs in the `desktop` profile, which `app.yml` runs on `ubuntu-24.04`.

**Mutations**, each restored afterwards:

| Mutation | Result |
|---|---|
| `SessionAuthority` skips the session comparison | 2 `SessionAuthorityTests` fail: the ended session and the new session on an old attachment |
| The app's claim is released as soon as it is taken | 2 `SessionOwnershipTests` fail; the gate fails at `keypaste agent`'s refusal |
| The app tries the password before claiming | `A_vault_another_session_holds_is_refused_before_the_password_is_tried` fails |
| `keypaste agent` claims a different key | The gate fails at `keypaste agent`'s refusal |
| The bridge drops the session from the audit record | `TheAuditLineNamesTheSessionThatAnswered` and the gate fail |

The first run of the agent mutation showed the gate had no bound on the agent it expected to be refused, so an agent that started would have left the gate hanging, not failed. The step now runs under `timeout 30`, and the mutation then failed the gate as recorded.

**Verification:** `./scripts/verify.ps1` on the finished tree ran workflows, scripts, backend, integration and desktop, and passed all five in 311 s. Backend ran 1,673 tests with 10 skipped on Windows, desktop 454 app tests and 40 consistency tests, none failing; the desktop profile ran the new gate, and integration ran the existing approval, policy, stdio, log-chain and demo gates on the version 2 protocol. `compat` needs KeePassXC and was not run; this step changes no vault write.

## Decisions

The ledger rows from this step, which constrain later work, are in [DECISIONS](../../DECISIONS.md): D-0309 and D-0310. The rules below bind only this step's code.

| id | date | decision | supersedes |
|---|---|---|---|
| D-0311 | 2026-09-23 | Until 4.4, the app's authority answers listings and refuses every credential request through a channel with nowhere to ask and `PolicyGate.None`, so no rule releases anything from the app without its prompt | — |
| D-0312 | 2026-09-23 | The bridge attaches before every request, so an idle connection learns of a new session before sending anything; a request whose reply was lost is retried once under the session it was first sent in | v1's reconnect-and-retry to whatever answered |

## Limits and follow-ups

This step does not lock anything new. A lock stops the app's listener, but pending approvals, grants and races with the lock belong to U.2. The app serves the snapshot it opened, as the agent does (U.3). Crash takeover of a stale Unix socket, the app's status read from the authority and quitting are 4.4b's to prove; the gate checks only that a killed app holds no claim. 4.3a's per-request checks in the authority and its denial records are not all done here, though attachment failures already reach the audit log as denials.

The claim and endpoint are scoped to `KEYPASTE_HOME`, and a Windows pipe name given explicitly can be shared by two owners of different vaults; both limits fail closed at attachment (T-29).

A v0.3.0 bridge cannot reach this approver, nor this bridge a v0.3.0 approver; they ship as a matching set. The published guides keep v0.3.0's wording where it differs and name the source behaviour beside it.

`list_entry_names` in the MCP test harness is served by a fake source rather than the bridge's owner connection, so the listing's session field is tested on `ApproverEntryNameSource` directly and through real binaries in the gate. macOS and Linux runs of the new tests come from CI, not from this machine.
