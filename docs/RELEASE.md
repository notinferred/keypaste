# Release contract

[STEPS](STEPS.md) owns delivery order; [CHANGELOG](../CHANGELOG.md) records changes by version. A release requires evidence that users can download, install and exercise its advertised behavior.

## Status vocabulary

| State | Required evidence |
|---|---|
| Implemented | Source and relevant behavior checks pass. |
| Packaged | A versioned distributable exists, its contents are checked and its checksum is recorded. |
| Published | That distributable is available at a permanent, anonymous public URL. |
| Installation-verified | The public download is verified, installed and exercised on the claimed OS and CPU architecture, with retained results. |

A desktop release is complete when every supported target is published and installation-verified. Record the tag, source commit and artifact hashes. Expiring CI artifacts do not establish publication.

## Floor evidence

[release-targets.json](../release-targets.json) records OS floors using the evidence types below. [verify-release-matrix.sh](../scripts/verify-release-matrix.sh) checks this vocabulary and the requirements for each value.

| `floor_evidence` | Meaning | Gate requirement |
|---|---|---|
| `container-check` | A repository check runs the binary in a named compatibility image | An `os_floor`. Currently `linux-x64`: Debian 12 must run it and Alpine must refuse it. |
| `runner-image` | The floor follows from the build image | An `os_floor`. NativeAOT and the self-contained app link against the build machine's glibc. |
| `unverified` | The asserted floor has no supporting run | An `os_floor` and a `floor_caveat` reproduced on every page stating the floor. |
| `cited` | An external source states the floor | An `os_floor`, `floor_citation` and `floor_caveat`; this does not establish an observed installation. |
| `none` | No floor is claimed | No `os_floor`. |

`macOS 13` and `Windows 10 1809` are cited .NET 10 floors. Keypaste has no recorded installation on those minimum versions. The Linux x64 check establishes Debian 12 compatibility; success there alone does not prove the stated glibc 2.35 minimum. R.0c owns CLI/MCP installation evidence; 4.7b installed internal desktop candidates on runner images, not on these floors.

<a id="current-distribution--2026-09-07"></a>

## Current distribution

[release-targets.json](../release-targets.json) owns the matrix and publication dates. The public CLI/MCP release is `v0.3.0` at `https://dl.keypaste.com/v0.3.0/`. It includes `setup` and the changes listed under 0.3.0 in CHANGELOG. `v0.2.0` and `v0.1.0` remain available as superseded, immutable releases, each with the defects SECURITY.md names. Later source changes belong to Unreleased.

| OS / CPU | RID | Public CLI/MCP | OS floor | Floor evidence | Desktop package in CI | Public desktop installer |
|---|---|---|---|---|---|---|
| Windows x64 | `win-x64` | ZIP; unsigned | Windows 10 1809 or later | `cited`; .NET 10 floor, no installation on the floor | Self-contained ZIP; internal unsigned per-user MSI | None |
| macOS ARM64 | `osx-arm64` | tar.gz; unnotarized | macOS 13 or later | `cited`; .NET 10 floor, no installation on the floor | Self-contained tar.gz | None; deferred to Expansion (D-0201) |
| Linux x64 | `linux-x64` | tar.gz | glibc 2.35 | `container-check`; Debian 12 runs it, Alpine refuses it | Self-contained tar.gz; internal unsigned AppImage; glibc 2.39 | None |
| Linux ARM64 | `linux-arm64` | tar.gz | glibc 2.35 | `unverified`; same build inputs as x64, no container check | None; RID declared but absent from package matrix | None |
| macOS Intel / Windows ARM64 | `osx-x64` / `win-arm64` | Source route | No claim | `none` | None | None |
| Linux musl | `linux-musl-x64` | Source route | No claim | `none` | None | None |

The desktop Linux archive has a higher floor because `app.yml` builds on `ubuntu-24.04` and CLI `release.yml` uses `ubuntu-22.04`. Packaging task 4.7a owns the selected floor; 4.7b installed its internal candidate on `ubuntu-24.04` (D-0205), and public installation evidence belongs to 4.7c. Source routes do not establish tested binary support. New targets require explicit minimum versions and native verification, including Intel macOS despite [runner availability](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).

## What exists

[release.yml](../.github/workflows/release.yml) builds CLI/MCP natively on four targets. Tags must match the source version, name a commit on `main` with required green gates, and have a changelog section. The workflow tests the actual NativeAOT binaries, including KeePassXC compatibility, before packaging. Linux x64 is checked on Debian 12 with an Alpine rejection control; ARM64 has no equivalent container check.

Publication rechecks transported hashes, includes source and licences, writes the release manifest, attests every asset and the manifest, checks that attestation, and restricts files and counts before uploading through [publish-release.sh](../scripts/publish-release.sh). The manifest and Sigstore bundle are published with the release, so provenance survives artifact expiry; [verify-provenance.sh](../scripts/verify-provenance.sh) checks the public bytes after upload and on `install.yml`'s Linux leg (D-0138). `publish-release.sh` is the sole bucket writer. It requires a successful, parseable listing naming the requested empty prefix and a positive-control listing of an existing release. Both key counts must be numeric. Invalid, unavailable or denied responses refuse publication. [verify-release-destination.sh](../scripts/verify-release-destination.sh) exercises these cases without credentials in CI and the release guard. Manual dispatch builds and verifies without publishing. Downloads are served from R2; GitHub Releases are not the distribution channel.

[app.yml](../.github/workflows/app.yml) packages three desktop targets, checks versions, runs a vault selftest, attests archives built from a tag and retains them for seven days. On `win-x64` it also wraps the staged payload in the per-user MSI that [release-targets.json](../release-targets.json) declares, named and labelled internal and unsigned (D-0139). [verify-windows-installer.sh](../scripts/verify-windows-installer.sh) checks its name, version, publisher, scope and missing signature, then extracts it with `msiexec /a` without installing, and requires byte-identical payload files that pass `--selftest`. On `linux-x64` [build-linux-appimage.sh](../scripts/build-linux-appimage.sh) wraps the same payload in the declared internal, unsigned AppImage with appimagetool and a type2 runtime, both pinned by SHA-256 in the definition (D-0142, D-0203). [verify-linux-appimage.sh](../scripts/verify-linux-appimage.sh) never runs the image: it reads the squashfs at the runtime's ELF end with `unsquashfs`, checks name, version, labels and developer in the desktop entry and AppStream record, and requires byte-identical payload files that pass `--selftest`. The selftest does not render a window, and native-file presence does not prove successful loading. Before packaging, both workflows restore the Artifact Signing dlib pinned in the definition and run [sign-windows.sh](../scripts/sign-windows.sh) over the Windows files keypaste compiles and the MSI; while `signing.policy` is `none` it signs nothing, and `authenticode` without its identity refuses before writing. A `signing_rehearsal` dispatch of `app.yml` signs with a runner-trusted certificate, checks acceptance and refusals with [verify-windows-signature.sh](../scripts/verify-windows-signature.sh), and uploads and attests nothing (D-0204). Its MSI changed-byte check edits a Property value, because a byte in an unused compound-file sector lies outside the signature. Prerelease suffixes reach the binary, version check, archive name and installer. Publication requires a successful app run on the release commit; dispatch it before tagging if the push did not trigger the app workflow.

[verify-release-matrix.sh](../scripts/verify-release-matrix.sh) compares the definition, workflows, projects and download pages, with fixture cases recorded in STEPS R.0a. It checks the site file at the selected commit. Cloudflare deploys `site/` from pushes to `main`; [verify-site-disclosure.sh](../scripts/verify-site-disclosure.sh) checks the live page manually. Public-origin verification is covered by requirement 4 below.

[install.yml](../.github/workflows/install.yml) runs README install blocks weekly or manually on fresh runners for all four advertised targets. It verifies CLI version and MCP presence, then creates a vault and injects a value into a child. Linux ARM64 uses the definition's `install_block` substitution. Its `version` input can exercise an unadvertised candidate in `published[]`. These checks exclude later setup/use instructions and desktop installation, and are not automatically triggered by publication. [install-desktop.yml](../.github/workflows/install-desktop.yml) is dispatched with a tag and its app run: it refuses a candidate whose hash or attestation differs through [verify-desktop-candidate.sh](../scripts/verify-desktop-candidate.sh), installs the internal MSI or AppImage on a fresh runner and drives the installed app through [exercise-desktop-install.sh](../scripts/exercise-desktop-install.sh), where an unreachable check fails as unreached. It establishes nothing about public downloads. [upgrade-desktop.yml](../.github/workflows/upgrade-desktop.yml) is dispatched with a published CLI/MCP version: it builds three unpublished candidates at distinct numeric versions from one commit and drives [exercise-desktop-upgrade.sh](../scripts/exercise-desktop-upgrade.sh) through install, upgrade, an interrupted install, a refused downgrade and uninstall on a fresh runner, recording the user data's hashes across every transition (4.7d, D-0206). Retain a successful run for every claimed release result.

## What only a tag reaches

Manual dispatch rehearses the build and verification without publishing. The following checks or inputs require separate evidence because a tag or publish condition gates them.

| Gated path | Purpose | Other coverage |
|---|---|---|
| Green-gates guard | Required workflows passed on the commit | [require-green-gates.sh](../scripts/require-green-gates.sh), exercised by [verify-green-gates.sh](../scripts/verify-green-gates.sh) with a fake `gh` |
| Changelog lookup | A whole-line `## <version>` heading exists | [require-changelog-section.sh](../scripts/require-changelog-section.sh) |
| Tag/source comparison | Tag matches the source version | [require-tag-matches-source.sh](../scripts/require-tag-matches-source.sh) |
| Publication allowlist, count and hashes | Only declared release assets become public | [require-release-assets.sh](../scripts/require-release-assets.sh) |
| R2 credentials | Secrets exist and reach the intended bucket | Presence remains tag-only; `publish-release.sh --check` tests access on credentialed dispatches using an existing release as a positive control |
| Desktop prerelease suffix | Full version reaches binary, archive and installer | A dispatch has no tag suffix, so this input requires a candidate tag |
| Upload | Bytes reach the intended destination | Fake-AWS fixtures test destination refusal; actual upload requires publication |
| Source tarball and aggregate checksum comparison | Source archive and `SHA256SUMS` agree | These checks remain inside publication |
| Attestation | Every asset and the manifest are attested by `release.yml` for the tag | The OIDC token exists only in a tag run; [verify-provenance.sh](../scripts/verify-provenance.sh) `--selftest` drives its refusals with a fake `gh` |

[verify-release-preflight.sh](../scripts/verify-release-preflight.sh) exercises changelog, version and asset checks. Its negative controls reproduce an rc heading incorrectly satisfying a release lookup and an asset count accepting a dropped target. Extract testable tag-only decisions into scripts; credential access and tag-provided inputs still require operational evidence.

## Release requirements

STEPS records implementation status and acceptance evidence for these requirements.

1. One version and supported matrix. R.0a implements recording and reconciliation through `release-targets.json`. Each advertised target has a runner, CPU, package format, OS floor and evidence. Advertised binaries and `source_only` routes remain separate; `published` is append-only. Workflows and publication allowlists derive from the definition, while projects and download pages are checked against it. Preserve full prerelease versions and exact changelog-heading matches. Native installation evidence belongs to R.0c for the CLI/MCP, 4.7b for internal desktop candidates and 4.7c for public desktop downloads.
2. Installable desktop packages. Provide a signed Windows installer and payload and a Linux AppImage or documented package tested on its runtime floor. The signed and notarized macOS app in a stapled DMG is deferred to Expansion with the macOS desktop app (D-0201). Free downloads receive the same signing. Browser downloads retain normal OS security checks.
3. Publisher verification. Check Windows signature, publisher and timestamp. macOS signing, notarization and Gatekeeper assessment are deferred to Expansion with 3.5b (D-0201); until then the macOS CLI stays unsigned and un-notarized as disclosed. Retain clean-machine prompts. A signature does not guarantee SmartScreen reputation, as [Microsoft documents](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation). Same-origin checksums alone cannot authenticate a publisher, so every CLI/MCP asset from `v0.3.0` onward carries a build attestation checked by the procedure in [SECURITY](../SECURITY.md#verifying-a-release); an attestation authenticates origin and is not a signature or a reproducibility claim.
4. Anonymous public installation. Download every promised asset without repository credentials, verify hashes and applicable signatures, install without an SDK, and check its version and advertised workflows. Cover injection, approval and setup where released. Desktop checks require native rendering, vault operations and CLI/app interoperability; create fixtures through the CLI until GUI creation ships. Keep the [manual checklist](desktop.md#checking-a-build-by-hand) for behavior automation cannot observe. CLI patches do not depend on unfinished desktop features.
5. Complete publication before promotion. R.0b implements the completion record and public-asset verification. Verify every release before changing download pages, package-manager entries or update channels, and keep the last verified release advertised until its replacement passes. A partial upload requires a new version because published paths cannot be overwritten or reused. The recovery procedure and last-known-good promotion mechanism remain open.
6. Upgrade, uninstall and recovery. 4.7d owns this evidence for the internal candidates through `upgrade-desktop.yml`; the first real-release upgrade is a dated observation at 4.7c. Test upgrades with existing vaults, settings and client connections. Define uninstall behavior while retaining user vaults. Test interrupted upgrades and recovery, retain previous installers, record backward readability, and document backup restoration when required. Automatic updates require a verified update path.
7. Retained evidence. Record the commit, tag, hashes, URLs, signing identity, tested OS/CPU versions, installation and GUI observations, upgrade/recovery results and known limits. Keep durable links to runs or reports. Check installation after publication and periodically afterward; artifact expiry must not erase release evidence.

Advertise a target or change its public install URL only after the applicable requirements pass. Packaging and signing integration may be prepared before account enrollment, but signing claims require verified identities and delivered files.

## Delivery tasks

STEPS owns task status and prerequisites.

| Delivery | Owning tasks |
|---|---|
| Data preservation, approval and transport repairs before the CLI/MCP patch | F.1a–c, F.3a–d; required by R.0c |
| Desktop preference, lock, clipboard and automation checks | F.2a–d; required by 4.7b |
| Release destination errors and publisher metadata | F.4a/b |
| Advertised-download defect disclosure | R.0d |
| Shared release definition, complete publication and provenance | R.0a, R.0b, 3.8 |
| Public CLI/MCP patch and native installation | R.0c |
| Desktop packaging, installation and upgrades | 4.7a, 4.7b, 4.7d; macOS 4.7a2 and 4.7e in Expansion |
| Signing identity and integration | 3.6a/b; macOS 3.5a/b in Expansion |
| Public signed desktop and CLI downloads and the developer journey | 4.7c, R.1 |
| Chrome and Firefox publication | 8.4a/b |
| Self-hosted and hosted drop relay | 5.2c, H.8 |
| Observed small-team pilot and managed sync clients | R.2, 5.3d |
| Phone distribution and upgrades | M.3 |
| Reviewed team-plan release | R.3 |
| Web client review and deployment | W.2c |
| Package managers and architectures | 3.7a–c, 3.9a–d |

Each client or service change needs updated artifacts published and exercised through its supported channel before its public-version claim can close.
