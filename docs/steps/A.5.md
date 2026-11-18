# A.5 — No retired decision or open-question numbers outside the step records

Completed 2026-11-18.

The ledger the D- and O- numbers named is gone, so each citation pointed readers at nothing. Every reference in source, configuration, scripts, workflows and messages outside `docs/steps/` was deleted, or its sentence reworded to carry the reason on its own; `release-targets.json` lost the `id` of five defects that had only a D- number, and both signing `reason` values now say "no signing identity yet".

## Evidence
`git grep -nE '\b[DO]-[0-9]{4}\b|DECISIONS\.md'` outside `docs/steps/` and CHANGELOG finds nothing; `verify.sh compile` and `verify-release-matrix.sh` pass.

## Limits and follow-ups
- `Directory.Packages.props` keeps each package's earlier multi-line reason; condensing them to the one line law 9 asks for is left to the next change of each package.
- What O-0015 distinguished in the app's signing reason was not recoverable.
