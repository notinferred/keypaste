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
or to something worse.

So it is allowed and it says so, once, on the line where it happens - one sentence on stderr,
naming no value, only on the inline form. Warning on the prompted form as well would be the fast
way to teach people that keypaste's warnings are noise. This was first shipped silent and changed
before the branch merged; the residual question of whether a script should be able to silence it,
the way `rm --yes` silences a confirmation, is **O-0009**.

**One primitive was added to the core for this and is not env-shaped:** `Vault.UpdateEntry`. The
vault had no update path at all - only add and remove - and the GUI's entry editor will need the
same one. It edits the underlying entry in place rather than removing and re-adding it, so the
UUID, timestamps, attachments and any custom string fields a user added in KeePassXC survive a
value change. A unit test asserts the history count directly, because keypaste reads no history
itself and nothing else in the codebase would notice it disappearing; that test was confirmed to
fail against a remove-and-re-add implementation.

---

## D-0015 - `env pull` is fail-closed, and it deletes the .env rather than claiming to shred it

**Date:** 2026-07-25 - **Stage:** 1.2a - **Status:** accepted

`keypaste env pull <project> [file]` reads a `.env` file and stores every variable in the project's
env set. The parser is `src/Keypaste.Core/DotEnv.cs` - public, in the core, because CORE.md law 4.3
is "no logic duplicated in frontends" and Stage 4's import dialog would otherwise grow a second
grammar that disagrees with this one on exactly the ambiguous cases below. Two keypaste frontends
importing one file into different secret sets is law 4.6's failure one layer up. It is also where
thoroughness is cheap: the grammar has around sixty tests that open no vault, while every
CLI-level test pays for a key derivation.

**There is no `.env` standard, only implementations that disagree**, so the rules were chosen
against the two that most likely produced the file being read - `motdotla/dotenv` (JavaScript) and
`joho/godotenv` (Go). Three divergences are deliberate:

- **An unquoted `#` starts a comment only when a space or tab precedes it.** dotenv truncates at
  any `#`, which silently turns `PASSWORD=hunter2#42` into `hunter2` - a shortened secret that
  fails much later somewhere else, with nothing saying why. When a comment *is* removed, `env pull`
  names the affected keys on stderr.
- **A key repeated in one file is an error.** dotenv keeps the first, godotenv keeps the last.
  Since the two disagree there is no answer to give, so it fails closed (law 3.7), exactly as
  `EnvStore.Read` already does for two entries sharing a name.
- **A key outside the POSIX rule is an error.** dotenv's key pattern allows `-` and `.`; a variable
  named that way cannot be exported to a child process, which is the only reason to store it. The
  message comes from `EnvConvention.IsValidKey`, so the import and `env set` refuse identically.

Backticks are accepted as a third literal quote, because dotenv v16 writes them and excluding them
would silently store the backticks as part of the value. `KEY: value` is refused although dotenv
accepts it - guessing at a YAML-ish shape on the secret path is not worth the convenience. Values
spanning lines are supported for all three quote characters, which is what a PEM private key needs.

Inside double quotes exactly five escapes expand - `\n \r \t \\ \"` - and everything else keeps its
backslash, so `"C:\logs\app"` survives. The sharp edge is that `"C:\temp"` is `C:` followed by a
tab, the same as in C, Python, or a shell's `$'...'`. A path-shaped exception to an escape rule
would be a worse surprise than the rule, so it is documented and tested rather than special-cased;
single quotes and the unquoted form are both literal and are the fix.

**`${NAME}` and `$NAME` are stored literally, never expanded.** Expanding against the importing
machine's environment would bake one laptop's `$HOME`, or a CI runner's, into a vault that is then
synced elsewhere - the same file would mean different things on different machines. Expanding
against the vault's own variables would invent an evaluation order inside a KDBX group that has
none and that KeePassXC can neither see nor maintain. Both are guessing about a secret. `env pull`
names the affected keys on stderr, `--help` says it, and the README says it. There is no `--expand`
flag: a flag that changes what a secret *means* is the wrong kind of flag.

**The import is all or nothing.** The file is read, decoded and checked completely - and every
problem reported at once, capped at ten - before the vault is even opened. A half-imported `.env`
whose original was then deleted is unrecoverable, and this command offers to delete the original,
so the import has to be atomic or the offer is a trap. It is also free, because `Vault.Save` is one
transactional write (D-0013) called once at the end. Everything about the file is settled before
the master password is asked for, so a typo in the path does not cost a password entry and a key
derivation to discover.

**A problem message may name the line number, the key, and the rule broken - never the value or
the raw line.** The natural phrasing of "unterminated quote on line 7" includes the line, and on a
malformed `.env` the line is the secret. A diagnostic is the likeliest place for a value to escape,
not the summary, and both a core test and the CLI hygiene sweep assert it. The fail-closed
behaviour was confirmed by breaking it: ignoring the parse result imports the good lines and turns
`Pull_WithABadLine_WritesNothing_AndReportsEveryProblem` red.

**Collisions print a plan and ask, rather than taking a flag.** The command classifies every
variable as new, updated, or unchanged, prints the counts and the names, and confirms - reusing
`rm`'s `--yes`-when-stdin-is-not-a-terminal rule verbatim. **Unchanged variables are not written.**
`EnvStore.TrySet` compares nothing, so rewriting an identical value spends one of the ten history
items the format keeps and bumps the modification time of an entry the user also maintains in
KeePassXC; `Set_WithTheValueItAlreadyHas_StillCostsAHistoryItem` pins why that matters. Two names
differing only in case - in the file, or against the project - are refused before the confirmation
rather than halfway through the write loop. The rejected alternative was `--overwrite`: a second
flag that every script would set unconditionally is noise, and the plan already names the keys.

**The word "shred" does not appear in the product, and there is no overwrite pass.** `prompts.md`
asked for one; this corrects it in writing, the same way D-0014 corrected PLAN.md. Overwriting a
file before deleting it does not destroy data on SSDs (the flash translation layer has already
remapped the block), on copy-on-write filesystems (APFS, btrfs, ZFS, ReFS), on any volume with
snapshots or backups, on network filesystems, or on NTFS files small enough to live in the MFT.
`shred(1)`'s own CAUTION section says so. On a 2026 developer machine there is essentially no
configuration where it works, so the pass would cost code, be untestable, and have exactly one real
effect: convincing the user the secret is gone when the correct action is to rotate it. SECURITY.md
already says a tool that overclaims is worse than one that is modest.

What ships instead is `--delete-source`, `--keep`, and a prompt that states plainly what deleting
does not do. It also warns when the file sits inside a git repository - checking for a `.git`
directory *or file*, since worktrees and submodules use a file - because history is usually the
larger exposure of the two. It deliberately does not shell out to `git`: a `.git` entry says the
file is inside a repository, not that it was ever committed, and claiming history for a
`.gitignore`d file would be the same overclaim in a smaller font. The note is conditional and hands
over the command instead.

**A piped run with no `--delete-source` neither deletes nor fails**, and says what it left behind.
That differs from `env rm --yes` on purpose: there the deletion *is* the command, so refusing to
guess is refusing to act. Here the import already succeeded and deletion is an optional epilogue,
so failing the whole run over a cleanup question would be wrong and deleting silently would be
worse. If the deletion is asked for and fails, the exit code is 2 rather than 0: the user asked for
two things, got one, and the half that failed is the half that left plaintext on disk.

**No filesystem seam.** `File.Exists`, `ReadAllBytes` and `Delete` are called directly and the
tests write real files into a temp directory. The existing seams - `ISecretPrompt`, `IClipboard`,
`IEnvironmentProbe` - exist because those things are untestable in-process: `Console.SetIn`
provably does not intercept `Console.ReadKey`, and the clipboard needs a subprocess and a
twenty-second wait. The filesystem has neither problem, `VaultSession.Open` already calls
`File.Exists` directly, and a fake filesystem would prove nothing about `File.Delete`, which is the
behaviour that actually matters here.

**Memory hygiene is not overclaimed.** The file's plaintext is an ordinary string from decode
onward, as is every value, and none of it can be zeroed - `VaultEntry.Password` is a `string`, so a
`SecretBuffer` here would become one two frames later, which is the case `SecretBuffer`'s own
documentation already names. No `GC.Collect`, no `SecureString`, no ceremony. What is claimed is
narrower and true: keypaste writes nothing in plaintext of its own accord. The file was already on
disk; the only write this command performs is the encrypted vault.

> **Amended in 1.3.** That last sentence originally read "keypaste writes nothing in plaintext of
> its own", full stop, and `env export` made it false. The claim is now "of its own accord" and it
> is still true: the only command that writes plaintext is one whose entire purpose the user typed,
> and it is loud about it. See D-0018. The same sentence in `DotEnv.cs`'s remarks was corrected in
> the same commit — a claim in a doc comment ages exactly as badly as one in a decision record, and
> it is the one a security auditor reads first.

The compatibility scripts are untouched. `env pull` writes through `EnvStore`, whose shape the gate
already proves in both directions; a third script would be testing KeePassLib, not the parser.

---

## D-0016 - `keypaste run`: the argument split, the merge, the signals, the exit codes

**Date:** 2026-07-25 - **Stage:** 1.2b - **Status:** accepted

`keypaste run <project> -- <command...>` unlocks the vault, reads the project's variables, closes
the vault, and then starts the command with those variables merged into its environment, sharing
keypaste's own stdin, stdout and stderr.

**Two phases, in that order, and the ordering is a function rather than a habit.** A child may run
for hours; holding a decrypted database open for the lifetime of an unrelated process is not
something a credential tool gets to do. `VaultSession.OpenThen` opens the vault, hands the caller
what it needs, closes it, and only then runs the second phase. What escapes the first callback is
*data* - values the caller already had a right to read - never a lifetime, so `Open`'s
`using var vault` is untouched and CA2000 is still satisfied by construction. A test asserts the
master-password buffer is already zeroed at the moment the child starts.

**The `--` split lives in the verb, not in `CommandLine`.** The parser already treats `--` as "the
rest are operands", `CommandLineTests` pins that, and five verbs depend on it. More decisively,
`run` needs something the parser cannot express at any severity: the right-hand side must be exempt
from option parsing *entirely*, so `keypaste run p -- mytool --vault x` gives `mytool` its own
`--vault`. A parser returning "the operands after `--`" has already decided that flag was
keypaste's. Only the first separator is a boundary, so `keypaste run p -- git log -- path` means
what it looks like. The `--` is required: `keypaste run dev npm start` is genuinely ambiguous with
a project called `npm`, and making it optional turns
`keypaste run dev --vault v npm start --vault w` into an argument about whose flag is whose.

**The merge: everything inherited, the project's variables on top.** A stale `DATABASE_URL` in your
shell must not beat the one you deliberately stored. Empty values are values. Names are compared
case-insensitively on Windows and exactly everywhere else, which is an OS-inherent divergence and
correct in both. A project that sets `PATH` is allowed to and gets one warning, because
`ProcessStartInfo.FileName` is resolved against keypaste's `PATH` rather than the child's: pinning
a per-project toolchain is legitimate, and naming the surprise costs a line. Resolving the
executable ourselves against the merged `PATH` was rejected - it means reimplementing `PATHEXT`,
the executable bit and the Windows search order inside a credential tool.

**Two things fail closed, and both answer the open half of O-0009.**

A name outside the POSIX rule cannot be exported, so the run stops, exit 2, listing *every*
offending name so one repair pass in KeePassXC is enough. Skipping them with a warning was the
alternative: a child booted with a silently incomplete environment does not fail here, it fails
later and elsewhere, as "connected to the wrong database".

Two names differing only in case stop the run **on every platform**, not conditionally. O-0009
listed hard failure, last-writer-wins, and a platform-conditional. The platform-conditional is the
worst of the three, not the safe middle: the same vault would run on Linux and refuse on Windows, a
failure a teammate cannot reproduce (against law 4.4). Last-writer-wins has no defensible "last" -
`EnvStore.Read` sorts ordinal, so `PATH` would beat `Path` purely because uppercase sorts first, and
sort order deciding which secret production receives is exactly law 3.7's failure. And
`ProcessStartInfo.Environment` is case-insensitive on Windows, so writing both is a crash or a coin
flip inside the BCL; failing closed replaces an implementation detail with a stated rule. Both
checks live in the CLI's injection code, not in `EnvStore`: reading stays permissive so `env ls` and
`env rm` can still show and clear whatever KeePassXC put in the file (law 4.6).

**Signals: relay, never escalate.** `PosixSignalRegistration` handles SIGINT, SIGTERM, SIGQUIT and
SIGHUP on every platform - the runtime maps the Windows console events onto them, so there is one
code path and no `#if`. The handler sets `Cancel = true`, which is what keeps keypaste alive long
enough to reap the child and report its status.

Relaying is conditional. SIGINT, SIGQUIT and SIGHUP are generated by the terminal driver and
delivered to the whole foreground process group, which the child is already in, so relaying them
there would deliver each twice - and a second Ctrl+C means "stop being graceful" to docker compose,
npm and most servers. When there is no terminal, nothing delivered them and keypaste must. SIGTERM
is never terminal-generated, so it is always keypaste's to relay: `docker stop` sends it to PID 1
and `timeout` sends it to the wrapper, and without the relay both would kill keypaste and orphan
the child. `ENTRYPOINT ["keypaste","run","prod","--","node","server.js"]` is a use this feature
invites, which is what justifies the interop.

**keypaste never sends SIGKILL.** `Process.Kill()` is SIGKILL on Unix and there is no managed way to
send anything else, so `NativeSignals` calls `kill(2)` directly. This adds no dependency in the
sense law 3.9 means - no package, nothing to pin, nothing new on the supply chain, and `src/` still
carries zero `PackageReference` entries (D-0004). It is `DllImport` rather than the newer
`LibraryImport` because that source generator emits an unsafe stub, and turning `AllowUnsafeBlocks`
on for the whole CLI to obtain one two-integer call is a far wider change than the call; the
signature is fully blittable, so nothing is generated and NativeAOT can still compile it. A child
that ignores SIGTERM makes keypaste hang, which is correct wrapper behaviour - systemd and docker
escalate on the cgroup, and keypaste does not get to decide when your database is allowed to die.

**The honest Windows gap:** Ctrl+C and Ctrl+Break work. Closing the console window raises
`CTRL_CLOSE_EVENT`, and Windows terminates the process a few seconds later regardless of `Cancel`,
which can orphan the child. Stated in SECURITY.md rather than papered over, and it is also why the
handler never blocks.

**Exit codes: once the child starts, the code is the child's.** Including Unix's 128+signal, which
.NET already computes. A command that could not be started reports the shell's 127 (no such
command) or 126 (not executable), because scripts already branch on those. The collision with
keypaste's own 0-4 is real and accepted - D-0009 is a repo-wide contract and `run` should not speak
a different dialect - so the rule is documented instead: keypaste's own failures always print a
line beginning `keypaste run:` and a child never does. GNU `env` and `timeout` reserve 125 for
"the wrapper itself failed", which is better known and removes the ambiguity, but it introduces a
second exit-code dialect inside one CLI and discards D-0009's 3-versus-4 distinction.

**Two new gates, as steps in the existing `test` job** - never a new job, because branch protection
names the existing checks. `run` hands the child real handles, so everything it does is beyond
`CliContext` and beyond every in-process test; the unit tests assert the environment keypaste
*builds*, and only a real child can be asked what it *received*.
`scripts/verify-run-injection.sh` runs on all three operating systems and asserts the child's output
is byte-equal to the sentinel - which proves the injection and that keypaste printed nothing onto
the shared stdout in one comparison - then asserts that **no file was written**, with every
temp-directory variable pointed at an empty directory. That is the narrow, testable half of what
SECURITY.md promises about injection, and asserting it in prose while nothing verified it would be
the overclaim that file exists to avoid. `scripts/verify-run-signals.sh` is Unix-only, because
Windows has no `kill(1)` equivalent and a runner has no tty; that is a gap in the test, not a
relaxation of the gate.

Both scripts run the child as **their own interpreter, by absolute path** (`$BASH`), never the bare
word `bash`. On a Windows developer machine `bash` on `PATH` is usually the WSL launcher, which
starts a Linux session with an environment of its own and discards the one it was handed - the
first version of the injection gate reported "injection did not happen" against a build where
injection worked perfectly.

**One bug `run` exposed and fixed here.** `ConsoleSecretPrompt` read the piped master password
through a `StreamReader`, which buffers ahead: reading one line consumed up to a bufferful of stdin
into managed memory nothing else could reach. Invisible for every verb until now, because nothing
downstream wanted stdin. For `run`, whose child inherits it,
`printf 'pw\nhello\n' | keypaste run p -- cat` would have printed nothing at all. It now reads byte
by byte to the newline and decodes once, which costs nothing at the length of a password, and the
test seam moved from `TextReader` to `Stream` so the leftover input is directly assertable.

---

## D-0017 - Saving a vault retries a transient file error, and says why when it gives up

**Date:** 2026-07-25 - **Stage:** 1.2b - **Status:** accepted

`Vault.Save` now makes up to four attempts, with a short linear backoff, when the write fails with
`IOException` or `UnauthorizedAccessException`. Everything else fails on the first attempt.

**This was found, not predicted.** Stage 1.2's tests roughly doubled how much vault writing CI
does, and `test (windows-latest)` began failing intermittently - a different test each time, across
`VerbTests`, `EnvVerbTests`, `EnvPullTests` and `RunCommandTests`, always exit 2. The first
occurrence took down a clipboard test written in Stage 0, so the defect predates this stage; the
extra load only made it visible.

Saving goes through a file transaction - write a temporary file, then replace the original
(D-0013). On Windows that replace competes with every process that watches the filesystem:
Defender and the search indexer open a newly written file in order to scan it, and for a few
milliseconds the replace cannot land. The observed error is `Access is denied`, an
`UnauthorizedAccessException` rather than the `IOException` one would guess, which is why the
retry predicate covers both.

**Retrying is safe because the write is transactional.** A failed commit leaves the original file
untouched, so a second attempt starts from exactly where the first did. A save that never succeeds
reports what it always reported. The retry can turn a spurious failure into a success and cannot
turn a real failure into corruption, which is the only property that makes this acceptable on the
write path of a credential store.

Proved by A/B rather than asserted: `VaultSaveTests.ATransientFailure_IsAbsorbed` fails at one
attempt and passes at four. `TheRetryHasRoomToAbsorbATransientFailure` pins the constants so that
quietly reducing them cannot silently restore the old behaviour, and two further tests pin that a
save which genuinely cannot succeed still fails, and fails promptly rather than hanging.

**Three earlier versions of those tests were wrong, and each mistake is worth naming.**

The first simulated the failure by holding the vault file open with `FileShare.None`, which is what
actually happens in the wild. That works on Windows and does nothing on Linux or macOS, where file
locking is advisory — the save simply succeeded, so `Assert.Throws` found no exception and the
tests failed on two of the three operating systems. A test that asserts real behaviour on one
platform and nothing at all on the others is worse than no test, because the green tick claims all
three. The mechanism is now a directory that is removed and, for the transient case, put back: it
fails everywhere, through the same retry path, for the same reason.

The second repaired the directory from `Task.Run`. On the Windows runner, with four cores already
saturated by other test collections, the thread pool did not schedule the repair until after the
retry window had closed, and the test failed. It now uses a dedicated thread, so the result does
not depend on how busy the machine is.

The third computed its expected elapsed time *from the constants it was testing*, so at one attempt
the expectation fell to zero and the assertion passed against a build with no retry at all. It now
compares against a fixed floor. This is the same failure as the two weak tests caught in Stage 1.1,
and it is only ever caught by running the test against a deliberately broken build — which is why
every guard here was A/B'd at one attempt and at four before being trusted.

**Three separate places were hiding the reason, and all three are fixed.** Diagnosing this took two
CI round trips that it should not have, because at every layer the interesting information was
discarded:

- `VaultException` carried the cause as an inner exception that nothing ever printed, so the user
  saw `Could not save 'vault.kdbx'.` and could not tell a full disk from a file open in KeePassXC.
  The message now names it.
- `CliHarness` asserted exit codes with `Assert.Equal`, which reports "expected 0, actual 2" while
  the line explaining why sits unread in the captured stderr. It now quotes stderr, and
  `SeedVault` checks its own steps rather than letting a seeding failure surface later as an
  assertion about something unrelated.
- `FakeProcessLauncher.Environment` returned an empty dictionary when no child had been started,
  turning "the run failed before reaching the launcher" into
  `KeyNotFoundException: 'UNRELATED' was not present`, which reads like a merge bug and is not one.
  It now throws and says so.

The general rule this stage keeps re-learning: a failure path that discards its reason costs far
more later than the line it saved.

---

## D-0018 - `env export` writes plaintext, and single quotes are what make it portable

**Date:** 2026-07-26 - **Stage:** 1.3 - **Status:** accepted

`keypaste env export <project> [file] --dotenv [--stdout]` writes a project's variables back out as
a `.env` file. It is the first and only command in the product that puts plaintext on disk.

**It is spelled `env export`, not `export`.** `PLAN.md` said the latter; this corrects it in
writing, the way D-0014 corrected PLAN.md and D-0015 corrected prompts.md. The thing being exported
is an env set, and a bare `keypaste export` would promise a whole-vault export that does not exist
and is not planned.

### Why this does not break law 3.4

CORE.md law 3.4 is *"no secret ever touches disk unencrypted **by keypaste's doing**."* The
qualifier is the whole sentence. This command writes a file only after the user names the format,
names the destination, and answers a confirmation - the same line `get --show` already sits on,
where printing a password to stdout is refused right up until somebody asks for it. What law 3.4
forbids is keypaste deciding on its own to spill: temp files, caches, a `.env` written behind the
user's back so a child process can read it. None of that is here, and `run` exists precisely so that
none of it is ever needed.

The alternative was no export at all. Rejected: a vault you cannot leave is a vault nobody should
adopt, and for a project whose entire pitch is trust, "your data is hostage" is a worse position
than "here is the door, clearly labelled". Every credible tool has an export; the honest version
says what it just did.

Two written claims went false the moment this shipped and were corrected in the same PR: the
`DotEnv.cs` remark and D-0015's memory-hygiene paragraph, both of which said keypaste writes nothing
in plaintext. Both now say "of its own accord". A claim in a doc comment ages exactly as badly as
one in a decision record, and the doc comment is the one an auditor reads first.

### Single quotes, not escaped double quotes - the one real correctness decision

The obvious writer double-quotes every value and escapes it. It round-trips perfectly through
`DotEnv` and it is **wrong**, because the round-trip property does not test the readers the file is
actually for. `motdotla/dotenv` post-processes a double-quoted value by expanding only `\n` and
`\r`; it does not unescape `\\`, `\"` or `\t`. Measured against dotenv 17.4.2:

| written | keypaste reads | node dotenv reads |
|---|---|---|
| `V='C:\logs\app'` | `C:\logs\app` | `C:\logs\app` |
| `V="C:\\logs\\app"` | `C:\logs\app` | `C:\\logs\\app` |
| `V="a\tb"` | `a<TAB>b` | `a\tb` |
| `V="say \"hi\""` | `say "hi"` | `say \"hi\"` |

So `WINDOWS_PATH` from the reader's own golden fixture - a file node reads *correctly* today - would
come back doubled after a pull-then-export, with every keypaste test green.

A second warning was designed and then **removed**: a value ending in a backslash puts one against
the closing quote, which older regex-based dotenv releases were reported to run past into following
lines, corrupting *other* variables. It does not reproduce. dotenv 17.4.2 reads
`A='trailing\'` and `A="trailing\\"` correctly in both quote styles, with the variables after them
intact, and so does `sh`. Shipping the note anyway would have meant a warning that fires on a case
that works, which is how warnings stop being read - the same reasoning that keeps the red alarm off
an empty export. If a reader that actually gets this wrong turns up, the note comes back with a
version number attached.

The rule that ships:

| value | form |
|---|---|
| empty | `KEY=` |
| only `A-Za-z0-9` and `_ . / : @ % + , - = ^` | unquoted |
| no apostrophe and no carriage return | **single-quoted, no escaping at all** |
| contains an apostrophe or a carriage return | double-quoted with the five escapes, and a note |

Single quotes are literal in keypaste, `motdotla/dotenv`, `python-dotenv`, `joho/godotenv`,
`compose-go` and `sh` alike, carry newlines so a PEM key stays a readable block, and **suppress the
`${VAR}` expansion godotenv and python-dotenv perform by default** - which keeps
`DotEnvNoteKind.LiteralInterpolation` a note rather than turning it into a live bug in the exported
file. The escaped form is therefore reached only for a value containing an apostrophe or a CR, and
those keys are named on stderr. Keys, never values.

**`~` is excluded from the unquoted set** although the reader accepts it there. `~/bin:~/local`
round-trips through keypaste and then tilde-expands - after every `:` - in any shell that sources
the file, baking one machine's home directory into the value. That is the same hazard the parser
already refuses to accept for `$`, and the fix costs one pair of quotes.

Conformance was checked by hand rather than gated in CI: the golden file read by node `dotenv`
17.4.2 returns all four values byte-identically, including a multi-line single-quoted value and a
`#` inside a value. A CI gate reading the file with node, python-dotenv and godotenv is the only way
to keep that claim honest over time and is parked in `ideas.md`; it was rejected for now because it
puts an npm and pip install on the three-OS test job for a property the docs can state precisely.
`docker run --env-file` is named in the docs as explicitly unsupported - it does no quote or escape
processing at all.

### Fail closed, and the boundary the two ends have to share

`DotEnvWriter.TryFormat` writes nothing and names every offender on: a key `EnvConvention.IsValidKey`
rejects, keys differing only in case, a key listed twice, a NUL, a lone surrogate, and **a file that
would exceed `DotEnv.MaximumBytes`**. That last one is the interesting one: the escaped form can
nearly double a value and non-ASCII costs up to four bytes a character, so without it keypaste could
write a file keypaste refuses to read - law 4.6's failure with both parties in-house. The writer
reuses the reader's constant and the reader's comparison, so the two agree on the boundary exactly.

Duplicate keys are the writer's own rule rather than a shared one: injecting the same name twice is
harmless, while a `.env` that sets a key twice is one `DotEnv.TryParse` rejects outright.

The unusable-name and case-collision checks moved out of `EnvironmentMerge` into
`Keypaste.Core.EnvNameRules`, now shared by `run` and by the writer. This does not contradict
D-0016's "the check lives in the CLI, not `EnvStore`": that argument was about `Read` staying
permissive so `env ls` and `env rm` can still show whatever KeePassXC put in the file, and `Read`
still does not call it.

### The file it leaves behind

`FileMode.CreateNew`, so an existing file is never followed or truncated; `--force` deletes and
re-creates rather than truncating in place, because truncation keeps the old file's permissions and
the mode below would then apply on a first export and silently not on a repeat.
`FileStreamOptions.UnixCreateMode` sets `0600` on Linux and macOS and must be left null on Windows,
where the setter throws and there is no equivalent - stated in SECURITY.md rather than papered over.
A `.git` ancestor is pointed out but does **not** refuse: a `.env` in a gitignored repo root is the
normal case, and refusing it would train people to reach for `--force`.

`--yes` is allowed, on the same rule as `rm` and `env pull`. prompts.md asked for "an explicit
interactive confirmation", which read strictly forbids the flag; that was rejected because
`env export --stdout > .env` defeats it in five characters, and SECURITY.md's own standard is that a
control which only looks like a control is worse than none. `--stdout` is exempt from the
confirmation entirely: naming the flag is the consent, exactly as `get --show` is, and nothing is
left behind to answer for. An empty project gets no warning and no question - shouting when there is
no secret is how a warning becomes furniture.

### The colour, and why it is not one code path

The warning is red, which required the first colour anywhere in the CLI. `IConsoleStyle.Alarm` takes
the writer rather than returning a decorated string, and the decision to colour is made once at
construction: stderr must be a terminal (**stderr**, not stdout, which `--stdout` pipes on purpose),
`NO_COLOR` must be unset or empty, and `TERM` must not be `dumb`.

The implementation is deliberately platform-split. On Unix the escape goes straight into the target
writer, because .NET's `Console.ForegroundColor` emits its escape to *stdout* - which would inject
escape codes into a pipe while trying to colour the terminal beside it. On Windows the reverse holds:
raw escapes render only when `ENABLE_VIRTUAL_TERMINAL_PROCESSING` is set, which conhost does not do
for us, while `Console.ForegroundColor` is the console-attribute API that works either way. Two
`kernel32` P/Invokes to force the mode were rejected under law 3.9: a dependency on the secret path
is not something to buy for a warning colour. The test harness's fake writes plain text, so every
existing substring assertion over stderr still holds and no test ever sees an escape.

### A reader bug found while writing the writer

`DotEnv.TryDecode`'s UTF-16 branches used `Encoding.Unicode` and `Encoding.BigEndianUnicode`, which
use the **replacement** decoder fallback - so the `catch (DecoderFallbackException)` could never fire
for either, and an ill-formed UTF-16 `.env` decoded to `U+FFFD` with `TryDecode` returning true. A
secret silently corrupted on the way in, in the branch that exists for Windows PowerShell 5.1, which
is exactly where an ill-formed file comes from. Now `UnicodeEncoding(bigEndian, false,
throwOnInvalidBytes: true)`, with a lone-surrogate test per endianness. Fixed here because it is the
other half of the same round trip, and carried as its own commit so it is reviewable alone - the way
1.2b carried the `ConsoleSecretPrompt` stdin fix.

### What the tests had to be, to be worth having

The round trip is asserted through the **byte** layer - format, encode, decode, parse - not from
text straight into `TryParse`. Two failure modes live only in the bytes: `Encoding.UTF8` emits a byte
order mark, so the obvious `File.WriteAllText(path, text, Encoding.UTF8)` writes one and the reader
then strips it off the first *key*; and the size ceiling is enforced on bytes. `DotEnvText` therefore
exposes `Utf8` and the CLI writes bytes, never a string, so the wrong encoding is not reachable.

The property that actually protects the design is **minimality**: double quotes appear only for a
value containing an apostrophe or a CR. Switching the writer to always-double-quote was tried, and
`EveryValueInTheCorpus_SurvivesTheRoundTrip` **stayed green** - as did every other round-trip
assertion. Only the minimality test and the named quoting cases went red. That is the whole argument
for writing them: the obvious property test cannot see the bug the design exists to avoid.

Each new gate was proved able to fail before being trusted: always-double-quote turns the quoting
tests red, dropping the size ceiling turns the size test red, restoring the replacement fallback
turns both UTF-16 tests red, and inverting the `--yes` guard, the overwrite refusal and the alarm
each turn their own CLI test red.

## D-0019 - The first dependency in `src/`: ModelContextProtocol.Core, and nothing more

**Date:** 2026-07-26 - **Stage:** 2.1 - **Status:** accepted

`src/` has carried zero `PackageReference` entries since D-0004, and `Directory.Packages.props` says
so in a comment. That ends here, on the agent bridge, which is exactly where CORE.md law 3.9 demands
the justification be written down.

PLAN.md's locked stack decision already names "official ModelContextProtocol C# SDK, stdio
transport", so the question was never *whether* but *how narrow*.

**Rejected: the main `ModelContextProtocol` package.** About twelve transitive packages on
`net10.0`, and its documented sample additionally wants `Microsoft.Extensions.Hosting`, which is
roughly twenty more. What that buys is `AddMcpServer()` DI sugar and attribute-based tool scanning.
What it costs on the secret path is a configuration system that reads files and environment
variables, a DI container, and a default console logger that writes to **stdout** - which on a stdio
MCP server *is* the protocol stream. A dependency whose happy path corrupts the transport is not one
to take for syntactic sugar.

**Rejected: hand-rolling JSON-RPC.** Roughly four hundred lines re-implementing a versioned protocol,
wrong in ways nobody would notice until a client bumped. CORE.md §6.5: boring beats clever.

**Taken: `ModelContextProtocol.Core` 1.4.1.** Apache-2.0, the `modelcontextprotocol` org,
co-maintained with Microsoft, implementing spec revision 2025-11-25. `StdioServerTransport`,
`McpServer.Create` and the tool types all live in it, so a host-free stdio server needs nothing else.
The closure is **four packages**, measured from the regenerated lock file rather than quoted from
documentation: the SDK plus `Microsoft.Extensions.AI.Abstractions`,
`Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`.
The lock diff is twenty-eight lines and contains no HTTP client, which is what makes THREATS.md T-9's
"nothing leaves the machine" a claim a reader can check rather than one they have to believe.
`Keypaste.Core` - the vault and crypto path - still has zero.

### The AOT gate passes, and the reason it was expected not to was wrong

D-0005 put `IsAotCompatible` and the trim/AOT analysers on `src/` as "a dependency-selection gate
disguised as a compiler setting", installed for precisely this moment. It fires clean:
`Keypaste.Mcp` keeps `IsAotCompatible=true` with no scoped suppression and no new open question.

The plan for this stage asserted that `McpServerTool.Create(Delegate, ...)` would trip IL2026 and
IL3050 and that hand-writing the tools was the escape. **That was checked and it is false** - both
forms compile clean under all four analysers with warnings as errors, and the SDK assembly carries
`[AssemblyMetadata("IsTrimmable","True")]` and `("IsAotCompatible","True")`, so its authors have
vouched for it rather than gone quiet. The analysers were confirmed to be awake rather than asleep by
a negative control in the same probe project: `JsonSerializer.Serialize` **does** fail there with
IL2026, IL3050 and CA1869.

Two things follow, and they are recorded because a decision record that repeats a comfortable
assumption is worse than none:

- The claim the gate actually supports is that no call keypaste *makes* is annotated unsafe. Whether
  the bridge AOT-**runs** is a different question, answerable only by a real publish and run, which
  is O-0006's standing remedy - now covering one more component.
- **`JsonSerializer` is still banned**, now for a demonstrated reason rather than an assumed one.
  Every JSON byte keypaste writes goes through `Utf8JsonWriter` and every one it reads through
  `JsonDocument`, which needs no source generator and no partial-class ceremony.

### The tools are still hand-written, for the reason that survived

The delegate path generates a tool schema from the C# signature. Measured against the four arguments
prompts.md specifies, it drops `additionalProperties`, the `field` enum, the length bounds on
`reason` and the range on `ttl_seconds`; it leaves `Annotations` **null**, so none of the four
behaviour hints are set and the spec's defaults - `destructive` and `openWorld` both true - apply;
and it renames `ttl_seconds` to `ttlSeconds`, because that is what the parameter was called.

A wire contract that is a byproduct of a method signature changes when somebody renames a parameter.
On a credential bridge the schema *is* the contract with the agent, so it is written down as a
literal, reviewed in a diff, and pinned by a test.

### Migration cost, agreed in advance

2.0.0 of the SDK was promised on or before 2026-07-28 and is a breaking rewrite: it removes the
`initialize` handshake and moves client identity to per-request `_meta`, where it is optional. 1.4.1
is pinned deliberately, and the blast radius of moving is bounded by keeping every rule in
`Keypaste.Core`, where the SDK cannot be referenced at all: only `src/Keypaste.Mcp/` changes, and
within it only the files that touch SDK types. The audit schema already models the client's name as
absent-able, so law 3.3's "every access is logged with who" survives identity becoming optional.

## D-0020 - The audit log: a precondition, not observability

**Date:** 2026-07-26 - **Stage:** 2.1 - **Status:** accepted

One JSON object per line at `~/.keypaste/audit.jsonl`, appended and never rewritten.

**It lives beside the policy file, not beside the vault.** The vault is a file the user syncs with
their own tooling - that is the local-first bargain in CORE.md §2. An append-only log that travelled
with it would produce a conflicted copy on every second machine, break the per-file hash chain Stage
2.4 adds, and hand anyone with the synced folder a write path into another machine's record. The log
describes what happened *here*.

**The schema is fixed now, in a fixed key order**, because Stage 2.4 hashes the raw bytes of each
line and "the bytes of a line" has to be a well-defined thing before anything commits to it. `v` is
on every line from the first, so 2.4 can write `v:2` and report the earlier prefix as *predates the
chain* rather than as *tampered with* - the distinction that stops the first `keypaste log verify`
from crying wolf. `decision` and `method` already carry the vocabulary 2.2 and 2.3 need (`prompt`,
`policy`), so neither has to migrate a field.

**What is redacted, and what deliberately is not.** No password, user name, URL, note, master
password, or entry title read *out of the vault* appears at any schema version; `field` records
which field was asked for and never its contents. The `entry` argument the agent itself supplied
*is* recorded, sanitized and capped - that is law 3.3's "which entry", and it is sanitized
segment-wise so that `env/dev/STRIPE_KEY` stays legible instead of collapsing into
`env dev STRIPE_KEY`. The agent's free-text `reason` is kept three ways: a 200-character sanitized
excerpt for the human, the true length so truncation is never silent, and a SHA-256 of the raw text
so 2.2 can prove the sentence shown in the approval dialog is the sentence that was recorded.

**Law 3.5 and law 3.3 do not conflict.** The resolving word is in 3.5 itself: *telemetry*. 3.5
governs what leaves the machine; 3.3 governs what is recorded on it. A log the user cannot read
would defeat 3.3; a log that left the machine would defeat 3.5. The separation is architectural
rather than promised - stdio only, no sockets, four pinned packages with no HTTP client among them -
and THREATS.md T-9 states it as something a reader can check by grepping the lock file.

### No record, no disclosure

If a line cannot be written, the call is refused - no credential and no entry names, even when
everything else would have succeeded. If the log cannot be opened at all, the server refuses to
start.

This looks severe and is not. If a call could succeed with its record unwritten, then *breaking the
logger becomes the mechanism for invisible access*: fill the disk, remove write permission, point
`HOME` at a read-only mount. Laws 3.3 and 3.7 together leave one answer. The record is therefore
written *before* the response is produced, so a crash in between over-reports an access rather than
under-reporting one, which is the safe direction.

### Two servers share one file, and the obvious implementation loses lines

Claude Desktop and Claude Code each spawn their own `keypaste-mcp`. `FileMode.Append` is **not**
enough to make that safe, which the tests found rather than the design: .NET's `FileStream` keeps
its own idea of the file's length and writes at that offset, so two streams on one path overwrite
each other. Twenty records became ten.

Appends therefore take a sidecar `.lock` file for the moment of the write and seek to the real end
first. A lock file left behind by a crash blocks nothing, because exclusion comes from holding the
handle open rather than from the file existing, and the operating system closes handles when a
process dies - which is what keeps this free of the stale-lock problem every "delete the lock on
exit" scheme eventually has.

A related trap is recorded for Stage 2.4: `File.ReadAllLines` asks for `FileShare.Read`, which
denies other *writers* and therefore fails outright on Windows while any server holds the log.
`keypaste log` must open `FileShare.ReadWrite`.

### What "append-only" claims

Records are only added, one whole record at a time, at the end; nothing in keypaste truncates,
rewrites or deletes, and the only seek is to the end immediately before a write. That is a statement
about keypaste's behaviour and nothing else - the file belongs to the user's account, so anything
running as that user can rewrite it. **Append-only by construction within keypaste; tamper-evident
from Stage 2.4; never tamper-proof.** There is no rotation, because rotation deletes lines.

## D-0021 - Exposure: the listing surface is default-deny too

**Date:** 2026-07-26 - **Stage:** 2.1 - **Status:** accepted

`list_entry_names` can name the `env/**` subtree and nothing else unless a human writes
`--expose <glob>` into the MCP client's configuration.

Law 3.2's "default is deny" is usually read as being about credentials. Entry names deserve the same
treatment: a complete inventory of a personal vault - bank, employer, recovery email - is exactly
what turns a vague request into a targeted one, even with no secret attached, and law 3.5 singles
out entry names as never-telemetered for that reason.

**The tool takes no arguments at all.** No `group`, no `prefix`, no `limit`. That is not
minimalism; it is that any such parameter is a knob the untrusted party turns. Scope is set in a
file the human wrote, and there is nothing in the protocol surface that can widen it.

**Globs match the group path and the title as two separate values**, never the joined
`VaultEntry.Path`. This is what stops a title from impersonating a path: an entry titled
`../../prod/ROOT_TOKEN` sitting in `env/dev` is matched as a *title*, so it cannot satisfy a group
pattern and cannot escape into `env/prod`. Deciding this now rather than in 2.3 is what stops the
policy file from defining a second, subtly different matching domain.

Three smaller rules, each of which is a way this could have failed open:

- Matching happens on the **raw** name, before sanitization, so no change to the sanitizer can ever
  widen what is exposed.
- Matching is **case-sensitive**. A case-insensitive match is a wider match.
- An exposure built from **no globs allows nothing**. "The user said nothing" must never collapse
  into "everything", so applying the default is something the front end does explicitly.

A malformed glob is fatal at startup rather than skipped. Skipping one would leave a *different*
exposure in force than the one the human wrote, and on this path the difference could be a wider
one. This is deliberately the opposite of how Stage 2.3 will treat a malformed `policy.toml`, where
ignoring the file leaves "prompt for everything" - the safe fallback there, and not here.

## D-0022 - The vault stays locked, and the seam that says so

**Date:** 2026-07-26 - **Stage:** 2.1 - **Status:** accepted

`keypaste-mcp` ships with exactly one implementation of its vault seam, and it always answers
"locked".

An MCP server's stdin and stdout *are* the JSON-RPC stream, and Claude Desktop spawns it with no
terminal, so there is nowhere to prompt for a master password. Both workarounds are worse than
waiting: putting it in the client's configuration file would place the secret that protects every
other secret into plaintext JSON, which is precisely what law 3.1 exists to prevent, and using MCP's
own facility for asking the user something would route it through the untrusted party. Stage 2.2
builds a human channel for approval; whatever owns that channel should own the unlocked session, so
building a throwaway one now means building it twice.

**The seam yields names, never entries.** The type that crosses it carries a group path and a title
and has no other members, so no implementation - including the real one 2.2 adds - can return a
password through the listing path even by accident. That is a structural guarantee rather than a
promise, and it is checkable by reading one short file.

**`request_credential` does not use that seam**, and 2.2 must give it a separate one. Fusing the two
into a single "vault access" abstraction would hand the listing path the ability to return a secret,
which is the single change most likely to turn `list_entry_names` into an exfiltration tool.

### The cost, stated rather than hidden

The listing, scoping and sanitizing code is complete and thoroughly tested, and **in the shipped
binary it is unreachable**. A test double is what exercises it. A green suite therefore says less
than it appears to, and both THREATS.md T-7 and the test class's own doc comment say so - because a
suite that looks like it proves the shipped path works, when it proves the type system instead, is
worse than one test fewer.

The same honesty applies to `request_credential`: every test asserting that it denies would pass
whether or not validation, scoping and logging existed, because it is hard-coded to deny. Those
tests earn their keep in 2.2. What is meaningful today is that the two refusals are *distinguishable*
- `out-of-scope` means keypaste will never discuss that entry, `not-implemented` means it cannot ask
yet - which is what makes 2.2 an added branch instead of a rebuild, and what lets an agent stop
retrying the first.

---

## D-0023 - The approver is a separate process the human starts

**Date:** 2026-07-26 · **Stage:** 2.2 · **Status:** accepted

`keypaste agent` is a foreground command. It unlocks the vault, listens on a local named pipe, and
asks the person who started it about every credential request. `keypaste-mcp` becomes a secretless
proxy: it validates, scopes, forwards, audits, and returns one field.

Three designs were on the table. Two of them put the approval flow inside `keypaste-mcp`, differing
only in whether the master password was collected by the approval dialog itself or by a separate
unlock held as a session. Both were rejected for the same reason, and it is not a technical one.

**keypaste exists to stop people putting secrets where they do not belong.** A window that an AI
caused to appear, asking for the master password, teaches exactly the habit the product is built to
break - and any local process can draw an identical one. Under this design the master password is
typed in a terminal the user opened, in response to a command they typed, using the same
`ConsoleSecretPrompt` ceremony as `keypaste get`. **Nothing an agent does can raise a password
prompt.** That is a property worth a whole extra process, and CORE.md tiebreaker 1 settles it on its
own: if it risks trust, no.

Three things fall out of it that were not the reason but are worth as much:

**It deletes a class of problem rather than solving one.** An MCP server's stdin and stdout *are*
the JSON-RPC stream, so an in-process prompt would have had to reach the controlling terminal
directly: `/dev/tty`, two incompatible `termios` struct layouts for Linux and macOS, a P/Invoke
surface on the exact code path D-0005's AOT gate exists to make expensive, and a `stty -echo` that
leaves somebody's shell typing invisibly if the process dies mid-prompt. Put the prompt in a process
whose stdin already *is* a terminal and none of that exists. `CONIN$` on Windows has the same shape
and the same answer.

**THREATS.md T-7 closes.** The listing, exposure and sanitization code that D-0022 admitted was
"complete, thoroughly tested, and unreachable in the shipped binary" is on the live path now, because
the approver has an unlocked vault to list from.

**Stage 4.3 becomes a re-skin.** prompts.md describes an Agent Activity screen with Approve/Deny
buttons "replacing the OS dialog when the app is open". That is another `IApprovalChannel`, not a
rewrite - and the seam it needs is the seam this stage had to cut anyway.

### What it costs, stated rather than buried

A second thing to be running, and a new IPC surface (D-0024, THREATS.md T-10). The refusal for "no
agent is running" therefore names the exact command to run and deliberately omits "do not retry",
because retrying is the right thing to do once somebody has started one.

**Deliberately not a daemon.** No service, no launch agent, no systemd unit, no PID file, no
starting itself on demand. Those turn "is anything able to act as me right now?" into a question
with a complicated answer, and the honest version of that answer is "look at whether the terminal is
still open".

**No idle auto-lock in 2.2.** The vault stays unlocked for as long as the agent runs; Ctrl+C is the
lock. It is not in the specification, Stage 4.1 explicitly owns idle locking, and
`VaultCredentialSource` already takes the vault through a delegate returning null - so adding it
later changes nothing else. Said in docs/approvals.md rather than left to be discovered.

**One measured finding that shaped the concurrency design.** The MCP SDK dispatches tool calls
concurrently - `ServerToolsTests.TwoToolCalls_RunAtTheSameTime` pins it. That is good news for the
demo, because a request parked for forty-five seconds waiting for a person does not stall the
session. It is bad news for the approver, because two requests really can race two prompts onto one
screen, which makes single-in-flight a load-bearing rule rather than a precaution and means
everything the flow mutates has to be thread-safe.

---

## D-0024 - The approver channel is a named pipe with CurrentUserOnly

**Date:** 2026-07-26 · **Stage:** 2.2 · **Status:** accepted

`NamedPipeServerStream` and `NamedPipeClientStream` with `PipeOptions.CurrentUserOnly`, on both
platforms, one code path.

The runtime does the access check. On Windows the option restricts the pipe's ACL to the current
user; on Unix, where .NET implements named pipes over a Unix domain socket, it creates the socket
owner-only and verifies on connect that the peer's socket is owned by the same user. That buys:
no `System.IO.Pipes.AccessControl` package (which would be the second runtime dependency in `src/`
and would have to clear D-0019's bar), no hand-written `PipeSecurity`, no
`UnixDomainSocketEndPoint`, and no `sun_path` length limit to discover on somebody's long home
directory. CORE.md law 3.9 rewards the option that adds nothing.

The name carries a per-user discriminator - sixteen hex characters of SHA-256 over the user's
profile path - because .NET's Unix emulation puts the socket at a predictable path under the shared
temporary directory, and without it two users on one machine would collide. It is a namespacing
device and not a secret, exactly like `EntryHandle`.

**The residual, for T-10.** That path is predictable, so another local user can pre-create it and
stop your approver binding. What they cannot do is be connected to, because the ownership check
refuses. Denial of service against the approver means keypaste denies every request, which is the
direction law 3.7 asks for.

**Wire format:** one JSON object per line, `Utf8JsonWriter` out and `JsonDocument` in only -
`JsonSerializer` stays banned for the demonstrated IL2026/IL3050 reason in D-0019. Frames are capped
at 64 KiB, and a peer that sends more without a delimiter loses its connection rather than growing a
buffer inside the process that holds the unlocked vault.

**One transport is not one seam.** D-0022 forbids fusing the listing path and the credential path.
That still holds: they are different message kinds with different handlers, and only
`CredentialReply` has anywhere to put a secret. Sharing a pipe does not fuse them; sharing an
interface would have. `CredentialReply.ToString()` is overridden to redact the value, because a
record prints every member by default and one interpolated string in a log line or an exception
message would put a live credential somewhere it can never be taken back from.

---

## D-0025 - The approval window is 45 seconds, not the 60 the specification asks for

**Date:** 2026-07-26 · **Stage:** 2.2 · **Status:** accepted

prompts.md 2.2 says "60-second timeout is deny". Shipping that would have been wrong, and the reason
is measurable rather than aesthetic.

**60 seconds is the MCP client's own request timeout.** The reference SDK's
`DEFAULT_REQUEST_TIMEOUT_MSEC` is 60000, and both Claude Desktop and Claude Code inherit it. A
60-second approval window therefore sits exactly on the client's wall: an approval given at second
55 arrives into a request that has already been abandoned, the user sees an error, and the agent's
retry raises a **second prompt for something the human has already approved** - which is prompt
fatigue and a confused deputy in one move.

45 seconds by default, `--approval-timeout` to change it, floor 5 and ceiling 55.
`ApprovalPromptTests.TheDefaultWindowSitsUnderEveryClientsOwnTimeout` is what stops somebody
"fixing" it back to 60. The number 60 appears in the documentation only as the client's ceiling and
the reason ours sits under it.

**Progress notifications were considered and rejected.** They renew the client's timeout only where
the client opted in with a `progressToken` and honours `resetTimeoutOnProgress`; several do not. A
design that must be correct against a hard wall anyway gains nothing from them but surface.

**A related finding, measured and not assumed:** with `ModelContextProtocol.Core` 1.4.1, a client
abandoning a single `tools/call` does not reach the server at all - the token the tool is handed is
never cancelled. So `AuditMethod.Cancelled` is written by the defensive checks in the bridge and the
gate, but is not produced by that path. `ServerToolsTests.ACallTheClientAbandons_IsStillAudited_AndCarriesNoCredentialIntoTheLog`
says so in its own doc comment rather than being named for a branch it never reaches. The consequence
worth knowing is not a leak - nothing reaches a party that was not already on that stream - but that
an abandoned request still spends a person's approval, and the agent's retry is then served from the
grant cache without asking them again.

---

## D-0026 - The grant cache stores the value, keyed on the connection

**Date:** 2026-07-26 · **Stage:** 2.2 · **Status:** accepted

A grant is keyed on `(ConnectionId, EntryHandle, Field)` and holds the released characters until its
TTL expires.

**Not the client's asserted name.** THREATS.md T-3 says that name is unauthenticated - any process
that can spawn the bridge can call itself `claude-code` - so it may be an audit field and never an
authorization input. A connection id is the strongest honest scoping available: it means *the process
the human approved for*, and when that process restarts its connection dies and its grants die with
it.

**The field is part of the key.** The specification says "repeat requests for the same entry", and
that is a bug as written: without the field, an approval a person gave for a user name would silently
satisfy a request for the password. `GrantCacheTests.AUsernameGrant_DoesNotSatisfyAPasswordRequest`
pins it.

**The value, not a capability.** A token meaning "you may re-open the vault and read field X" would
be worse three ways: it keeps a capability alive rather than a datum, it re-enters the vault on every
hit, and the vault may have changed underneath so the second answer differs from the one the person
approved. Storing the value makes a grant exactly what was approved, makes expiry an overwrite, and
lets the approver drop its unlocked vault without invalidating grants a person already gave. A hit
hands out a *copy*, so one caller cannot zero another's grant.

**Expiry zeroes rather than forgets.** Each grant carries a one-shot timer for its own TTL, so an
unused grant is cleared at the moment it expires instead of lingering in the heap until something
happens to look for it.

**The effective TTL is `min(requested, --max-ttl)`, default 300 seconds, and the prompt shows the
number that will actually apply.** Showing an agent's requested hour when five minutes will be
granted would make the prompt a worse source of truth than the audit log, which is backwards.

### The residual, which is real (THREATS.md T-12)

On a cache hit the human never sees the second request's stated reason. An agent can win one approval
honestly and then reuse the grant for the rest of the TTL with reasons nobody reads. What is done
about it: every hit is still audited, as `granted` / `grant-cache`, recording *that* request's reason
excerpt, length and SHA-256 - so divergence from the reason a person actually read is visible in the
log when the two lines are paired. And the approver prints a line per hit to its own terminal, so a
storm is visible without being modal.

**Deliberately not done:** re-prompting when the reason is "materially different". That is a
heuristic over untrusted text, which is the same argument T-1 already makes for refusing a phrase
blocklist, and it would hand an agent a lever for forcing re-prompt loops.

---

## D-0027 - Two ways of saying no are not one way

**Date:** 2026-07-26 · **Stage:** 2.2 · **Status:** accepted

`AuditMethod` gained seven members in this stage: `prompt`, `grant-cache`, `timed-out`, `cancelled`,
`no-approver`, `busy`, `cooldown`, `failed`. Each one selects both the word written to the audit log
and the sentence an agent reads, keyed in one place (`ToolText.Refusal`) so the two cannot drift.

The distinction that earns its keep is **whether retrying could ever help**, because an agent reads a
refusal and decides what to do next:

- *no approver* names the command to run and says to try again after - that refusal is five seconds
  from being fixed, and the agent is the only party in a position to say so.
- *a person said no* and *cooldown* say **do not retry**. Without that sentence a capable agent loops
  on a considered refusal, which is both a token bill and a stream of prompts until somebody clicks
  the wrong one (T-11).
- *timed out* and *busy* deliberately **omit** it. Nobody decided anything - they were away from the
  keyboard, or looking at another request - so one later attempt is reasonable.

`ServerToolsTests` pins the presence and the absence of "do not retry" in adjacent tests, so a change
that flattened every refusal into the same wording goes red.

**One bug was found doing this.** `AuditLog.Wire(AuditMethod)` fell back to `"vault-locked"` for any
member it did not name, so each of the seven new ones would have been recorded as a denial that never
happened - the quietest possible way to make the log law 3.3 requires say something untrue. The
fallback is now `"unknown"`, and `AuditLogTests.EveryAuditMethod_HasItsOwnWireString` is what
actually stops the next member slipping through. It does not throw: an audit write must not be the
thing that takes the server down.

**One information leak was closed.** "There is no such entry" and "that entry is outside your
exposure" have to be the *same* answer, or the difference is an oracle an agent can use to enumerate
what exists in parts of the vault it was never allowed to see - the exposure rule undone by an error
message. Both produce `out-of-scope`, with identical text, an identical decision and an identical
empty entry field. The audit line still records which it really was, because that reader is the
human. `ApproverHandlerTests.AMissingEntryAndAForbiddenOne_AreIndistinguishableToTheAgent`.

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
visible to WMI and Sysmon on Windows, and written to the shell's history file. keypaste allows it
deliberately and warns once on stderr when it happens (D-0014).

What is still open is the escape hatch. A CI job that uses the inline form on purpose emits that
line on every run, and a warning nobody can silence is a warning people learn to filter. The
obvious answer is a flag in the shape `rm --yes` already uses, which trades one flag for a warning
that stays meaningful. It is not shipped yet because one person running this does not need it, and
the wrong version of it - a flag that suppresses the message without changing the exposure - is
worse than nothing. Decide before Stage 3, when the audience stops being one person.

**Two variables differing only in case are one variable on Windows.** ~~Reading is not refused~~ -
**answered in D-0016 (Stage 1.2b): a hard failure on every platform, not a platform-conditional and
not last-writer-wins.** Reading still lists both, so `env ls` and `env rm` can show and clear what
KeePassXC put there; only injection refuses. The reasoning, including why the platform-conditional
is the worst of the three options rather than the safe middle, is in D-0016.

Only the `argv` half of this entry is still open.
