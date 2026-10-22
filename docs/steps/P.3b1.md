# P.3b1 — Fill same-entry placeholders in released values

Completed 2026-10-20 on `main` at `637eda8`, source only: ci 39406681772 and app 39406681996, whose Windows desktop gates passed on a second attempt (below). The step was built directly on `main` in several commits, each pushed and run; this record, the STEPS, ROADMAP, CHANGELOG and BACKLOG edits and ACAF v1.1 follow in one more.

## Amendments

The founder selected P.3b1 on 2026-10-19 with G.4b and G.3, moved all three into 0.5.0, and directed removing T4 from STEPS once P.3b1 left it empty. T4 stays a track in PRODUCT and in ROADMAP's order. The approved plan settled what the row left open (D-0465):

- **Reach.** Placeholders fill in everywhere a value leaves keypaste: a credential an agent receives through an Allow, an Automatic release or a timed allowance; env sets for `run`, the MCP `run` tool, tokens and the app's Run; `kp://` lines; `get`, `share` and `env export --dotenv`; and the app's copy buttons and its opening of a web address. `show --reveal`, the app's reveal and the edit forms show the stored text, and nothing writes filled text.
- **Fields.** `{TITLE}`, `{USERNAME}`, `{PASSWORD}`, `{URL}`, `{NOTES}` and `{S:<field>}`, where `{S:}` reads only a field that can leave by name (D-0388). Keywords and prefixes match in any case, as in KeePass 2, and a field's name exactly. An empty field fills in as empty.
- **Refusals.** A value keypaste cannot fill is refused whole, naming the placeholder and never a value, rather than released as written.
- **Terminal edits.** N.11's prompt names any placeholder a terminal edit's value plants.
- **ACAF.** Version 1.1 is written now, as a full page at `/acaf/v1.1/` beside version 1, whose Status points to it.

During the build the founder settled three more points:

- A credential refused because it cannot be filled is recorded as its own method, `unfillable`, rather than `failed`. The agent is told the item's value holds a placeholder keypaste does not fill in and not to retry, `keypaste agent`'s terminal says so, and the field cools down as after a denial, so a retry does not ask the person again.
- The app's refusals end with "Edit the item so keypaste can fill it in."
- A username or web address whose copy filled in another field is copied with the clearing countdown (D-0300), since it may carry a password.

## What changed

- **The resolver.** `KeePassPlaceholders` scans a value once, fills each field at most once whatever fans out to it, and refuses past 10 levels, on a loop or past 1 MiB. Refusals keep the prefix `holds the KeePass placeholder {X}, which …`. A refusal names the innermost placeholder at fault, and a size refusal names the one whose fill passed 1 MiB. A conversion or reference with another placeholder inside it, such as `{T-CONV:/{USERNAME}/Upper/}`, is refused whole rather than half filled, while in `{S:{PASSWORD}}` the inner placeholder fills in and `{S:` stays text, as in KeePass. A `{DT_…}` that is not one of KeePass's date and time names is shown without its letters, and an `{S:…}` names its field only when the item has one by that name. `PlaceholderFill` carries the value, the refusal and the filled parts, and its `ToString` shows no value.
- **Reads.** `Vault.FillPlaceholders` serves the CLI and the app, for the current item and a revision. `Vault.ReadSavedFill` fills from the saved file under the same comparison as `ReadSavedField` (D-0317). Nothing more is read when a value holds no placeholder.
- **Env.** `EnvResolution` fills each member before judging the set, and `EnvResolved.Fills` keeps the filled parts by variable. `kp://` project references carry them, and entry references fill from the saved file. `EnvMatrix` and `env diff` compare filled values, and `env export --dotenv` refuses an unfillable value with exit 2 and writes nothing.
- **The run wire.** A released set's reply carries an optional `fills` at protocol version 2, and the MCP `run` tool scrubs every filled part with the whole values. `run --session` and `run --bundle` carry no `fills`, since their runners scrub nothing.
- **Credentials.** `VaultCredentialSource` fills the chosen field from the saved file, and a timed allowance keeps the filled value. `CredentialFailure.Unfilled` and `AuditMethod.Unfillable` refuse what cannot be filled, and `ApprovalGate.CoolDown` holds the field back. An older bridge reads the new method as `failed`, as it reads every method added after it.
- **CLI and app.** `share` refuses the whole share for one unfillable field, and `get` exits 2 pointing to `show --reveal`. The app's copies and its opening of a web address fill in, and an unfillable one leaves the clipboard untouched and is not opened.
- **Terminal edits.** `TerminalEdits.Check` adds one sentence after the change for `add`, `set`, `set --field`, `env set` and `env pull` (one per variable), naming keywords only. The sentence appears in the holder's prompt.

## Evidence

ci 39406681772 and app 39406681996 at `637eda8`:

- **Core.**
  - `KeePassPlaceholdersTests`: each keyword in any case, edge text, every refusal's wording with no value in it, depth 10 filled and 11 refused, direct and indirect loops, the fan-out and the size cap, nested conversions and `{DT_…}`.
  - `VaultSavedStateTests`: `ReadSavedFill` refuses when unsaved or changed on disk, reads nothing without a placeholder, and fills a revision from that revision.
  - Also `TaggedEnvResolutionTests`, `SessionAuthorityEnvTests`, `EnvReferenceResolutionTests`, `EnvMatrixTests`, `ApproverRunProtocolTests` (`fills`), `VaultCredentialSourceTests`, `CustomFieldReleaseTests`, `ShareServiceTests`, `ApproverHandlerTests` (unfillable, refused and cooled for an Allow and an Automatic release), `TerminalEditsTests` and `SessionAuthorityEditTests`.
- **MCP.** `RunToolTests`: a spliced part comes back as `[keypaste:DATABASE_URL]`. `SecretHygieneTests`: only the named field leaves, and an unfillable credential leaves no sentinel and is asked about once.
- **CLI.** `EnvExportTests`, `VerbTests` (`get`), `ShowVerbTests` (still stored), `TaggedProjectRunTests` (a child receives `postgres://u:<password>@db`) and `TerminalApprovalChannelTests`.
- **App.** `SecretCopyParityTests`: every copy and the open fill in, and an unfillable value leaves the clipboard untouched. `TerminalEditWindowTests` draws the planted sentence.
- **Gates.**
  - `scripts/verify-keepassxc-projects.sh`: KeePassXC writes `postgres://u:{PASSWORD}@db`, and the child receives what `keepassxc-cli show -a DATABASE_URL` prints; `{REF:…}` is refused.
  - `scripts/verify-run-session.sh`: the `broken` set holds `{REF:…}`.
  - `scripts/verify-mcp-run.sh`: `DATABASE_URL=pre-{S:DEPLOY_KEY}-post` comes back scrubbed on both streams. It answers `y`, `h`, `o`, `d` and expects 6 audit lines, with the denial last because it cools down every field it asked for.

**What failed on the way, all on `main`:**

- `136ca17`, the step's code, failed to build Core on IDE0005: `KeePassPlaceholders.cs` imported `System.Text`, which Core's project already imports for every file. `064acf9`, the unfillable refusal and the app's wording, was built on it and failed the same way. `637eda8` removed the import.
- At `637eda8`, app's `desktop gates (windows-2025)` ran its one step for 24 minutes, where it usually takes 8, until the job's timeout cancelled it, and GitHub kept no log of the step. Ubuntu and macOS passed. A rerun of the job passed in 8 minutes. The diagnosis is open: which gate stalled, and whether this step caused it, is unknown. Each gate runs under an 8-minute `timeout`, so the step outliving it suggests that on Windows a stalled gate's child processes kept the step's output open after `timeout` ended the gate. The next experiment is to make the step leave each gate's log when that happens: `af4c565` keeps each gate's output in a file, printed when the gate ends, and on later runs it showed the failing gate's output each time a gate failed. The stall has not recurred.

## Decisions

D-0465, and D-0359, D-0378 and D-0422 amended to it, in [DECISIONS](../../DECISIONS.md). T-8, T-35 and T-43 in [THREATS](../../THREATS.md). [ACAF v1.1](https://keypaste.com/acaf/v1.1/).

## Limits and follow-ups

- KeePassXC fills about 5 levels and releases the rest as written, while keypaste fills 10 and refuses beyond, so 6- to 10-level chains differ.
- A credential that cannot be filled is refused after the Allow, and its cooldown holds the field back for a minute even once the item is fixed.
- The log and the prompt name the field asked for, not the fields filled into it.
- An older bridge does not scrub filled parts, and every non-empty part is scrubbed however short, so a short title or user name filled into a value mangles a run's output wherever it appears.
- A generated password could, with negligible probability, hold text shaped like a placeholder; KeePassXC's `\{…\}` escape is honoured as KeePassXC 2.7.11 honours it, which 2.7.0 to 2.7.10 do not (D-NEW-X31) (gaps-1c, RUNS-PENDING).
- A revision copy fills in only while the revision at its position still holds the password the list read, checked under the lock its fields are read under, so an edit saved between the reads copies nothing and says the item changed; an edit that moves an identical password into that position still fills in from the moved revision's fields (gaps-1c, RUNS-PENDING).
- Tagging an item into a project, in the app or at `env tag`, names each joining variable that already holds a placeholder, with its keyword, before it is written (D-0509, T-43) (gaps-1b, RUNS-PENDING).
- Source only.
