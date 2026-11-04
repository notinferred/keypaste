# Step records

Each completed [STEPS](../STEPS.md) task gets one record here, named by its ID, written when its verifier passes.

Records begin with V.3a; earlier steps' history is in Git. The [rescope of 2026-09-19](rescope-2026-09-19.md) records what left the plan and how retained IDs changed meaning.

## Record template

```md
# <ID> — <outcome>

Completed <date> in `<sha>`, source only; runs <ci id>, <app id>.

## Amendments
Changes to the row as selected, with the founder's direction that made each; the row itself is in Git at the selecting commit.

## Evidence
Tests and gates by name, counts, runs and source SHAs.

## Limits and follow-ups
Gaps, defects found and their STEPS rows, BACKLOG options.
```
