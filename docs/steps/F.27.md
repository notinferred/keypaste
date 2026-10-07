# F.27 — Keep a revision KeePassXC's merge drops when two saves share a second

Completed 2026-09-30 on `task/f27` above `a432ade`, source only; dev runs 36739715281 (compat on every OS) and 36739919037 and 36739923011 (`VaultHistoryTests` on every OS) at `865faab`, and 36740307525 (every lane on Linux), 36740311608 and 36740477892 (the tests and the integration gate on Windows and macOS) at `da77b73`, which changes a comment and documents.

## Amendments

- The discovery found two causes, and the step repairs both: the merge loss the row names, and the fields gate's own check, which produced the failure the row cites.
- The probe's trials ran at `3b9e179`, in dev run 36727975315 on `task/f27` and 36728872732 on `task/f27-probe`, and were deleted once the diagnosis closed; their results are below.
- `ReadHistory_AfterAReopenCollapsesTheTimesOntoOneSecond_StillOrdersNewestFirst` made its tie from three keypaste updates in one second, which keypaste no longer writes. It became `ReadHistory_OfRevisionsSharingASecond_StillOrdersNewestFirst`, over a vault the vendored library writes as KeePassXC would, so the tiebreak (D-0229) keeps its test.
- `ReadHistory_AfterThreeUpdates_ReturnsEveryRevisionNewestFirst` now allows each revision to be a second ahead of the clock per revision (D-0387).
- The comments in the `KeePassInterop` members this step changed were brought to the one-line rule.
- After review, `AnEditOfAVersionStampedAhead_IsStampedAfterIt` covers every edit verb rather than `UpdateEntry` alone, and the fields gate tries ten times, not three, to save twice inside one second and prints the attempt that did.

## Evidence

**The merge loss.** On the probe, the fields gate ran eleven trials on fresh vaults: KeePassXC imports one entry, keypaste saves field `A`, `|` waits for the next second of the clock, and KeePassXC merges in a newer copy of the entry. Every trial's saves landed in the seconds planned. Dev run 36727975315:

| Trial | Saves | ubuntu-24.04, KeePassXC 2.7.6 | macos-15, KeePassXC 2.7.12 |
|---|---|---|---|
| same | `a1 a2` | 4 of 4 lost `a2` | 4 of 4 lost `a2` |
| apart | `a1 \| a2` | 0 of 4 lost anything | 0 of 4 lost anything |
| crowded | `a1 a2 \| a3` | 3 of 3 lost `a1` and kept `a3` | 3 of 3 lost `a1` and kept `a3` |

KeePassXC's `Merger::mergeHistory` keys the merged history the same way in 2.7.6 and 2.7.12: by each version's modification time in whole seconds, keeping one version per key. The version the merge moves into history is added only when no revision holds its second, and revisions sharing a second collapse to one.

**The gate's failure.** `history_xml | grep -qF` ran under `pipefail`: `grep -q` stops reading at its match, and while the export was still writing, the pipeline failed with SIGPIPE as if the value were missing. In dev run 36728872732 at `3b9e179`, `ubuntu-24.04` failed with the message ci run 36711854555 gave, "after the merge the history lost the value keypaste wrote", while the export read just before the check held `fields-before-merge` in the revision at `14:45:58`. That run's saves were in separate seconds, which the trials show KeePassXC keeps. The gate now captures the export before searching it.

**Without the repair.**
- Dev run 36729060746 at `b79ffa6`: the fields gate, now saving `MERGED` twice inside one second before the merge, failed with "lost the second of the two values saved in one second". Every gate before it passed; the projects and keyfile gates, which run after it, did not run.
- Dev run 36733129672 at `61ce784`, the capturing gate without the repair: the same failure, with the same gates passed and not run.
- Dev run 36729105716 at `b79ffa6`, `VaultHistoryTests` on `windows-2025`: 18 of 20 passed; `EditsInOneSecond_EachReachTheFileInASecondOfTheirOwn` and `AnEditOfAVersionStampedAhead_IsStampedAfterIt` failed.
- Dev run 36739702424 at `8340169` on `task/f27-fix-norepair`, the reviewed tests with `MarkEdited` stamping by the clock alone, `VaultHistoryTests` on `ubuntu-24.04`: 18 of 27 passed. All eight edit verbs of `AnEditOfAVersionStampedAhead_IsStampedAfterIt` applied their edit and failed on its stamp, the clock's second rather than the one after the shared second, and `EditsInOneSecond_EachReachTheFileInASecondOfTheirOwn` failed.

**With the repair.**
- Dev run 36733134101 at `a16474e`: every compat gate passed on `ubuntu-24.04` (KeePassXC 2.7.6), `macos-15` and `windows-2025` (2.7.12), and again on the first two in dev run 36735901834.
- Dev run 36732738935 at `10e9160`, every lane dev runs selected by the change: on `ubuntu-24.04` the backend tests passed 3,101 with 18 skipped, the desktop tests 839 with 4 skipped, the consistency tests 43 and the integration gate. The fields gate then failed on "the first of the two values" through the check this step replaced, while KeePassXC's merge warnings put each keypaste revision in a second of its own. On `macos-15` the backend passed 3,102, and four desktop tests failed that no CI run had executed there and that fail at `a432ade` too (dev run 36726742125), filed as F.28. On `windows-2025` the backend passed 3,110, the desktop 840, the consistency tests 43 and the integration gate, and the job reached its 30-minute limit during the compat gate.
- At `865faab`, after review: dev runs 36739919037 and 36739923011 passed all 27 `VaultHistoryTests` on `ubuntu-24.04`, `windows-2025` and `macos-15`, and dev run 36739715281 passed all eleven compat gates on the three, each fields gate saving twice inside one second on its first attempt.
- At `da77b73`, on `ubuntu-24.04` dev run 36740307525 ran every lane dev runs: the backend tests passed 3,108 with 18 skipped, the desktop tests 839 with 4 skipped and the consistency tests 43, and the integration gate, all eleven compat gates and the script and workflow checks passed. On `windows-2025` dev run 36740311608 passed the backend 3,117 with 11 skipped, the desktop 840 with 3 skipped, the consistency tests 43 and the integration gate. On `macos-15` dev run 36740477892, leaving out the app tests F.28 fails, passed the backend 3,109 with 17 skipped, the consistency tests 43 and the integration gate. The plans left `aot`, `appcompat`, `markers` and `package` to `ci.yml` and `app.yml`.

## Decisions

D-0387.

## Limits and follow-ups

- **Times can run ahead.** Edits to one entry faster than one a second are stamped up to a second apiece ahead of the clock, and a version merged in with a time ahead of this machine's clock pushes the next edit past it. Another copy edited within those seconds loses to keypaste's version in a merge that goes by time.
- **Other writers still share seconds.** KeePassXC's own edits, and files keypaste wrote before this step, can hold revisions in one second, which its merge still collapses; the fields gate's KeePassXC import and attachment do so on every run.
- **The same check shape elsewhere.** `verify-keepassxc-import.sh` pipes `kx ls` into `grep -q` twice under `pipefail`; its output is a few lines, and no failure from it has been seen.
