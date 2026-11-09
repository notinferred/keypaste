# F.94 — List every package a release carries

Completed 2026-11-09 on `backlog-2026-11`, source only; CI has not run on it, so it names no runs.

## Amendments

The founder's decision of 2026-11-09: list all shipped packages, generated from what the release packages carry, including indirect dependencies, and check it at each release.

## Evidence

[third-party-notices.sh](../../scripts/third-party-notices.sh) writes `THIRD_PARTY_NOTICES.md`'s delimited section from the restored `project.assets.json` of `Keypaste.Cli` and `Keypaste.App` for `release-targets.json`'s targets, and `check` runs after the package steps of `release.yml` and `app.yml`. Generated locally from a locked restore of both solutions: 62 packages, 10 for the CLI and 61 for the desktop. Its 50 `Keypaste.App` packages match a built `keypaste-app.deps.json` exactly; the other rows are the CLI bridge's packages, .NET's runtime packs and the Windows SDK projection. `--selftest` passed under macOS's bash 3.2, the only bash on this machine, including a missing package, a wrong version, a wrong product and a stale row each failing the check. `third_party/licenses/` holds SPDX's `MIT.txt` and apache.org's `LICENSE-2.0.txt` as fetched on 2026-11-09, for packages whose licence is an expression with no file.

## Limits and follow-ups

- **No licence in the package.** `Yubico.NativeShims` 1.18.0 states no licence, copyright or project, and `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57 gives only a licence URL, which resolves to the Windows SDK's terms, where the hand-written section says MIT. The rows say what the packages say; the founder or a lawyer decides.
- **A dependency change** fails the desktop package job until the section is regenerated; the CLI's check runs only in `release.yml`, on a tag or a dispatch.
