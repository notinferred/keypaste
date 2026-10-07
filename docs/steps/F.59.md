# F.59 — Write settings files whole, keep others' recent vaults, and count a policy's hour monotonically

Completed 2026-10-07 at `4e4f498` on `task/f59`: the dispatched ci 37659966049 and app 37659980570 runs passed every job on all three runners. The integrated commit differs from that tree only in this record.

It had no STEPS row: the last review of keypaste against the founder's two engineering references confirmed these three gaps as small ([F.58](F.58.md)), and the founder asked for the review's findings to be fixed.

## What changed

- `ProjectMappings.Save` and `RecommendationDismissals.Save` write through `AtomicFile`, as every other settings file does, so a crash or power loss mid-write no longer leaves `projects.json` torn, which the desktop then refuses to replace, and the file is never briefly readable by others.
- The unlock screen reads `recent.toml` again before remembering or forgetting a vault, so a vault `keypaste import` added while the screen was open is kept.
- `PolicyRateLimiter` counts a rule's hour on the monotonic clock, so stepping the wall clock forward no longer gives a rule its `max_per_hour` allowance back early; across a sleep it holds the cap longer, which fails closed.

## Evidence

- `ProjectMappingsTests` and `RecommendationDismissalsTests.Saving_replaces_the_file_owner_only_and_leaves_nothing_beside_it`, `FirstRunTests.A_vault_another_writer_remembered_survives_this_screen_forgetting_and_remembering_others` and `PolicyGateTests.SteppingTheWallClockForward_DoesNotGiveTheAllowanceBack`. The two file tests also pass against the old code, which set owner-only mode after writing; what they cannot show is a write interrupted halfway, which `AtomicFileTests` covers for the writer itself.

## Limits and follow-ups

- The review's other eight gaps are BACKLOG investigation candidates ([F.58](F.58.md)).
