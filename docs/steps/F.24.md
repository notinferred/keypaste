# F.24 — Expect every desktop package in the provenance self-test

Completed 2026-09-28 on `main` above `d85deb5`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

The row was opened on 2026-09-28, when `bash scripts/verify.sh` failed its `scripts` profile before the PRODUCT v1.8 commit. It was selected the same day, before `main` is pushed.

**Build:** On local `main` at `d2ced03`, `bash scripts/verify.sh` fails `verify-provenance.sh --selftest`. The case `app-genuine-staged-directory` expects "all 4 assets" (`verify-provenance.sh:283`). Since `2348d54` added the macOS `keypaste.app` zip to the app component, the staged directory holds 6. `ci.yml` and `release.yml` run the same self-test, so both fail once `main` is pushed.

**Verify (V-F.24):** the self-test accepts the genuine staged directory at the definition's current asset count, and still refuses one missing an asset.

## What changed for users

Nothing. Only the self-test changed.

## Evidence

**Mechanism.** The self-test builds the staged desktop directory from the release definition (`release-completion.sh --component app names`). Since `2348d54`, the definition names three packages, the Windows MSI, the Linux AppImage and the macOS app zip, each with its checksum. `verify-provenance.sh` verified all six and said "all 6 assets", while the case still looked for the hard-coded "all 4 assets".

**Repair.** The expected count is now the number of names the definition gives, and an empty list is refused, so declaring another package, such as 4.7a2's DMG, moves the expectation with it.

`stage_app` now takes an asset to leave unattested, as `stage` already does for the CLI. A new case, `app-asset-left-out-of-the-bundle`, leaves the macOS zip out of the attestation and must be refused with "is not attested".

**Local, Windows 10 Pro 19045.**
- `bash scripts/verify-provenance.sh --selftest` passed 20 of 20 cases; the unrepaired script failed 1 of 19.
- A copy that leaves nothing unattested failed exactly the new case: "expected refuse saying 'is not attested', got exit 0".
- `bash scripts/verify.sh` ran workflows, scripts, backend and integration and passed in 249 s; desktop was skipped because no changed path maps to it.

## Decisions

None.

## Limits and follow-ups

The CLI cases still expect a hard-coded "all 11 assets", so a change to the CLI targets needs the same derivation. Hosted `ci.yml` has not run the repair; it runs when `main` is pushed.
