# P.4a1 — Resolve Auto-Type sequences in Core

Completed 2026-10-29 on `t6-autotype` in `1e04352e` on `t6`, source only. At `34564a1a`, ci 40909948638 passed every job on all three runners on its first attempt, and app 40909966048 on its second: its first failed desktop tests (macos-15) in `DrawnMaskTests`, which F.73 holds, and only that job was rerun.

## Amendments

None by the founder: the row is built as selected, to the design approved on 2026-10-24 (D-0547). Its acceptance asks that every KeePass 2 divergence be refused, and reading KeePass 2.59's `SendInputEx.Parse` and `SprEngine.Compile` beside KeePassXC 2.8's `AutoType::parseSequence` found more than the design lists. Each is refused as the row's own are (D-0604):

- **A modifier at the end, or before anything but one key or character.** KeePass 2 fills a sequence's fields in before it reads it, so `+{USERNAME}` shifts the username's first character there and nothing in KeePassXC; `^{DELAY 5}` holds Control through the pause in KeePass 2 only; and a trailing `+` types a plus in KeePassXC and nothing in KeePass 2.
- **A count on anything but a key, a character or `{DELAY n}`**, such as `{TOTP 2}`, which KeePassXC types ignoring the count and KeePass 2 refuses.
- **`{DELAY}` with no count and a `{DELAY=…}` that is not digits**, which KeePass 2 refuses and KeePassXC reads as 1 ms and 0 ms.
- **A control character in the sequence**, which KeePassXC's pattern skips and KeePass 2 types, and a stray `}` or a brace that opens nothing.
- **A `%…%` holding no brace or `=`**, which KeePass 2 fills in from the environment variable of that name, when one is set, before it reads the sequence (`SprCompileFlags.EnvVars`), while KeePassXC reads each `%` as Alt.

Two of KeePassXC's own readings are not copied, since they are defects: a count or delay too long for an integer, such as `{delay 5000000000}`, which KeePassXC reads as 0 and its test accepts, is refused as over the limit; and a character outside the basic plane, of which KeePassXC's `AutoTypeKey(character[0])` types only the first half, is one character of two code units. A `{s:…}` in a sequence is refused, as KeePassXC reports it invalid, while D-0465 still fills one in inside a value.

## What changed

- **`Core/AutoType/`**, beside `KeePassPlaceholders`, which is unchanged. `AutoTypeSequence` is the grammar: it spells `{{}` and `{}}` as KeePassXC does, keeping where each character came from so a refusal names the token as written, matches each brace to its partner as KeePassXC's recursive pattern does, splits a placeholder's inside into its name and count as that pattern's lazy groups split it, and checks the name in KeePassXC's order (D-0604). `AutoTypeSettings` holds what KDBX keeps and applies KeePassXC 2.8's inheritance, defaults and switches (D-0605), and chooses the sequence for a title through `WindowPattern`, which runs a pattern through .NET's `Regex` under a 100 ms timeout, a wildcard on the non-backtracking engine (D-0606). `AutoTypePlan` turns the tokens into steps: presses and characters with their modifiers, each field's text filled in through `KeePassPlaceholders.Fill`, a code step holding the item's TOTP, pauses, delays between keys and `{CLEARFIELD}`; one refusal drops every step and the seed, and disposing the plan does the same (D-0607).
- **Reading.** `KeePassInterop.ReadAutoType` reads an entry's `AutoType` and every group above it, and the username and password by length only; `HasField` and `ReadStoredField` read a field by its exact name, the latter leaving the value protected in the open database. `Vault.PlanAutoType` resolves under the vault's lock and gives no plan for an item it cannot find or one in keypaste's reserved groups; recycled items answer to no name. `SetAutoTypeUnchecked` and `SetGroupAutoTypeUnchecked` are test seams, since keypaste writes no Auto-Type setting.
- **The app driver's `autotype` act** unlocks through the unlock screen and prints, for each item and title, where the sequence comes from and the sequence as the vault holds it, or why nothing would be typed, and each pattern that matched nothing; it types nothing.
- **`verify-keepassxc-workflows.sh`'s seed** gains a sequence on `servers`, Auto-Type off on `review`, an item sequence on `servers/cache`, and on `servers/database` an association whose pattern is the regular expression `//^kp4a1-regex [0-9]+//` and one whose pattern `*{TITLE} kp4a1-placeholder*` holds a placeholder. The app driver reports eight sequences for given titles as soon as KeePassXC has imported each vault, the vault's bytes unchanged, and again after the last of keypaste's saves; after every write the intact check finds each setting's marker outside history and the `review` group still off.

## Evidence

Nothing was built or run on this machine but the compile check, its restore, build and format steps run one by one because the agent's sandbox refused `verify.sh`; it passed. Each class the step adds ran in `dev.yml` at `ef8d367e`, which differs from the step's commit only in this record, each passing on its first run:

| dev run | Class | Runners | Tests |
|---|---|---|---|
| 40891021914 | `Keypaste.Core.Tests.AutoTypeSequenceTests` | `ubuntu-24.04` | 140 passed |
| 40891026862 | `Keypaste.Core.Tests.VaultAutoTypeTests` | `ubuntu-24.04` | 8 passed |
| 40891353714 | `Keypaste.Core.Tests.AutoTypeSequenceTests` | `ubuntu-24.04`, `macos-15`, `windows-2025` | 140 passed on each |
| 40891357854 | `Keypaste.Core.Tests.VaultAutoTypeTests` | `ubuntu-24.04`, `macos-15`, `windows-2025` | 8 passed on each |

What each shows:

- `AutoTypeSequenceTests`, over a plan resolved from settings and an item built in the test: KeePassXC 2.8's `testAutoTypeResults` and `testAutoTypeSyntaxChecks` sequences type or refuse as KeePassXC reads them, its long sequence refused for its bare `~` and typing every key, character and delay without it, and its errors, unbalanced braces, `{}`, a count over 100 and delays over 10,000 and 500 ms, refused; each named key, escape, numpad digit, modifier and the limits themselves type; every token the row lists and every KeePass 2 reading above refuses the whole sequence, naming the token and no value, with no step left; a field types what D-0465 fills in, nested placeholders filled and `{TAB}` inside a value typed as text, and D-0465's refusals, D-0529's and D-0531's come back in D-0465's sentence with the item pane's ending; a `{S:…}` the item lacks, a TOTP field and a braced name are refused, a field holding a line break or tab too; `{TOTP}` and `{TIMEOTP}` on a manual clock give RFC 6238's `287082` at 59 seconds and the next period's code one second later, and an item with no TOTP or one D-0498 refuses is refused; disposing the plan zeroes the seed and drops the steps; the plan's and steps' text holds no value; KeePassXC's `testGlobalAutoTypeRegExp` patterns, wildcards, `////` and `///` match as KeePassXC matches them, `//(//` is unreadable, a catastrophic expression times out, and the choice names each association that could not be filled, read or matched in time.
- `VaultAutoTypeTests`, on KDBX files keypaste writes and reopens: KeePassXC's `testAutoTypeEffectiveSequences` cases, the shortened defaults, each way a group turns an item off and the walk stopping at the first group with a sequence; KeePassXC's entries 3, 4 and 6 matched through the vault, a pattern naming a custom field D-0388 never releases named as unmatched; a recycled item and one in `.keypaste/tokens` never offered, an expired one typing marked, its `{TOTP}` giving the vault's code; fields filled through the vault and refused with D-0465's sentence and D-0531; two-channel obfuscation read and refused; and `UpdateEntry`, `SetFields` and a save keeping every setting.

The KeePassXC workflows gate runs in app's KeePassXC workflows jobs, in the batch's full run, which is pending.

## Decisions

D-0604 to D-0607; D-0465 gains Auto-Type among its exits. THREATS' Auto-Type entries and SECURITY's subsection are P.4a2's and P.4a3's, as their rows say.

## Limits and follow-ups

- **Nothing types yet.** P.4a2 builds the typing seam and the Windows engine, which presses `{CLEARFIELD}`'s keys and a code step's characters; P.4a3 macOS, P.4b1 X11, and P.4a4 the chooser that names an item's unmatched patterns.
- **Kept as the design approved, though KeePass 2 differs:** `{BREAK}` is KeePassXC's Pause where KeePass 2 sends Cancel, `{NUMPAD0}` to `{NUMPAD9}` type digits where KeePass 2 presses the keypad's keys, `{CLEARFIELD}` is KeePassXC's keys where KeePass 2 presses Home, Shift+End and Backspace, and a pattern of exactly four characters, `////`, is a regular expression as in KeePassXC where KeePass 2 needs five.
- **Stricter than KeePassXC, by the row's rules:** an item whose walk up to a sequence meets a group that says off types nothing, even through an association with its own sequence, which KeePassXC's global Auto-Type would type; a pattern's placeholders fill in under D-0465, so `{S:CustomAttrFirst}`, a field not named like an environment variable, refuses its pattern where KeePassXC fills it in; and a `{S:…}` the item lacks is refused where KeePassXC types nothing.
- **.NET's regular expressions are not PCRE2's.** A pattern .NET cannot read matches nothing and is named; where the two dialects accept the same pattern and read it differently is not catalogued, the design's open question.
- **The environment-variable refusal is broad:** any `%…%` without a brace or `=` is refused, so a KeePassXC sequence such as `%a%b`, Alt+A then Alt+B, is refused too.
- Source only.
