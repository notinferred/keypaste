# C.6 — Keep no compatibility with keypaste's own releases before 0.5.0

Completed 2026-10-03 on `task/c6` above `680a73b`, source only; dev 37125590163 at `8766a27`.

## Amendments

C.6 replaced C.3, the move of a 0.3 project onto tagged fields. On 2026-10-03, when C.3 came up as the next step, the founder said keypaste should keep no compatibility with its own earlier app ("Nobody is using 0.3.0"; "we need to remove all the code about backwards compatibility with OUR OWN APP"), that KeePass compatibility is what matters, and that the work is one step with no children. `680a73b` ratified PRODUCT v1.11 with D-0416, retired C.3 and added this row; the row as selected is in Git there. Choices the row left open:

- **The `env` root stays reserved.** No group can be created as `env` at the top or renamed to or from it. That rule was never about 0.3: it stops a rename from moving a whole subtree into, or out of, the bridge's default exposure `env/**`, which this step keeps.
- **Projects reach the app from tags.** The sidebar and Env profiles list `ProjectCatalog`'s projects, tag-only ones included, each counted by its tagged entries; C.4 still builds the Projects screen.
- **The Secrets screen's "env variable" kind is gone.** It existed only for entries of the layout: such an entry is now a password entry like any other, without the Profiles card or the variable's rotate wording, and a group under `env` lists its subgroups' entries like any folder. Items' Import .env goes to the first project, since a group no longer names one.
- **`share kp://<p>/<e>/<KEY>`** shares the field named by the key on the one entry holding it, refusing none or several with the entries named.
- **Env requests always name their profile** (`env-profile`), and `run --session` says only that the owner did not answer when none does. Version-skew tolerance between keypaste processes otherwise stays, since every upgrade needs it.
- **`keypaste import`** counts the projects its source's entries are tagged into.
- **`ICredentialSource.RequiresLiveApproval`** has no default: its default was the path rule, and a protection answer should not fall back to no.
- **Found and fixed here.** Since C.1b a bridge run's reply, audit `entries` and release ledger listed a source entry once per key it supplied; they now list each once. The run prompt still shows each key with its entry. `EnvResolution.Judge`'s case-only refusal could no longer fire, every variable field being named in capitals, and is removed.

## Evidence

Every build and test ran on GitHub; this Mac built nothing. Five subagents edited separate file sets after the Core change, at the founder's allowance of up to five.

- **Core.** `TaggedEnvResolutionTests.Untagged_entries_under_env_alone_make_no_project`, `ProjectCatalogTests.Untagged_entries_under_env_make_no_project_and_no_environment`, `TagProtectionTests` (an untagged `env/acme/prod/DB` is not asked live, an entry tagged `env:acme:prod` is), `VaultCreateEntryTests.An_item_is_created_in_a_group_under_env_like_any_other`, `VaultOrganizeRefusalsTests.ANameUnderEnv_IsAsOrdinaryAsAnywhere`, `KdbxImportTests.AnEnvGroup_IsCopiedAsAPlainGroupUnderInto_KeepingItsEntriesTags`, `AuditChainTests.ASchemaOneRecordBeforeTheChain_IsABreak`, the approver protocol pinning `env-profile` requests, and `SessionAuthorityRunTests` expecting each source entry once. Fixtures that used the layout seed through `ProjectVariables`, as `env set` writes.
- **CLI, MCP and app.** `TaggedProjectRunTests` (a child never receives an untagged `env/billing/OLD_KEY`, which `get` still reads), `EnvTagVerbTests.Ls_reads_no_project_from_an_untagged_entry_under_env`, `ShellViewModelTests.Projects_come_from_tags_alone_and_an_untagged_env_entry_is_no_variable`, `DesktopApprovalTests.An_untagged_entry_under_a_prod_path_is_offered_the_timed_allow`, and the suites converted from the layout.
- **Gates.** [verify-keepassxc-projects.sh](../../scripts/verify-keepassxc-projects.sh) gained V-C.6 on vaults KeePassXC made with untagged `env/billing/OLD_KEY` and `env/billing/prod/DB`: `run` in every form, `env export`, `env diff` and `env ls` leave them out, `get` reads OLD_KEY, a real agent and `keypaste-mcp` release DB for the hour while prod-tagged entries stay once-only, and a child inheriting OLD_KEY fails the comparison. The run, write-back, organize, import, log-chain and run-session gates follow D-0416, and every gate that seeded the layout uses `project_var` or a plain entry.

| Run | Commit | Scope | Result |
|---|---|---|---|
| 37123777966 to 37124753023 | `541287b` to `42333c0` | dev, Linux, both gates | Four failures, each fixed on the branch: a nullable dereference in a hygiene test, two CLI export tests and an app typography test that seeded variables no field can now be (a case-only pair, `BAD-NAME`, a variable's title), and the run-session gate reading the `{PASSWORD}` placeholder its refusal names as a password prompt |
| 37125590163 | `8766a27` | dev, Linux, both gates | Green: backend 3244 of 3264 passed and the rest skipped, App.Tests 880 of 884, Consistency 43, the integration gates, and compat under KeePassXC 2.7.6 with the projects gate's C.6 half |

## Decisions

D-0416 in [DECISIONS](../../DECISIONS.md). The choices under Amendments bind only this step's code.

## Limits and follow-ups

- `scripts/exercise-desktop-upgrade.sh` builds its fixture with the published 0.3.0 CLI and reads the variable as an entry; once 0.5.0 is published, its `env set` writes a field, and the fixture follows.
- `env rm`, `env export` and `env diff` still say "profile" where `env ls` says "environment"; N.6 owns the wording.
- The published `v0.3.0` claims in README and on keypaste.com's home page describe that release; L.1 rewrites them for 0.5.0.
