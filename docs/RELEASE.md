# Release contract

This is the operational definition of a release. [STEPS](STEPS.md) owns delivery order; [CHANGELOG](../CHANGELOG.md) owns changes by published version. A successful build is evidence toward a release. It does not establish that someone can download, install and use it.

## Status vocabulary

| State | Required evidence |
| --- | --- |
| Implemented | Source and the relevant behavior checks pass. |
| Packaged | A versioned distributable exists, with its contents checked and its checksum recorded. |
| Published | That exact distributable is available at a permanent, anonymous public URL. |
| Installation-verified | The public download is verified, installed and exercised on the claimed OS and CPU architecture, with the result retained for that release. |

A desktop release is complete only when its supported targets are both published and installation-verified. A CI artifact with an expiry is not a public release. A source version string is not sufficient proof of release identity: record the tag, source commit and artifact hashes.

## Floor evidence

An OS floor is a claim to a stranger about their machine, so [`release-targets.json`](../release-targets.json) records one only with a named way it was established. This table owns those names: [`verify-release-matrix.sh`](../scripts/verify-release-matrix.sh) reads the values out of it, refuses a definition that uses one this table does not list, and refuses a value listed here that it has no rule for. Neither file carries a second opinion.

| `floor_evidence` | What it means | What the gate requires |
| --- | --- | --- |
| `container-check` | A check in this repository runs the binary on the floor and fails if it is wrong | An `os_floor`. Today only `linux-x64`: `release.yml` runs it on a clean Debian 12 and requires Alpine to refuse it. |
| `runner-image` | The floor is a property of the image the target is built on rather than of any run | An `os_floor`. NativeAOT and the self-contained app link against the build machine's glibc, so the runner choice is the floor. |
| `unverified` | The project asserts the floor from the build inputs; no run backs it | An `os_floor` and a `floor_caveat`, published verbatim on every page that states the floor. |
| `cited` | An external document states the floor; no run in this repository backs it | An `os_floor`, a `floor_citation` and a `floor_caveat`. Never counted as evidence the floor holds. |
| `none` | No floor is claimed | No `os_floor`. A target carrying one with this value is refused. |

**`cited` is not observation.** `macOS 13` and `Windows 10 1809` are .NET 10's supported-OS floors, not keypaste's: nobody has installed a keypaste release on either and looked. They are recorded so the download pages say something about the machine rather than nothing, and they carry a caveat for the same reason `linux-arm64` carries one. What establishes a floor is a clean machine, under R.0c and 4.7b.

## Current distribution — 2026-09-07

[`release-targets.json`](../release-targets.json) owns this matrix. The table below restates it for a reader; [`verify-release-matrix.sh`](../scripts/verify-release-matrix.sh) fails if the two disagree, and the workflows build from the file rather than from a list written out beside it (R.0a).

The public release is CLI/MCP **`v0.1.0`**, served from `https://dl.keypaste.com/v0.1.0/`. All four archive URLs returned HTTP 200 on this date; that availability check is not a new installation verification. `main` contains the [0.2.0-rc.1](../CHANGELOG.md#020-rc1) changes, including `setup`, and `Directory.Build.props` declares `0.2.0`; neither is in the download above.

| OS / CPU | RID | Public CLI/MCP | OS floor | How the floor was established | Desktop package in CI | Public desktop installer |
| --- | --- | --- | --- | --- | --- | --- |
| Windows x64 | `win-x64` | ZIP; unsigned | Windows 10 1809 or later | `cited` — .NET 10's floor; no run backs it | Self-contained ZIP | None |
| macOS ARM64 | `osx-arm64` | tar.gz; unnotarized | macOS 13 or later | `cited` — .NET 10's floor; no run backs it | Self-contained tar.gz | None |
| Linux x64 | `linux-x64` | tar.gz | glibc 2.35 | `container-check` — Debian 12 runs it, Alpine must not | Self-contained tar.gz; glibc 2.39 | None |
| Linux ARM64 | `linux-arm64` | tar.gz | glibc 2.35 | `unverified` — same build inputs as x64; no container check | None; RID declared but absent from package matrix | None |
| macOS Intel / Windows ARM64 | `osx-x64` / `win-arm64` | No public binary; source route documented | — | — | None | None |
| Linux musl | `linux-musl-x64` | No public binary; source route documented | — | — | None | None |

The floor column is the whole of what this project claims about a reader's machine, and the column beside it is why that claim is worth what it is. Only `linux-x64` is backed by a run. The desktop Linux archive carries a **higher** floor than the CLI's because `app.yml` builds on `ubuntu-24.04` while `release.yml` builds on `ubuntu-22.04`; 4.7a owns whether that moves, and 4.7b owns verifying whatever it ends up being.

Source routes are not promises of tested downloadable support. Minimum OS versions and native installation evidence must be specified before adding a supported target. [GitHub currently offers Intel macOS runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners); adding that target still requires project work.

## What exists

- [`release.yml`](../.github/workflows/release.yml) builds CLI/MCP natively on each of four targets. A tag must match the source version, point to a commit on `main` with passing CI, and have a changelog section. The workflow exercises the actual NativeAOT binaries, including real KeePassXC compatibility, before packaging. It checks Linux x64 on Debian 12 and rejects Alpine execution; the arm64 container equivalent is absent.
- The publish job rechecks transported hashes, includes corresponding source and license notices, allows only release files, and uploads to a versioned R2 prefix through [`publish-release.sh`](../scripts/publish-release.sh). That script is the only thing that writes to the bucket, and it uploads only after a listing has succeeded, parsed, named the prefix it was asked about and reported no objects — plus a second listing of an already-published prefix that must come back non-empty, so a denied or unreachable listing cannot read as an empty destination (F.4a). Both listings must come back with a key count that is actually a number: trusting `jq -e` to exit nonzero on empty output let an empty answer reach an upload on jq 1.6, which the publish runner ships (D-0106). Anything else refuses and writes nothing. [`verify-release-destination.sh`](../scripts/verify-release-destination.sh) holds it to that against a fake AWS CLI with no credentials, and runs on every push as well as in the guard job, so a manual dispatch exercises the same code a tag would. Manual dispatch builds and verifies without publishing. GitHub Releases are not the current download channel.
- [`app.yml`](../.github/workflows/app.yml) packages three desktop targets, checks the version, runs a vault selftest and retains archives for seven days. It does not publish them. The selftest renders no window; checking that native library files exist does not prove they load on first render. It now passes `-p:VersionSuffix` the way `release.yml` does, so a candidate tag reaches the desktop binary, its version check and its archive name intact. A tag requires a successful `app` run on the same commit before it may publish, which its `paths:` filter does not guarantee: **dispatch `app.yml` at the release commit and let it go green before tagging** when the push that landed it did not match those paths.
- [`verify-release-matrix.sh`](../scripts/verify-release-matrix.sh) holds `release-targets.json`, the workflows, the csprojs and the download pages to one answer, against nineteen fixture cases and a negative control. It runs in `ci.yml` and in `release.yml`'s guard. **It reads `site/public/index.html` at the checked-out ref, not what keypaste.com serves** — the site is deployed by hand with `wrangler deploy` and no workflow publishes it, so the live page can differ from a passing run. Verifying the public origin is requirement 4's job, and `install.yml` does the part of it that needs no credential.
- [`install.yml`](../.github/workflows/install.yml) runs the README install blocks weekly or manually for Linux x64, macOS ARM64 and Windows x64. It checks the installed CLI version and presence of MCP. It does not cover Linux ARM64, the subsequent setup/use instructions, desktop installation or automatic post-publication verification. A retained successful run is required to claim its result for a specific release.

## What only a tag reaches

`release.yml` builds and verifies on `workflow_dispatch` and publishes nothing, which makes a dispatch a real rehearsal for most of it. These parts are not rehearsed, because they are gated on `github.ref_type == 'tag'` or on the publish job it sets. **This is the list to check first when something in a release surprises you, and the list to add to when a new decision goes behind a tag gate.** F.4a fell open for two releases for exactly this reason (D-0104), and R.0a's own tag guard was the second instance.

| Gated on a tag | What it decides | Reachable another way? |
|---|---|---|
| Guard: the green-gates check | every required gate is green on the commit | **Yes** — [`require-green-gates.sh`](../scripts/require-green-gates.sh), driven by [`verify-green-gates.sh`](../scripts/verify-green-gates.sh) through a fake `gh` |
| Guard: the changelog lookup | a whole-line `## <version>` heading exists | **Yes** — [`require-changelog-section.sh`](../scripts/require-changelog-section.sh) |
| Guard: tag versus the source's version | a tag names the version the source declares | **Yes** — [`require-tag-matches-source.sh`](../scripts/require-tag-matches-source.sh) |
| Publish: the allowlist, the asset count and the checksums | what may become world-readable | **Yes** — [`require-release-assets.sh`](../scripts/require-release-assets.sh) |
| Guard: the three R2 secrets | that they are set, and that they reach the bucket this project publishes to | **Partly, and not by a fixture.** *That they are set* stays tag-only and unfixturable: asserting that a set variable is set proves nothing. *That they work* now runs on any dispatch of a repository that has them — [`publish-release.sh`](../scripts/publish-release.sh) `--check` makes the real listing and refuses unless its positive control finds objects at an already-published prefix, so a dead credential and a credential pointed at the wrong bucket are both refused before the first build rather than after four (D-0112) |
| `app.yml`: the prerelease suffix | `-p:VersionSuffix` reaching the desktop binary, its version check and its archive name | **No, and it cannot be.** A dispatch has no suffix to carry, so what is untested is not a decision but the absence of an input |
| Publish: the upload itself | where bytes land | Partly — [`publish-release.sh`](../scripts/publish-release.sh) refuses a destination it cannot verify empty, driven by [`verify-release-destination.sh`](../scripts/verify-release-destination.sh) against a fake `aws`. That R2 answers a listing in that shape needs a tag |
| Publish: the source tarball and the `SHA256SUMS` diff | that the archive and the aggregate agree | Not yet extracted; both are cheap and neither has a wrong answer on record |

[`verify-release-preflight.sh`](../scripts/verify-release-preflight.sh) drives the three middle rows over twenty cases, with two negative controls that reproduce answers this repository has actually given: an unanchored changelog match taking an rc heading for the release, and an asset count without its published floor taking a release that dropped a target.

Two rows stay tag-only and are marked so rather than left as a to-do: whether a secret is set, and a missing build input. Neither is a decision, and wrapping either in a script would buy a green fixture and no evidence. **The question underneath the first one did move**, because three set variables are not a working credential and only R2 can say otherwise. Everything else was extractable, which is the answer to the question worth asking about the rest — **a decision that only a tag can reach belongs in a script a fixture can drive**, and the two exceptions are the shape of thing that is not a decision at all.

## Requirements still to implement

These are release requirements, **not capabilities of the current pipeline**. Track their implementation and evidence in STEPS.

1. **One version and one supported matrix.** *Implemented by R.0a for the recording and reconciling half; the native exercise of each target is still R.0c's and 4.7b's.* [`release-targets.json`](../release-targets.json) records every advertised target with its runner, CPU, package format, OS floor and how that floor was established, separates advertised downloads from `source_only` routes, and keeps an append-only `published` record that the download pages are held to. `release.yml`, `app.yml` and the publication allowlist derive from it; the csprojs, the README, keypaste.com and the table above are held to it. A prerelease candidate now keeps its full version through the desktop build as well as the CLI's, and the changelog lookup is a whole-line match so `## 0.2.0` is no longer satisfied by a `## 0.2.0-rc.1` heading.
2. **Installable desktop packages.** Provide a signed Windows installer and executable payload, a signed/notarized macOS app in a stapled DMG, and a Linux AppImage or documented package with its runtime dependencies tested on the declared compatibility floor. The free downloads receive the same signing treatment. Browser downloads must retain normal OS security checks.
3. **Publisher verification.** Verify the downloaded Windows signature, expected publisher and timestamp; verify macOS signing, notarization and Gatekeeper assessment. Record actual clean-machine prompts. A valid Windows signature does not guarantee absence of SmartScreen warnings: new signed applications can lack reputation. [Microsoft documents this distinction](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation). Checksums detect corruption; a checksum beside an archive does not authenticate its origin. Document the independent signature/provenance verification available for Linux and other archives.
4. **Anonymous public installation.** After publication, fetch every promised asset from the public origin without repository credentials. Verify hashes and applicable signatures, install without an SDK, and check the reported release. Exercise the component's advertised workflows, including environment injection, credential approval and setup where supported by that version. For desktop releases, open and render the GUI on each native target, exercise its advertised vault operations and verify CLI/app interoperability; create the fixture through the CLI while GUI creation remains unshipped. A CLI patch release does not depend on unfinished desktop features. Keep the [desktop manual checklist](desktop.md#checking-a-build-by-hand) as explicit evidence where automation cannot observe behavior.
5. **Complete publication before promotion.** Introduce a release manifest/completion record and verify all public assets before updating download pages, package-manager entries or any future update channel. Keep the last verified release advertised until its replacement passes. Today the recursive R2 upload can stop partway through, and the destination guard then refuses that version on the next attempt — deliberately, because the alternative is overwriting objects somebody may already have fetched. Recovery currently means a new version number. Do not overwrite or reuse the partial version. A documented partial-publication recovery procedure and last-known-good promotion mechanism remain to be built.
6. **Upgrade, uninstall and recovery.** Test upgrading from the previous public version with real vaults and configuration. Preserve vault data, client connections and user settings; define what uninstall removes and retain user vaults. Test interruption and recovery. Retain previous installers and state whether older application versions can read data written by the new one; document restore-from-backup when they cannot. Define a verified update path before claiming automatic updates.
7. **Retained release evidence.** Record the source commit, tag, hashes, public URLs, signing identity, tested OS/CPU versions, installation and GUI results, upgrade/recovery results and known limits. Include persistent links to verification runs or reports. Installation checks must run after publication and continue periodically; expiry of an Actions artifact must not erase the release or its evidence.

Only advertise a target or change its public install URL after the applicable requirements have evidence. Preparing packages, signing integration and verification can proceed before the signing accounts are available; the public signing claim depends on successful account enrollment and verification of the delivered files.

## Delivery tasks

[STEPS](STEPS.md) owns the executable work and prerequisites. These references connect the release
requirements to implementation; all remain open until their own evidence passes.

| Delivery | Owning tasks |
|---|---|
| Existing data-preservation, approval and transport repairs before the CLI/MCP patch | F.1a–c, F.3a–c; required by R.0c |
| Existing desktop preference, lock, clipboard and automation checks | F.2a–d; required by 4.7b |
| Release destination error handling and first-party publisher metadata | F.4a/b; required by the relevant release paths |
| Executable version/platform definition, complete publication, provenance | R.0a, R.0b, 3.8 |
| Public CLI/MCP patch and native installation | R.0c |
| Desktop candidate packaging, installation and data-preserving upgrades | 4.7a, 4.7b, 4.7d |
| Signing identity and integration | 3.5a/b, 3.6a/b |
| Public desktop installation | 4.7c; complete daily-use product verified by R.1 |
| Chrome and Firefox store publication | 8.4a/b |
| Self-hosted and managed relay deployment | 5.2c, H.8 |
| Updated clients for the hosted pilot | 5.3d; observed pilot verified by R.2 |
| Phone distribution and upgrades | M.3 |
| Reviewed billing-capable client/service release | R.3 |
| Web client review and public deployment | W.2c |
| Package managers and additional architectures | 3.7a–c, 3.9a–d |

Every later feature that changes a client or service also needs its updated artifacts published
and exercised through the supported channel before its public-version claim can close. Passing
an earlier release row does not publish subsequent code automatically.
