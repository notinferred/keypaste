# DECISIONS.md — engineering decision log

> One entry per decision that a future contributor (or a future you) would otherwise have to
> reverse-engineer. docs/PRODUCT.md decides *what* keypaste is; this file records *how*, and why.
> Add an entry per decision, and when one is superseded, rewrite it to say what is true now and
> why the earlier answer failed. Keep the reasoning; scrap the was-then-now narration. Git holds
> what the file used to say.

---

## D-0001 — Project naming: PascalCase projects, kebab-case binaries

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted

`docs/STEPS.md` and the build prompt name the packages `keypaste-core`, `keypaste-cli`, `keypaste-mcp`. C#
namespaces cannot contain hyphens, and a `keypaste-core.csproj` yields the root namespace
`keypaste_core`, which fights every analyzer naming rule forever.

Projects are therefore `Keypaste.Core`, `Keypaste.Cli`, `Keypaste.Mcp`, while the *shipped binaries*
keep the roadmap's names: `keypaste` and `keypaste-mcp`. Kebab-case survives where it is
user-visible, PascalCase where it is code. The mapping table is in the README so the roadmap
documents still read true.

## D-0002 — Target framework `net10.0`

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted

docs/STEPS.md locks ".NET 8+". .NET 10 is the current LTS and the only SDK on the development machine.
The SDK is pinned in `global.json` at `10.0.302` with `rollForward: latestPatch`, which keeps the
compiler and analyzer behaviour inside one feature band while still accepting SDK security patches.
`LangVersion` is pinned to `14.0` rather than `latest` so language semantics do not drift when the
SDK rolls forward.

## D-0003 — Test stack: xUnit v3 on Microsoft.Testing.Platform

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted

Considered xUnit v2, MSTest 3, NUnit, and TUnit.

xUnit v3 wins on dependency count, which is the deciding factor under docs/PRODUCT.md §3.9: one package
(`xunit.v3`) against v2's three (`xunit` + `Microsoft.NET.Test.Sdk` +
`xunit.runner.visualstudio`) and their transitive tail. v3 test projects are self-executing
executables on Microsoft.Testing.Platform, which also matters concretely for what is coming: Stage
0.2 tests shell out to `keepassxc-cli`, and Stage 1.2 tests spawn child processes and assert on
signal forwarding. VSTest's testhost proxying makes those flaky; MTP does not.

TUnit is faster and AOT-native but young and single-maintainer. For a security tool, docs/PRODUCT.md §6.5
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

docs/PRODUCT.md §3.9 requires dependencies to be minimised and *pinned*. Central Package Management
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
- `IsAotCompatible` and the trim/AOT analyzers on `src/` — docs/STEPS.md commits the CLI to AOT single
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
an AI assistant for authorship. docs/PRODUCT.md §3.6 says "mature audited libraries" and §6.5 says
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

docs/PRODUCT.md §4.6 makes KeePassXC compatibility sacred, and docs/PRODUCT.md cannot change. The `compat` job
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
delegate: without that seam not one password path in the CLI would be testable, and docs/PRODUCT.md §4.5
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
compatibility claim nobody can test is not.

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
`--no-history` flag is in docs/IDEAS.md, not in this stage.

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
env set. The parser is `src/Keypaste.Core/DotEnv.cs` - public, in the core, because docs/PRODUCT.md law 4.3
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

**The word "shred" does not appear in the product, and there is no overwrite pass.** Overwriting a
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

The claim is "of its own accord", and the qualifier is load-bearing: the one command that writes
plaintext is `env export`, whose entire purpose the user typed, and it is loud about it (D-0018).
`DotEnv.cs`'s remarks say the same thing in the same words — a claim in a doc comment ages exactly
as badly as one in a decision record, and it is the one a security auditor reads first.

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

**It is spelled `env export`, not `export`.** `docs/STEPS.md` said the latter; this corrects it in
writing, the way D-0014 corrected docs/STEPS.md and D-0015 corrected the build prompt. The thing being exported
is an env set, and a bare `keypaste export` would promise a whole-vault export that does not exist
and is not planned.

### Why this does not break law 3.4

docs/PRODUCT.md law 3.4 is *"no secret ever touches disk unencrypted **by keypaste's doing**."* The
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

`DotEnv.cs`'s remark and D-0015's memory-hygiene paragraph both say keypaste writes nothing in
plaintext *of its own accord*, and the qualifier is what this command costs. A claim in a doc
comment ages exactly as badly as one in a decision record, and the doc comment is the one an
auditor reads first.

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
to keep that claim honest over time and is parked in `docs/IDEAS.md`; it was rejected for now because it
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

`--yes` is allowed, on the same rule as `rm` and `env pull`. The build prompt asked for "an explicit
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
so in a comment. That ends here, on the agent bridge, which is exactly where docs/PRODUCT.md law 3.9 demands
the justification be written down.

docs/STEPS.md's locked stack decision already names "official ModelContextProtocol C# SDK, stdio
transport", so the question was never *whether* but *how narrow*.

**Rejected: the main `ModelContextProtocol` package.** About twelve transitive packages on
`net10.0`, and its documented sample additionally wants `Microsoft.Extensions.Hosting`, which is
roughly twenty more. What that buys is `AddMcpServer()` DI sugar and attribute-based tool scanning.
What it costs on the secret path is a configuration system that reads files and environment
variables, a DI container, and a default console logger that writes to **stdout** - which on a stdio
MCP server *is* the protocol stream. A dependency whose happy path corrupts the transport is not one
to take for syntactic sugar.

**Rejected: hand-rolling JSON-RPC.** Roughly four hundred lines re-implementing a versioned protocol,
wrong in ways nobody would notice until a client bumped. docs/PRODUCT.md §6.5: boring beats clever.

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
the build prompt specifies, it drops `additionalProperties`, the `field` enum, the length bounds on
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
their own tooling - that is the local-first bargain in docs/PRODUCT.md §2. An append-only log that travelled
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
prompt.** That is a property worth a whole extra process, and docs/PRODUCT.md tiebreaker 1 settles it on its
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

**Stage 4.3 becomes a re-skin.** The build prompt describes an Agent Activity screen with Approve/Deny
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
directory. docs/PRODUCT.md law 3.9 rewards the option that adds nothing.

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

The 2.2 build prompt says "60-second timeout is deny". Shipping that would have been wrong, and the reason
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

## D-0028 - The policy file is used whole or ignored whole, and keypaste parses it itself

**Date:** 2026-07-26 · **Stage:** 2.3 · **Status:** accepted

`~/.keypaste/policy.toml` lets a person pre-authorize a narrow pattern so an agent gets that one
credential without a prompt. docs/PRODUCT.md law 3.2 authorises it in the constitution's own words - "after
one explicit human approval (or a pre-approved policy the human wrote)" - and it is still the first
feature in this product that hands an agent a secret with nobody watching. Everything below is about
bounding that.

### All of the file, or none of it

A section keypaste cannot use invalidates the **whole document**, not just that section. Two good
rules and one bad line means zero rules, and every request goes back to a person.

The argument is that there is no way to know which direction a partial reading is wrong in. Skipping
the bad rule leaves a *different* policy in force than the one the human wrote, and on this path
"different" could be wider. The only safe reading of a file that is partly wrong is that it says
nothing (law 3.7).

This is the deliberate **opposite** of how `--expose` treats a bad glob, where the front end refuses
to start, and the difference is which way each failure points. A malformed exposure would leave a
server running with no scope at all; a malformed policy leaves a human being asked about everything.
So nothing here is fatal: refusing to start `keypaste agent` on a bad policy file would turn a typo -
or a planted `chmod 000` - into a denial of service on somebody's own vault.

Six states, all indistinguishable to an agent and all distinct on the operator's terminal: absent,
empty, in force, malformed, unreadable, writable-by-others. Indistinguishable to the agent is
required - telling them apart lets a request find out whether a policy exists at all, and invites a
per-entry diagnostic, which is the enumeration oracle D-0027 closed once already.

### The parser is hand-written, and the strictness is the feature

`Keypaste.Core` has carried zero `PackageReference` entries since D-0004 and D-0019 brags about it.
That does not change for this. `Policy/Toml.cs` accepts `#` comments, `[[allow]]` headers, and
`key = value` where the value is a quoted string, a whole number, or an array of quoted strings.
Everything else a real TOML document may hold - dotted keys, inline tables, literal and multi-line
strings, floats, booleans, dates, a singular `[table]` header - is a parse error naming the
construct.

**Taking a TOML package would have been defensible and was rejected on two grounds.** It would be the
first dependency in `Keypaste.Core` and the second in `src/`, on a file that decides authorization.
And full compliance is not actually the property wanted here: a policy file is a document keypaste
*obeys*, so a construct it would have to guess the meaning of is one it must refuse. A strict subset
is not a limitation of the hand-rolled reader, it is what the reader is for. `CommandLine.cs`,
`DotEnv.cs` and `MessageFramer.cs` are the precedent.

The cost is real and stated: somebody writes valid TOML keypaste rejects. It is bounded by the reader
naming the construct and the line, by `keypaste policy ls` printing that message, and by the failure
being in the direction of prompting.

**Forward compatibility fails closed.** An unknown section - `[[deny]]`, or a rule shape from a later
keypaste - invalidates the file rather than being skipped while the `[[allow]]` rules stay in force.

### Nothing defaults, and no rule string may lie about what it says

Every key but `max_per_hour` is required. Each default that suggests itself is a way for a rule to
become silently wider than what was written - an absent `fields` meaning all four, an absent
`entries` meaning everything, an absent `client` meaning anyone - and a typo in a key name would then
select one of them.

Every pattern and label must survive `EntryNameSanitizer` byte-identically. `EntryExposure` already
rejects control characters and backslashes, which is necessary and not sufficient: it accepts U+202E,
zero-width characters and the Unicode tag block, so a rule that *renders* as `env/dev/**` and *means*
`env/**` would otherwise be writable. Reusing T-1's tested sanitizer as a validator makes every
pattern safe to print by construction.

### The trap in the pattern syntax, and why the renderer exists

D-0021 fixed the matching domain in 2.1 specifically so the policy file would not invent a second
one, and a rule constructs an `EntryExposure` rather than reimplementing its algorithm. That is
inherited correctness, and it comes with an inherited surprise: unless a pattern's last segment is
exactly `**`, the last segment is the **title**. `env/dev*` - the example the build prompt itself suggests
- means *group exactly `env`, title starting `dev`*. It matches nothing under `env/dev/`, and it does
match an entry sitting directly in `env` called `devops_ROOT_TOKEN`.

Fixing this would mean a second matching language, which D-0021 rules out and which would be worse.
So `keypaste policy ls` **never echoes the line the user wrote**: it prints the two halves each
pattern parsed to, on separate lines, so the reader checks the parse instead of confirming their own
text. `PolicyRuleTests.APolicyRuleWithATrailingStar_ConstrainsTheTitleNotTheGroup` asserts both
directions, so the test documents the surprise rather than the wish.

### A spent allowance denies rather than escalating

`max_per_hour` is optional, per rule, counted process-wide on a true sliding window rather than
minute buckets - a bucket lets twice the allowance through across a boundary, and on an authorization
cap exactness is worth an array of N longs. It is counted per rule and not per connection because a
client could otherwise reset its own quota by spawning a fresh bridge.

When it is spent, the request is **denied** (`policy-limit`), not escalated to a prompt. Falling
through looks strictly safer - a human still decides - and it is worse: it turns a quota into a
prompt generator, which is THREATS.md T-11 with a lever attached, and it would make
`keypaste policy ls` lie about what the number means. An omitted allowance renders as "No limit on
how often", out loud, because an unlimited rule should be a thing somebody chose.

### `policy ls` needs no vault, and there is no `policy add`

A policy file names patterns, not entries; reading it resolves nothing and decrypts nothing. A master
password prompt in front of the one command an operator reaches for when something already looks
wrong is the wrong trade, so `--vault` is not in its option spec at all - passing it is a usage error
rather than a flag that looks accepted and does nothing.

There is no command that writes the file. keypaste must not be a writer of its own authorization
document: a command that edits it is a command an agent could talk somebody into running.

---

## D-0029 - Where the policy sits in the order, and what it deliberately does not touch

**Date:** 2026-07-26 · **Stage:** 2.3 · **Status:** accepted

`ApproverHandler`'s doc comment has said since 2.2 that the order is the security property. This is
where the policy check goes in it, and every position is an argument.

```
field releasable -> exposure parses -> resolve -> exposure re-check -> grant cache
  -> COOLDOWN -> POLICY -> prompt -> read the field, last of all
```

**After the exposure re-check, never before.** A rule is a *narrowing* of what a person would
otherwise be asked about, not a parallel grant of authority. Ahead of that check, a rule reading
`entries = ["**"]` would release an entry the bridge's own `--expose` never permitted - a file in the
user's home directory overriding the client's configuration. Both gates must pass and the exposure is
the ceiling. This is what makes "a rule can never widen to secrets outside its pattern" structurally
true rather than checked.

**After the grant cache.** A live grant is a decision a person made about this exact connection,
entry and field. Serving it as `policy` would re-attribute human-approved reuse to a machine rule and
spend allowance on a request that was going to be free.

**After the cooldown, which is new public surface on `ApprovalGate` for this.** A person's explicit
no is more specific and more recent than a rule they wrote last month. Worth being honest about: no
shipped path can currently reach that state - a rule that matches never prompts, so it never arms a
cooldown for its own request, and a spent allowance denies rather than escalating. The check is
defence in depth for the paths 2.4 and 4.3 add, and the test that pins it arms the state directly
through the gate rather than pretending to reach it, because a test driven through `RequestAsync`
would pass whether or not the check existed.

**In `ApproverHandler`, never inside `ApprovalGate`.** The gate is the object that asks a human. A
policy-matched request evaluated inside it would take the one-at-a-time semaphore and could come back
`Busy` while somebody was mid-prompt - fail-closed, but it would teach an operator that policy is
flaky and push them to widen rules.

**Before the read, which stays last of all.** The file's central claim survives intact: nothing has
decrypted a field until a person said yes to this request, or said yes in advance to a rule covering
it.

### Both ceilings apply, and the arithmetic is not the caller's

`ApprovalLimits` gained `EffectiveTtlSeconds(requested, ruleCeiling)`. A rule may only lower: one
saying `max_ttl_seconds = 3600` under an approver started `--max-ttl 60` grants 60. The overload
exists so the policy branch does no arithmetic of its own - a hand-written `Math.Min` at the call
site is exactly how a file in `~` would come to raise a ceiling set on the command line, and it is
one of the negative controls.

A request *over* the rule's ceiling is clamped, not bounced to a person. Bouncing it would hand an
agent a one-integer lever for manufacturing prompts on demand.

### A policy grant does not seed the grant cache

Four reasons. It would blow a hole in `max_per_hour` by construction, serving every later request
inside the TTL free and off the count. It would hide releases two onward behind `grant-cache` lines
that name no rule. There is nothing to suppress, because with a rule in force nobody is being asked
twice. And it keeps `GrantCache`'s own doc comment - "what a human has already said yes to" - true as
written. The cost is one vault read per call: CPU spent to buy an accurate log.

### The listing path is not handed the policy at all

Structural rather than behavioural: `ListAsync` does not receive it. A rule can neither make an entry
listable that the exposure excludes nor hide one.

---

## D-0030 - A rule keys on the label the operator wrote, not the name the agent asserts

**Date:** 2026-07-26 · **Stage:** 2.3 · **Status:** accepted

The build prompt describes rules of the form "allow client `claude-code` to read ...". Taken literally that
keys authorization on `CredentialRequest.ClientName`, which THREATS.md T-3 says outright is
unauthenticated: any process that can spawn the binary can call itself `claude-code`. T-3 also left
2.3 a written instruction - key on something supplied out of band, or say plainly that client-scoped
policy narrows convenience rather than authority.

**Both.** A rule matches `--client-label`, which the human writes into the MCP client's own
configuration file. That is a real improvement over the asserted name: whoever *connects* cannot
choose it. It is also a small one: whoever **spawns** the bridge chooses its argv, which assumption 2
has always said. So the honest sentence, and the one docs/policy.md gives users in these words, is
that **client-scoped policy narrows convenience, not authority**. THREATS.md T-14 carries the
residual, and it is the one paragraph in that document where 2.3 is weaker than 2.2.

**A bridge with no label matches no rule, including one written `client = "*"`.** The star means "any
client the operator gave a name to", not "any client at all" - a standing grant to something nobody
named is not something to hand out because a key was left blank. `keypaste policy ls` therefore
renders it as "Any labelled client", never "any client".

### The label travels raw, and the wire version does not move

`CredentialRequest` gained an optional `ClientLabel`, carried as `client_label`. It is
`options.ClientLabel`, **not** the sanitized `client.Label` the audit line uses: the operator writes
one string in the client's config and another in `policy.toml`, and those two have to compare as
written. `EntryNameSanitizer` is lossy, and two distinct labels collapsing into one identical display
string would be a widening. The audit line keeps the sanitized copy, because that one is for reading.

`ApproverProtocol.Version` **stays at 1**. It guards frame interpretation, so bumping it would make
every mixed-version pair fail at the framing layer - no reply, no audit line beyond `no-approver` -
over one optional field. Both mismatched directions degrade the same way instead: the label is
absent, no rule matches, every request is shown to a person. That is the same state a malformed
policy file produces, and it is the state this whole stage falls back to.

## D-0031 - The audit chain commits to bytes, and forgives the damage a machine does to itself

**Date:** 2026-07-27 · **Stage:** 2.4 · **Status:** accepted

D-0020 froze the audit schema in a fixed key order and put `v` on line one of every record, both for
this. Schema 2 adds two fields at the end: `prev`, the previous record's `hash`, and `hash`, over
this record's own bytes. THREATS.md T-5 is what this closes.

**The hash covers raw bytes, not re-serialized fields.** It is the line exactly as it stood
immediately before the writer appended `hash` - the whole line minus its final
`,"hash":"<64 hex>"}`, no newline, no carriage return. The alternative, reconstructing the bytes from
parsed fields to check them, would hand a future change in `Utf8JsonWriter`'s escaping or number
formatting the power to turn *intact* into *tampered*. That is the worst failure this feature has
available to it, and no amount of care in the verifier removes it - only not depending on the
serializer does.

Two consequences are load-bearing and easy to lose. **`prev` comes before `hash`**, so the committed
bytes include the link; the other order lets a record's link be re-pointed without disturbing its
hash, which is a chain that only looks like one. **`hash` is last and fixed-width**, so verification
is a slice rather than a second parse. Classification is by leading bytes - every record begins
`{"v":1,` or `{"v":2,` - so no JSON parser sits anywhere on the path that produces a verdict. Parsing
is confined to `AuditReader`, where a failure costs a row in a table.

### The writer reads the file, so it opens the file twice

`prev` cannot come from memory: two servers share one log (D-0020), so a record must link to whatever
is actually at the end of the file. `FileMode.Append` forbids `FileAccess.Read`, and giving it up was
rejected. .NET itself throws on a seek before the append start, which makes "no code path in keypaste
rewrites the log" a runtime invariant rather than a claim about the code - and it is the literal
sentence T-5's mitigation rests on. So the log keeps a second, read-only handle on the same file,
opened at startup and pointing at the same inode. The tail is read inside the sidecar lock, which now
serialises reading the predecessor as well as writing the successor.

The window is `4 × MaximumRecordBytes`. Twice is the minimum that is correct at all - an unterminated
fragment of up to one record may sit after a last complete line of up to one record - and the rest is
margin, because a window that fails to contain a complete line is indistinguishable from a corrupt
log and would deny every credential request.

`MaximumRecordBytes` **stays 4096**. The atomicity argument is about total bytes written, so raising
the cap to absorb the 148 bytes the chain costs would be a number chosen to hide a change rather than
to state one. `exposure` remains the one field with no cap of its own, and it is operator-controlled;
that cliff is 148 bytes closer than it was, and is now covered by a test.

### `seq` becomes the file's, and stays advisory

It counted what one `AuditLog` instance had written, so two servers produced `1, 1, 2, 2` and a
reopen restarted at 1 - a number that looks like a record index and is not one, in the file whose
whole job is to be read back afterwards. It is now the record's position in the chain, derived from
the same tail read that produces `prev`, and it restarts at 1 where the chain starts.

**It is advisory where `hash` is authoritative.** Deriving it means reading a number an attacker may
have authored, and a predecessor with an absurd `seq` must not be able to deny every subsequent
credential request. So a `seq` that cannot be read is recorded as unknown, and a discontinuity is a
warning from `verify`, never a break.

### The writer records around what it did not write, and refuses over one thing

It links to the last *chained* record, stepping back over anything that is not one - the same linking
rule the verifier applies forwards, because a writer and a verifier that disagreed would make a
healthy log read as a broken one. The first version looked at exactly one line and refused if that
line was not a record, on the theory that stepping back could be walked to a line of an attacker's
choosing. That theory is wrong: the last chained record is the last chained record, and reaching an
older one means deleting the newer ones, which is the truncation residual rather than a new way in.
And the cost of the refusal was real - one appended byte, a blank line out of an editor or an `echo`,
became a permanent denial of every credential request, which under assumption 1 is a cheaper lever
for an attacker than the one it was meant to close.

**A record from a newer schema is the exception and still refuses.** Appending beneath it would fork
the chain, and unlike every other unreadable line, "upgrade keypaste" is something the operator can
act on. A version number too large to hold is *not* treated as newer (it is a torn line), because
telling somebody to upgrade in answer to garbage is worse than saying nothing.

### Genesis is zeros, and only one path reaches it

`prev` is the previous chained record's hash, or 64 zeros when the chain starts here - written only
when the whole file was examined and holds no chained record at all. **Never on a read that failed,
and never on a window that did not reach the start of the file**, because silently starting a fresh
chain when the end of the file cannot be understood is precisely what somebody who has just truncated
it wants to happen. It is also why the writer steps back past a planted v1 record rather than
treating it as the end of the chain: a genesis link after a chained record is the signature of a
truncation, and manufacturing one would have keypaste report an attack on itself, permanently, in
answer to one line anybody could append.

Anchoring the first chained record to a hash of the last v1 record was rejected: it gives one field
two hashing rules for a transition that happens once per installation, protects exactly one legacy
record, is permanent debt in every future verifier, and cries wolf on a v1 line whose whitespace was
touched - the exact failure `v` was put on line one to prevent.

### Not crying wolf is half the mitigation

A verifier that reddens after a power cut is ignored within a week, and then the one alarm that
mattered is the one nobody reads. Four things are reported *and* called intact: records that predate
the chain, a file that ends mid-line, a file whose line endings or opening bytes were rewritten by
some tool that copied it, and a record that stops partway.

The last of those is the subtle one. A write cut short is always a *prefix* of a record, so it can be
told from a foreign line by its first bytes - and the linking rule makes forgiving it free, because
`prev` links to the nearest preceding *chained* record. Anything not in the chain is stepped over,
and a record somebody mangled into that shape still breaks the link of the record after it. So the
forgiveness costs no strength, which is the only reason it is affordable.

**The forgiveness stops at the last line, and that is deliberate.** An unterminated final segment is
classified like any other line and verified if it is a whole record; only a segment that is *not* a
record is the interrupted write this forgives. That is what a crash actually leaves, because a record
and its newline are one write. Skipping the last line because it lacked a terminator would have made
deleting one byte the way to edit the newest record freely - and the newest record is the one an
attacker has just caused.

### An unverifiable record is a hole unless it is marked

Inserting a record the chain cannot check - one claiming `v:1`, or a version from the future - breaks
no link, because nothing before or after it changes. It parses, and it renders in the table exactly
like a verified record. Two things close that:

1. **A v1 record sitting after a v2 one is a break.** keypaste never writes one there, so it is an
   insertion rather than a log that grew across an upgrade. The same shape *before* the chain starts
   is what every upgraded log looks like, and is not condemned.
2. **Every record the chain cannot vouch for is marked in the rendering**, and the report carries the
   line numbers so a renderer can do it. Counting them in a footnote is not enough: the question a
   reader has is "can I believe *this row*", and the chain's answer is per record.

### What is not claimed

The chain holds no secret, so anyone who can write the file can recompute it; and records deleted
from the end leave a chain that is internally perfect. Both are residuals in T-5 rather than gaps to
discover, and `keypaste log verify` prints them **on every pass**, not only on a failure. For the
second there is `--expect <hash>`: keypaste prints the latest hash and keeps no copy of it, because
an anchor stored beside the thing it anchors is worth nothing.

**`--expect` is answered by the chain, not by searching the file's text.** A hash that merely appears
somewhere in the log proves nothing: the `entry` argument is text the agent writes, so a substring
search would accept a hash planted by the very request that destroyed the record it names - no file
access required, only knowledge of the anchor. It matches a record whose own bytes still hash to it,
and nothing else.

## D-0032 - `keypaste log` sanitizes on the way out, and fails on a broken chain

**Date:** 2026-07-27 · **Stage:** 2.4 · **Status:** accepted

The table is built in `Keypaste.Core.Audit.AuditText`, not in the CLI, for the reason `PolicyText`
gives: the GUI's Agent Activity screen (Stage 4) has to say the same thing about the same file, and
law 4.3 does not allow that sentence to be written twice. ASCII only, and widths from the data with
no truncation - an audit table whose entry column has been cut short cannot answer the question it
exists to answer, and the writer already caps every field it records.

**Untrusted text is sanitized in `AuditReader`, not in each renderer.** `entry` and `reason_excerpt`
are written by the agent and `client.name` is asserted by whoever connected, so a record is the last
thing between text an attacker chose and a terminal (T-1, T-2). Doing it in the reader means a second
front end cannot forget. It is done again on the way out even though the writer already scrubbed on
the way in, because a *tampered* line need not honour anything the writer did - which is the whole
premise of the command.

**A filtered view always says it is filtered**, with the count it shows out of the count in the file,
and an empty result says "no records matched" rather than printing nothing. Silence after a filter
reads as "nothing happened", which is the one conclusion an audit tool must never invite by accident.
For the same reason `--client` matches a case-insensitive substring of the label or the name:
matching too much costs noise, and matching too little costs the answer - somebody types `claude`,
sees an empty table, and concludes nothing happened while `claude-code` read credentials all morning.

**Both commands check the chain, and a break exits 5.** `ExitTamperDetected` is its own code because
none of usage, not-found or internal-error fits, and because "did anything touch my audit trail"
has to be answerable from a shell. `keypaste log` still prints the table when the chain is broken,
with an alarm in front of it: refusing to show an edited log would hand whoever edited it a way to
make the record unreadable, which is worse than showing it with a warning on it. **A chain that could
not be checked at all is not a pass** - `keypaste log` reports it and exits non-zero rather than
handing over a table nothing stands behind, on the same reasoning `verify` refuses to call a missing
file intact.

**A broken report still carries the observations a passing one would.** They were suppressed at
first, which was exactly backwards: the counts of unverifiable records, the marked rows, the
unfinished last line are what tell the person already looking for a problem which rows they can
believe.

**An absent log is success for `keypaste log` and an error for `keypaste log verify`.** Listing
nothing is a true answer about a machine no agent has used. Verifying nothing is not an answer at
all, and a script that read exit 0 from a missing file as "the log is intact" would be taking a
reassurance out of an absence.

## D-0033 - The demo is a recorded real session, and the recording is committed as text

**Date:** 2026-07-27 · **Stage:** 2.5 · **Status:** accepted

docs/PRODUCT.md law 5.1 says the demo is the marketing. This records what the demo *is*, because the tool
choice is a footnote and the rest is not.

**It is a real session, and a person presses the `y`.** `vhs`, `ttyd` and `asciinema-automation`
would each produce a shorter, cleaner artefact that CI could regenerate, and every one of them would
type the approval keystroke. keypaste's entire claim is that a person decides and nothing is
released without them. A demo whose `y` came from a script is a demo of a mock, and - this is the
part that makes it a decision rather than a preference - **the difference is invisible in the
output**. Nobody watching could tell. So it has to be written down instead.
`scripts/demo/record-demo.sh` drives everything up to the moment a decision is needed and then
stops and hands over the keyboard; its `--auto-approve` exists for rehearsals and is off by default.

**The `.cast` is committed beside the `.gif` because a recording of a security tool should be
reviewable.** A GIF is a megabyte of pixels nobody can check. An asciicast is JSON lines: anyone can
grep it and confirm the master password never appears, that the released value is a sentinel, that
the dialog on screen is the one `TerminalApprovalChannel` actually emits, and that the audit table
was not retouched. It is also what lets the asset be re-rendered at another size or theme without
re-shooting. Same instinct as `keypaste log verify`: the artefact that makes a claim should carry
the means to check it. `--speed` is a stated transformation and belongs in the caption; editing
timestamps inside the cast is a fabrication and is forbidden in the renderer's own header.

**The vault holds a sentinel, and `ToolResults.Release` is what forces it.** The released value is
returned twice - once in the text content and once in `structuredContent`, so that a client reading
either half works. Any *truthful* recording of a successful request therefore shows the credential
on screen, and this one is committed to git forever. `make-demo-fixture.sh` refuses to build a vault
whose value does not match `^sk_test_(EXAMPLE|FAKE)_`, and `record-demo.sh` refuses to hand over a
cast containing the master password or anything shaped like a real key. It also refuses one where
the sentinel is **absent**, because a take that released nothing proves nothing - the negative
controls need a positive one beside them.

**The whole recording lives inside one operating system, and that is not convenience.** The approver
channel is a .NET named pipe (D-0023): `\\.\pipe\<name>` on Windows, a socket under the temporary
directory on Unix. A Windows Claude Code spawns a Windows `keypaste-mcp`, which cannot reach a Linux
`keypaste agent` - the mixed setup does not look subtly wrong, it denies every request. So the
recording runs entirely in WSL, against Linux binaries published from a clone at `HEAD` (whose SHA
is written to `BUILT_FROM`, so the GIF traces to a commit), with a WSL-native Claude Code that has
its own credentials. The cost is a second Claude Code install and a second login, and it is not
optional.

### The geometry is a measurement

112 columns by 40 rows, panes **stacked** rather than side by side. The widest line that must not
wrap is the approver's startup banner at 109 characters, then `keypaste log verify`'s hash line at
84, then the dialog's `That sentence was written by the agent...` disclaimer at 79.

That disclaimer is the reason for the stack. Two 60-column panes fit the 60-character rule
beautifully and wrap the one sentence in the dialog that tells a viewer the agent's reason is a
claim rather than a fact - which is the sentence the whole design exists to put in front of them.
Shortening the banner with `--approver` was rejected for a related reason: the demo must show what a
reader gets by following `docs/mcp-setup.md`, and that guide does not mention the flag.

### Not a CI gate, and it cannot become one

A gate must be able to be green. A step needing a live model, a paid API and a human keystroke
cannot be, and one that flakes teaches people to ignore the ones that do not. What is gated is
`scripts/verify-demo.sh` - see D-0034.

## D-0034 - The demo page is held to the binaries, and Claude is deliberately not in CI

**Date:** 2026-07-27 · **Stage:** 2.5 · **Status:** accepted

`scripts/verify-demo.sh` checks a document against behaviour, which nothing else in the suite does.
Two things make it necessary. What rots is a Markdown transcript, and no unit test reads Markdown.
And the artefact the page reproduces - the approval dialog - exists only on the stderr of a second
process that a human is supposed to be looking at, so no in-process test can see it at all. The
first thing a stranger does with a demo page is type it in; a stale one costs more than a missing
one.

**The check runs in both directions.** Every literal output line the page prints must be produced by
the run, and the dialog block the run produces must appear in the page character for character. It
also asserts `expires_in_seconds == 300` against a request for 900, because the page prints
`for      300 seconds` and that number is the `--max-ttl` clamp rather than a constant.

**Claude is not in it, as a decision rather than an omission.** A model's choice is not
deterministic, and a check this project asks strangers to trust should not depend on a paid,
networked, non-reproducible service. The stand-in is the same scripted JSON-RPC client
`verify-approval-e2e.sh` uses: a transport, not an agent - it decides nothing and chooses no wording,
so everything keypaste renders is fully determined. The boundary is stated in the script's header,
in the CI step's comment, and in the page's own "Verifying it yourself", because a reader who
mistakes it for coverage of the agent has been misled by our silence.

### Two things the harness cannot see, and does not pretend to

`ConsoleSecretPrompt.ReadLine` writes its prompt only when stdin is a terminal, and CI has none - so
`Approve? [y/N] ` and `Master password: ` never reach stderr under redirection. They are still the
lines a person's screen ends on. The gate therefore asserts the *page* carries them rather than
claiming to have observed them, and says so where it does it.

The rule drawn around the dialog is checked for shape, not for bytes. .NET encodes stderr for the
console's code page, so on Windows the `U+2500` arrives as a single `0xC4` in CP850 rather than the
three UTF-8 bytes this repository stores; it renders correctly on the console it was written for,
and only a byte comparison cannot survive the trip. That is also why the diffed block stops short of
the rule and is pure ASCII throughout - which was luck the first time and is now a constraint. A
related trap, recorded so the next person does not lose an hour to it: those code page bytes make
`grep` treat the stream as binary and **silently drop context lines**, so `-a` is load-bearing in
that script rather than decorative.

## D-0035 - The bridge waits half a second for an approver, not two

**Date:** 2026-07-27 · **Stage:** 2.5 · **Status:** accepted

`ApproverConnection.ConnectTimeout` was two seconds. With no approver running that is pure latency
in front of a refusal, and it is paid on *every* call until somebody starts one - which is the
ordinary state of a bridge an MCP client spawned at its own convenience, hours before its user
opens a terminal.

Measured on one machine, ten runs each, medians: a bare `keypaste --help` 71 ms; a full
`request_credential` round trip against a **running** approver - process start, connect, prompt,
release - **248 ms**; the same call with **nothing listening**, **2306 ms**. Almost the entire budget
is spent only when nobody is there, which is the structural point rather than a statistical one:
`ApproverListener` binds in its constructor and re-creates the pending instance before serving the
accepted one, so with an approver up the operating system completes the connect with no application
involvement.

Half a second therefore takes 1.8 s off every refusal without touching the path that works. Measured
after the change: refusals 756 ms, the working round trip **251 ms** - unmoved, with every request
still granted. The fail direction is safe: a timeout too short turns a grant into a `no-approver`
refusal, which is the direction docs/PRODUCT.md law 3.7 asks for, and the refusal names the command that
fixes it. It also slightly narrows THREATS.md T-10, where another local user pre-creating the socket
path costs a caller the full timeout per call.

**Recorded separately from D-0033 on purpose.** It is a behaviour change in a shipped binary, found
while polishing a demo but not part of one, and burying it inside the demo's entry would hide it
from anyone reading the log for changes to the bridge.

Two other candidates were measured and deliberately **not** acted on. The Argon2 pause after the
master password is **255 ms** - not a pause, a keypress - and the alternative was adding a line to
the one screen in the product that should stay quiet. And `PublishReadyToRun` would recover perhaps
half of the 380 ms gap between a cold start (458 ms) and a warm one (71 ms), but it is a
publish-time property, CI runs `dotnet build`, `PublishAot` is already booked for Stage 3, and
flipping it moves the `artifacts/bin/**/release/` layout that six `scripts/verify-*.sh` resolve
binaries from. Stage 3's release pipeline owns it.

## D-0036 - The launch page claims only what a gate or a citation can hold

**Date:** 2026-07-27 · **Stage:** 3.1 · **Status:** accepted

`README.md` and `site/public/index.html` were rewritten as a front page rather than a manual, and
the interesting part is not the layout. Three of the things the 3.1 build prompt asks for could not be
written honestly as asked, and each was resolved by narrowing the claim rather than by softening
the wording.

**Install one-liners were not written, because there is nothing to install.** There is no release
workflow, no published artifact, no tap and no bucket; `PublishAot` is still `false` and O-0006 -
whether vendored KeePassLib survives AOT at all - is unresolved and explicitly gated on Stage 3.
A `curl | sh` line on a launch page is the single instruction a stranger runs first, and one that
404s is worse than an honest `dotnet build`. Both pages therefore say there are no prebuilt
binaries yet and that single-file binaries are next. The release pipeline became its own docs/STEPS.md
item and its own prompt instead of being smuggled into this one.

**The comparison table names four products, and every cell was read out of that vendor's own
documentation in July 2026 rather than recalled.** That research changed what the page says. The
draft assumed the wedge was unoccupied; it is not. Keeper's MCP server prompts a human before
returning unmasked secret data. Bitwarden published an Agent Access SDK in March 2026 with the
same request-and-approve shape. 1Password's Environments MCP server asks for approval too and then
deliberately never hands the credential over - a different answer to the same problem, and on one
axis a safer one. `kprun` already injects KeePass entries into a child process and writes a local
JSONL log. So both pages state that outright, immediately under the table, and claim only the
*combination*: a KDBX file you own, no account and no server, a person answering each request, and
a log that stays local. Every competitor breaks at least one of those, and that sentence survives
contact with someone who knows the field. "Nobody does this" would not have.

Three claims were specifically cut as unverifiable from primary sources: that 1Password is closed
source (they publish no page saying so - the page says only that they do not publish the source),
that `op` can read a cached vault fully offline, and which Infisical features exactly are behind
the enterprise licence. Where a vendor does not document something the table says "not documented"
rather than "no".

**The transcripts on both pages are now held to the binaries by `scripts/verify-demo.sh`.** The
approval dialog is the first thing on both pages - it stands in for the demo GIF until that is
recorded - and until now the only gated copy was the one in `docs/demo.md`. README.md and
docs/approvals.md and docs/mcp-setup.md all carried near-copies that nothing checked, which is how
the near-copies came to differ from each other in the first place. `DOC` became
`TRANSCRIPT_PAGES`, the dialog diff and the rule, `Approve? [y/N]` and log-header checks loop over
it, and `README.md` and `site/public/index.html` are in the list. The HTML page is in it for the
same reason as the README: `grep` and `diff` do not care about markup, and a marketing page is
where a stale transcript survives longest. Dropping the block entirely does not evade the gate -
the extraction is asserted non-empty per page.

Left undone deliberately: `docs/approvals.md` and `docs/mcp-setup.md` also carry the dialog and are
still ungated. They should join the list, but they are documentation rather than the front page,
and adding them means reconciling three more copies in a change that is already large.

## D-0037 - keypaste.com collects email addresses, and cannot read them back

**Date:** 2026-07-27 · **Stage:** 3.1 · **Status:** accepted

**keypaste.com stopped being a page and became a page with a database behind it, which is a
security decision and not a marketing one.** It now accepts an email address at `POST /subscribe`
and stores it in PlanetScale Postgres. Everything below exists so that the footer's promise stays
literally true and so that the smallest possible thing is at risk.

**The form works with no JavaScript**, because a page that asks a privacy-minded audience to run a
script in order to be told about a privacy tool has already lost the argument. It is a plain
`<form method="post">`; the Worker answers `303` to a static `/thanks/` page. That is also why the
success page is a real asset rather than markup inside `worker.js`: the site's HTML stays in one
language and cannot rot in two places. Only the two rare failure paths - a malformed submission and
a database that does not answer - are rendered by the Worker, from string literals, with nothing a
visitor typed interpolated into them.

**Hyperdrive rather than a direct connection, and the reason is TLS rather than speed.** The
connection string asserts `sslmode=verify-full`. A Worker connecting directly has no system CA
store to verify against, so the practical setting is `require`: encrypted, but the server
unauthenticated. Silently downgrading a stated `verify-full` would be a poor look anywhere and an
absurd one here. Hyperdrive takes an uploaded CA and does `verify-full` properly. It also pools
origin connections, which is what stops a Show HN spike from exhausting PlanetScale's connection
limit and dropping signups - and, best of all, means the password lives in an account-level
Cloudflare config rather than in the Worker's environment. There is no `wrangler secret` in this
design and there should not be one. `site/README.md` step 5 verifies the config's sslmode is the
one that was asked for; if it is not, the reason for the whole arrangement is gone.

**The role the Worker connects as can `INSERT` into one table and cannot `SELECT` from it.** This
is the highest-value line in the change. `INSERT ... ON CONFLICT DO NOTHING` without `RETURNING`
needs only the insert privilege, so a fully compromised Worker - or a compromised dependency inside
it - cannot dump the subscriber list. Adding `RETURNING`, or switching to `DO UPDATE`, would
require `SELECT` and quietly undo it, so `schema.sql` says so next to the grants. If PlanetScale
turns out to forbid `CREATE ROLE` on a managed instance, the fallback is a credential that *can*
read the list, which is materially worse and must be recorded here rather than quietly accepted.

`postgres` (postgres.js) over `pg`: one package with no transitive dependencies against roughly a
dozen, on the secret path of a public endpoint, and its tagged-template API is parameterised by
default where `pg` makes parameterisation opt-in. Both are pinned exactly, with a committed
lockfile, and everything lives under `site/` so the .NET solution is untouched. `nodejs_compat` is
required; `compatibility_date` was deliberately **not** bumped, since `2026-07-25` already exceeds
the point where that flag means what it now means.

Nothing is stored but the address, the timestamp and a source tag. No IP, no user agent, no
country - `request.cf.country` is one property access away, and the page promises privacy, so the
column does not exist to tempt anyone. Errors are logged as name, code and message; never the error
object, whose driver options would print connection parameters, and never the address.

### What was deliberately not shipped

**Turnstile and managed challenges**, both of which inject a script and would falsify the footer
for exactly the people it was written for. **A form timing check**, which needs a server-rendered
timestamp and is therefore incompatible with a cached static asset - this is the one abuse control
that was wanted and could not be had. **Rate limiting in code**: it belongs in a Cloudflare rule,
and whether the account's plan offers a useful one is unverified, so the honeypot and the body,
content-type and origin guards are what is genuinely shipped rather than what was intended.

**And there is no CI job for any of this.** Ninety lines of JavaScript deployed by hand did not
justify one; the consequence is that `site/README.md` carries a written manual checklist, including
loading the page with JavaScript disabled, because a promise nobody tests is a promise that breaks.

### The commitment this record exists to hold

Anyone can type anyone else's address into a public form. That is harmless while nothing is ever
sent to the list, and becomes mail-bombing on the day something is. **The list is double opt-in
before a single message goes to it** - a confirmation first, and no confirmation means no mail.
`/thanks/` already tells signers this will happen, which makes it a published promise rather than
an intention. It is written down here so it is not rediscovered on launch day.

`SECURITY.md` previously placed "the keypaste.com marketing site" out of scope. A site that stores
other people's email addresses is precisely where a report is wanted, so that exclusion was
narrowed to the static content.

### What the first configuration actually looked like

Recorded because the gap between this record and the account it describes is the whole risk, and
because the second half of it is still open.

The Hyperdrive config was created through Cloudflare's PlanetScale integration rather than by the
steps in `site/README.md`, and it was wrong in both of the ways this record argues about.

**The TLS half is now right, and the API would not let it be otherwise.** Cloudflare refuses
`verify-full` unless a CA certificate is bound to the config - `ca_certificate_id: cannot be blank`
- which is a better guarantee than the one assumed above, because the mode cannot be set and then
silently ignored. PlanetScale serves a Let's Encrypt chain terminating at ISRG Root X2 cross-signed
by X1; X1 was uploaded and the chain verified against it alone, with hostname checking, before it
was bound. A query through the binding then succeeded, so `verify-full` is real rather than merely
accepted.

**The role half is still wrong at the time of writing, and is worse than this record assumed.** The
config connects as the integration's own `pscale_api_…` credential, which a query through the
binding reports as `rolcreaterole`, `rolcreatedb`, `rolbypassrls`, and a member of
`pg_read_all_data`, `pg_write_all_data` and `postgres`. The claim above - that nothing reachable
from the Worker can read the list back - is therefore **false of the deployment**, though true of
the code and of `schema.sql`. It is stated here rather than left as a mismatch someone finds later.

The fallback this record reserved for "if PlanetScale forbids `CREATE ROLE`" is not needed, but the
remedy it assumed was wrong in a way worth writing down, because the obvious reading of the
observed username is the wrong one twice over. `pscale_api_yq4xhf9tbm3v` is **not** an API or admin
credential; PlanetScale discards the name you type when creating a role and issues a generated one
in that shape, so the problem is the role's inherited roles and not what it is called. And the
`.jb6eu3wgh2u3` suffix is the **branch id, not part of the credential** - the proxy routes on the
username, so a client connects as `<role>.<branch-id>` while `current_user` inside the database is
the bare `<role>`. That also means the credential pasted into a chat transcript, which was
`postgres.<branch-id>`, is the cluster's *default* role and is **not** the one this Hyperdrive
config uses. Rotating it does not break the bridge; it is still disclosed and still wants rotating,
via `pscale role reset-default`, which the PlanetScale docs warn costs downtime - itself the reason
their docs say never to point an application at the default role.

`schema.sql` was rewritten accordingly. Raw `CREATE ROLE` works and was the original plan, but
PlanetScale's documented path is a **managed** role - dashboard, `pscale role create`, or the Roles
API - which appears in the dashboard, rotates with `pscale role reset`, and can carry a TTL, where
a role conjured in SQL is invisible to all of that and its lifecycle silently becomes yours. The
catch is that the managed builder only offers cluster-wide predefined roles and cannot say "INSERT
on one table". So the shape is both: a managed role created with **no** inherited roles, then the
table-level grants applied in SQL. Cloudflare's own Hyperdrive guide suggests `pg_read_all_data`
plus `pg_write_all_data`, which is exactly the posture this record exists to avoid, and is declined
in a comment next to the grants so nobody re-adds it from that page.

**Corrected on 2026-07-28: it is deployed, and this entry said otherwise.** The Worker is live on
keypaste.com - `GET /subscribe` answers 303 to `/`, which is its own non-POST branch, and an unknown
path gets the static 404, so the form reaches real code. `site/README.md` said "do not deploy" and a
deploy happened anyway; the record and the reality had drifted apart in the direction that matters.

What is still true is that nothing is at risk, and the reason is worth stating because it is luck
rather than design: `public.signup` does not exist, so the insert throws, so the handler returns its
503 page saying in as many words that the address was not stored. The over-privileged role can read
every table in the cluster and there is simply no subscriber table for it to read. Nothing has been
silently dropped and nothing has been stored - every signup since the deploy has failed honestly.

**That fixes the order of the fix.** Because the exposure begins the moment the table exists, the
role must be swapped before, or in the same sitting as, applying `schema.sql`. Creating the table
first would put the first real signup somewhere this entry's guarantee does not hold.

**Closed on 2026-07-28. The Worker connects as a role that can insert one row and read nothing.**
`keypaste_signup_writer` has `INSERT` on `public.signup` and no other privilege - not superuser, not
`bypassrls`, `NOINHERIT`, zero role memberships - and as that role `select`, `count(*)`,
`returning`, `update`, `delete` and reading any other table are each refused with 42501. The table
exists, a live submission stores a row and redirects to `/thanks/`, a duplicate is a no-op, the
honeypot stores nothing, and a bad address, a foreign `Origin` and a non-form body each get 400.
CONNECT on the database is no longer held by PUBLIC.

**It is a plain SQL role, not a managed one, which contradicts the paragraph below.** The managed
path needs the `pscale` CLI and an interactive login; the raw role was the one that could be
created and verified in a single sitting. The cost is the one this entry already names: it is
invisible to `pscale role reset` and to TTLs, so rotation is `alter role ... password` plus a
`wrangler hyperdrive update`. Swapping it for a managed role later changes nothing else and is worth
doing when the list stops being empty.

**Two things bit while fixing it, both worth keeping.** `wrangler hyperdrive update --origin-user
--origin-password` silently emptied the `mtls` block, dropping the CA and `verify-full` - so a
credential swap quietly downgrades the TLS posture this entry exists to guarantee unless the sslmode
is re-passed and then re-read. And naming the conflict target in `ON CONFLICT (email) DO NOTHING`
requires SELECT on PostgreSQL 18.4, so the Worker's own statement was incompatible with the role it
was designed for: correctly configured, every signup still returned 503. The bare
`ON CONFLICT DO NOTHING` needs only INSERT and is what ships, and `schema.sql` says so.

**The posture it replaced was worse than "it can read the list", established by walking the role
graph on 2026-07-28.** `pscale_api_yq4xhf9tbm3v` inherited `postgres`, which inherits
`pscale_superuser`, and that brings `pg_create_subscription`, `pg_signal_backend`, `pg_maintain`
and `pg_checkpoint`. Logical replication plus write access everywhere is continuous exfiltration of
the whole cluster, from a credential reachable by a public HTTP endpoint. This does not change what to do - the fix was
already "swap to a role with INSERT on one table" - but it changes how long it is reasonable to
leave undone, and it is the argument against ever letting a Hyperdrive config keep whatever role an
integration wizard hands it.

## D-0038 - The launch essay was retitled because the title docs/STEPS.md gave it was false

**Date:** 2026-07-27 · **Stage:** 3.2 · **Status:** accepted

The 3.2 build prompt and docs/STEPS.md both asked for an essay called "Your password manager can't talk to AI".
It is `docs/keepass-and-agents.md`, and it is called "Your **KeePass vault** can't talk to AI - and
everyone is pasting secrets into chats instead" instead.

**The original title is refuted by this project's own research.** D-0036 recorded that reading four
vendors' documentation in July 2026 killed the draft's assumption that the wedge was unoccupied:
1Password's Environments MCP server authorises every interaction, Bitwarden's Agent Access SDK has
the same request-and-approve shape, and Keeper wants a confirmation before it unmasks a field. That
argument was already fought and lost once inside the README. A title is the part of an essay most
likely to be quoted and least likely to be read in context, so shipping the losing version of it on
the front of a launch post would have handed the first commenter the whole thread. Narrowing to
*your KeePass vault* costs nothing - KDBX genuinely has no agent integration, which is the entire
reason keypaste exists - and it aims the piece at r/KeePass and the KeePass forums, which docs/STEPS.md
already names as launch venues. The three vendors are then conceded by name in the second
paragraph, before a reader can catch us out.

**The essay is in `TRANSCRIPT_PAGES`, so it is held to the binaries like the other three pages.**
It reproduces the approval dialog and the `keypaste log` header, and D-0036's whole finding was
that near-copies nothing checks are how near-copies come to differ. Adding it to
`scripts/verify-demo.sh` cost one word. The gate was then watched failing - one character changed
inside the dialog, the run refused and named `docs/keepass-and-agents.md` - because a gate never
observed failing is not known to be a gate.

**Every figure in it was checked against the primary source, and four did not survive.** GitGuardian
says 28.65 million, not 28.6. The 2,349 credentials came out of 1,079 attacker-created
*repositories*, not 1,079 machines. The `--yolo` and `--trust-all-tools` flags are real but appear
in Wiz's write-up, not in the GitGuardian piece the sentence cited, and the draft had also dropped
the flag belonging to the first tool it named. And the KeePassXC browser-integration document does
not actually say what the draft leaned on it for - it documents a Confirm Access dialog with an
optional Remember, but not the transport and not the replacement of KeePassHTTP, which needed the
2.3.0 release announcement and the keepassxc-browser README instead. Cyberhaven's 2026 report,
LayerX's 77%, Netskope's regional splits and a widely repeated CVSS 9.4 were all cut for want of a
verifiable primary source. **Nothing quantifies how often developers hand a `.env` to an agent**, so
the essay makes that claim qualitatively, out of named incidents, and attaches no number to it.

**No Show HN text was written, though 3.2 asks the essay to be adaptable to one.** 3.3 owns the
launch posts, and a launch post should not exist before 3.4 gives a stranger something to install.
The essay's first three paragraphs are written to stand alone so that adaptation is a compression
rather than a rewrite.

At roughly 1,400 words the essay is over the ~1,200 the prompt asks for. The overage is the
honest-limits section and the citations; both were judged to be the wrong things to cut from a page
whose argument is that the project does not overclaim.

---

## D-0039 - The launch copy is written, and the launch is blocked by a list of things that are false

**Date:** 2026-07-27 · **Stage:** 3.3 · **Status:** accepted

`launch.md` holds a preconditions checklist, copy for all six venues docs/STEPS.md names, and a
fourteen-day follow-up plan. Nothing in it can be sent, and the file says so in its first line.

**The copy was written before the launch was possible, on purpose.** D-0038 deferred the Show HN
text to this stage on the grounds that a launch post should not exist before 3.4 gives a stranger
something to install. That is still true of *posting* and it is not true of *writing*: copy drafted
on the morning sells, and copy drafted cold argues and can be checked. Every factual sentence in
the six posts was read back against README.md, SECURITY.md, THREATS.md and the source before it was
kept, which is not a thing anyone does at hour zero of a launch. Four claims did not survive that
pass and are recorded below.

**Writing it surfaced a list of published statements that are false right now, and the checklist is
that list.** The repository is private, so every link in every post would 404 - and D-0006 records
it as public, reasoning from free Actions minutes that do not currently apply. The account renamed
from `ochoadan` to `notinferred` and four URLs in README.md and `site/public/index.html` did not
follow. keypaste.com is live and serving, while `site/README.md` says in bold *"The role: still
wrong. Do not deploy."* and this file said at D-0037 that nothing is deployed - so either the
Worker writes through a role that can read the list back, falsifying the promise printed in the
page's own footer, or `public.signup` does not exist and every address entered so far has been
dropped. None of these were fixed here; each is a checkbox, because a launch checklist whose items
have already been quietly resolved is a checklist nobody reads twice.

**No post claims novelty, and the file says why in the section every post draws from.** D-0036 lost
the "nobody does this" argument in private against Keeper, Bitwarden's Agent Access SDK, 1Password
Environments and `kprun`; losing it again in a comment thread would cost more. The Show HN text
concedes all four by name, and says of 1Password's never-return-the-secret design that it is
stronger on that axis - which it is. The ratified claim is the combination, and the fourteen-day
plan pairs each predictable objection with the file that already concedes it, so the answer at hour
six is the same as the answer at hour zero.

**`launch.md` is in `TRANSCRIPT_PAGES`, and that constrains its layout rather than just its
content.** The dialog extraction in `scripts/verify-demo.sh` is a `sed` range, so a second copy in
the same file is also extracted and the diff fails. A file of six posts naturally wants six copies
of the dialog, several of them shortened to fit a venue. It gets one, quoted once in a shared
section that the posts reference by marker, and the script's comment now records the constraint so
the next person to add a page learns it from the script rather than from a red build. The gate was
watched failing - one character changed inside the block, the run refused and named `launch.md` -
and then reverted.

**Four claims in the draft were wrong, and the checking pass is the only reason they are not in a
post.** The draft said that if the audit log cannot be written the call is refused;
`src/Keypaste.Mcp/Program.cs` does the opposite deliberately, refusing to *start* rather than
surfacing later as a mysterious per-call refusal. It said a denied request never reaches the vault,
when resolution happens first and it is the *field read* that is deferred until after the yes. It
told r/KeePass that a red build means nothing merges, which nothing enforces: branch protection is
unavailable on a private repository on this plan and merges happen locally, so that is a habit
rather than a rule - the checklist now carries switching it on, and the post claims only that the
compatibility job is permanent and runs on every push. And the Show HN text opened by saying the
author had been building keypaste for a couple of months, which no commit supports; the history runs
from 2026-07-25. The first three are the failure mode D-0036 exists to catch, a claim that sounds
like the design and overstates it. The fourth is worse, because nothing in the repository would ever
have contradicted it.

**docs/STEPS.md's launch-posts box stays unchecked.** It says "Launch posts", and no post has been sent.
The file existing is 3.3; the box is 3.3 going out.

---

## D-0041 - A release is four native builds that are run before they are uploaded

**Date:** 2026-07-27 · **Stage:** 3.4 · **Status:** accepted

**The release workflow does not trust its own build.** After publishing, it deletes
`artifacts/bin` and points every gate at the native binaries through `KEYPASTE_BIN` and
`KEYPASTE_MCP_BIN`. The deletion is the load-bearing line: `artifacts/bin` is exactly where the
scripts look when those variables are unset, so without it a typo in either name would silently
test the ordinary JIT build and report a green release. Two assertions back it up — an AOT binary
has no managed `.dll` and no `runtimeconfig.json` beside it — and the version the binary prints
must equal the tag. The binary that ships is the binary that was proved.

**Distribution is Cloudflare R2 behind `dl.keypaste.com`, not GitHub Releases.** The repository is
private, and a private repository's release assets return 404 to every reader without a token, so
the documented install command would have been false on the day it was written. That is precisely
what D-0036 refused to do and what D-0039 catalogued. R2 has no egress charge, an S3-compatible
API the workflow already knows how to speak, and Cloudflare is already in this project's stack for
keypaste.com. Serving from a domain the project controls was weighed against docs/PRODUCT.md §3.5 and
passes: §3.5 forbids telemetry on secret content and entry names, and an HTTP request log for a
download is neither. The honest cost is a second origin, recorded in THREATS.md rather than
implied.

**`curl | sh` was rejected.** docs/PRODUCT.md §3.8 makes auditable code the trust strategy, and a pipe to a
shell asks the reader to execute code they have not read, from an origin that is not the audited
repository, in a form where the server can serve different bytes to `curl` than to a browser. The
documented install is instead a download, a checksum verification that fails closed, and an
extract — five lines, or six on macOS. The third line is the one that makes the other four worth
typing. Build-from-source stays on both pages permanently, because THREATS.md T-9's argument is
that a reader can check the dependency closure themselves, and a prebuilt binary is not that.

**The checksum proves integrity, not authenticity.** It is served from the same origin as the
asset, so anyone who can replace the archive can replace the hash next to it. This is the same
distinction D-0008 already drew about KeePassXC's `.DIGEST`, and it is stated on the install page
rather than left for a reader to work out. Authenticity needs signing, which is O-0010. Both
spellings are published from one computation and asserted to agree: `SHA256SUMS` for anyone
verifying everything or scripting it, and a per-asset `.sha256` so the documented command is a bare
`sha256sum -c` with no flags and no way to pass by accident.

**`osx-x64` is not published.** macOS 26 is the last release that runs on Intel Macs, macOS 27 is
Apple Silicon only, Blacksmith has no Intel Mac at all, and GitHub retires its x86_64 image in
August 2027. Cross-compiling the slice was possible but would have shipped a binary no gate had
ever executed, which is exactly what O-0006 forbade. Rosetta runs x64 on Apple Silicon and never
the reverse, so this costs Intel Mac owners the binary and not the product: they build from source,
which is documented and works. Dropping it also left all four remaining RIDs on one runner fleet,
which is one fewer party able to substitute bytes strangers download. Tracked as O-0013.

**The version lives in `Directory.Build.props` and the tag only names it.** Nothing is passed with
`-p:Version=`, so a local publish at the tagged commit produces the same inputs the release did,
and the guard job fails a tag whose base disagrees with `VersionPrefix`. The same job refuses to
release a commit that is not an ancestor of `main`, or that has no successful `ci` run, or that has
no section in `CHANGELOG.md` — docs/PRODUCT.md §4.7 turned into a gate rather than an intention.

**`release.yml` pins its actions to commit SHAs and `ci.yml` does not, deliberately.** O-0004
deferred pinning on the reasoning that pins without Dependabot age badly, and that reasoning still
holds for a workflow that only runs tests. It does not hold here: a mutated action tag on this path
substitutes a binary a stranger downloads, and no gate in this repository would notice.
`.github/dependabot.yml` exists now so the pins can move. It covers the github-actions ecosystem
only — a bot that bumps a NuGet version without regenerating `packages.lock.json` produces a pull
request that cannot pass CI (D-0004).

**Each release publishes its own source.** AGPL-3.0 section 6 obliges the corresponding source to
every recipient of a binary, and the workflow satisfies option (d) by `git archive`-ing the tag
next to the assets. The repository's history, issues and branches stay private; the source of every
released version does not. That is a consequence of shipping binaries at all, not of choosing R2.

**Two preflights were added after the fact, and the reason is worth keeping.** The `publish` job is
the only job a `workflow_dispatch` never runs, so anything asserted there is asserted for the first
time by a real tag, after four platform builds have been paid for. The AWS CLI was the case in
point: nothing in the workflow installs it and it was only present because the runner image ships
it. That check now lives in `guard`, on the same runner label, where a dispatch reaches it -
observed answering `aws-cli/2.33.5` rather than assumed. The three R2 secrets are checked there too,
on a tag only, so a missing secret costs one minute instead of twenty.

**A released version is now immutable by refusal rather than by comment.** `aws s3 cp` overwrites
without asking, so re-running a tag would have replaced bytes that readers already hold a checksum
for - which turns a documented `sha256sum -c` into a failure for an honest download, and an honest
failure is indistinguishable from an attack. The publish job now lists the destination prefix first
and refuses if anything is there. **This control has not been observed firing**, because doing so
needs a second tag at the same version; by this file's own standard (a gate never observed failing
is not known to be a gate) it is weaker evidence than the checks around it, and it is recorded that
way rather than counted as proved.

**Left undone deliberately:** signing and notarization (O-0010), package managers (O-0011),
reproducible builds and provenance attestation (O-0012), `osx-x64`, `win-arm64` and
`linux-musl-x64` (O-0013). The Linux binaries are built on Ubuntu 22.04 rather than 24.04 so the
glibc floor is 2.35 rather than 2.39, which is the difference between running on Debian 12 and not;
that floor is asserted on a clean Debian 12 container on every release, with Alpine asserted to
fail, so it is a tested claim rather than a guess.

## D-0042 - CI runs on one provider now, because billing stopped the other one

**Date:** 2026-07-28 · **Stage:** 3.4 · **Status:** accepted

**This was forced, not preferred.** Every `ubuntu-latest` and `macos-latest` job began failing one
second after starting, with no runner assigned and no steps recorded - the annotation reads "The job
was not started because recent account payments have failed or your spending limit needs to be
increased." Nothing was wrong with the code. Every Blacksmith job in the same run passed, including
the AOT publish. The three-OS matrix in `ci.yml` therefore moved to
`blacksmith-4vcpu-ubuntu-2404`, `blacksmith-4vcpu-windows-2025` and `blacksmith-6vcpu-macos-15`.

**D-0006 predicted the mechanism and the prediction came true.** It recorded that publishing the
repository "makes GitHub Actions free, which is what unblocked CI: the three-OS matrix bills at
1× / 2× / **10×** per minute on a private repository." The repository is private today, so the
matrix has been billing at those multipliers, and the macOS row at 10x is what consumed the limit.

**A red `ci` is a hard stop on shipping, not a nuisance.** `release.yml`'s guard job refuses to
build a tag that has no successful `ci` run on its exact SHA. So this was not a background
annoyance to fix eventually; with GitHub-hosted runners unable to start, no release could be cut at
all. That is the guard behaving correctly, and it is also why this decision could not wait.

**All three operating systems survive, which is the part docs/PRODUCT.md law 4.6 cares about.** The
KeePassXC compatibility gate still runs on Linux, macOS and Windows, and the architectures are
unchanged - `ubuntu-latest` and the Blacksmith Ubuntu image are both x64, `macos-latest` and
`blacksmith-6vcpu-macos-15` are both arm64. Changing which image hosts an operating system is a
different act from dropping one, and `ci.yml`'s standing comment now says so, because the next
person to read that list should not have to guess which one happened. Every step in both jobs keys
off `runner.os` rather than the runner label, so the swap needed no other edit - which is the
reason that convention was worth having.

**What is genuinely lost is a second opinion.** Two providers meant a Blacksmith-specific image
quirk could show up as a disagreement between them; one provider means it shows up as nothing.
Against that, `release.yml` already built every shipped binary on these same four Blacksmith
runners, so `ci` and `release` now test on the machines that produce the artifact instead of
straddling two fleets. That is a real gain in what a green `ci` implies about a tag. The loss is
recorded rather than mitigated: if the second provider comes back it belongs as extra matrix rows
for cross-checking, not as a replacement.

**This removes cost as an argument for publishing the repository.** D-0006's case for going public
was partly that CI became free; with CI on a flat-rate provider that argument is gone, and
publication should now be decided on docs/PRODUCT.md §3.8 grounds alone - auditable code as the trust
strategy - which is where it always belonged. Tracked as O-0014, along with the fact that
`SECURITY.md` had been asserting the repository was public and no longer does.

---

## D-0043 - The install gate had never run, and neither reason was laziness

**Date:** 2026-07-28 · **Stage:** 3.4 · **Status:** accepted

`v0.1.0-rc.1` and then `v0.1.0` published to `dl.keypaste.com` once the three R2 secrets were set.
The credential was checked against the real bucket before a tag was spent - list, put and delete,
then the probe object removed - because the alternative was discovering a read-only token after
four native builds had already run. `install.yml` then ran for the first time in its life and
failed on two of three platforms.

**Reason one: `workflow_dispatch` resolves against the default branch, not against the ref.**
`install.yml` lived only on `stage-3.4-install-docs`, so `gh workflow run --ref
stage-3.4-install-docs` returned `HTTP 404: workflow install.yml not found on the default branch`.
A gate written on the branch it gates cannot be dispatched at all until it is on `main` - which
means the file has to land ahead of the documentation it exists to check. It was committed to
`main` by itself for exactly that reason. The consequence to remember: a workflow added on a
feature branch is unrunnable, so its first execution is necessarily after a merge unless it is
split out first.

**Reason two: the gate was looking in the wrong place.** `run_block()` executes the documented
block with `HOME` reassigned to a scratch directory, so `mkdir -p ~/.local/bin` resolves under
`$SCRATCH/home`. The check that followed asked for `"$HOME/.local/bin/keypaste"`, expanded in the
outer shell where `HOME` is still the runner's own. It installed to one directory and searched
another, and could never have passed on macOS or Linux. Windows passed throughout, because its
block deliberately stops after extracting and the binaries were found by the working-directory
candidate instead. Fixed by naming `$SCRATCH/home`.

**The negative control was already right; what was missing was ever having run it.** Nothing about
this defect was subtle, and no amount of reading would have been as cheap as one execution. A gate
that has never executed is an assertion about the world, not a check on it - and it is worth less
than no gate, because a green file in `.github/workflows` reads as coverage. Both defects were
found within ten seconds of the first real dispatch.

**What the gate still does not prove, stated so nobody reads more into it than it earns.** It
locates the installed binary at a known path. It does not resolve `keypaste` through a real user's
`PATH` in a fresh login shell, and that distinction matters: `~/.local/bin` is not on `PATH` by
default on macOS, and Ubuntu's `~/.profile` adds it only at login and only if the directory already
existed at that point - which `mkdir -p` in the install block guarantees it did not. So a stranger
can follow every documented line, see no errors, and still get `command not found` until they open
a new terminal or edit a profile. `README.md` says to check with `command -v keypaste` rather than
promising it is on `PATH`, which is honest but leaves the work with the reader. Whether the block
should append the `PATH` line itself is undecided and belongs with O-0011, since a Homebrew tap
would make the question disappear for most macOS users.

## D-0044 - The desktop shell is Avalonia, and neither of the two shells this repository had already named survived being checked

**Date:** 2026-07-28 · **Stage:** 4.1 · **Status:** accepted

This repository named two different desktop shells and never wrote a record for either. `docs/STEPS.md`,
in the checked LOCKED block, said *"Desktop shell via **Photino.NET** … with Electron as
fallback if Photino friction appears."* Its Stage 4 line and the 4.1 build prompt said **Tauri**. Nothing
in this file mentioned any of them. Stage 4.1 could not start without settling it, so it is settled
here, with the evidence, because a record that says only "we chose Avalonia" cannot stop somebody
proposing Photino again next year.

**Tauri was ruled out on what it would cost, before anything was measured.** Its backend is Rust;
`Keypaste.Core` is .NET 10, and this repository has no FFI, no C-ABI export and no Rust anywhere. A
Tauri shell means a fourth binary and a wire protocol carrying the master password and released
field values. That protocol *is* the secret path, so docs/PRODUCT.md law 4.5 makes its tests mandatory —
and the one that already exists, `src/Keypaste.Core/Ipc/ApproverProtocol.cs`, is 445 hand-written
lines with no `JsonSerializer` anywhere because reflection trips the trim analyzers this repository
builds with (D-0019). Paying that a second time to reach a library that is already in-process is
not a trade; `Keypaste.Cli` and `Keypaste.Mcp` both reach the core through a plain
`ProjectReference`, and law 4.2 says the GUI calls the same core library the CLI does.

**Photino won the architecture argument and then failed verification.** It has the property that
mattered — a plain `ProjectReference`, no bridge — so it was the recommendation until it was
checked. Checked on 2026-07-28:

- Latest release **4.0.16, 2025-01-23**. No code commits to `photino.NET` or `photino.Native`
  since; the only 2026 commit on either is a README edit. Open issue **#279, "Is this project
  dead?"**, filed 2026-07-19, unanswered. Effectively three contributors.
- The package declares **`net8.0;net9.0` only**. There is no `net10.0` asset; a 2025 commit
  narrowed the target frameworks deliberately.
- It declares neither `IsAotCompatible` nor `IsTrimmable`, so no trim or AOT analyzer has ever run
  over it, while `Directory.Build.props` sets `IsAotCompatible` for this whole repository. Its
  constructor uses `[DllImport]` with `[MarshalAs(UnmanagedType.FunctionPtr)]` delegate fields and
  a `ByValArray` of `LPStr` — the shape those analyzers exist to flag. The organisation's only AOT
  sample is Blazor-based and its own ReadMe says trimming makes it fail at runtime; the documented
  workaround is blocked by open issue #143, because modern SDKs refuse `PublishTrimmed=False` under
  AOT.
- Its Linux natives require **`GLIBC_2.38`**, read out of the shipped `.so`. This repository's
  published floor is **glibc 2.35** — `ci.yml` builds the AOT check on `ubuntu-2204` for exactly
  that reason and CHANGELOG 0.1.0-rc.1 promises Debian 12. **The GUI would not have run on the
  oldest Linux the CLI supports.**
- `RegisterCustomSchemeHandler` never fires for the initial page load (#209, closed *wontfix*),
  which is how a static frontend would have been served.

docs/PRODUCT.md law 3.9 requires written justification for every new dependency on the secret path. A
dormant, three-contributor native-interop library running **inside the process that holds the
unlocked vault** does not have one. The LOCKED line's own escape clause — *"if Photino friction
appears"* — is what fired, so this amends that line rather than overriding it.

**Avalonia keeps the property that mattered and was checked before it was chosen.** 12.1.0,
released 2026-07-09, repository pushed the day this was written; MIT, with a Devolutions sponsorship
of three million dollars over three years that explicitly preserves the licence. `net10.0` is a
first-class target framework in the shipped package. NativeAOT is officially documented and its
stated prerequisite is `IsAotCompatible=true` on all libraries — which this repository already sets,
so the existing AOT posture is the entry ticket rather than an obstacle. On Linux it needs **no GTK
and no WebKit**: Skia renders directly, the package list is `libx11-6 libice6 libsm6 libfontconfig1`,
and Skia is built against glibc 2.17 — *below* the CLI's floor, so unlike Photino the app does not
narrow this project's Linux support. All five published RIDs are covered.

**It also subtracts the webview entirely.** No Chromium, no WebKit, no JavaScript runtime, no CSP,
no custom-scheme handler in the process holding decrypted secrets — and no serialization boundary at
all, because the UI calls `Keypaste.Core` objects directly. Worth recording alongside that: Tauri's
published advisories cluster into one class, the WebView trust boundary leaking, most recently
CVE-2026-42184 (2026-05-06, origin confusion letting remote pages invoke local-only IPC). That is
precisely the boundary that would be guarding a vault. Avalonia has no published advisories, which
partly reflects less scrutiny rather than only fewer defects, and is stated that way on purpose.

**The cost, recorded rather than glossed.** The founder's strongest stack is web and Avalonia is
XAML. The LOCKED line's other rationale — that the Next.js skills would also cover keypaste.com — was
already stale: the site is a single hand-written `site/public/index.html` behind a Cloudflare Worker.
Avalonia's own bundler, Parcel, is a paid Accelerate product whose CLI is not in the free community
licence; the free path is `dotnet publish` plus hand-rolled `codesign` and `notarytool`. That is
O-0015 and it does not bite in 4.1, which publishes nothing.

**Four new direct packages** — `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`,
`Avalonia.Fonts.Inter`, plus `Avalonia.Headless` for tests — take this repository from two to six,
with roughly twenty transitive. That is the largest single change to its supply chain. What Avalonia
is trusted with: rendering the window and delivering the keystrokes of the master password. What it
is not trusted with: it never touches the KDBX file, never derives a key, never reads the audit log,
never opens a socket. `Keypaste.Core` still has no `PackageReference` at all, and that is the line
that matters. `Avalonia.Diagnostics` was deliberately not taken — it has no 12.x release, and a
runtime visual-tree inspector attached to a process holding an unlocked vault is not something to
ship even in Debug. `CommunityToolkit.Mvvm` was not taken either, for the reason D-0028 gave for
writing a TOML parser by hand: sixty lines of `INotifyPropertyChanged` is not a dependency's worth
of work. Both are worth revisiting when 4.2's entry list produces real evidence.

**The master password is not typed into a `TextBox`, and the reason is specific.** Avalonia's
`TextBoxAutomationPeer.Value` returns `Owner.Text` with no check for `PasswordChar`, and `TextBox`
does not override `OnCreateAutomationPeer` to suppress it. `Avalonia.FreeDesktop.AtSpi` is in the
dependency closure and AT-SPI is a session-bus service, so a master password in a `TextBox` is
readable by another process on the machine. `TextBox` also keeps an undo stack whose states each
hold a `string`, which is a retained history of partial passwords that cannot be zeroed. So
`Keypaste.App.Controls.MaskedInput` holds no secret at all: it raises one event per character, the
`SecretBuffer` lives in a view model that is disposed on every route out, and its automation peer is
a plain `ControlAutomationPeer` with no value pattern. Measured limit, stated in SECURITY.md rather
than glossed: `TextInputEventArgs.Text` is a `string`, one character long when typed and **the whole
password in one piece when pasted**. Narrower than a field that holds the password for its lifetime;
not nothing.

**`AppVaultSession` consults two clocks, and a timer alone would not have been enough.** Idle
locking compares wall-clock and monotonic elapsed time and locks if either exceeds the timeout,
because monotonic time does not advance across suspend on every platform and wall time can be moved
backwards by a clock correction. A test then showed that was insufficient on its own: timers are
scheduled against the monotonic clock, so a machine that slept through the timeout never fires one
at all. `Reevaluate()` exists for that, and the window calls it on activation — an unattended
sleeping laptop wakes locked. That hole was found by a test in three seconds and would have been
found by hand only by suspending three operating systems.

**CA2000 is satisfied the way `GrantCache.Store` satisfies it** — a narrow scoped suppression around
`Vault.Open`, with a comment naming every route to disposal — because the app's vault must outlive
the method that created it and `VaultSession`'s callback shape is therefore unavailable.
`AppVaultSession.TryUnlock` takes a `ReadOnlySpan<char>` exactly as `Vault.Open` does, rather than
taking ownership of a `SecretBuffer`: ownership transfer reads well but makes CA2000 unprovable at
every call site, and `.editorconfig` makes CA2000 an error precisely so that disposal is visible
rather than promised in a comment.

**What 4.1 deliberately does not do.** It does not bind the approver pipe: `keypaste agent` binds it
at startup and a name already held is a startup failure, so if the app bound it too, whichever
started second would lose — and the loser would be a *silent* loss of the approval path. Stage 4.3
owns that hand-off; 4.1 probes and says one true sentence. It sets no `PublishAot`, ships no release
artifact, changes no line of `release.yml`, and does not adopt the design exploration, which was
never accepted and whose sign-in-first premise inverts docs/PRODUCT.md §4.1.

---

## D-0045 - What a screen may show, decided because a test made somebody decide

**Date:** 2026-07-28 · **Stage:** 4.2 · **Status:** accepted · Related: D-0044, D-0011, D-0014

4.1's `SecretHygieneTests` claimed that no destination surfaced anything from inside the vault, and
said in as many words that the day 4.2 put an entry list on screen it would fail and whoever wrote
that list would have to decide what belonged there. It failed. This is the decision.

**A list row carries a title and a group, and no field value.** That is what `keypaste ls` prints. A
username column was considered and rejected: it is a disclosure surface no CLI verb has, it is
readable over a shoulder, and `docs/IDEAS.md`'s screenshot strategy puts this screen in marketing images —
which is the thing T-24 already worries about for vault paths. **The detail pane widens to username,
URL and notes for the one entry a person selected**, which is `keypaste get`'s scope minus the
password. **A password appears nowhere, in any state, including after Copy**: the copy command reads
it out of the open vault at the moment of the press and hands it to the clipboard, so it is never a
property, never a binding, and never in the visual tree.

**The blanket claim was not given an allow-set, because that would have retired the test rather than
narrowed it.** Four of five sentinels would have moved into an allowed column, and the survivor never
reaches a view model anyway — leaving a gate that passes for an implementation with no list, no
detail pane and no copy button. It is replaced by four two-sided claims and one new total invariant:
**after a lock, every surface built while unlocked holds no sentinel of any kind, including the ones
it was allowed to show a moment earlier.** Two details make that worth something. The sweep holds a
direct reference captured before the lock, because `Dispose` nulls `Content` and a sweep starting at
the shell finds an empty graph and passes for an object that is still alive. And it counts properties
that *refuse* to answer, because a view model reading lazily through a disposed vault throws from
every getter and sweeps perfectly clean — "found nothing" and "asked nothing" have to be told apart.

**Writing that invariant found a real defect immediately.** `EntryDetailViewModel` went on holding a
username, a URL and a notes field after the vault that produced them was gone, and could be reached
by an in-flight continuation long after the shell stopped pointing at it. It is now disposed on
every selection change and on the lock. A `string` cannot be wiped, so the characters may survive in
the heap until a collection — T-18's territory, unchanged; what this buys is that no live object
exposes them, which is exactly what the test asserts.

**The env pane reveals and the entry pane does not, and the asymmetry is deliberate.** An environment
value gets compared by eye against a `.env` file or a provider's dashboard, so reading it is the
task. An entry password gets pasted into a login form, so copying it is the task, and `keypaste get
--show` exists for the times it genuinely has to be read. Adding a reveal to the entry pane later
means widening the gate's allow-set, which is where that argument belongs.

**The list also shows environment variables, and that is correct.** A variable is an ordinary entry
under `env/<project>` (D-0014), so `keypaste ls` lists it and so does this. The two screens differ in
what they do with such an entry, not in whether they can see it.

## D-0046 - The clipboard rule is shared, the transport is not

**Date:** 2026-07-28 · **Stage:** 4.2 · **Status:** accepted · Related: D-0011, O-0008

D-0011 built the clipboard for a CLI and left `IClipboardClearStrategy` as the seam a detached
implementation would drop into. **That seam held for the policy and not for the shape**, which is a
more useful thing to record than pretending it held for both: its signature is a blocking call
taking a `TextWriter`, and a GUI can neither block its UI thread for twenty seconds nor write status
to a console.

So the **rule** moved to `Keypaste.Core.Clipboard.ClipboardClear.Should` — one function, two callers,
carrying the whole of D-0011's logic including the fail-closed branch where a read-back that failed
clears anyway (docs/PRODUCT.md §3.7). It had no test of its own while it lived inside the CLI's blocking
strategy; it has one now, because it is on two front ends' secret paths and law 4.5 is not optional.

The **transports stay apart, and the app's is the windowing system's rather than a subprocess.** The
reason is specific and it is the one thing the app can do that the CLI cannot: a data object can
carry `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` and
`CanUploadToCloudClipboard`, which is how a clipboard owner opts out of Windows Clipboard History and
Cloud Clipboard. `clip.exe` has no way to express them. All three go on in one `SetDataAsync`,
because the history service acts on the notification raised when the clipboard closes and a second
pass arrives after the copy has been recorded. **This closes O-0008 for the desktop app and leaves it
open for the CLI.** It does not touch third-party clipboard managers, which decide independently, or
RDP and Citrix redirection, which hand the value to another machine.

The cost, stated rather than smoothed over: Avalonia's clipboard has `TryGetTextAsync`, which is
exactly the member D-0011 refused to declare on `IClipboard`. The equality guard needs a read-back
and there is no ownership API to use instead, so the call is made in one file, hashed at once, the
reference dropped — and a test greps the app's sources and fails if it appears anywhere else.

**Two things the app promises that the CLI cannot.** Locking clears the clipboard immediately,
because a secret on it is derived from an open vault and `ShellViewModel`'s rule is that nothing
derived from an open vault survives a lock. And an orderly quit clears before the process exits. Both
are conditional, so a clipboard the user has changed since is left alone. Neither survives `kill -9`,
End Task, an OOM kill, a power cut or a logout, and THREATS.md T-19 says so.

**The countdown asks the clock rather than counting down.** Subtracting a second per tick makes the
deadline a function of how many callbacks ran, so a busy UI thread or a suspended machine buys the
secret more time. It consults both clocks and takes whichever elapsed more — the rule
`AppVaultSession` already uses, inverted: whichever says more time has passed is the one that clears
sooner, and sooner is the safe direction for a secret.

## D-0047 - The masked value cell answers a threat T-22 only half described

**Date:** 2026-07-28 · **Stage:** 4.2 · **Status:** accepted · Related: D-0044, T-22

`MaskedInput`'s answer to T-22 is that it holds no password at all, so there is nothing for a peer to
return. That answer is unavailable to reveal-on-hold, where putting the characters on screen is the
feature. The claim is narrower and checkable instead: the value exists in the control only between a
press and its release, it is drawn rather than handed to a control that publishes text, and no
accessibility path returns it.

**`TextBlock` was ruled out, and not for the reason `TextBox` was.** Avalonia 12.1.0 ships
`TextBlockAutomationPeer`, whose name comes from the control's text — so a `TextBlock` publishes a
secret over AT-SPI as the automation *name* rather than through a value pattern. That is a different
property on the same bus, and **T-22's original wording, which reasons entirely about
`IValueProvider`, does not cover it.** T-22 has been amended. `SelectableTextBlock` is worse again: it
adds a selection and a clipboard path nobody asked for.

`RevealedValue` is therefore a `Control` that renders itself, holding the value in a **private field
rather than a `StyledProperty`** — a styled property is readable through `GetValue`, bindable,
visible to a diagnostics overlay, and retained for the control's lifetime rather than the hold's. Its
peer is `NoneAutomationPeer`, which is stronger than `MaskedInput`'s `ControlAutomationPeer` and
correctly so: the button beside the cell carries the name, and it names the variable rather than what
the variable holds. The tests assert the peer's properties **while the value is displayed**, because
checking them at rest is the version every implementation passes.

**The honest limit.** Drawing text needs a `FormattedText`, which takes a `string`, so for the length
of the hold the value exists as a string the runtime will not let anyone wipe — the same limit
`MaskedInput` records for keystrokes, T-18 unchanged. A screenshot, a screen recording, a
remote-desktop session and a shoulder all still see it. SECURITY.md says so.

## D-0048 - A save that would revert somebody else's write is refused

**Date:** 2026-07-28 · **Stage:** 4.2 · **Status:** accepted · Related: D-0017, D-0014, O-0018

4.1's app never wrote. 4.2's does, and it holds a vault for up to eight hours
(`AppVaultSession.MaximumIdleTimeout`) while `Vault.Save()` writes the whole in-memory tree back. A
`keypaste env set` from a terminal inside that window was silently reverted — **with no history item,
because the entry it carried never existed in the saving process's tree for KeePass to snapshot.** Not
in KeePassXC's History tab. Not anywhere. And the user is doing the thing `docs/desktop.md` describes
as normal: alt-tabbing to a terminal.

docs/PRODUCT.md §3.7 is fail-closed. Silent data loss with a paragraph in SECURITY.md is fail-quiet, and the
guard is small: `Vault` digests the file at open and after every save, re-checks before writing, and
throws `VaultChangedOnDiskException` having written nothing. `SaveOverwriting` exists so a caller who
has put the choice to a person is not stuck.

**The digest is the whole file, not the modification time.** Several filesystems round mtime to a
second or two, and a rewrite inside the same second is the common case here rather than the exotic
one. A vault is kilobytes.

**A file that cannot be read is not a conflict, and the first version of this getting that wrong is
worth recording.** Treating an absent or locked file as "changed" pre-empted the retry D-0017 exists
for — the one that absorbs Windows Defender holding a newly written file — and broke four tests that
had been written precisely to stop that. "This changed underneath you" is a claim about a file that
exists and now holds something else; anything else is a write problem, and the save path already has
a retry and an error naming what the operating system said. The residual: on Windows a file another
process holds open cannot be read, so a save racing a concurrent writer that narrowly is not
detected. The replace then fails and is retried, so it is loud rather than silent.

**No `FileSystemWatcher`, anywhere.** It is racy, unreliable on network shares and inconsistent on
macOS, it puts a background thread and a callback near a process holding an unlocked vault, and the
reload it would trigger discards the user's in-progress edit — trading a silent loss of somebody
else's write for a silent loss of your own.

**No `--force` on any verb.** A CLI command opens, edits and saves in milliseconds, so it reaches the
exception only in a genuine race, and re-running is the whole recovery. An override flag on five
commands would buy nothing and would be reached for.

## D-0049 - The two front ends get one test project, and it is in neither solution

**Date:** 2026-07-28 · **Stage:** 4.2 · **Status:** accepted · Related: D-0040, D-0042, D-0044

4.2's prompt asks for a test that a GUI edit is visible to the CLI immediately. Reopening with
`Vault.Open` cannot hold that claim: core is the shared path, so a round trip through it proves
persistence and *assumes* agreement. The mutation that matters is a GUI writing environment variables
under `envs/<project>/` — it round-trips through `Vault.Open` perfectly and only `keypaste env ls`
comes back empty. So the test has to ask the CLI.

The obvious move was adding `Keypaste.Cli` to `keypaste.app.slnx`. **Measured before doing it: that
takes the app solution's restore from 2091 MB to 2580 MB**, because `Keypaste.Cli` sets `PublishAot`
over four `RuntimeIdentifiers` and both are restore-time inputs (D-0040), so any restore that can see
it pulls four RID-specific ILCompiler packs whether or not anything is published. `app.yml`'s header
publishes a cost table promising that a `Keypaste.Core` push pays "the gate job only, one OS, a few
minutes"; 490 MB of AOT compiler on every such push would have made that table untrue, and CLAUDE.md
says to ask what a workflow costs on every push before adding it.

`PublishAot` cannot be scoped to one solution — it is recorded in `packages.lock.json` and a restore
resolving a different set fails `--locked-mode`. The clean fix is splitting the CLI into a library and
a thin AOT host, which moves `artifacts/bin/Keypaste.Cli/release/keypaste`, a path nine
`scripts/verify-*.sh` gates, `ci.yml` and `release.yml` all hard-code. Worth doing one day; not worth
doing inside 4.2.

So `tests/Keypaste.Consistency.Tests` references both front ends, sits in neither solution, and
`app.yml` restores, formats, builds and runs it in steps guarded by the same `app-changed` check the
packaging job uses. A `Keypaste.Core` push pays nothing for it. An `App` or `Cli` push pays, which is
proportionate, because the thing under test is what changed. The CLI harness is **compiled in** rather
than copied, because two copies of a hundred lines of fakes would drift the first time a seam moved,
and the point of the project is that two things agree.

## D-0050 - The KeePassXC gate covers the app's writes, and that is checked rather than asserted

**Date:** 2026-07-28 · **Stage:** 4.2 · **Status:** accepted · Related: D-0005, D-0036

docs/PRODUCT.md §4.6 says any KDBX keypaste writes must open in KeePassXC, tested in CI against real
KeePassXC. That gate lives in `ci.yml` and drives the CLI. 4.2 makes the app a writer, so it needs
either a gate of its own or an argument.

**The argument.** Every mutation the app can perform goes through `Vault.AddEntry`, `UpdateEntry`,
`RemoveEntry`, `EnvStore.TrySet` or `EnvStore.Remove`, and every write through `Vault.Save()` into
the same vendored KeePassLib with the same `KdbxFormat` parameters — the identical set of calls the
CLI makes. An inline edit calls `UpdateEntry`, whose `<History>` element is exactly what section A of
`scripts/verify-keepassxc-writeback.sh` already opens in KeePassXC.

**"No new gate is needed" is itself a claim, and D-0036's standard applies to it too.**
`TheAppSharesTheWriterTests` holds it: one test greps `src/Keypaste.App` for `KeePassLib`,
`File.WriteAllBytes`, `File.Create`, `new FileStream`, `File.Copy` and `File.Move`, and one asserts
the app's `ProjectReference` set is exactly `{Keypaste.Core}`. **The expiry condition, stated so it
can be checked: the day the app writes a KDBX by any route other than `Vault.Save()`, `app.yml` needs
a KeePassXC job.** A decision without a named expiry is an assumption.

The narrower risk is shapes rather than bytes: the compat fixture's shape is the CLI's, so if the GUI
could create entries the CLI has no verb to produce, the gate would never have seen them. The answer
is not to add fixture rows but to not widen what can be written — the GUI validates with
`EntryNameSanitizer` and `EnvConvention`, the same functions the CLI calls, and
`GuiEditIsVisibleToTheCliTests` drives a table of names through both and requires every verdict to
agree.

---

# Open decisions

## O-0015 - Bundling and signing a desktop app, when the official bundler is a paid product

**Stage:** 4.1 · Related: O-0010, O-0012

Avalonia's own bundler is **Parcel**, and its documentation states the CLI is not available in the
free community licence, so using it in CI needs a paid Accelerate tier. The free path is a normal
`dotnet publish` plus hand-rolled per-OS packaging: WiX or Inno Setup on Windows, an `.app` bundle
with `Info.plist` and `codesign`/`notarytool` on macOS, `dpkg-deb` or Flatpak on Linux. All of it is
documented and all of it is real work.

This compounds O-0010 rather than repeating it. An unsigned CLI binary draws a SmartScreen warning;
an unsigned GUI that a person double-clicks draws the same warning at the moment they are deciding
whether to trust a password manager. Nothing is decided here because 4.1 publishes nothing — but the
answer has to exist before the app appears in a `v*` tag, and it is partly a spending decision rather
than an engineering one.

## O-0016 - The desktop app's size, which is not the CLI's

**Stage:** 4.1 · Related: O-0013

The published CLI binaries are about 10 MB each, NativeAOT, one file.

**Measured, 2026-07-28:** `dotnet publish -c Release -r win-x64 --self-contained`, no trimming and
no AOT, produces **207 MB**. That is the honest starting number and it is roughly twenty times the
CLI. Trimming and eventually `PublishAot` will cut it substantially — Avalonia documents AOT support
and this repository already sets its prerequisite — but nothing will bring it near 10 MB, because
Skia and HarfBuzz ship as native assets that sit beside the binary under every option. The CLI's
one-file property is not available to the GUI at all.

Two things follow and both belong in the answer. The install story on `dl.keypaste.com` promises one
file per platform, and the GUI cannot match that shape — an installer or an archive is the honest
form, which is the same conversation as O-0015. And 207 MB is large enough that it changes what a
download costs a person on a slow connection, so whatever appears on a page about it must be the
measured number rather than an optimistic one (D-0036).

## O-0017 - Who owns the approver pipe once the app can approve

**Stage:** 4.1, to be answered in 4.3 · Related: D-0023, D-0024

`keypaste agent` binds `keypaste-agent-<user>` at startup, and `ApproverListener` fails to bind a
name somebody already holds. Stage 4.1 therefore does not bind it at all: the app probes, reports
one true sentence, and disconnects.

4.3's prompt says the Agent Activity screen replaces the OS dialog "when the app is open", which
means two processes will want the same name. The options are a hand-off protocol, the app taking the
pipe and the agent stepping aside, or the agent staying the only approver and the app observing
through some other channel. Whichever is chosen, the property that must survive is the one D-0023
was written to protect: the master password is typed somewhere a person chose to open, never in a
window an agent caused to appear. A design where the losing process fails silently is not
acceptable, because a silently missing approver denies every request — which is the fail-closed
direction, but is indistinguishable to the user from the product being broken.

## O-0002 — Contribution terms: DCO or CLA

Undecided, and it must be decided before the repository accepts its first outside pull request.
docs/IDEAS.md notes "clean IP, CLA or DCO from day one". A DCO is lighter and better received in
open-source communities; a CLA preserves relicensing freedom. Pick one and add
`CONTRIBUTING.md`.

The original O-0001 — "AGPL-3.0 vs the KDBX library licence, must resolve in Stage 0.2" — was
removed rather than answered: its premise that KeePassLib is GPL-2.0-only was factually wrong.
See D-0007. Relicensing freedom therefore matters less than it appeared to, but it is still
cheap only while there is a single copyright holder, which keeps this entry urgent.

## D-0006 — Business notes live outside the repository, and publishing is irreversible

**Date:** 2026-07-25 · **Stage:** 0.1 · **Status:** accepted

**The repository is private.** Whether it becomes public is O-0014, and `docs/PRODUCT.md` §3.8 —
auditable code is the trust strategy — is the claim pulling against the current state. CI cost is
not part of that argument any more; D-0042 removed it.

The benchmarks, pivot conditions, pricing ladder, and acquisition notes are kept in private storage
outside the repository and are registered in `docs/ARTIFACTS.md` by location. A project whose entire
pitch is trust should not also publish the conditions under which its author would abandon it.

Because GitHub can serve any commit ever pushed once a repository is public — including unreachable
ones — the private repository was deleted and recreated rather than rewritten in place. If sensitive
content ever lands in a commit again, recreating the repository is the only reliable remedy; a
force-push is not.

## O-0004 — Deferred CI hardening

CodeQL, dependency-review, Dependabot, and SHA-pinned GitHub Actions are deliberately not in the
Stage 0.1 workflow. Action tags are mutable, so tag pinning without Dependabot ages badly, so
revisit them together.

**Half-answered in 3.4, and the split is deliberate.** `release.yml` pins all four of its actions
to commit SHAs, and `.github/dependabot.yml` now exists for the github-actions ecosystem so the
pins can move. `ci.yml` is still on tags. The reasoning above holds for a workflow that only runs
tests, and stops holding for one that produces binaries strangers download: a mutated action tag
on the release path substitutes an artifact, and nothing in this repository would notice. If time
pressure ever forces a choice, unpin `ci.yml`, never `release.yml`. CodeQL and dependency-review
are still deferred, and Dependabot deliberately does not cover NuGet — a version bump without the
matching regenerated lock file cannot pass CI (D-0004). Note the premise "free now that the
repository is public" is still false: the repository is private.

## O-0005 — `macos-latest` is arm64

Relevant from 0.2 onward: any native dependency (an Argon2 binding, `keepassxc-cli` from Homebrew)
needs an arm64 story, and Stage 3's AOT publish needs the Xcode command line tools on macOS and
clang plus zlib headers on Linux.

Partly answered in 0.2. The vault path has **no** native dependency at all: vendored KeePassLib's
Argon2 is managed C# with no P/Invoke (D-0007), so arm64 needs nothing special. `keepassxc-cli`
comes from the Homebrew cask, which ships an arm64 build. The AOT half of this entry remains open
and is now tracked more precisely by O-0006.

**Fully answered in 3.4, and one sentence above needs correcting.** The toolchain guess was right:
`clang` and `zlib1g-dev` on Linux, the Xcode command line tools on macOS, and the MSVC linker on
Windows, all now installed or asserted by `release.yml`. arm64 needed nothing beyond a native
runner. But "Argon2 is managed C# with no P/Invoke" is not accurate as written -
`Argon2Kdf.cs:167` calls `Argon2Native` *first* and only falls back to the managed
`Argon2Transform` at `:172` when it returns null, and the native path holds `DllImport`s for
`KeePassLibN` and `libargon2`. The conclusion survives because those libraries are not shipped, so
the load throws `DllNotFoundException`, which is caught at `:230` and `:250`, and the managed path
runs every time. The observed behaviour is what the entry claims; the mechanism is not. Recorded
rather than quietly edited, because a wrong reason for a right answer is the thing that bites
later.

This entry's title names a runner `ci.yml` no longer uses (D-0042). The answer is unaffected:
`blacksmith-6vcpu-macos-15` is arm64 as well, so the arm64 story it asked for is still the one being
tested.

## D-0040 - Vendored KeePassLib survives NativeAOT, proved by running the published binaries

**Date:** 2026-07-27 · **Stage:** 3.4 · **Status:** accepted (supersedes O-0006)

**The answer is yes, and it was established by running the binaries rather than by compiling
them.** O-0006 asked whether vendored KeePassLib survives NativeAOT and insisted the answer could
not come from a compile, because the failure mode for reflection-driven code is a runtime
`NotSupportedException`. So both executables were published with `PublishAot=true` and then put
through every gate this repository owns: `verify-run-injection`, `verify-run-signals`,
`verify-mcp-stdio`, `verify-approval-e2e`, `verify-policy-e2e`, `verify-log-chain`, `verify-demo`,
and both directions of the KeePassXC compatibility gate. A KDBX file written by the AOT binary
opens in real KeePassXC with Argon2, AES-256, unicode values, nested groups and history intact,
and a value KeePassXC writes is read back by the AOT binary. That last one is the whole answer:
the vault writer is the code most likely to be broken by trimming, and it is the code docs/PRODUCT.md §4.6
makes constitutional.

**All three of O-0006's named suspects were wrong, and the real list is eleven diagnostics in three
files.** The XML suspicion was misplaced: KDBX read and write are streaming `XmlReader` and
`XmlWriter` (`KdbxFile.Read.Streamed.cs:101`, `KdbxFile.Write.cs:203`), which AOT handles. The one
live `XmlSerializer` (`XmlUtilEx.cs:165,180`) is reachable only from `KcpKeyFile.Xml.cs:121,126`,
the `.keyx` key-file path, and `KeePassInterop.cs:281-289` builds the composite key from
`KcpPassword` alone, so it trims away. `CryptoRandom`'s static initialisation is clear: the
reflective parts are behind `#if !KeePassUAP`, and that symbol is defined in the file. What
actually remains is `SimpleStat.cs` (eight), `NativeLib.cs` (two) and `IOConnection.cs` (one).
There are **no IL3xxx diagnostics at all** — nothing reachable requires runtime code generation,
which is the class of failure that cannot be worked around.

**Every one of the eleven is a probe that already treats failure as "feature unavailable".**
`SimpleStat` loads Mono.Posix to preserve Unix owner and mode across a vault save; under AOT
`Assembly.Load` fails, the `try`/`catch` at `SimpleStat.cs:107-125` returns false, `Get` returns
null, and `FileTransactionEx.cs:269` calls `Set` only when non-null. The vault is written either
way and only the permission copy is skipped — which is already what happens on every machine
without Mono.Posix installed, meaning all of them since .NET Core. `NativeLib.ProcessArchitecture`
falls back to `IntPtr.Size` at `:124` and only feeds a native-library loader keypaste does not
ship. `IOConnection` reaches `HttpWebRequest` internals on remote-vault paths keypaste never takes.
None of this was assumed; each was traced to a caller.

**Two mechanical claims in the plan for this stage were false, and finding out cost a negative
control rather than a release.** The first: `-r <rid>` does not add a RID, it *narrows* the
project's set to one, so a lock file recording four can never satisfy a single-RID restore. The
release workflow therefore must never pass `-r` to restore; it restores the solution whole and
publishes per RID with no restore, which is what declaring `RuntimeIdentifiers` in the plural is
for. The second: `Microsoft.DotNet.ILCompiler` is compared as a package reference, not a package
download, so lock files do record it — which inverts the conclusion. `PublishAot` had to be
committed in the csproj rather than passed at publish time, because a property that changes the
restore graph must be constant or locked mode cannot hold.

**That inversion turned out to be a gain.** The lock files now pin the AOT compiler and each
per-RID native compiler by content hash, so the toolchain that produces the shipped binary is
covered by the same discipline D-0004 applies to every other dependency. The costs are real and
worth stating: roughly 160 MB of ILCompiler packages on a cold restore, and twenty trim feature
switches added to `runtimeconfig.json` on an ordinary `dotnet build`, including
`IsDynamicCodeSupported=false`. The second is the more interesting one, and it cuts the right way:
the JIT build the nine verify scripts exercise is now configured more like the binary that ships.

**The trim diagnostics were not suppressed, because suppressing them would have re-created the
problem O-0006 was complaining about.** `SuppressTrimAnalysisWarnings` also sets
`EnableTrimAnalyzer=false`, which would disarm D-0005's gate for our own code as well as the
vendored tree. Instead `IlcTreatWarningsAsErrors` is off and `scripts/verify-aot-trim.sh` diffs the
emitted diagnostics against `scripts/aot-trim-baseline.txt` character for character, the same shape
as `verify-demo.sh` holding the documented transcripts to what the binaries print. The gate rejects
any diagnostic naming `src/` unconditionally and separately, so our own code keeps the stricter
rule. The effect is that the quarantine no longer swallows an upstream re-merge that introduces an
AOT-hostile construct: it becomes a diff and a red build. That is the gate O-0006 said the
quarantine had disarmed, put back somewhere it can work.

**Observed failing, three ways.** Removing a line from the baseline fails the gate; injecting a
diagnostic that names `src/` fails it with the stricter message; and a locked restore was watched
emitting NU1004 on exactly the four projects that declare RIDs, with the three test projects
restoring clean, before anything was regenerated. Also worth recording as a trap: an *incremental*
publish emits no diagnostics at all, so the gate must run against a clean publish. It fails rather
than passing vacuously in that case, which is the correct behaviour, but the reason is not obvious
from the error.

**Measured on win-x64, locally.** `keypaste` 10.4 MB and `keypaste-mcp` 9.1 MB as single native
files with no managed assembly and no `runtimeconfig.json` beside them. `--help` runs in 54 ms
against 75 ms for the framework-dependent build, ten runs each. The AOT binaries are about a
quarter faster to start and need no .NET installed, which is the entire point of the exercise.

**Then confirmed on all four target platforms in CI, which is the claim that matters.** The first
end-to-end run of `release.yml` published `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64`,
and every one of them passed the whole suite against its own native binary: the nine verify
scripts, both directions of the KeePassXC gate, and the trim baseline. Packaged archives came out
at 8.5, 8.0, 7.5 and 7.6 MB compressed. The eleven diagnostics are identical on every platform
after path normalisation, so the baseline is platform-independent rather than per-RID - which was
worth checking rather than assuming, because a per-RID baseline would have been four files to keep
in step.

**Three defects were found by running it, none of which a compile would have shown.** The `guard`
job failed after every check in it had passed, because `setup-dotnet` was asked to cache a NuGet
folder in a job that never restores. Packaging failed on macOS alone: it copied `keypaste*` and
then deleted known debris by extension, which works only as long as every platform's build output
can be enumerated in advance - macOS writes `keypaste.dSYM` as a *directory*, and `cp` refuses it.
It now copies the two binaries by name and fails if either is missing. And Git Bash's `sha256sum`
defaults to binary mode, writing `<hash> *<name>` where coreutils writes two spaces, which would
have failed the `publish` job's aggregate-versus-per-asset diff on the first real tag - and
`publish` does not run on a dispatch, so no dry run would ever have caught it. The checksum files
are now written by hand in one format and verified with `-c` on the machine that wrote them.

**What this does not settle.** O-0007 asked whether the vendored tree needs trimming, and its
trigger condition was "if O-0006 forces trimming for AOT size". It did not: nothing had to be
removed and the binaries are around 10 MB. The published binaries are unsigned and un-notarized
(O-0010), not reproducible (O-0012), and `osx-x64` and `linux-musl-x64` are unserved (O-0013).

## O-0007 — Trim the vendored tree?

`PwGroup.Search.cs`, `QualityEstimation.cs`, `PopularPasswords.cs`, `PasswordGenerator/**` and
`HmacOtp.cs` are severable — roughly 3,000 lines keypaste never calls. They are kept because every
`<Compile Remove>` is a decision a future re-merge must re-justify, and the current cost is only
build time.

Revisit if O-0006 forces trimming for AOT size, or if `HmacOtp.cs` becomes load-bearing when TOTP
arrives from docs/IDEAS.md. Note the exclusion mechanism is already in place and costs nothing to
extend: files stay on disk, only the compilation changes.

**The trigger condition fired in 3.4 and did not fire.** O-0006 is answered (D-0040) and AOT forced
no trimming at all: the published binaries are 10.4 MB and 9.1 MB, the ILC trimmer removes the
unreachable code without any `<Compile Remove>` being added, and none of the eleven trim
diagnostics comes from the severable files this entry lists. So the size argument for trimming is
gone - the trimmer already does it. What is left is the original argument, that fewer vendored
lines are fewer lines to re-justify on re-merge, which was never urgent. Still open, now for a
weaker reason than when it was written.

## O-0008 — Windows clipboard history and cloud sync retain the secret

**Closed for the desktop app, still open for the CLI.** The app sets all three opt-out formats on
one `SetDataAsync`, because it owns a window and Avalonia's clipboard takes a data object (D-0046).
`clip.exe` cannot express them, so `keypaste get` is unchanged and everything below describes it.
Third-party clipboard managers and RDP redirection are unaffected on both.

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

**How KeePassXC does it, read from their source on 2026-07-28 rather than assumed.** There is no
native Win32 clipboard code in KeePassXC at all - `WinUtils` does not override `setClipboardText`,
so everything falls through to Qt. `src/gui/Clipboard.cpp` attaches three entries to the
`QMimeData`: `ExcludeClipboardContentFromMonitorProcessing` = `"1"`,
`CanIncludeInClipboardHistory` = four zero bytes, and `CanUploadToCloudClipboard` = four zero
bytes. Qt's `QLastResortMimes` passes unrecognised MIME names through `RegisterClipboardFormat`,
and publishes the lot as one `IDataObject`. The first arrived in 2.6.3 (Jan 2021), the other two in
2.7.10 (Mar 2025).

Three details are worth more than the format names. **All formats must be set inside one
`OpenClipboard`/`EmptyClipboard`/`SetClipboardData`×N/`CloseClipboard` session** - the clipboard-
update notification the history service acts on fires at `CloseClipboard`, so a second session to
add the markers has already leaked. **Per Microsoft's own documentation
`ExcludeClipboardContentFromMonitorProcessing` alone covers both history and cloud sync**; the
other two are finer-grained and redundant. And **KeePassXC clears rather than overwrites**
(`QClipboard::clear()`, changed deliberately in 2.7.5), guarded by comparing the current clipboard
against what it put there, so it cannot wipe something the user copied since.

**Two defects in their implementation that keypaste should not inherit.** Every tagged release that
has the extra fields - 2.7.10, 2.7.11 and 2.7.12, the current one - spells the third format
`"CanUploadToCloudClipboard "` with a trailing space, which registers a different and meaningless
format. It is fixed on `develop` and in no release. It happens to be harmless because the Exclude
format already covers cloud sync, but the lesson is that a string literal passed to
`RegisterClipboardFormat` cannot be checked by review: **verify it with a `GetClipboardFormatName`
round-trip in a test.** Separately, they hold the copied secret in a plain `QString` for the whole
clear-timeout window purely to power the equality guard. keypaste gets the same guard from a hash
and should.

**What this can and cannot close, which is the part that decides the SECURITY.md sentence.** The
formats are a request, not an enforcement boundary - Windows does not restrict who may read the
clipboard, it only asks well-behaved consumers to abstain. Implementing them closes the
**first-party** Clipboard History and Cloud Clipboard, which is exactly the defect this record
names, and closes nothing else. Third-party clipboard managers each decide independently whether to
honour the formats and most do not, and RDP/Citrix/VDI redirection hands the secret to a peer
machine whose history keypaste cannot reach. So the honest resolution is: implement the formats,
and state the residual rather than describing the clipboard as safe. Eliminating the class means
not using the clipboard - direct injection, or a paste-once broker - which is a Stage 4 design
question, not a packaging one.

The same defect exists on the other platforms with different vocabulary and is not covered by this
record: `org.nspasteboard.ConcealedType` on macOS is a community convention rather than an Apple
API, KeePassXC sets nothing for Universal Clipboard, and Linux is per-clipboard-manager convention
(`x-kde-passwordManagerHint`) with a `wl-copy -c` shell-out because Wayland forbids an unfocused
app from touching the clipboard.

**Still open, and narrowed to a decision rather than a research question.** Nothing is implemented.
Stage 4.2's prompt says "copy buttons (auto-clearing clipboard)", and that phrasing overstates what
any platform can deliver - it should name the timeout and point here. Settle before that UI is
written, because the answer decides whether the GUI offers a copy button at all.

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

**Two variables differing only in case are one variable on Windows.** Injection is a hard failure on
every platform — not a platform-conditional, and not last-writer-wins (D-0016). Reading still lists
both, so `env ls` and `env rm` can show and clear what
KeePassXC put there; only injection refuses. The reasoning, including why the platform-conditional
is the worst of the three options rather than the safe middle, is in D-0016.

Only the `argv` half of this entry is still open.

## O-0010 - The released binaries are unsigned and un-notarized

Nothing signs the artifacts. On macOS that is not cosmetic: Gatekeeper quarantines anything a
browser downloaded, so the documented install carries a sixth line that removes the quarantine
attribute. A tool whose entire argument is that a person approves each secret is telling its users
to strip a security attribute before they can run it, which is a defect with a price rather than a
quirk of packaging. On Windows, SmartScreen warns on an unsigned executable with Mark-of-the-Web,
and no gate can fix that from inside the repository.

The prices are known: 99 USD a year for the Apple Developer Program plus a notarization step in
the release workflow, and an organisation-validated or extended-validation certificate for
Windows, which is the more expensive and more bureaucratic half. docs/PRODUCT.md §5.4 settles one thing in
advance - signing is security, so a signed binary can never be the paid tier's artifact while the
free one is unsigned.

Resolve before, or with, the repository going public. Until then the install page says the binaries
are unsigned in as many words, and D-0041 records why the checksum beside them proves integrity but
not authenticity.

## O-0011 - There is no Homebrew tap, no Scoop bucket and no winget manifest

For most developers "install one-liner" means `brew install keypaste`, and keypaste does not have
that. What it has is five lines of download, verify and extract, which is honest but is not what
the phrase promises.

Each channel wants a versioned, checksummed artifact - which now exists - plus a maintenance
commitment on every release, and winget's manifest process in practice assumes a signed installer.
So O-0010 gates at least the Windows half. A Homebrew tap is the cheapest of the three and the one
the r/selfhosted and Show HN audiences will ask for first; it is also the one that makes the
release workflow responsible for a second repository.

Decide after the first real release has been installed by someone who is not the author.

## O-0012 - The builds are not reproducible, and nothing attests to where they came from

`Deterministic=true` governs the managed compile only. NativeAOT link output is not expected to be
byte-identical across runs or machines, so no reproducibility claim is made anywhere and none
should be added casually. The practical consequence is that a reader cannot rebuild the published
binary and compare hashes; they can only check that the bytes they downloaded are the bytes the
checksum names.

Three options, in rising cost: say nothing further and rely on D-0041's integrity-not-authenticity
sentence; sign with minisign using a key the maintainer holds, which is cheap and puts the trust
question on key distribution; or adopt `actions/attest-build-provenance`, which needs
`id-token: write` and `attestations: write` and gives readers a `gh attestation verify` command -
but a provenance attestation the documentation does not teach anyone to check is decoration.

Related to O-0010: a signature answers authenticity, an attestation answers origin, and they are
not the same question. Do not let one be sold as the other.

## O-0013 - osx-x64, win-arm64 and linux-musl-x64 are unserved

The release publishes `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64`.

`osx-x64` was dropped in 3.4 with reasons in D-0041: macOS 26 is the last release that runs on
Intel Macs, no runner fleet in use still offers one, and shipping a slice no gate had executed
would have contradicted O-0006. Intel Mac owners build from source. Revisit only if someone asks.

`linux-musl-x64` is the more likely to be missed. Alpine is common in containers, and the release
workflow asserts that the glibc binary does *not* run there, so the gap is tested rather than
assumed. Adding it is one matrix row and one more archive; the reason it is not there is that
nobody has asked and the RID list should not grow on speculation.

`win-arm64` has no runner in either fleet today. Windows on ARM users can run the x64 binary under
emulation, which works and is slow.

Reconsider the whole list after the first release, on evidence about who actually downloaded what,
which is exactly the kind of evidence a download log can supply and docs/PRODUCT.md §3.5 permits.

## O-0014 - The repository is private and docs/PRODUCT.md says the code is open

docs/PRODUCT.md law 3.8 reads "The code is open source (permissive or copyleft — decided once, in docs/STEPS.md)
and stays open. Auditable code is the trust strategy for an unknown founder." The licence is
AGPL-3.0 and every release publishes its own corresponding source (D-0041), so the *code* is open in
the licensing sense. The *repository* is private, so nobody outside can actually read it, and
"auditable" is the word law 3.8 uses.

D-0006 carries the precondition for answering this: GitHub can serve any commit ever pushed once a
repository is made public, including unreachable ones, so anything sensitive that ever landed in a
commit means recreating the repository rather than force-pushing. That is a precondition for
whenever this question is answered, not a historical note.

**Two things follow, and they pull in opposite directions.** Every launch channel in `launch.md`
sells an auditable vault tool, and a private repository makes the central claim unverifiable by the
reader it is aimed at - which is the same defect D-0036 refused to ship on the landing page. Against
that, publishing is irreversible in the way D-0006 describes, and this is a secrets tool whose
history has already had one near-miss with private business notes.

D-0042 removed the one argument that was pushing this decision for the wrong reason: CI cost. It is
now a question about trust and about what is in the history, which is where it belongs. It must be
answered before 3.3's launch posts go out, not before `v0.1.0` publishes.

## O-0018 - Two writers, one KDBX, and no merge

**Stage:** 4.2 · Related: D-0048, D-0014

D-0048 makes a lost write loud. It does not make concurrent editing work. Two people, or one person
and an agent, cannot both hold the file open and both save: the loser is told to reload and redo,
and "redo" may be a long edit.

The format is why. KDBX defines no locking and no merge. KeePassXC writes a `.lock` sidecar and
warns; it does not resolve. A real answer is either advisory locking that keypaste and KeePassXC both
honour — which needs KeePassXC to agree — or an entry-level three-way merge using the UUIDs and
timestamps the format already carries, which is a feature rather than a guard and would need its own
compatibility gate.

Nothing is decided here because the guard covers the case that actually happens. The question becomes
real when the app can write on somebody else's behalf, which is Stage 5's sharing work.

## O-0019 - macOS could ask its pasteboard to conceal a secret, and does not yet

**Stage:** 4.2 · Related: D-0046, O-0008

`NSPasteboard` has an `org.nspasteboard.ConcealedType` convention that asks clipboard managers not to
record an item — the macOS counterpart of the Windows exclusion formats D-0046 sets. Avalonia's
clipboard can carry an arbitrary platform format, so expressing it is probably a few lines.

It is not done because nothing here has been measured: unlike the Windows formats, which have a
citation and a documented defect to test against (O-0008's KeePassXC trailing space), this is a
convention that third-party managers opt into, and a format written on the strength of a blog post is
the kind of thing that looks like a mitigation and is not. Worth doing with a macOS machine, a
clipboard manager that honours it, and something to check afterwards.

## O-0020 - Nothing automated has ever seen this app draw

**Stage:** 4.2 · Related: D-0043, O-0015

`app.yml` publishes on three operating systems and runs `--selftest`, which constructs no window and
cannot: a runner has no display. So every claim in this stage about what is *on screen* — that the
mask shows dots, that the countdown drains, that holding reveals and releasing conceals, that a paste
after a quit comes back empty — rests on the manual checklist in `docs/desktop.md` rather than on a
gate. The view models are tested exhaustively; the pixels are not tested at all.

That is stated rather than implied by a green check, which is D-0043's rule. The options are a
headless render test comparing images, which is brittle across platforms and font stacks; driving the
published binary under a virtual display, which tests one of the three operating systems; or leaving
it manual and honest. 4.2 leaves it manual. It should not stay that way through a release, because
the checklist is only run by somebody who remembers it exists.
