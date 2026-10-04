# C.4 — Build the Projects screen on tags

Completed 2026-10-04 on `task/c4` above `c4b7c75`, source only; dev 37171205360 at `a568538`, then ci 37172413655 and app 37172414920 at `e21f382`, its squash with the dev run added to this record, and the journey and launch tests on all three systems in dev 37205763460 and 37206035009 at `d26fe05`. The commit on `main` is `e21f382`'s tree with the journey's wait for a toast and these run lines; ci and app did not run again for that one test file, which those dev runs cover.

## Amendments

The row as selected is in Git at `c4b7c75`. On 2026-10-03 the founder chose two things it left open, and the rest are choices recorded here:

- **Verifier (founder).** V-C.4 asks for a KeePassXC vault and a drive of the window that reads the automation tree and counts amber, which live in different tools. Two existing tools share it, as N.1b and C.1c did: `ProjectsJourneyTests` drives the window as launch composes it on a vault Core writes with V-C.4's layout, and the projects gate repeats every write through `Keypaste.AppDriver`'s view-model acts on a vault KeePassXC made, KeePassXC reading each back. The journey's own vault is not one KeePassXC made.
- **Wording (founder).** The screen, called Env profiles until now, is titled by its project, with "Projects" when none is open, and says "environment" wherever it said "profile" (D-0417). Core's refusals, such as "'Prod' is not a profile name", the prompt windows and the CLI keep their words for N.6, and identifiers such as `SelectedProfile` keep their names.
- **Remove entry** takes off every tag of the entry's own that puts it in that environment, so `env:billing` and `env:billing:dev` both go, and deletes neither the entry nor a field.
- **Add entry** offers every entry outside the recycle bin and keypaste's own groups that is not already in the environment, by path, narrowed by a filter and shown 50 at a time. Choosing one shows Core's `ProjectTagChange` lines, and nothing is written before Add to <environment>.
- **Remove key** names the environments its entry serves, as Replace does, since removing a field takes it out of every one of them.
- **Where a value lives** is a muted line under each value: its entry, then "also" and the other environments that entry serves, another project's as `project/environment`. The cell's own automation name is unchanged.
- **Malformed tags** are listed for the whole vault, as `env ls` warns of them.
- **Run's confirmation** names the entries its values come from, each once.
- **A project whose last entry leaves** stays open, as a new project does before its first entry or key.

## Evidence

Every build and test ran on GitHub; this Mac built nothing.

- **Core.** `ProjectCatalogTests.An_entry_names_every_environment_its_own_tags_put_it_in_and_nothing_else` holds `ProjectCatalog.EnvironmentsOf`: an entry in two environments of one project and one of another, a malformed tag, the recycle bin and keypaste's groups adding none.
- **App.** `EnvProfilesTests` lists each environment's entries with their keys and marks, the cells' entries, and the Replace and Remove prompts naming every environment a shared entry serves; Add entry says what it reaches, offers no member, bin or keypaste entry, leaves the file's bytes unchanged when cancelled and writes the tag when confirmed; Remove takes off both tags and keeps the entry, its field and an empty bin; and the screen names `env:acme-api:Prod`. `EnvLaunchThroughAppTests` holds the run card's sources, and `AccessibleDriveTests` the new controls' names.
- **Journey.** `ProjectsJourneyTests` launches the app as `App.Launch` composes it, unlocks through the unlock screen and presses controls where the window draws them. The automation tree names `web` and `billing`, each value's entry, Stripe in dev and staging, and the ignored `env:billing:Prod`. On `billing` it adds Queue to staging, takes Mail out of dev, replaces the shared value, whose form names both environments, adds a key with no entry chosen and imports a `.env`; the declined add, removal and replace and the refused `api_key` leave the file's bytes unchanged. Run starts `Keypaste.EnvReporter` with exactly dev's fields, through `cmd.exe` on Windows and a stand-in emulator on Linux, and `web`'s mapped Run gives its child `WEB_KEY`. Every frame it checks holds at most one amber element, the one primary where a form has one.
- **Consistency.** `EntriesTaggedOnTheProjectsScreenAreWhatTheCliRunsTests`: an entry the screen adds is in `env ls` and `keypaste run`, and one it takes out is not, while `get` still reads its field.
- **Gate.** [verify-keepassxc-projects.sh](../../scripts/verify-keepassxc-projects.sh) gained its C.4 half on a fifth vault KeePassXC made, through `Keypaste.AppDriver`'s new `projects`, `env-entry-add`, `env-entry-rm`, `env-export` and `env-set --decline` acts, with KeePassXC reading every tag, value and protection written and `keypaste run --env-file` resolving the exported references to exactly dev's fields.

| Run | Commit | Scope | Result |
|---|---|---|---|
| 37166112889 to 37167455186 | `27fef15` to `8e909c9` | dev, Linux, both gates | Three failures, each fixed on the branch: CA1416 on the journey's stand-in terminal, the journey reading a nullable `ChildResult` as a class, and two test failures: another project's environment drawn as `billing dev` by the name sanitizer, now the path sanitizer's `billing/dev`, and the journey reaching the sidebar before the window had drawn a frame |
| 37171205360 | `a568538` | dev, Linux, both gates | Green: backend 3245 of 3265 passed and the rest skipped, App.Tests 885 of 889 with `ProjectsJourneyTests` and its Run on the stand-in emulator, Consistency 44, the integration gates, and compat under KeePassXC 2.7.6 with the projects gate's C.4 half |
| 37172413655 | `e21f382` | ci dispatch, every lane | Green on Ubuntu 24.04, macOS 15 and Windows 2025, with the aot publish |
| 37172414920 | `e21f382` | app dispatch, every job | Green, the KeePassXC workflows and first-run gates on every runner they cover among them; its desktop tests run on Linux only |
| 37173559498 to 37205433010 | `e21f382` to `d26fe05` | dev, `ProjectsJourneyTests`, all three systems, then Windows | Linux and macOS green; on Windows the press on Run in a terminal started nothing and said nothing while the toast Save shows was up. The journey now waits for the toast to clear, and says what a press hit when it does nothing |
| 37205763460 | `d26fe05` | dev, `ProjectsJourneyTests`, all three systems | Green: Run reached `Keypaste.EnvReporter` through `cmd.exe` on Windows and the stand-in emulator on Linux, and macOS held the refusal |
| 37206035009 | `d26fe05` | dev, `EnvLaunchThroughAppTests`, all three systems | Green, the run card naming its source entry among them |

## Decisions

D-0417 in [DECISIONS](../../DECISIONS.md). The choices under Amendments bind only this step's code.

## Limits and follow-ups

- `ScreenRenderer`'s new frames, `46-env-add-entry`, `47-env-remove-entry` and `48-env-ignored-tag`, are drawn only with an output folder, which no workflow sets; the journey's frames are the amber evidence.
- README's `env-profiles.png` still shows the screen as Env profiles; L.1 retakes the screenshots for 0.5.0.
- The app does not rename a project, which the row did not ask for: renaming an `env/` group in Items still moves a home entry but not its tag (C.1c).
- Run is refused on macOS until E.1d, and on Linux only a stand-in emulator has run it (R.1a).
- On Windows the toast Save shows sat over Run in a terminal for its 2.8 seconds, so a press then did nothing; the journey waits it out. Whether a toast should cover a control at the window's foot is not settled here.
