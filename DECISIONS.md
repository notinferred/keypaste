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

---

# Open decisions

## O-0001 — AGPL-3.0 vs the KDBX library licence — **must resolve in Stage 0.2**

The repository ships AGPL-3.0 per `prompts.md` 0.1 and PLAN.md's locked "copyleft core".

**This constrains the 0.2 library choice.** The official `KeePassLib` is **GPL-2.0-only**, which is
incompatible with AGPL-3.0 — the two cannot be combined in one distributed work. So the 0.2
evaluation must treat licence compatibility as a hard gate, and should prefer an MIT/BSD-licensed
KDBX4 implementation. If KeePassLib nevertheless wins on maturity, the project relicenses to
GPL-2.0-compatible terms *before* taking the dependency, and records it here.

Relicensing is cheap while there is a single copyright holder and stops being cheap the moment the
first external contribution lands — which makes O-0002 urgent too.

## O-0002 — Contribution terms: DCO or CLA

Undecided, and it must be decided before the repository accepts its first outside pull request.
ideas.md notes "clean IP, CLA or DCO from day one". A DCO is lighter and better received in
open-source communities; a CLA preserves relicensing freedom (see O-0001). Pick one and add
`CONTRIBUTING.md`.

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

## D-0007 — GitHub is the source of record; self-hosting was considered and dropped

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted

Moving the repository to self-hosted Gitea (`git.ochoa.pro`) was evaluated and rejected. Gitea hosts
code perfectly well and would satisfy CORE.md §3.8 — the problem is CI. Gitea Actions runners are all
self-hosted, and §4.4 requires macOS, Linux, and Windows while §4.6 pins the KeePassXC compatibility
test into CI permanently. macOS needs Apple hardware, so self-hosting would have meant either buying
a Mac, paying for a hosted one, or shipping a known compliance gap — to replace something the public
repo provides for free.

Keeping GitHub also preserves the launch-discovery surface the roadmap depends on. Revisit only if
GitHub's terms or pricing change, or if Apple hardware appears for other reasons.

## O-0004 — Deferred CI hardening

CodeQL, dependency-review, Dependabot, and SHA-pinned GitHub Actions are deliberately not in the
Stage 0.1 workflow. Revisit them together when O-0003 is settled: action tags are mutable, so tag
pinning without Dependabot ages badly, and the first three are free only on public repositories.

## O-0005 — `macos-latest` is arm64

Relevant from 0.2 onward: any native dependency (an Argon2 binding, `keepassxc-cli` from Homebrew)
needs an arm64 story, and Stage 3's AOT publish needs the Xcode command line tools on macOS and
clang plus zlib headers on Linux.
