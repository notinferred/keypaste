# Changelog

> CORE.md law 4.7: small releases, real changelogs, semantic versioning. Written for someone
> deciding whether to upgrade, not for someone reading commits. The release workflow refuses to
> publish a tag that has no section here.

Versions are the ones published at `https://dl.keypaste.com/v<version>/`. Every release carries a
`SHA256SUMS` file and a per-asset `.sha256`, plus the corresponding source for that tag. The
binaries are unsigned and un-notarized (O-0010).

## 0.1.0-rc.1

First tag, and the first time anything has been published. It exists to run the release pipeline
end to end rather than to be installed; treat it as a dry run with real bytes.

**Native binaries for four platforms.** `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64`,
compiled with NativeAOT: one file each, no .NET runtime to install, about 10 MB per binary and a
little under half the startup time of the framework-dependent build. `osx-x64` is not published -
macOS 26 is the last release that runs on Intel Macs, and neither runner fleet still offers one to
build on (O-0013). Intel Macs build from source.

**Every gate runs against the published binary, not a rebuild.** The release workflow deletes the
ordinary build before testing, so the artifact that gets uploaded is the artifact that was proved:
credential approval and refusal across two real processes, MCP over real pipes, the audit chain's
tamper detection, environment injection, the demo transcripts, and - on all four platforms - a
KDBX written by that exact binary opening in real KeePassXC.

**The Linux binaries need glibc 2.35 or newer** (Debian 12, Ubuntu 22.04 and later). This is
checked on a clean Debian 12 container on every release. Alpine and other musl distributions are
not covered.

Nothing about the vault format, the approval flow or the audit log changed in this release.
