# K.10 — The CLI parsed with System.CommandLine

Completed 2026-10-31 at `0b79ae7f` on `k10`, source only; runs ci 41203232296 and app 41203240059 at that commit. The integrated commit differs from `0b79ae7f` only in this record.

## Amendments

- With the plan (founder, 2026-10-31, D-0678):
  - **Help and parse errors are generated, Apple-style.** Every option and operand is declared once with a one-line description, and one renderer draws each verb's help in the top-level help's layout.
  - **This amends "the CLI tests pass unchanged".**
    - The tests that pinned the old `usage:` lines or operand wording now assert the new output.
    - The tests that built the deleted parser or called `run`'s `Split` now use the parsed line.
  - **The package is System.CommandLine 2.0.14**, the latest stable release; 3.0 is a release candidate. It is recorded with no justification text.

## What changed

- **The shared syntax.** `Keypaste.Infrastructure/Syntax.cs` replaces `CommandLine.cs`. It holds:
  - the parser's settings;
  - the factories a verb declares its options and operands with;
  - a line's path, and what follows its first `--`;
  - the refusals;
  - the help renderer.

  Response files and POSIX bundling are off. The library would otherwise accept four things, which `Syntax.Problem` now refuses in today's words:
  - an undeclared `--option` that an operand could take;
  - `--flag=value`, which it splits into the flag and an operand;
  - a repeated flag;
  - a bracketed first argument, which it reads as a directive.
- **The verbs.** Every verb declares a `CliVerb`, or a `Verb` for a group, with its options and operands. What went:
  - the `OptionSpec` arrays, parse blocks and help blocks;
  - the operand-count checks the parser now makes;
  - the hand-written `WriteUsage` and `WriteHelp` methods;
  - the dispatch switches of `CliApp` and of the `env`, `field`, `attachment`, `token`, `log`, `grants` and `share` groups;
  - `McpCommand.cs`, whose three refusals are now `CliApp`'s.

  What `CliApp` does now:
  - It parses the line and runs the verb.
  - It refuses the removed spellings before anything parses, their help included (D-0470).
  - `keypaste <group> help` still prints the group's help.
  - `search` still tells you to quote words that go together.
- **run.** Everything after the first `--` is passed through verbatim and read back with `Syntax.Passed`. `RunCommand.Split` and `RunArguments` are gone.
- **The bridge.** `ServerOptions` declares its options the same way and keeps its `TryParse`. The same renderer draws `keypaste mcp --help`, as plain text, and a refused line says which help to read. An option's error still comes before help (D-0467).
- **share.** Its transport moved onto `CliContext.ShareTransport`, which its tests set.
- **The packaging gates.** Five gates checked that the packaged `keypaste mcp --help` began `usage: keypaste mcp`: the published bridge, the macOS app and dmg, the Windows installer and the Linux AppImage. `scripts/lib/terminal.sh` checks the same line in a new terminal. They now check for the first line of the generated help, `the MCP server your AI tools start`, as `BRIDGE_HELP`. The install and upgrade exercises' self-test fixtures use that line too.
- **The help.** A verb's help gives:
  - its summary, the line `keypaste --help` shows, now written once;
  - an overview where the old help explained more;
  - USAGE, COMMANDS, ARGUMENTS and FLAGS.

  It stays within 80 columns, and a `` `code` `` span is never broken across lines. A label longer than 26 characters puts its description on the next line. Every usage error from the parser ends with `see keypaste <verb> --help`.
- **Line counts.**

  | Area | Added | Removed | Net |
  |---|---|---|---|
  | C# in `src` | 1,416 | 2,394 | −978 |
  | Tests | 91 | 213 | −122 |
  | 19 lock files | 196 | 19 | +177 |

  Most of the tests' removal is the parser's own test file. The lock files that reach Infrastructure gain only System.CommandLine.

## Evidence

- **Probes of 2.0.14, before the shared layer was written:**
  - `@file` stays an argument and `-abc` an operand.
  - A value starting with `-` is taken whole.
  - Tokens after `--` arrive verbatim.
  - The library accepts the four forms above.
- **On the founder's Mac** (macOS 26.6, arm64, SDK 10.0.302 from `~/.dotnet`):
  - `verify.sh compile` passed in 61 s.
  - One `verify.sh test` run passed 1,086 CLI tests with 5 skipped, 33 bridge tests (`ServerOptionsTests`, `PlainWordsTests`, `SessionAttachmentTests`) and 42 `HealthCheckTests`. Nothing was left running.
  - The first run had failed 18 tests. Fifteen had pinned the old help. Three found regressions, which the second run passed:
    - `keypaste env help`;
    - `search`'s advice for unquoted words;
    - a code span broken across two lines.
  - `CliAppTests` holds the refusals above and that neither `@file` nor `-pdev` adds an option.
- **The package:**
  - The package restored is the one nuget.org serves.
  - Its assembly holds no `ModuleInitializer`, so THREATS' property of the binary's closure holds.
  - Its copyright, `© Microsoft Corporation`, is not a form the publisher-metadata check reads.
- **CI at `ac89a2fb`:**
  - ci 41201975819 passed every job.
  - app 41201982990 failed `publish + selftest` on all three runners: "the copied keypaste does not answer mcp --help", which the packaging-gates change fixes.
  - In the same run, `keepassxc first run (macos-15)` failed, BACKLOG's former F.74. Every other job passed, the desktop tests and gates on all three runners included.
- **CI at `0b79ae7f`:**
  - ci 41203232296 passed every job.
  - app 41203240059 passed every job but `keepassxc first run (macos-15)`, which failed as F.74 does: KeePassXC exited before opening the vaults after the Qt font-alias warning.
  - `publish + selftest` passed on all three runners.
- **main at `5cd60be8`:**
  - ci 41205756948 passed every job.
  - app 41205756958 failed `keepassxc first run (macos-15)` as F.74, on its rerun too.
  - It also failed `desktop gates (windows-2025)`, whose rerun passed: in `verify-crash-report.sh` the UI-thread crash left two reports where one was expected. This is its second occurrence after K.8's, so it is now in BACKLOG's investigation candidates (D-0673).

## Decisions

D-0678.

## Limits and follow-ups

- **Behaviour that changed beyond the help:**
  - `--opt:value` is accepted, as `--opt=value` is.
  - `setup` refuses an operand, which it ignored.
  - `log` with an unknown word says "unknown subcommand", not "unexpected argument".
  - `lock`, `doctor`, `use`, `approve`, `clients`, `grants` and `generate` used to print their usage after a usage error; they now print the `see` line.
- **System.CommandLine reaches the desktop app's output** through Infrastructure, unused there, as `CommandLine.cs` did.
- **The `mcp` summary is written twice:** in `CliApp`'s list and in the bridge's own help. The CLI may not reference `Keypaste.Mcp` outside `Program.cs`.
