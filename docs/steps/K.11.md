# K.11 — TOML and CSV tested by property

Completed 2026-10-31 on `k11`, source only; the ci and app runs are under Evidence.

## Amendments

- With the plan (founder, 2026-10-31, D-0679):
  - **FsCheck 3.6.0 instead of CsCheck, and no Verify snapshots.** Each is essentially one person's project: CsCheck has 778 of about 783 commits by its author, and Verify 6,028 by its author. PRODUCT §4.9 refuses a single-maintainer package.
  - **Only FsCheck's core package.** FsCheck.Xunit.v3 needs xunit 4.
  - **Parity is checked by Stryker.NET 5.0** on GitHub's runners, in a probe workflow on the branch.
- At the first checkpoint (founder, 2026-10-31): **TOML and CSV only.**
  - Their property code came to the length of the examples it replaced, against an estimate of 325 fewer lines.
  - The placeholders, the PSL matcher and the merge keep their examples. BACKLOG holds the option.

## What changed

- **`TomlTests`** replaces `PolicyTomlTests`, `TomlLimitsTests` and `TomlWriterTests`. Its properties:
  - **Round trip.** What the writer writes reads back as written through its UTF-8 bytes, with every section and pair on its line, and `TryGet` finds each key.
  - **Layout.** Spacing, comments, blank lines, CRLF and a byte order mark change no parse, and a byte that is not UTF-8 is refused.
  - **Refusals.** Each of the 36 constructs the reader refuses fails the whole file naming its line, injected at a generated line of a generated file.
  - **Limits.** Each limit takes exactly its number and refuses one more, with its exact message, under the policy's limits, the paths' and generated ones. A backslash is refused under all of them.
  - **The writer.** It writes a value or a list exactly when that reads back as itself within the limits. It takes only bare names, whole numbers and comments without control characters.

  The policy constants and the two null guards stay examples.
- **`CsvTests`.** Its properties:
  - Generated records, each field bare or quoted, read back with the line each starts on.
  - Malformed text is refused with its exact message and line, and never its text: the five kinds the reader names, and a byte that is not UTF-8.
  - The first record is read from a file cut anywhere after its line.
  - The three export readers refuse four files: a malformed one, an empty one, a header naming a column twice, and one missing a required column.

  The vendor suites lose their byte-order-mark and line-break tests, their malformed-export tests and their duplicate-header tests, which these properties state. Their mappings, recognized headers, registration, import into a vault and no-value tests stay.
- **The helpers.**
  - `Properties.Hold` runs a rule from seed `(20261031, 1)`.
  - `Noise` draws the choices a generated case makes.
- **Line counts in `tests`:**

  | Area | Net lines |
  |---|---|
  | TOML (446 against 459) | −13 |
  | CSV | −19 |
  | `Properties.cs` | +28 |
  | C# in total (+612, −616) | −4 |

  The lock file gains FsCheck and FSharp.Core.

## Evidence

- **How the probe worked.**
  - Stryker.NET 5.0.0 ran with the MTP runner and `perTest` coverage on `ubuntu-24.04`.
  - Its test-case filter does not reach MTP, so every test of `Keypaste.Core.Tests` ran against each mutant. A suite's kills are the mutants its own classes are named as killing.
  - Stryker mutates only a project the test project references directly, so the probe added a reference to Infrastructure.
  - The probe workflow and `k11-base` are deleted.

  **The runs:**
  - Baselines:
    - 41214578767 at `f45c1311`: the export readers, the placeholders and the PSL. Its TOML and CSV jobs failed to find Infrastructure, and its merge job was cancelled once the founder kept the merge's examples.
    - 41215522812 on `k11-base`, with the same tests: TOML and CSV.
  - After-run: 41216645434 at `5cb42e75`.

- **The results:**

  | Mutated | Killed by the suite, before → after | Gained | Lost |
  |---|---|---|---|
  | `Toml.cs`, `TomlWriter.cs` | 232 → 280 | 51 | 3 |
  | `Csv.cs` | 104 → 106 | 2 | 0 |
  | `CsvExport.cs` and the three readers | 231 → 226 | 12 | 17 |

  The lost mutants, one by one:
  - **TOML's three are real.** Two are the backslash refusal's message and its result, and one is `CanWrite`'s `<=` at the string limit. Each gained a check, and planted by hand each now fails `TomlTests`.
  - **The readers' 17 were planted by hand against the CSV and vendor classes.**
    - 11 fail the new suite.
    - The other 6 fail neither the old suite nor the new one: `LastPassCsvReader`'s `"Format"`, `"Date"`, `":"`, `"URL"` and `"\n"` strings, and its empty-URL condition. The baseline's verdict on them was wrong.
  - **The report is not reliable on its own.** The after-run reported as surviving the 11 that fail when planted, so the MTP runner's verdicts disagree with planted runs in both directions. Parity rests on the report together with those planted runs.
  - A planted `pairs.Count > limits.Pairs` in `Toml.cs` fails the limits property, a case today's examples missed.
- **The kept suites' baselines, for the BACKLOG option:**
  - `KeePassPlaceholders.cs` and `PlaceholderFill.cs`: 174 killed, 17 survived, 14 timeouts.
  - `PublicSuffixList.cs`: 33 killed, 12 survived, 11 timeouts.
- **On the founder's Mac** (macOS 26.6, arm64, SDK 10.0.302):
  - `verify.sh compile` passed.
  - `verify.sh test` passed 61 tests: `TomlTests`, `CsvTests` and the three vendor classes. Nothing was left running.
- **The package.** The restored `.nupkg` files hash as nuget.org serves them, and the lock file holds their content hashes.

## Decisions

D-0679.

## Limits and follow-ups

- **A fixed seed** means a property meets a new case only when its seed or generators change.
- **The no-value tests stay examples** over each vendor's real fixture. A property over generated secret cells would have cost about what it saved.
- **Properties for the placeholders, the PSL matcher and the merge** are in BACKLOG.
