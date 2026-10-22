# N.13 — Search and show fields from the terminal

Completed 2026-10-19 at `a2f6583` on `task/N.13`, source only: ci 39247881860 and app 39247893957. The integrated commit differs from that tree only in this record.

## Amendments

The row said only that `search` looks at names (D-0278) and that `show` masks values and prints none without `--reveal`. The founder selected it on 2026-10-19 with the removal of T1 from STEPS, which it left with no open task, and approved a plan that settled the rest:

- `search` is `Vault.Search` as the app uses it: title, group path, username and URL, never a password, a note or a custom field. A row is the item's path, followed by the app's words for a username or URL match. Empty text is a usage error, and no match is success with `[]`.
- `show` lists an item's tags, the standard fields that hold something and every custom field with its protection. Every value is masked, the username and URL included, until `--reveal`. Revealed text passes through `DisplayTextSanitizer`, and `--json --reveal` carries the values as the vault holds them (D-0463).
- N.13 moves from ROADMAP's reach milestone to 0.5.0, which is where it ships, by the founder's direction that this is 0.5.0 work. T1 stays a track in PRODUCT and in ROADMAP's order.

## Evidence

- [SearchVerbTests](../../tests/Keypaste.Cli.Tests/SearchVerbTests.cs), 11 cases:
  - a title match, and a username, URL and group match;
  - no match on a password, a note or a custom field;
  - a recycled item and keypaste's own groups are not found;
  - the JSON shape;
  - no match is `[]` and never echoes the query;
  - empty text and two unquoted words are refused before the vault opens.
- [ShowVerbTests](../../tests/Keypaste.Cli.Tests/ShowVerbTests.cs), 9 cases:
  - every value masked, and the whole text pinned;
  - `--reveal`, with a notes line indented under the first;
  - an empty standard field left out;
  - JSON without values and with them;
  - a bidi override in a value, cleaned and told, and raw in `--json --reveal`;
  - KeePassXC's `otp`, read-only and revealed;
  - a missing item, and no item name.
- Changes to existing tests:
  - `SecretHygieneTests` sweeps `search` by title, by URL and as JSON, and `show` as text and JSON, for all six sentinels.
  - `HostileNameRenderingTests` gained a spoofed title for `search`, and a spoofed title and field name for `show`.
  - `KeyfileOptionTests` gained both verbs.
  - `CliAppTests` pins the two help lines and 22 verbs, and `PlainWordsTests` sweeps their help.
- The Linux iteration (dev run 39246816638) failed one case. `Json_with_reveal_carries_each_value_as_the_vault_holds_it` expected the CRLF its fixture wrote, and the reopened vault held LF. The output was right; the fixture now writes LF. keypaste's own save and reopen does not keep a note's CR.
- ci 39247881860 and app 39247893957 passed at `a2f6583`.

## Decisions

D-0463 in [DECISIONS](../../DECISIONS.md).

## Limits and follow-ups

- Search matches an item's own tags by substring, and `search` names the tag it matched (gaps-1, RUNS-PENDING).
- A custom field KeePassXC made under a standard name in another case, such as `password`, stays masked under `--reveal`, because core reads none (V.7a).
- `show` resolves a path as `get` does, so it reaches an item in keypaste's own groups when named.
- `get` keeps `--show` beside `--reveal` until N.9.
- Source only. FEATURES gains the verbs through L.1.
