# C.1c — Write keys onto entries

Completed 2026-10-03 on `task/c1c` above `d4f508f`, source only; dev 37089778378 at `d2ca47d`, then ci 37090813016 and app 37090814974 at `7ba7e3e`, the squash of that tree with the dev run's lines added to this record. The commit on `main` is `7ba7e3e`'s tree with these run lines.

## Amendments

The row as selected is in Git at `d4f508f`. On 2026-10-02 the founder settled three things it left open, and the rest are choices recorded here:

- **Fixtures (founder).** Gates and tests that need a variable of the `env/<project>` layout as a fixture seed it through `keypaste add env/<p>/<KEY>` (`legacy_var` in `scripts/lib/common.sh`) or `Vault.AddEntry` (`LegacyVariables` in the tests), so each keeps testing what it tested. Gates that check `env set` itself now assert the field on the home entry.
- **Demo (founder).** Only `docs/demo.md` changes: its vault is built with `keypaste add env/demo/STRIPE_KEY`, so the dialog the five transcript pages carry still names that entry. L.1 redoes the demo around fields.
- **Legacy (founder).** A project is legacy while its `env/<project>` group holds an untagged entry or a subgroup (D-0414), so a project `env set` creates is a project of tags.
- **Key names.** A new key must be a field a project releases (`EnvConvention.IsEnvNamedField`, no standard name), so `api_key`, `KPXC_X` and `URL` are refused with the rule named; an existing legacy key of any spelling is still updated in place.
- **Protection.** Every value the writers write is protected, updates included.
- **`--entry`** on `env set`, `env pull` and `env rm` names where a new key goes, which must be an entry tagged into the environment, and which copy to write or remove when several entries hold a key; a key one entry holds is updated there, so `env pull --entry` can also update keys living elsewhere. A key several entries hold is refused naming them unless `--entry` is one. In the app each row addresses its own entry, and a key two entries hold has no row, its cell saying so.
- **Home entry.** One that exists untagged, tagged only elsewhere, or twice is refused, naming it.
- **Unchanged values.** A write of the value a key already holds writes nothing, in `env set` as in `env pull`.
- **Which tag changes ask.** Only tags starting `env:`, from `env tag`, `env untag` and the entry pane (D-0415). The New item form already shows every field and tag before it creates the entry, and is unchanged.
- **The app.** Add offers the environment's home entry, the default, and its tagged entries; import puts new keys on the home entry.

## Evidence

Every build and test ran on GitHub; this Mac built nothing.

- **Core.** `EnvStoreTests` rewritten for the writer: a new key creating the tagged home entry with a protected field and no revision; `.env.staging` for another environment; a tagged key updated on its entry, protected, one revision; a legacy variable updated in place with its fields and history; an unchanged value writing nothing; `--entry` onto a tagged entry, refused off one or missing; a key on two entries; thirteen refused names; a case pair; an untagged or foreign home entry; one plan touching three entries costing each one revision; a field's removal kept in history; legacy removal, nested paths and duplicates. `EnvImportTests` over the new plan. `ProjectCatalogTests` gains the legacy rule, and `ProjectTagChangeTests` what a tag change says.
- **CLI.** `EnvVerbTests`, `EnvPullTests` and `EnvTagVerbTests` cover the messages, `--entry`, refusals, a declined and a confirmed tag and `--yes` without a terminal; the other suites seed the legacy layout through `LegacyVariables` or read the home entry.
- **App.** `EnvProfilesTests` reads, replaces and removes a tagged key on its entry and adds on the chosen entry or the created home entry; `EntryFieldsTests` cancels and confirms a project tag change on the pane; the consistency tests follow.
- **Gate.** [verify-keepassxc-projects.sh](../../scripts/verify-keepassxc-projects.sh) gained its C.1c half on a vault KeePassXC made, through the CLI and, on an untouched copy, through `Keypaste.AppDriver`'s new `env-add`, `env-set`, `env-rm`, `env-import` and `tag-add`/`tag-rm --decline` acts, KeePassXC reading every value, protection, tag and revision count. The other gates seed the earlier layout through `legacy_var`, and the keyfile and workflows gates read `env set`'s new key off the home entry.

| Run | Commit | Scope | Result |
|---|---|---|---|
| 37087383497 to 37089081500 | `9de112a` to `3095d15` | dev, Linux, both gates | Six failures, each fixed on the branch: two test compile errors, two stale `cref`s, CA1826 in the app, `env pull --entry` refusing keys other entries hold (now `--entry` only places new keys and picks among holders), a replace re-adding a key removed meanwhile (now refused, writing nothing), and a consistency test leaving a project tag unconfirmed |
| 37089778378 | `d2ca47d` | dev, Linux, both gates | Green: backend 3275 of 3295 passed and the rest skipped, App.Tests 881 of 885, Consistency 44, the desktop and integration gates, and compat under KeePassXC 2.7.6 with every C.1c step of the projects gate |
| 37090813016 | `7ba7e3e` | ci dispatch, every lane | Green on Ubuntu 24.04, macOS 15 and Windows 2025, with the aot publish |
| 37090814974 | `7ba7e3e` | app dispatch, every job | Green, the KeePassXC workflows gate on Ubuntu and Windows among them |

## Decisions

D-0413, D-0414 and D-0415 in [DECISIONS](../../DECISIONS.md). The choices under Amendments bind only this step's code.

## Limits and follow-ups

- Renaming an `env/` group in the app moves a home entry but not its tag, so its keys stay in the project the tag names; C.4 owns projects' names.
- `keypaste add` still writes a variable of the `env/` layout, which the gates use as their fixture.
- The guides describe the writers only where this step made a statement false; L.1 rewrites them for 0.5.0.
