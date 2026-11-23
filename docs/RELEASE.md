# Releases

What keypaste has published, how a release is cut, and how an installation is verified. [release-targets.json](../release-targets.json) is the definition: it owns the matrix, publication dates and what each target claims. [CHANGELOG](../CHANGELOG.md) records changes by version.

## States

Every release claim uses one of four states, with version, platform and evidence.

| State | Required evidence |
|---|---|
| Implemented | Source and relevant behavior checks pass. |
| Packaged | A versioned distributable exists, its contents are checked and its checksum is recorded. |
| Published | That distributable is available at a permanent, anonymous public URL. |
| Installation-verified | The public download is verified, installed and exercised on the claimed OS and CPU architecture, with retained results. |

Expiring CI artifacts do not establish publication. A source build, a CLI demo or a green pipeline does not establish a release. Every advertised behavior needs evidence from the version that contains it.

keypaste.com's product labels map onto these states:

- **Beta**: at least one part is Published (pre-1.0), and its page says which parts are only Implemented.
- **Building**: Implemented, at most Packaged internally, nothing Published.
- **Planned**: not yet Implemented.

<a id="current-distribution--2026-09-07"></a>
<a id="current-distribution"></a>

## Published releases

The public release is the CLI and MCP bridge `v0.3.0` at `https://dl.keypaste.com/v0.3.0/`. Later source changes are under Unreleased in CHANGELOG and ship as 1.0.0.

| Release | Implemented | Packaged | Published | Installation-verified |
|---|---|---|---|---|
| `v0.3.0` CLI and MCP bridge | Tag `v0.3.0` | Four archives with checksums, a release manifest and a Sigstore attestation bundle | 2026-09-16; every asset fetched anonymously from the origin and its attestation checked | On runner images, not on the OS floors: `install.yml` ran the published install blocks on `ubuntu-24.04` (x64), `ubuntu-22.04-arm` (arm64), `macos-15` and `windows-2025` on 2026-09-16 and 2026-10-05 (F.50), each creating a vault and injecting a value into a child |
| `v0.2.0` | Tag | Four archives with checksums | 2026-09-10; superseded, with the defects SECURITY names | On runner images on all four targets (R.0c) |
| `v0.1.0` | Tag | Four archives with checksums | 2026-07-28; superseded, with the defects SECURITY names | On runner images for Linux x64, macOS and Windows; Linux arm64 not verified |

Superseded releases stay available and immutable. 0.4.0 and 0.5.0 are never published. `v0.5.0-rc.1` is an unadvertised release candidate.

**The desktop app has no release.** Its internal packages are Packaged. Its first release is 1.0.0, for Windows x64, macOS arm64 and Linux x64, and the CLI/MCP ships with it at the same number on its four targets. A desktop release is complete when every target it advertises is Published and Installation-verified.

## Distribution matrix

| OS / CPU | RID | Public CLI/MCP | OS floor | Floor evidence | Desktop package in CI | Public desktop installer |
|---|---|---|---|---|---|---|
| Windows x64 | `win-x64` | ZIP; unsigned | Windows 10 1809 or later | `cited`; .NET 10 floor, no installation on the floor | Self-contained ZIP; internal unsigned per-user MSI | None; the publication path exists and refuses an unsigned package; 4.7c2 publishes |
| macOS ARM64 | `osx-arm64` | tar.gz; unnotarized | macOS 14 or later | `cited`; .NET 10 floor, no installation on the floor | Self-contained tar.gz; internal `keypaste.app` in a DMG, signed with keypaste's Developer ID (app run 44921556401) and unnotarized, checked from the mounted image | None; the internal DMG is Packaged (app run 36738215762) and its install and upgrade were rehearsed on `macos-15` (4.7e1, 4.7e2); 3.5b notarizes it and 4.7c2 publishes |
| Linux x64 | `linux-x64` | tar.gz | glibc 2.35 | `container-check`; Debian 12 runs it, Alpine refuses it | Self-contained tar.gz; internal unsigned AppImage; glibc 2.39 | None; the publication path exists and refuses an unsigned package; 4.7c2 publishes |
| Linux ARM64 | `linux-arm64` | tar.gz | glibc 2.35 | `unverified`; same build inputs as x64, no container check | None; RID declared but absent from package matrix | None |
| macOS Intel / Windows ARM64 | `osx-x64` / `win-arm64` | Source route | No claim | `none` | None | None |
| Linux musl | `linux-musl-x64` | Source route | No claim | `none` | None | None |

- The desktop Linux package has a higher floor because `app.yml` builds on `ubuntu-24.04` and the CLI's `release.yml` on `ubuntu-22.04`.
- Nothing desktop is published while `signing.policy` is `none`.
- A source route does not establish tested binary support.
- A new target needs an explicit minimum version and native verification, Intel macOS included.

## Floor evidence

Each target's OS floor in the definition carries one of these `floor_evidence` values. [verify-release-matrix.sh](../scripts/verify-release-matrix.sh) reads this table and enforces each value's requirement.

| `floor_evidence` | Meaning | Gate requirement |
|---|---|---|
| `container-check` | A repository check runs the binary in a named compatibility image | An `os_floor`. Currently `linux-x64`: Debian 12 must run it and Alpine must refuse it. |
| `runner-image` | The floor follows from the build image | An `os_floor`. NativeAOT and the self-contained app link against the build machine's glibc. |
| `unverified` | The asserted floor has no supporting run | An `os_floor` and a `floor_caveat` reproduced on every page stating the floor. |
| `cited` | An external source states the floor | An `os_floor`, `floor_citation` and `floor_caveat`; this does not establish an observed installation. |
| `none` | No floor is claimed | No `os_floor`. |

`macOS 14` and `Windows 10 1809` are cited .NET 10 floors, and keypaste has no recorded installation on them. The Linux x64 check establishes Debian 12 compatibility, not the stated glibc 2.35 minimum. Every recorded installation, CLI/MCP and internal desktop alike, ran on runner images above these floors.

## Cutting a release

1. Refresh the vendored Public Suffix List with [update-public-suffix.sh](../scripts/update-public-suffix.sh), as an ordinary commit. A tag refuses a list more than 60 days old.
2. Set the source version and add a whole-line `## <version>` heading to CHANGELOG that matches it exactly, prerelease suffix included.
3. On `main`, dispatch `ci.yml` with `-f tools=true` at the commit; a full weekly run also counts. Make sure `app.yml` has a successful run at the same commit, dispatching it if the push did not.
4. Once both are green, push the tag `v<version>`. `release.yml` runs the [checks only a tag reaches](#checks-only-a-tag-reaches), builds and tests the binaries, publishes, attests, and verifies what the public origin serves.
5. Add the version to `published[]` in the definition by hand, unadvertised. A green run never promotes itself.
6. Dispatch `install.yml` with that `version` to exercise the published download before any page points at it.
7. Once it passes, mark the version advertised and update [install.md](install.md) and the other download pages. Until then, the last verified release stays advertised.
8. Record the result under [Published releases](#published-releases), keeping a successful run for every claimed result.

A release that includes the desktop app also needs the [manual desktop checklist](desktop-checklist.md), run on the packages being released, before step 7.

Published paths are immutable. An upload that stops partway uses up its version, so recovery is a new version number. A recovery procedure and last-known-good promotion wait until after the first desktop release.

## Release requirements

Advertise a target, or change its public install URL, only after these pass.

1. **One version and supported matrix.** The definition records each advertised target's runner, CPU, package format, OS floor and evidence, apart from `source_only` routes. Each component records whether it `publishes`, its origin, and the workflow whose attestation covers it. `published` is append-only, keyed on version **and** component. Workflows and publication allowlists derive from the definition; projects and download pages are checked against it.
2. **Installable desktop packages.** A signed Windows installer, a Linux AppImage tested on its runtime floor, and a signed, notarized macOS DMG of `keypaste.app` (3.5b). A package is published only where the definition records it signed under a policy that is not `none`. Until then it is internal, unsigned and refused by the publication path.
3. **Publisher verification.** Check the Windows signature, publisher and timestamp, and macOS signing, notarization and Gatekeeper assessment (3.5b). Until a published release carries them, the macOS CLI stays unsigned and unnotarized, as disclosed. A signature does not guarantee [SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation). Every CLI/MCP asset from `v0.3.0` on carries a build attestation ([SECURITY](../SECURITY.md#verifying-a-release)); it authenticates origin and is not a signature or a reproducibility claim.
4. **Anonymous public installation.** Download every promised asset without credentials, verify hashes and signatures, install without an SDK, and exercise the released workflows: injection, approval and setup. Desktop checks cover native rendering, vault operations and CLI/app interoperability, such as creating a vault in the app and opening it with the CLI. CLI patches do not depend on unfinished desktop features.
5. **Complete publication before promotion.** [release-completion.sh](../scripts/release-completion.sh) writes the completion record and verifies the public assets before any download page, package-manager entry or update channel changes.
6. **Upgrade, uninstall and recovery.** Test upgrades with existing vaults, settings and client connections, and interrupted upgrades. Uninstall keeps user vaults. Keep previous installers and record backward readability. Automatic updates need a verified update path. 4.7d and 4.7e2 hold this evidence for internal candidates; 4.7c2 records the first real-release upgrade.
7. **Retained evidence.** Record the commit, tag, hashes, URLs, signing identity, tested OS/CPU versions, installation and GUI observations, upgrade results and known limits, with durable links. Artifact expiry must not erase release evidence.

A signing claim needs a verified identity and delivered files. Package managers and agent marketplaces carry a version only after it is published and verified. Marketing and announcements are not release gates.

<a id="what-exists"></a>

## Pipeline

### CLI and MCP build

[release.yml](../.github/workflows/release.yml) builds the CLI/MCP natively on four targets and tests the NativeAOT binaries, KeePassXC compatibility included, before packaging. A dispatch builds and verifies without publishing. Each archive's `THIRD_PARTY_NOTICES.md` must list exactly the NuGet packages it ships: after a dependency change, restore both solutions and run [third-party-notices.sh](../scripts/third-party-notices.sh) `generate`.

### Publication

[publish-release.sh](../scripts/publish-release.sh) is the only writer to the R2 bucket; GitHub Releases are not a distribution channel.

- It rechecks hashes, adds source and licences, writes a manifest and Sigstore bundle per component into one version prefix, and limits which files upload.
- It refuses unless a listing shows the prefix empty and a positive control lists an existing release. [verify-release-destination.sh](../scripts/verify-release-destination.sh) tests these refusals without credentials.
- Desktop packages are not rebuilt: `release.yml` takes the tag's `app.yml` run, holds each package to [verify-desktop-candidate.sh](../scripts/verify-desktop-candidate.sh), then attests what it publishes.
- [verify-provenance.sh](../scripts/verify-provenance.sh) checks the public bytes after upload and on `install.yml`'s Linux leg.
- `release.yml` refuses to publish while the definition holds a macOS target, since only a finished `notarize.yml` run holds notarized macOS packages (4.7c2).

[verify-release-matrix.sh](../scripts/verify-release-matrix.sh) holds the definition, workflows, projects and download pages to each other. keypaste.com's repository checks its own download page.

### Desktop packages

[app.yml](../.github/workflows/app.yml) packages three desktop targets, runs a vault selftest, attests archives built from a tag and keeps them seven days. Every payload carries the NativeAOT `keypaste` beside the app, so a client connected from the app can start `keypaste mcp`; from an AppImage, it reaches it through `~/.local/bin/keypaste`.

| Target | Package | Checked by |
|---|---|---|
| `win-x64` | Internal, unsigned per-user MSI | [verify-windows-installer.sh](../scripts/verify-windows-installer.sh), without installing: metadata, missing signature, payload, bridge and the per-user PATH entry |
| `linux-x64` | Internal, unsigned AppImage from [build-linux-appimage.sh](../scripts/build-linux-appimage.sh) | [verify-linux-appimage.sh](../scripts/verify-linux-appimage.sh), without running it: desktop entry, AppStream record, payload, and that `AppRun cli` and `AppRun cli mcp` start `keypaste` and the bridge |
| `osx-arm64` | Internal `keypaste.app` in a DMG, from [build-macos-app.sh](../scripts/build-macos-app.sh) and [build-macos-dmg.sh](../scripts/build-macos-dmg.sh) | `build-macos-app.sh`, launching through LaunchServices with `--selftest`; [verify-macos-cli-link.sh](../scripts/verify-macos-cli-link.sh) for the `/usr/local/bin/keypaste` link |

Each check requires byte-identical payloads that pass `--selftest`. The selftest renders no window, and a native file's presence does not prove it loads.

- **File association.** Each package declares `.kdbx` without taking its default: an Alternate handler in macOS's `Info.plist`, the MIME type `application/x-keepass2` in the Linux `.desktop` entry, and the per-user ProgId `keypaste.Vault` in the MSI, removed on uninstall. Only an installation shows whether the desktop then hands the file over. On Linux it reaches a file manager only through an AppImage integration tool.
- **ICU.** The desktop uses the system's ICU. On Linux without libicu it starts with invariant globalization: ISO dates and code point sorting. [verify-no-icu.sh](../scripts/verify-no-icu.sh) holds this.
- **Notifications** (Q.14) are Implemented, and no person has seen one. On Windows only the MSI posts them, through its Start menu shortcut's AppUserModelID `com.keypaste.app`, and the locked notice is always the app's own window. On macOS only an app bundle posts them, Focus can hold them, and whether an unsigned bundle can be given permission is untried. On Linux they need a notification server that shows actions.

### Signing and notarization

The definition's `signing.policy` is `none` for both components: no signing identity yet.

- **macOS.** The `signing.macos` policy is `developer-id`. [sign-macos.sh](../scripts/sign-macos.sh) signs the payload, the bundle inside-out and the DMG under the hardened runtime. A tag or dispatch of `app.yml`, and every `release.yml` run, refuses without the identity; other runs, such as a fork's pull request, sign ad hoc. Neither workflow notarizes. [notarize.yml](../.github/workflows/notarize.yml) submits the app run's bundle, DMG and code, and the release run's CLI code; a later dispatch collects Apple's verdicts, staples the bundle and DMG, and uploads the notarized packages. The archives' bare Mach-O cannot hold a ticket, so Gatekeeper looks it up online. While any verdict is pending, it uploads nothing and is dispatched again later. [verify-macos-signature.sh](../scripts/verify-macos-signature.sh) checks the result.
- **macOS provisioning profile.** A Developer ID `keypaste.app` embeds [embedded.provisionprofile](../packaging/macos/embedded.provisionprofile), Apple's Developer ID profile for `com.keypaste.app`, and takes the keychain entitlements it authorizes, which Touch ID needs ([TN3137](https://developer.apple.com/documentation/technotes/tn3137-on-mac-keychains)). The profile holds nothing secret: every value in it is readable from the signed app. `sign-macos.sh` refuses a profile for another app or team, or one that has expired, and the bundle's `--selftest` fails when the keychain does not admit it. It also grants time-sensitive notices, which `sign-macos.sh` passes on. The profile made on 2026-11-23 expires on 2044-11-18; renewing it is a commit replacing the file before then, and `security cms -D -i packaging/macos/embedded.provisionprofile` shows its dates; no other build takes these entitlements, since a program that has them without a profile does not start.
- **Windows.** [sign-windows.sh](../scripts/sign-windows.sh) signs the Windows files keypaste compiles and the MSI, and signs nothing while `signing.policy` is `none`. A `signing_rehearsal` dispatch of `app.yml` signs with a runner-trusted certificate, checks it with [verify-windows-signature.sh](../scripts/verify-windows-signature.sh), and uploads nothing.

## Checks only a tag reaches

A dispatch rehearses the build without publishing. These paths need a tag, so they need separate evidence.

| Gated path | Other coverage |
|---|---|
| Green gates: required workflows passed on the commit, ci's with its real-tools jobs | [require-green-gates.sh](../scripts/require-green-gates.sh), exercised by [verify-green-gates.sh](../scripts/verify-green-gates.sh) |
| A whole-line `## <version>` CHANGELOG heading | [require-changelog-section.sh](../scripts/require-changelog-section.sh) |
| Tag matches the source version | [require-tag-matches-source.sh](../scripts/require-tag-matches-source.sh) |
| Public Suffix List at most 60 days old | [require-public-suffix-fresh.sh](../scripts/require-public-suffix-fresh.sh) `--selftest` |
| Only declared assets, counts and hashes become public | [require-release-assets.sh](../scripts/require-release-assets.sh) |
| R2 credentials reach the intended bucket | `publish-release.sh --check` on credentialed dispatches |
| Desktop prerelease suffix reaches binary, archive and installer | None; needs a candidate tag |
| Upload reaches the intended destination | Fake-AWS fixtures test refusal only |
| Source tarball and `SHA256SUMS` agree | None outside publication |
| The tag's `app.yml` run is found and its artifacts downloaded | `require-green-gates.sh --run-id`, exercised by `verify-green-gates.sh` |
| Every published asset and manifest is attested | [verify-provenance.sh](../scripts/verify-provenance.sh) `--selftest` |

[verify-release-preflight.sh](../scripts/verify-release-preflight.sh) exercises the changelog, version and asset checks with negative controls. Move testable tag-only decisions into scripts; credential access and tag inputs still need operational evidence.

## Installation verification

An installation is verified from the published bytes, checked against their hashes and provenance, on each claimed OS and CPU. [Published releases](#published-releases) records the results.

| Workflow | Installs | Establishes |
|---|---|---|
| [install.yml](../.github/workflows/install.yml), weekly or dispatched | The [install.md](install.md) blocks on fresh runners for all four CLI/MCP targets | The version and the bridge, then a new vault and a value injected into a child. Its `version` input can exercise an unadvertised candidate in `published[]`. Not later setup or use, and not the desktop |
| [install-desktop.yml](../.github/workflows/install-desktop.yml), dispatched with a tag and its app run | The internal MSI, AppImage or DMG on a fresh runner, after [verify-desktop-candidate.sh](../scripts/verify-desktop-candidate.sh) checks hash and attestation | [exercise-desktop-install.sh](../scripts/exercise-desktop-install.sh) and [exercise-desktop-acts.sh](../scripts/exercise-desktop-acts.sh) drive the installed app, with screenshots in the `acts-<rid>` artifact. Nothing about public downloads |
| [upgrade-desktop.yml](../.github/workflows/upgrade-desktop.yml), dispatched with a published CLI/MCP version | Three unpublished candidates at distinct versions from one commit | [exercise-desktop-upgrade.sh](../scripts/exercise-desktop-upgrade.sh) runs install, upgrade, an interrupted install, a refused downgrade and uninstall, hashing user data at every step (4.7d, 4.7e2) |

Either desktop workflow can run as a rehearsal before a candidate is tagged, on a successful app run's packages at its own commit. A rehearsal verifies no attestation and is never release evidence.

No workflow observes native rendering, a browser download's SmartScreen or Gatekeeper prompt, or a person's use. The [manual desktop checklist](desktop-checklist.md) covers them.
