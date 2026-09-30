# C.5a1 — Release one env field to an agent

Completed 2026-09-30 on `task/c5a` above `a432ade`, source only; dev runs 36735692059 (Linux), 36736980306 (macOS), 36736976209 (Linux and Windows), 36743700009 (core and CLI, three OSes) and 36743396926 (`DesktopApprovalTests`, macOS).

## Amendments

- C.5a was split on 2026-09-30 by the fix pass, after review found three clauses of V-C.5a unmet: the app-held path, the policy rule, the `run` tool, `run --env-file` and the share ran on vaults the CLI made rather than one KeePassXC made; no share ran against the Worker on loopback; and `verify-desktop-approval.sh` never ran on macOS. C.5a1 is the Build with the checks below; C.5a2 holds the rest, and C.5a's row and V-C.5a stay open. The split awaits the founder's ratification.
- The row was built in parallel with C.1b, B.2 and other 0.5.0 tasks on 2026-09-30, on a development machine with no SDK: every build and test ran through `dev.yml`.
- The process checks extend [verify-approval-e2e.sh](../../scripts/verify-approval-e2e.sh), [verify-policy-e2e.sh](../../scripts/verify-policy-e2e.sh), [verify-desktop-approval.sh](../../scripts/verify-desktop-approval.sh) and [verify-keepassxc-fields.sh](../../scripts/verify-keepassxc-fields.sh) rather than adding a script, by direction for this task. Only the last reads a vault KeePassXC made; the first three seed theirs with the shipped CLI, which the integration and desktop lanes can run without KeePassXC.
- `request_credential`'s schema advertises the four standard names as an enum and custom names as `^[A-Z][A-Z0-9_]{0,127}$` under `anyOf`, without a look-ahead some clients reject; the bridge and the owner refuse the `KPEX_`, `KPXC_`, `KP2A_` prefixes and standard names in capitals that the pattern admits.
- `ShareService.Fields` stays the desktop's list of choices; `ShareService.IsShareable` is the rule `share` applies, those choices or a releasable custom field.

## Evidence

- **Core.** [CustomFieldReleaseTests](../../tests/Keypaste.Core.Tests/CustomFieldReleaseTests.cs), 28 cases over a real vault whose `api/OpenAI` holds distinct sentinels in its password, a protected `OPENAI_API_KEY` and `Recovery codes`: the rule accepts env-named fields and refuses `Recovery codes`, `otp`, `KP2A_URL_1`, `KPEX_PASSKEY`, `KPXC_BROWSER`, `URL`, `PASSWORD`, `TITLE`, `openai_api_key`, `_EXEC_CMD` and `1KEY`, for release and for sharing; a real `ApproverHandler` releases exactly the field on Allow with the prompt naming it; a timed grant for it serves it again and a `password` request is asked about; four names are refused as `invalid-request` with the fixed rule and nobody asked; a rule with `fields = ["OPENAI_API_KEY"]` releases it as `policy` while `password` reaches a person, and one naming `Recovery codes` is refused when the file is read; an unsaved field change is refused as `vault-changed`; an unsaved change or another writer's save made after the source reads the entries and before it reads the custom field releases nothing, as `Unsaved` or `ChangedOnDisk`; `kp:///api/OpenAI#OPENAI_API_KEY` resolves to the sentinel and `#Recovery%20codes` is refused with no value in the refusal; a share of the field seals one `ShareField` named `OPENAI_API_KEY` that `ShareCrypto` opens, and `Recovery codes` uploads nothing.
- **Vault.** `VaultSavedStateTests` has `ReadSavedField` return the saved value, then nothing and `Unsaved` after an unsaved `SetFields`, and nothing and `ChangedOnDisk` after another writer saves.
- **Bridge.** [SecretHygieneTests](../../tests/Keypaste.Mcp.Tests/SecretHygieneTests.cs) plants the two custom sentinels beside the four standard ones: `OPENAI_API_KEY` returns only its own over a real bridge, pipe and approver, and `Recovery codes`, `otp`, `KP2A_URL_1` and `URL` each return the fixed denial, ask nobody, write one line with `field: invalid` and `method: invalid-request`, and leave no sentinel in the result, the wire or the log. `ServerToolsTests.TheSchemaAndTheCoreAgreeAboutFields` holds the schema to `CredentialFields`.
- **CLI.** `ShareVerbTests` shares `--field OPENAI_API_KEY` and `kp:///Banking/Chase#OPENAI_API_KEY` as one field under its name, and `--field "Recovery codes"`, `--field otp` and `#Recovery%20codes` upload nothing. `PolicyVerbTests` shows `policy ls` naming `OPENAI_API_KEY` and refusing a file naming `Recovery codes`.
- **App.** `DesktopApprovalTests.A_custom_field_is_named_on_the_prompt_and_a_grant_for_it_serves_no_other_field`: the app's drawn prompt reads `OPENAI_API_KEY`, Allow once returns its sentinel, and after Allow for 1 hour a `password` request draws a prompt that exposes no sentinel.
- **Desktop process.** `verify-desktop-approval.sh` sets the same two fields with the shipped CLI; a shipped `keypaste-mcp --expose 'api/**'` asks the app held by `Keypaste.AppDriver hold`, whose drawn prompt reads `field=OPENAI_API_KEY`; Allow once returns exactly its sentinel, audited with that field and `granted_seconds` 0; on a second connection allowed for 1 hour, a `password` request draws its own prompt, and `Recovery codes`, `otp`, `KP2A_URL_1` and `URL` each get the fixed denial, `field: invalid` and no prompt.
- **Policy process.** `verify-policy-e2e.sh` phase G: through `keypaste agent` with stdin at EOF, a rule with `fields = ["OPENAI_API_KEY"]` releases exactly it as `policy` with no prompt while `password` reaches a prompt; a file whose rule names `Recovery codes` is reported not in force, so `OPENAI_API_KEY` reaches a prompt and nothing is released, and a `Recovery codes` request reaches nobody.
- **KeePassXC.** `verify-keepassxc-fields.sh` has KeePassXC import `api/OpenAI` with distinct values in its password, a protected `OPENAI_API_KEY`, `Recovery codes`, `otp` and `KP2A_URL_1`; a real `keypaste agent` and `keypaste-mcp --expose 'api/**'` return exactly the `OPENAI_API_KEY` KeePassXC wrote on Allow once, `Recovery codes`, `otp`, `KP2A_URL_1` and `URL` get the fixed denial with the agent asked once in all, five audit lines show the release and four `invalid` fields, and no other value reaches a result, the log or the terminal.
- **Agent process.** `verify-approval-e2e.sh` now also sets `OPENAI_API_KEY` and `Recovery codes` with the shipped CLI; `keypaste run --env-file` puts exactly the referenced field in the child and `#Recovery%20codes` starts nothing; a shipped `keypaste-mcp --expose 'api/**'` asks `keypaste agent`, whose prompt names `OPENAI_API_KEY` and whose Allow once returns exactly its sentinel; `Recovery codes`, `otp`, `KP2A_URL_1` and `URL` get the fixed denial with the agent asked no more; a `run` under `--allow-run` naming `kp:///api/OpenAI#OPENAI_API_KEY` is asked about as `api/OpenAI · OPENAI_API_KEY`; nine audit lines name the field or `invalid` and none holds a sentinel.

**Runs.** Every build and test ran in `dev.yml` from a Mac with no SDK:

| Run | Commit | Scope | Result |
|---|---|---|---|
| 36727825321 | `054872d` | build, Linux | Green |
| 36729452816 | `93cb244` | every lane, Linux | `CustomFieldReleaseTests.ARuleNamingTheField…` failed: its requests carried no client label, which a `"*"` rule requires; fixed |
| 36732809941 | `877e5ab` | every lane, three OSes | Backend green on all three and desktop on Linux and Windows. The refused-reference check matched "nothing was started" in keypaste's own refusal; fixed. macOS desktop hit F.28 |
| 36735692059 | `d03475a` | every lane with integration and compat, Linux | Green |
| 36736980306 | `8ed4b5e` | core, CLI and MCP with integration and compat, macOS | Green |
| 36736976209 | `8ed4b5e` | every lane with integration and compat, Linux and Windows | Windows: backend, desktop and integration green, and the fields gate with its new step passed under KeePassXC 2.7.12; the job then reached dev.yml's 30-minute limit in the projects gate, so the compat gates after it did not run. Linux: backend, desktop and integration green; compat failed at F.27's merge check, before the new step, which passed in 36735692059 |
| 36743345181 | `c1ea518` | core and CLI, three OSes | `dotnet format` refused the new source test's undisposed out value (CA2000); fixed |
| 36743396926 | `c1ea518` | `DesktopApprovalTests`, macOS | Green, 20 of 20 |
| 36743700009 | `d7d1e98` | core and CLI, three OSes | Green; on Linux Core.Tests 2243 of 2257 and Cli.Tests 760 of 764 passed, the rest skipped |

The app lane has not passed whole on macOS: in 36732809941 F.28 stopped the desktop step before its process gates, and 36736980306 did not select it. On macOS only the desktop test class ran green (36743396926); `verify-desktop-approval.sh` and the consistency tests did not run there.

## Decisions

- D-0388: what leaves by name is the four standard fields or an env-named custom field that is no standard name, and `request_credential` reads a custom field from the saved file only after an Allow or a rule.
- Binding only this step's code: `Vault.ReadSavedField` reads one custom field under `ReadSaved`'s saved-state check, so no custom field is answered from an unsaved or overwritten copy.

## Limits and follow-ups

- **Read before a run's prompt.** A run the owner asks about, such as the `run` tool's, resolves its references, custom fields included, into the owner's memory before it asks, so a denied run has still read them; T-8 states it beside T-18's standard fields.
- **KeePassXC-made vault, agent only (C.5a2).** On a vault KeePassXC made, the release is checked through `keypaste agent`; the app, the policy rule, `run --env-file`, the `run` tool and the share are checked on vaults the shipped CLI made.
- **Desktop gate on macOS (C.5a2).** F.28, found here and unrelated to this step's code, stops the macOS desktop step before `verify-desktop-approval.sh`.
- **Share not against the Worker (C.5a2).** The share checks use the fake server and `ShareCrypto`; no check runs `share --field` against the Worker on loopback and opens the link with `share-crypto.js`.
- **Schema admits more than the owner releases.** `request_credential`'s pattern lets through the `KP*_` prefixes and standard names in capitals, which the bridge and the owner refuse.
- **F.27 masks the new KeePassXC step.** The custom-field check in `verify-keepassxc-fields.sh` runs after F.27's merge check, so that flake skips it.
