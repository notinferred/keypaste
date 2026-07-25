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

## D-0009 — The CLI contract: exit codes, stream split, one line per prompt

**Date:** 2026-07-25 · **Stage:** 0.3 · **Status:** accepted

The 0.1 contract was "data on stdout, everything else on stderr; 0 success, 1 usage, 2 internal".
The stream split survives untouched and is now load-bearing: prompts, progress, the clipboard
countdown and every diagnostic go to stderr, so `keypaste get x --show | tr -d '\n'` is a sane
thing to write. keepassxc-cli gets this right in `getPassword()` and then throws it away in its
`clip` verb, whose countdown goes to stdout and corrupts every pipeline it appears in (upstream
issue #3855). Not repeating that is most of why the split is stated here rather than assumed.

Exit codes gained two entries — **3 not found**, **4 wrong master password**. The 0.3 prompt asks
for output that is "script-friendly", and `keypaste get x || handle` is only script-friendly if
the caller can tell "you typed the password wrong" from "that entry does not exist" from "you
misused the command". Three is where the distinction stops being useful, so there is no separate
code for a missing clipboard: that is an environment failure, which is what 2 already means.

**When stdin is not a terminal, each prompt consumes exactly one line, in a fixed order that does
not depend on which flags were given.** `init` takes two lines, `add` takes two, everything else
takes one. This mirrors keepassxc-cli exactly and kills the whole "my CI hangs waiting for a
prompt" class of bug. `init` confirms the master password even when piped, because one code path
means the compatibility gate exercises the branch a human takes, and it costs a script one extra
`printf` argument. `add` deliberately does **not** confirm the entry password: a typo there is
recoverable in seconds, a typo in a master password locks you out of the vault forever. Confirm
where the cost is unrecoverable, nowhere else.

`rm` without `--yes` on a pipe is a usage error rather than a silent delete, because the
alternative is having an irreversible confirmation answered by whatever the next line of stdin
happens to be.

**There is no default vault path.** `--vault` beats `KEYPASTE_VAULT`, and absence is a usage error
naming both. A credential tool that silently picks a vault when you forgot to say which one
eventually writes a secret into the wrong file, or reports "not found" against a vault the user
has never seen. An empty environment variable counts as unset.

`ls` prints an indented tree by default and full paths under `--flat`, names only, and is
**ASCII-only** — a test asserts every character is below U+0080. Box-drawing characters would
look nicer and would break code pages, CI logs, and the diff against `keepassxc-cli ls -R -f`
that proves keypaste and KeePassXC agree about the shape of the same file. `--flat` output is
byte-identical to that command's, which is verified rather than hoped for.

Argument parsing is hand-rolled in `CommandLine.cs`. `System.CommandLine` is a NuGet package and
`src/` carries none (D-0004); five verbs with at most five options each do not justify one. Option
values are consumed positionally even when they look like options, so `--notes '--weird'` works.

## D-0010 — Hidden input: stderr prompts, UTF-8 pipes, and no `string`

**Date:** 2026-07-25 · **Stage:** 0.3 · **Status:** accepted

`Console.ReadKey(intercept: true)` is the only BCL way to read a keystroke without echoing it,
and it throws when stdin is redirected — but the platforms disagree about *when*. Unix pre-checks
`Console.IsInputRedirected` and throws immediately; Windows does not check, calls
`ReadConsoleInput`, and throws only after that fails, by which point the prompt has already been
printed. `ConsoleSecretPrompt` therefore decides for itself, before writing anything, so both
platforms behave identically and piped runs do not litter stderr with prompts nobody read.
`Console.SetIn` does **not** intercept `ReadKey`, which is why the keystroke source is an injected
delegate: without that seam not one password path in the CLI would be testable, and CORE.md §4.5
does not allow that on the secret path.

The redirected path decodes **UTF-8 explicitly** rather than reading `Console.In`, which on
Windows decodes with the console input code page — typically an OEM page — and silently mangles a
non-ASCII password arriving through a pipe. A pipe has no code page and every shell on all three
platforms writes UTF-8.

**Nothing is echoed, not even asterisks**, which leak the secret's length to anyone reading the
screen or a recorded terminal session. Backspace therefore has no visible effect; that is sudo's
trade and it is deliberate. Both backspace encodings are accepted (`\b` from Windows, U+007F from
most Unix terminals), Ctrl+U clears, and a `KeyChar` of `'\0'` — what Windows sends for modifier
and navigation keys — is dropped rather than appended as an invisible character. Ctrl+C is
classified as cancel but rarely reaches the table, since the runtime raises SIGINT or
`CTRL_C_EVENT` first; **Escape is the cancel key that actually works**, and the usage text says so.

Secrets are carried in `SecretBuffer`, never a `string`. `Vault.Create`/`Open` take a
`ReadOnlySpan<char>` and zero their UTF-8 copy in a `finally` (D-0007); reading the master password
into a `string` would make that promise worthless one layer up, because strings are immutable and
cannot be cleared. **What this does not protect against is stated in the type's own doc comment
and in SECURITY.md, not glossed over:** the GC may relocate the array and leave an unreachable
copy, the value can reach swap or a core dump, a debugger or any process running as the same user
can read it, and it necessarily becomes a `string` later anyway — `VaultEntry.Password` is one such
place. This narrows the window and reduces copies. It is not a security boundary.

## D-0011 — The clipboard is an OS tool over stdin; auto-clear blocks, behind a seam

**Date:** 2026-07-25 · **Stage:** 0.3 · **Status:** accepted

There is no clipboard in the BCL, and D-0004 leaves two options: P/Invoke into three native APIs,
or shell out to the tool each platform already ships. Shelling out wins on every axis that matters
— no `DllImport` for the trim and AOT analyzers to argue with (D-0005), no marshalling of a secret
through an `IntPtr`, and behaviour a user can reproduce by hand when it misbehaves.

**The secret reaches the child on stdin, never argv.** `/proc/<pid>/cmdline` is world-readable on
Linux, `Win32_Process.CommandLine` is readable over WMI, and Sysmon ships full command lines to a
SIEM by default. Windows and macOS tools are invoked by absolute path, because we are piping a
plaintext password into whatever the name resolves to and a `clip.exe` planted earlier on `PATH`
would receive it; Linux tools have no fixed location, so that asymmetry is an accepted residual
risk rather than parity. `clip.exe` reads UTF-16LE and mojibakes anything else, with the BOM
suppressed so it does not paste a stray U+FEFF. Four process rules are written down because each
is a hang if forgotten: drain both pipes before writing stdin, `Write` not `WriteLine`, close
stdin before waiting, and always use the bounded `WaitForExit` — `wl-copy` and `xclip` fork a
daemon that inherits the pipe, so EOF may never arrive.

**`IClipboard` has no member that returns clipboard text**, only `TryReadHash`. Auto-clear needs
to know whether the clipboard still holds what keypaste put there, nothing more; returning the
text would pull the user's entire clipboard — passwords keypaste never wrote included — into this
process for no benefit. The baseline is taken immediately after the copy, which makes the scheme
robust rather than tidy: whatever a platform's read-back does to the bytes, it does identically at
both ends of the wait, so the read-back need only be deterministic, never faithful.

**Auto-clear blocks in the foreground**, and the known cost is recorded here rather than
discovered later: **`keypaste get x | foo` makes `foo` wait twenty seconds, and the terminal is
held for the duration.** That is precisely the complaint against `keepassxc-cli clip` (#3855).
Blocking was chosen anyway because the alternative — a detached clearer, as `pass` and `gopass` do
— needs a `setsid` P/Invoke on Unix, a separate Windows path, and a hidden `unclip` verb, and
cannot be tested in-process at all; both of those projects also had to add predecessor-killing as
a follow-on fix. `--timeout 0` copies without clearing, and `IClipboardClearStrategy` exists with
exactly one implementation so a detached model can replace it without touching command code. When
this becomes the top complaint, that seam is where the fix goes.

The clear is **conditional** — SHA-256 plus `FixedTimeEquals`, skipped if the clipboard changed —
which the KeePassXC GUI does and its own CLI does not, wiping whatever you copied in the meantime.
**Failing to read the clipboard back means clear anyway**: leaving a password on the clipboard
indefinitely is the worst outcome available (§3.7). Both `Console.CancelKeyPress` and
`PosixSignalRegistration` for SIGINT/SIGTERM/SIGHUP are registered, because neither covers
everything; handlers only set an event so the clear runs once, on the main thread.

**No design here survives SIGKILL**, and two further gaps belong in the same sentence: on X11 and
Wayland the secret also lives in the forked `wl-copy`/`xclip` daemon, because those clipboards are
owner-served; and Windows clipboard history retains a copy that clearing does not remove — O-0008.

Headless is a **loud failure**. With no display, nothing is spawned at all, because "install
xclip" is wrong advice on a server; the message names `--show`. keypaste never prints a secret
unless asked, and a test asserts the secret appears in neither stdout nor stderr on that path.

## D-0012 — `tools/Keypaste.CompatFixture` retired; the gate runs the shipped binary

**Date:** 2026-07-25 · **Stage:** 0.3 · **Status:** accepted (completes the plan in D-0008)

D-0008 designed the fixture generator to be throwaway and gave it a contract precisely so this
swap would be cheap. It was: `scripts/make-compat-fixture.sh` drives `keypaste init` and three
`keypaste add` invocations, one CI step changed, and **`scripts/verify-keepassxc-compat.sh` was
not touched at all** — not one assertion, not one expected value. D-0008's three non-negotiable
properties are intact.

The gate is strictly stronger for it. It now covers argument parsing, group-path splitting, the
non-echoing prompt's redirected-stdin path, `init`'s confirm-twice loop and `add`'s
open-modify-save cycle, instead of the vault writer alone. It is also the only test in the
repository that exercises `Vault.Open` followed by `Vault.Save` on all three operating systems.

The binary is invoked **directly** rather than through `dotnet run`, which would put the SDK's
stdin forwarding between the pipe and the process under test — and D-0008's whole argument is that
this gate must test the thing that ships.

One new exposure was accepted knowingly and then checked: the non-ASCII entry values now reach
.NET through argv from Git Bash on `windows-latest`, a hop Stage 0.2 never exercised because its
Unicode lived in a C# source literal. The chain is UTF-8 in the script, MSYS2's conversion for
`CreateProcessW`, and .NET's `GetCommandLineW`. It was verified byte-for-byte before the fixture
project was deleted rather than assumed. `CompatGateIsPermanentTests` now also asserts the
workflow names `make-compat-fixture.sh` and no longer names `Keypaste.CompatFixture`, so a future
change cannot quietly narrow the gate back to the writer.

## D-0013 — File transactions apply on open, not only on create

**Date:** 2026-07-25 · **Stage:** 0.3 · **Status:** accepted

A latent defect that Stage 0.3 would have promoted to a live one. `KeePassInterop` set
`UseFileTransactions = true` inside the method that also configures the KDF, and that method ran
only on the create path. KeePassLib defaults the flag to `false` and `PwDatabase.Close()` — which
`Open()` calls first — resets it, and `Save()` passes it straight into `FileTransactionEx`.

Stage 0.2 only ever created a vault and saved it once, so nothing noticed. Every CLI verb except
`init` is open-modify-save, which means **an interrupted `add` or `rm` would have truncated a
vault that was previously readable** — data loss on the secret path, and a fail-open write in the
sense of §3.7.

The flag moved into its own step applied on both paths. It could not simply be fixed by re-running
the format settings on open: those re-randomise the KDF salt, which would rewrite the key
derivation of an existing vault on every save — a worse bug than the one being fixed. The
regression test asserts the flag directly rather than looking for leftover files, because an
in-place save also leaves no debris, so a debris-only assertion would pass with the defect present;
it was confirmed to fail when the fix is reverted.

## D-0014 - Env sets are one KDBX entry per variable, not custom string fields

**Date:** 2026-07-25 - **Stage:** 1.1 - **Status:** accepted

An environment set is stored as the group `env/<project>`, holding one ordinary entry per
variable: title = `KEY`, password = value. No custom string fields, no marker attributes, no
keypaste-only metadata anywhere in the file. The group path is the marker.

**The alternative was one entry per project carrying KEY-to-value as custom string fields, and it
was ruled out by a single verifiable fact:** `keepassxc-cli add` and `keepassxc-cli edit` have no
option to write a custom string field. They can read one (`show -a`, `show --all`), but nothing in
the CLI can create or change one; only the GUI can. Checked against KeePassXC 2.7.10, and against
2.7.12 on CI.

That matters because the requirement for this stage was not "readable in KeePassXC", it was
**editable** - a user changes `DATABASE_URL` in KeePassXC and keypaste picks it up. Under the
custom-field convention that claim could only ever have been demonstrated by a human clicking
through a GUI, which means CI could not hold it and it would rot. Under one-entry-per-variable,
KeePassXC has full parity: `ls -R -f` enumerates variables, `edit` changes one, `add` and `rm` add
and remove them. `scripts/verify-keepassxc-writeback.sh` now proves exactly that on three
operating systems, in both directions.

Two smaller reasons point the same way. Values land in the `Password` field, so they inherit the
existing protection rule in `KeePassInterop.SetField` unchanged and are masked in KeePassXC rather
than shown as plaintext advanced attributes; and `VaultEntry` needs no new members, so
`keypaste ls` and `keypaste get env/<project>/<KEY>` work on environment variables for free.

**The cost is real and is accepted openly:** a `.env` with forty keys becomes forty KDBX entries,
and `keypaste ls` gets noisy in a vault that holds several projects. Noise is recoverable; a
compatibility claim nobody can test is not. PLAN.md previously specified the custom-field shape
and has been corrected.

**Validation is strict on write and permissive on read.** keypaste refuses to create a project
name that is empty or contains a separator (group-path resolution discards empty segments, so both
write to a path no read can reach), and refuses a variable name outside the POSIX
`[A-Za-z_][A-Za-z0-9_]*` rule, or one differing from an existing name only in case - that pair is
two variables on Linux and one on Windows. But anything already in the file is listed exactly as
KeePassXC shows it, with unusable names flagged on stderr rather than hidden, because keypaste and
KeePassXC disagreeing about the contents of one file is the failure law 4.6 exists to prevent.
Two entries sharing a name have no correct answer and fail closed (section 3.7).

**Overwriting a value keeps the previous one as KDBX history**, which is what KeePassXC's own
editor does and what a KeePass user expects to find in the History tab. This sits awkwardly beside
the rationale on `KeePassInterop.RemoveEntry`, which removes outright rather than to a recycle bin
so that "a vault the user asked to delete from should not keep a readable copy of the secret". The
tension is real: rotating a credential *because it leaked* leaves the leaked value in the file,
encrypted, until the ten-item history cap evicts it - and keypaste has no feature that reads
history, so it is invisible in `keypaste ls` and `get` while being plainly visible in KeePassXC.
It is accepted because silently discarding history on an entry the user maintains in KeePassXC
would be a worse surprise, and because `keypaste env rm` does remove the entry and its history
together. `env set` says so on the line where it happens rather than only in this file. A
`--no-history` flag is in ideas.md, not in this stage.

**`keypaste env set <project> KEY=value` takes the value from `argv`**, where it is visible in the
process list and lands in shell history. This contradicts the comment on `AddCommand`, which
refuses a `--password` flag for exactly that reason, and the contradiction is deliberate rather
than overlooked: the piped form exists and is what the compatibility gate uses, but a one-liner is
what people will reach for, and refusing it outright pushes them to clean up shell history by hand
or to something worse. It is documented in SECURITY.md instead of warned about on every run, and
tracked as **O-0009**.

**One primitive was added to the core for this and is not env-shaped:** `Vault.UpdateEntry`. The
vault had no update path at all - only add and remove - and the GUI's entry editor will need the
same one. It edits the underlying entry in place rather than removing and re-adding it, so the
UUID, timestamps, attachments and any custom string fields a user added in KeePassXC survive a
value change. A unit test asserts the history count directly, because keypaste reads no history
itself and nothing else in the codebase would notice it disappearing; that test was confirmed to
fail against a remove-and-re-add implementation.

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

## O-0008 — Windows clipboard history and cloud sync retain the secret

Clearing the Windows clipboard does not remove the entry from clipboard history (Win+V) or from
cloud clipboard sync, so a password keypaste copied outlives its twenty seconds on Windows and can
outlive the machine. Windows provides opt-out clipboard formats —
`ExcludeClipboardContentFromMonitorProcessing` and `CanIncludeInClipboardHistory` — but setting
them requires `OpenClipboard`/`SetClipboardData` via P/Invoke, which `clip.exe` cannot express and
which D-0011 avoided deliberately.

Resolve by Stage 3 at the latest: it is a claim SECURITY.md has to make correctly before strangers
rely on it. The options are a Windows-only Win32 path (three P/Invokes, one
`[SupportedOSPlatform]` class, and an argument with the trim/AOT analysers), documenting the gap
and telling Windows users to disable clipboard history, or making `--show` the Windows default.
Until then SECURITY.md states the gap rather than implying the clear is complete.

## O-0009 - Values on the command line, and case-colliding names on Windows

Two loose ends from D-0014, both of which should be settled before strangers depend on the
answers.

**`env set p KEY=value` puts a secret in `argv`.** It is world-readable through `/proc` on Linux,
visible to WMI and Sysmon on Windows, and written to the shell's history file. keypaste ships this
deliberately, because the alternative is people moving secrets around some other way that is no
safer. The open question is whether it stays silent: a one-line stderr note costs nothing but
trains people to ignore warnings, and a flag that silences it (the shape `rm --yes` already uses)
costs a flag. Decide before Stage 3, when the audience stops being one person.

**Two variables differing only in case are one variable on Windows.** Writing the second is
refused today. Reading is not: a vault that already contains `PATH` and `Path` lists both, and
Stage 1.2's `keypaste run` will have to decide whether that is a hard failure, a last-writer-wins
with a warning, or a platform-conditional. Failing closed is the section 3.7 answer and the likely
one, but it belongs with the injection code that has to implement it.
