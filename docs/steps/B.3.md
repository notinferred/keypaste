# B.3 — Keep one copy of the brand files and delete the finished minimize observer

Completed 2026-09-30 on branch `task/b3` above `a432ade`, source only; dev runs 36726998011, 36728602328, 36734081054, 36735992376, 36739690570 and 36740070332, and app run 36739701827.

## Amendments

None.

## Evidence

**Brand files.**

- `cmp` found each of the eight SVGs in `docs/design/assets/` byte-identical to its namesake in `assets/brand/`; the folder is deleted, and [outline-brand-marks.py](../../scripts/outline-brand-marks.py) writes the marks to `assets/brand/` alone.
- `docs/design/SKILL.md` was the design handoff's generic skill, which told the reader to explore its own folder and ask what to build. [The project skill](../../.claude/skills/keypaste-design/SKILL.md) points at BRAND, the handoff, the prototypes, the tokens, `assets/brand/` and the app's theme, and nothing in the older file is missing from it. It is deleted.
- Each rule in `docs/design/BRAND.md` was matched against [BRAND](../BRAND.md): the voice, palette, type scale, spacing, radii, depth, backgrounds, states, motion, icons, terminal characters, marks, their weights and their minimum sizes are all there. One still-true rule was not: the marks come from BRAND even on a prototype's screen, because the prototypes still draw the earlier monogram. BRAND's opening paragraph now says so, where it gives a prototype precedence for its own screen. The product description (PRODUCT's), the index of prototype files and the `lucide-static` icon font (both the handoff README's) and the "Swiss-precision grid" phrase, which describes BRAND's 4px base and 16px grid, were not carried. The file is deleted.
- `git grep` for each deleted path, across the documents, `.claude/skills`, the prototypes, `site/`, `src/`, `tests/`, `scripts/` and `.github/`, then found only the step records and archived rows, which are not edited. The handoff [README](../design/README.md)'s Assets and Files sections now point at `assets/brand/` and BRAND.

**Observer.** R.1a's eight acts include no minimize act, and D-0342 accepts an act only when it is made on a real desktop, which a runner is not, so R.1a cannot use `observe-desktop.yml`. No other open row names minimize-lock. The workflow's one result, run 35014816424 at `9b18750`, stays in the F.2b2 row of the [steps index](README.md), and the real Linux desktop acts in [F.2b3b](F.2b3b.md).

- `observe-desktop.yml` and `scripts/observe-minimize-lock.sh` are deleted, the script leaves `verify.sh`'s selftest and shellcheck lists, and ARCHITECTURE's workflow list no longer names them. [diagnostics](../diagnostics.md) did not name them.
- `Keypaste.MinimizeObserver` keeps only its `markers` scenario, N.12's clipboard gate in `app.yml`. Its six minimize scenarios and the idle-clock reader they printed are gone, the `ready` event keeps only the platform, display and clipboard readings, and `--scenario` accepts `markers` alone, which is what [verify-clipboard-markers.sh](../../scripts/verify-clipboard-markers.sh) passes. The project keeps its name, which `app.yml`, the desktop solution, `verify.sh`'s map and the app's `InternalsVisibleTo` carry.
- [desktop](../desktop.md)'s minimize section no longer names the deleted files. Its old condition, that macOS observation is needed if that desktop target is selected from BACKLOG, now holds, since the macOS app is in 0.5.0; the section says the real macOS acts are still needed.
- THREATS T-23 still called the real Linux desktop record outstanding, which F.2b3b made; its evidence sentence now credits F.2b3b and leaves the real macOS acts outstanding.

**Dev loop.** `verify.sh --since origin/main --plan` selected every lane, because `scripts/verify.sh` changed. No failure below touches a changed file, and F.28's four cases fail the same way at the base commit in dev run 36726742125. The `markers` lane runs only in `app.yml`, so the trimmed observer was checked there.

| Command, commit | Run | Result |
|---|---|---|
| `dev.sh --os all`, `508ca10` | 36726998011 | Scripts and workflows passed: every selftest, shellcheck and actionlint. Backend passed on all three (3,099, 3,108 and 3,100 tests). Desktop passed on Windows, 840 and 43 consistency tests, with integration and every compat gate up to projects before the job's 30-minute limit stopped it at keyfile; Linux failed one case (F.35); macOS failed four (F.28). |
| `dev.sh` (auto, Linux), `a3adbf8` | 36728602328 | Scripts, workflows, backend, desktop (839 and 43) and integration passed. Compat passed eight gates, then the fields gate lost a merged revision, which is F.27. |
| `dev.sh --target build --gates both --os all`, `a6e68e1` | 36734081054 | Every project built with warnings as errors on all three. Integration and all eleven compat gates passed on Linux and Windows; on macOS the signal gate failed (F.36), so its compat gates did not run. |
| `dev.sh --target build --gates compat --os macos`, `ca64c35` | 36735992376 | Green: the build and all eleven compat gates on macOS. |
| `dev.sh --target build --os all`, `b3552d7` | 36739690570 | Green: every project restored, formatted and built with warnings as errors on all three, the trimmed observer included. |
| `gh workflow run app.yml`, `b3552d7` | 36739701827 | Green: desktop format, analyzers and tests (839 passed, 4 skipped, and 43), KeePassXC workflows on Linux and Windows, first run and packaging on all three, and clipboard markers on Linux and macOS, whose ready, cleared and plain readings passed through the trimmed observer. |
| `dev.sh` (auto, Linux), `c7f31fc` | 36740070332 | Green: scripts, workflows, backend (3,099 passed, 18 skipped), desktop (839 and 43), integration and all eleven compat gates. |

## Decisions

None.

## Limits and follow-ups

- **Found here.** F.35, a manual lock's withdrawn request answered with no reply on Linux, once; the row joins two other Linux runs that ended just past `SessionHost`'s two-second stop grace. F.28, four env-launch cases that expect a terminal on macOS. F.36, a SIGTERM that killed `keypaste run` on macOS before it relayed, with F.37 its repair once F.36 measures the cause. ROADMAP's 0.5.0 build-infrastructure outcome and track order now name the four, since each keeps CI or the dev loop from staying green. F.27 recurred in run 36728602328. A dev run of every lane on Windows exceeds `dev.yml`'s 30-minute job limit.
- **Real macOS.** A minimize click and `Cmd+H` on a real macOS desktop are unobserved, and no row makes them. Whether R.1a's macOS leg or a row of its own does is the founder's choice.
- **Prototype text.** The Brand System prototype's footer reads "tokens: styles.css · assets/", naming the handoff's former folder as display text, not a link; the prototypes stay as delivered.
