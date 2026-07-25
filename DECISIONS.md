# DECISIONS.md — engineering decision log

> One entry per decision that a future contributor (or a future you) would otherwise have to
> reverse-engineer. CORE.md decides *what* keypaste is; this file records *how*, and why.
> Append entries; do not rewrite history. Supersede an entry by adding a new one that says so.

---

## D-0001 — Project naming: PascalCase projects, kebab-case binaries

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted

`PLAN.md` and `prompts.md` name the packages `keypaste-core`, `keypaste-cli`, `keypaste-mcp`. C#
namespaces cannot contain hyphens, and a `keypaste-core.csproj` yields the root namespace
`keypaste_core`, which fights every analyzer naming rule forever.

Projects are therefore `Keypaste.Core`, `Keypaste.Cli`, `Keypaste.Mcp`, while the *shipped binaries*
keep the roadmap's names: `keypaste` and `keypaste-mcp`. Kebab-case survives where it is
user-visible, PascalCase where it is code. The mapping table is in the README so the roadmap
documents still read true.

## D-0002 — Target framework `net10.0`

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted

PLAN.md locks ".NET 8+". .NET 10 is the current LTS and the only SDK on the development machine.
The SDK is pinned in `global.json` at `10.0.302` with `rollForward: latestPatch`, which keeps the
compiler and analyzer behaviour inside one feature band while still accepting SDK security patches.
`LangVersion` is pinned to `14.0` rather than `latest` so language semantics do not drift when the
SDK rolls forward.

## D-0003 — Test stack: xUnit v3 on Microsoft.Testing.Platform

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted

Considered xUnit v2, MSTest 3, NUnit, and TUnit.

xUnit v3 wins on dependency count, which is the deciding factor under CORE.md §3.9: one package
(`xunit.v3`) against v2's three (`xunit` + `Microsoft.NET.Test.Sdk` +
`xunit.runner.visualstudio`) and their transitive tail. v3 test projects are self-executing
executables on Microsoft.Testing.Platform, which also matters concretely for what is coming: Stage
0.2 tests shell out to `keepassxc-cli`, and Stage 1.2 tests spawn child processes and assert on
signal forwarding. VSTest's testhost proxying makes those flaky; MTP does not.

TUnit is faster and AOT-native but young and single-maintainer. For a security tool, CORE.md §6.5
applies: boring beats clever.

Two traps recorded for whoever adds the next test project:

- `dotnet new xunit` on SDK 10.0.302 still emits **xUnit v2 + VSTest**. Copy an existing test
  csproj instead of scaffolding one.
- `dotnet test` still defaults to VSTest. The opt-in is the `"test": { "runner":
  "Microsoft.Testing.Platform" }` block in `global.json`. MTP's option surface differs from
  VSTest's (`--report-trx`, not `--logger trx`; `--coverage`, not `--collect`), so VSTest-era CI
  snippets found online will fail with "unknown option".

## D-0004 — Dependencies: central versions plus committed lock files

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted

CORE.md §3.9 requires dependencies to be minimised and *pinned*. Central Package Management
(`Directory.Packages.props`) pins direct versions; `packages.lock.json` pins the entire transitive
closure by version and content hash. CI restores with `--locked-mode`, so a dependency cannot enter
the graph without a reviewable lock-file diff. `NuGetAudit` runs at `all`/`low` and, with warnings
as errors, a newly disclosed CVE turns the build red with no code change — fail closed (§3.7)
applied to the supply chain.

`NuGet.config` clears inherited package sources and maps every pattern to nuget.org, because a
machine-level feed makes restores machine-dependent and is a dependency-confusion vector.

Consequences to know:

- Adding a package is two steps: declare it, then `dotnet restore --force-evaluate`, then commit the
  regenerated lock files.
- The escape hatch when an unfixable advisory blocks CI is a **project-scoped, dated, issue-linked**
  `<NoWarn>NU1903</NoWarn>` — never repo-wide, never undated.
- `src/` currently has zero `PackageReference` entries. Keep it that way as long as possible; every
  future entry needs a justification here.

## D-0005 — Build gates encode the security laws

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted

Rather than trusting review to catch these, they are compiler errors from commit one:

- `TreatWarningsAsErrors` + `Nullable` — an unexpected null on an error path is how fail-closed
  silently becomes fail-open (§3.7). This is the cheapest test in the repository.
- The whole **Security** analyzer category as error, plus CA5350/5351/5379/5401/5404 — it is not
  possible to reach for MD5, SHA-1, DES, or a weak KDF in this repository. §3.6 enforced by the
  compiler, in place *before* the crypto arrives.
- CA1307/CA1310 (ordinal string comparison) with `InvariantGlobalization` — a culture-sensitive
  match deciding *which* secret an agent receives is a security bug, not a style nit.
- `IsAotCompatible` and the trim/AOT analyzers on `src/` — PLAN.md commits the CLI to AOT single
  binaries. With the analyzers on now, an AOT-hostile KDBX library surfaces as a build error the day
  it is added in 0.2, not during the Stage 3 launch. This is a dependency-selection gate disguised
  as a compiler setting.
- `AnalysisLevel` is `latest-recommended`, not `latest-all`. `all` would require a 40-line `NoWarn`
  block to build at all, and a long `NoWarn` block is where real warnings go to die.

Test projects inherit all of this except the trim/AOT analyzers, which xUnit's reflection would
otherwise fill with noise (`tests/Directory.Build.props`).

## D-0007 — KDBX4 via vendored KeePassLib, not a NuGet package

**Date:** 2026-07-25 · **Stage:** 0.2 · **Status:** accepted

**First, O-0001's premise was factually wrong and is retracted.** KeePass 2.x is licensed
GPL-2.0-**or-later**, not GPL-2.0-only — stated at <https://keepass.info/help/v2/license.html>
and in the header of every `KeePassLib/*.cs` ("either version 2 of the License, or (at your
option) any later version"). The "or later" grant permits taking the GPLv3 option, and
AGPL-3.0 §13 permits combining GPLv3 work with AGPLv3 work. There was never a conflict, no
relicensing is needed, and the original wording would have pushed a future reader into an
unnecessary one. Licence compatibility was therefore not the deciding factor here; maturity
was.

**The .NET KDBX4 ecosystem has no maintained, adopted library.** The full survey: the official
`KeePassLib` NuGet package is net35 and references `System.Windows.Forms` (last published
2015). `ModernKeePassLib` is netstandard1.2, dead since 2020, and ships no LICENSE file at all.
`pt.KeePassLibStd` is the most-downloaded port but dead since 2023 and drags in SkiaSharp's
per-RID native binaries. `KeePassLib.Standard` requires `System.Drawing.Common` — which throws
`PlatformNotSupportedException` off Windows — plus an ASP.NET Core data-protection stack.
`KPCLib` relicenses KeePassLib-derived GPL-2.0-or-later code as LGPL-3.0, which its copyright
holder cannot have authorised. The only two packages with a clean shape, `DgNet.Keepass` (MIT)
and `LibKdbx` (GPL-3.0), were at evaluation time 0-star repositories with **six hours** and
**ten days** of total commit history respectively and roughly 350 downloads each; one credits
an AI assistant for authorship. CORE.md §3.6 says "mature audited libraries" and §6.5 says
boring beats clever. Neither qualifies, and the vault format is not where to find out.

So `KeePassLib` is vendored from the `TimothyByrd/KeePassNetStandard` port at tag `v2.61`
(commit `87c2770`), which tracks upstream KeePass 2.61. Twenty years of field use, a fully
managed Argon2 with no P/Invoke and no libsodium, no native binaries anywhere — which also
answers O-0005's arm64 question for the library, if not for `keepassxc-cli`.

**The cost is real and is accepted openly**: we are now the maintainer of roughly 30,000 lines
of 2007-era C#. Upstream security patches are hand-merged, Dependabot can do nothing for us,
and "vendored fork of a GPL project" is a harder story to tell than "one MIT package". The
mitigation is `third_party/KeePassLib/UPSTREAM.md`, which records the exact commit, every local
modification, and the re-merge procedure. `v2.61` was taken rather than the port's `HEAD`
because the one commit past the tag adds NuGet packaging metadata and a
`System.Security.Cryptography.ProtectedData` reference.

**Three dependencies were removed to get to zero.** The port had substituted
`Microsoft.AspNetCore.DataProtection` (three packages) for Windows DPAPI in `ProtectedBinary`'s
*in-memory* protection, backed by an *ephemeral* provider that also creates a key directory
under `%APPDATA%/KeePass2`. Upstream already carries an in-tree, fully managed ChaCha20 path for
exactly that purpose — it is what real KeePass uses on Linux and macOS. Defining
`KEYPASTE_NO_DPAPI` makes `ProtectedMemorySupported` report false, which selects upstream's own
ChaCha20: identical on all three platforms, no dependency, and still not a line of our own
cryptography (§3.6). `KEYPASTE_NO_GFX` similarly drops `System.Drawing.Common`, which is
Windows-only since .NET 7; only the decode-PNG-to-`Bitmap` convenience is lost, while
`PwCustomIcon.ImageDataPng` — the bytes that actually live in the KDBX file — is untouched, so
custom icons still round-trip. `packages.lock.json` for the vendored project resolves to
`"net10.0": {}`, keeping D-0004's "src/ has zero PackageReference" claim true in substance.

Code keypaste does not build is removed from the *compilation* in `KeePassLib.csproj` rather
than deleted from disk, so a re-merge stays a clean `git diff`. `third_party/Directory.Build.props`
severs inheritance from the root props: MSBuild stops at the first `Directory.Build.props` found
walking up, so its mere presence turns off the analyzers, nullable, warnings-as-errors and XML
docs for vendored source only. `dotnet format` is given `--exclude third_party/` for the same
reason — an `.editorconfig` alone would not do it, since `dotnet format` reads `.editorconfig`
but is not bound by MSBuild properties.

**The interop boundary is a rule, not a convention:** `src/Keypaste.Core/Internal/KeePassInterop.cs`
is the only file in the repository permitted to reference KeePassLib. Everything else speaks in
`VaultEntry` and `VaultException`. That is what makes §4.3 ("one core library") enforceable and
what would make a future library swap a single-file change.

**Argon2 parameters are stated, not inherited**: Argon2d, 2 iterations, 64 MiB, parallelism 2,
version 0x13, pinned in `KdbxFormat` and asserted by `keepassxc-cli db-info`. They agree with
KeePass 2.61's defaults today. Pinning them means that if an upstream default ever changes it
surfaces as a failing test rather than as a silent change to how every keypaste vault is
protected.

**No `SecureString`, deliberately.** Master passwords cross the API as `ReadOnlySpan<char>`.
`SecureString` does not encrypt on Linux or macOS, and Microsoft advises against it in new
code; using it here would be a gesture that reads as a security guarantee, which is worse than
its absence. `Vault` copies the span into a UTF-8 buffer, derives the key, and zeroes the buffer
in a `finally`. Recorded here so nobody "improves" it later.

## D-0008 — The KeePassXC compatibility gate is permanent

**Date:** 2026-07-25 · **Stage:** 0.2 · **Status:** accepted

CORE.md §4.6 makes KeePassXC compatibility sacred, and CORE.md cannot change. The `compat` job
in `.github/workflows/ci.yml` plus `scripts/verify-keepassxc-compat.sh` are that law's
enforcement, so they are not subject to the "delete it if it is annoying" latitude that applies
to every other CI step.

It runs on all three operating systems rather than Linux only, because the interesting failures
are the OS-specific ones — path handling, text encoding, `SetConsoleCP` on Windows, arm64 on
macOS — and a Linux-only gate would assert the least interesting third of the surface. The
marginal cost is roughly eight free minutes; GitHub's 2×/10× Windows/macOS multipliers apply
only to billed minutes, which public repositories do not consume (D-0006). Were the repository
ever made private, this job alone would bill about 39 minutes per run — worth knowing as a
number rather than a vibe.

Three properties are non-negotiable, in increasing order of subtlety:

1. **Argon2 is asserted, and the KDBX major version is read straight from the file.** Argon2
   cannot be represented in KDBX 3.1, so `KDF: Argon2*` from `db-info` is KeePassXC
   independently confirming a real KDBX4 container. The raw header byte check is the belt to
   that braces and survives any change to `keepassxc-cli`'s output format. A silent downgrade
   to 3.1 would round-trip perfectly and open fine — no other assertion would notice it.
2. **Absent tooling is a failure, never a skip.** No path through the script exits 0 without
   having actually talked to KeePassXC.
3. **The negative control.** A wrong password must be *observed* to fail. Without it, a gate
   that quietly stopped testing anything would report green forever, and that — not deletion —
   is the most likely way this law dies.

Expected values are duplicated between `tools/Keypaste.CompatFixture` and the assertion script
on purpose. Generating the expectations from the writer under test would make them
self-fulfilling.

What actually prevents removal is branch protection: the three `keepassxc compat (…)` checks
must be configured as required status checks on `main`. A deleted job never reports, and a pull
request that never reports can never merge. **This still needs doing in the GitHub UI — it is
the one part of this decision that does not live in the repository.** The tripwire test in
`CompatGateIsPermanentTests` is a complement, not a substitute: it converts silent removal into
deliberate removal and nothing more. CODEOWNERS was considered and skipped — with one
maintainer, self-approval is ceremony.

Linux (apt) and macOS (Homebrew cask) versions float with the runner image, which is correct
semantics: the claim is "opens in KeePassXC", not "opens in one build of it", and an older
reader is free extra coverage. Only the Windows build is pinned by version and SHA-256, because
there we fetch a binary ourselves; the hash is a repo-side constant rather than the release's
own `.DIGEST`, which is served from the same origin as the zip and so proves integrity but not
authenticity.

`tools/Keypaste.CompatFixture` is throwaway. Stage 0.3 replaces it with `keypaste init` +
`keypaste add`, testing the shipped binary instead, which is strictly stronger. Its contract —
`argv[0]` is the output path, `KP_COMPAT_PASSWORD` is the master password — is what makes that
a one-line change, leaving the script, the expected values, and the job untouched.

---

# Open decisions

## O-0002 — Contribution terms: DCO or CLA

Undecided, and it must be decided before the repository accepts its first outside pull request.
ideas.md notes "clean IP, CLA or DCO from day one". A DCO is lighter and better received in
open-source communities; a CLA preserves relicensing freedom. Pick one and add
`CONTRIBUTING.md`.

The original O-0001 — "AGPL-3.0 vs the KDBX library licence, must resolve in Stage 0.2" — was
removed rather than answered: its premise that KeePassLib is GPL-2.0-only was factually wrong.
See D-0007. Relicensing freedom therefore matters less than it appeared to, but it is still
cheap only while there is a single copyright holder, which keeps this entry urgent.

## D-0006 — Repository is public; business notes live outside it

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted (supersedes the original O-0003)

The repository is public from the start, per CORE.md §3.8 — auditable code is the trust strategy,
and it starts paying on day one rather than at launch. Publishing also makes GitHub Actions free,
which is what unblocked CI: the three-OS matrix bills at 1× / 2× / **10×** per minute on a private
repository.

Before publishing, the benchmarks, pivot conditions, pricing ladder, and acquisition notes were
removed from `PLAN.md` and `ideas.md` and moved to private storage outside the repository. A public
repo whose entire pitch is trust should not also publish the conditions under which its author would
abandon it.

Because GitHub can serve any commit ever pushed once a repository is public — including unreachable
ones — the private repository was deleted and recreated rather than rewritten in place. If sensitive
content ever lands in a commit again, recreating the repository is the only reliable remedy; a
force-push is not.

## O-0004 — Deferred CI hardening

CodeQL, dependency-review, Dependabot, and SHA-pinned GitHub Actions are deliberately not in the
Stage 0.1 workflow. Action tags are mutable, so tag pinning without Dependabot ages badly; all of
these are free now that the repository is public, so revisit them together.

## O-0005 — `macos-latest` is arm64

Relevant from 0.2 onward: any native dependency (an Argon2 binding, `keepassxc-cli` from Homebrew)
needs an arm64 story, and Stage 3's AOT publish needs the Xcode command line tools on macOS and
clang plus zlib headers on Linux.

Partly answered in 0.2. The vault path has **no** native dependency at all: vendored KeePassLib's
Argon2 is managed C# with no P/Invoke (D-0007), so arm64 needs nothing special. `keepassxc-cli`
comes from the Homebrew cask, which ships an arm64 build. The AOT half of this entry remains open
and is now tracked more precisely by O-0006.

## O-0006 — Is vendored KeePassLib AOT-compatible? — **must resolve before Stage 3**

`third_party/Directory.Build.props` sets `IsAotCompatible=false` and turns the trim/AOT analyzers
off for vendored source. That is a deliberate trade — 2007-era code would otherwise bury the build
in noise — but it also disarms the dependency-selection gate D-0005 installed for exactly this
moment, so the answer must come from somewhere else.

That somewhere is a real `dotnet publish -p:PublishAot=true` of `Keypaste.Cli` on all three
operating systems, followed by *running* the round-trip against the published binary. A compile is
not enough: the failure mode for reflection-driven code is a runtime `NotSupportedException`, not a
build error. The known suspects in KeePassLib are `XmlUtilEx`/`KdbxFile`'s XML handling, the
`Assembly` reflection in `NativeLib`, and static initialisation order in `CryptoRandom`.

Resolve it early rather than at launch. If the answer is no, the options are trimming the vendored
tree, `rd.xml`-style roots, or dropping the AOT promise in PLAN.md — all cheaper to choose now than
in Stage 3.

## O-0007 — Trim the vendored tree?

`PwGroup.Search.cs`, `QualityEstimation.cs`, `PopularPasswords.cs`, `PasswordGenerator/**` and
`HmacOtp.cs` are severable — roughly 3,000 lines keypaste never calls. They are kept because every
`<Compile Remove>` is a decision a future re-merge must re-justify, and the current cost is only
build time.

Revisit if O-0006 forces trimming for AOT size, or if `HmacOtp.cs` becomes load-bearing when TOTP
arrives from ideas.md. Note the exclusion mechanism is already in place and costs nothing to
extend: files stay on disk, only the compilation changes.
