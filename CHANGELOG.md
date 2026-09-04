# Changelog

> docs/PRODUCT.md law 4.7: small releases, real changelogs, semantic versioning. Written for someone deciding whether to upgrade, not for someone reading commits. The release workflow refuses to publish a tag that has no section here.

Versions are the ones published at `https://dl.keypaste.com/v<version>/`. Every release carries a `SHA256SUMS` file and a per-asset `.sha256`, plus the corresponding source for that tag. The binaries are unsigned and un-notarized (O-0010).

## 0.1.0

**The first release meant to be installed.** Everything below was already true of `0.1.0-rc.1`; what changed is that the pipeline has now been run end to end, an asset has been downloaded and checked by hand off the published origin, and the install commands on the README and on keypaste.com name this version. The rc exists in the open at its own URL and stays there; nothing links it.

**What keypaste does at 0.1.0.** A local KDBX4 vault you own, environment variables injected into a child process without touching disk, and an MCP bridge that lets an agent ask for exactly one field of one entry - answered by a person, for a lifetime shown before they answer, with every call appended to a hash-chained local log. No account, no server, no network.

**Known limits, stated rather than discovered.** The binaries are unsigned and un-notarized, so nothing cryptographically ties them to this project (O-0010); the checksum beside each asset proves the bytes arrived intact, not who made them. There is no released GUI - the desktop app builds from source and is not in this release - and approval is a terminal prompt. Linux needs glibc 2.35 or newer; Alpine and other musl distributions are not covered. Intel Macs and Windows on ARM build from source. `THREATS.md` T-21 is the full account of what you trust by downloading instead of building, and building from source remains strictly stronger.

## 0.1.0-rc.1

First tag, and the first time anything has been published. It exists to run the release pipeline end to end rather than to be installed; treat it as a dry run with real bytes.

**Native binaries for four platforms.** `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64`, compiled with NativeAOT: one file each, no .NET runtime to install, about 10 MB per binary and a little under half the startup time of the framework-dependent build. `osx-x64` is not published - macOS 26 is the last release that runs on Intel Macs, and neither runner fleet still offers one to build on (O-0013). Intel Macs build from source.

**Every gate runs against the published binary, not a rebuild.** The release workflow deletes the ordinary build before testing, so the artifact that gets uploaded is the artifact that was proved: credential approval and refusal across two real processes, MCP over real pipes, the audit chain's tamper detection, environment injection, the demo transcripts, and - on all four platforms - a KDBX written by that exact binary opening in real KeePassXC.

**The Linux binaries need glibc 2.35 or newer** (Debian 12, Ubuntu 22.04 and later). This is checked on a clean Debian 12 container on every release. Alpine and other musl distributions are not covered.

Nothing about the vault format, the approval flow or the audit log changed in this release.
