# A.1 — The product builds, tests and releases without `site/`

Completed 2026-11-18.

keypaste.com is moving to its own repository, so nothing the product builds, tests or releases reads under `site/` any more. `site/` itself stays until A.2.

- **Vectors.** The share and crash vectors are the product's, in `tests/vectors/`; `ShareCryptoTests` and `CrashReportShapeTests` read them there, and the site's tests keep reading byte-identical copies in `site/test/`.
- **Release definition.** `release-targets.json` no longer lists the site's download page among the pages that advertise targets or disclose signing, defects and upgrade notices, so `verify-release-matrix.sh` holds only the README, SECURITY, CHANGELOG and RELEASE to the definition. `verify-demo.sh` no longer checks the download page's transcript.
- **Site checks.** The site's node tests left ci's checks job for a new `site.yml`, which runs on changes to `site/`, the vectors, the definition or the disclosure script, and does nothing when `site/` is absent. It also compares the two vector copies and runs `verify-site-disclosure.sh` over the download page at the commit, which keeps the defect and upgrade-notice disclosure there checked until A.2. A separate workflow rather than a job in ci was chosen because `verify-green-gates.sh` counts ci's and app's runs toward a release, and the site must count toward none. `verify-site-endpoint.sh` and `crash-reports.sh` run by hand against the site and are unchanged.

## Amendments
The founder directed the task on 2026-11-18 with KeePassLib, `src/Keypaste.Infrastructure` and `extension/` to become submodules at their current paths later, so references to them remain.

## Evidence
`ShareCryptoTests`, `CrashReportShapeTests`, `WorkflowRulesTests` and `verify-release-matrix.sh`'s 80 fixture cases pass with `site/` present and with it moved out of the tree, as does `verify.sh compile`.

## Limits and follow-ups
- Until A.2, the download page's install blocks, floors, signing disclosure and transcript are checked by nothing; only its defect and upgrade-notice disclosure is, by `site.yml`. The site's repository takes those checks.
- `scripts/outline-brand-marks.py` still writes the site's favicon and inline wordmarks, so it fails without `site/`; it is a by-hand tool, not a gate.
- `verify-site-disclosure.sh --selftest` fails one fixture on macOS, where `head -n -1` is not BSD `head`'s; Linux CI is unaffected and this step did not change it.
