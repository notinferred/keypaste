# F.69 — Agree with KeePassXC on a `{S:…}` whose name holds braces

Completed 2026-10-24 on `t6` in `2f1c2c31`, source only. At `803e9cb3`, app 40187228211 passed every job on all three runners on its first attempt, and ci 40187220469 on its second: its first failed one job, test (windows-2025), in `LockedAppEndpointTests`, which F.70 holds, and only that job was rerun.

## Amendments

The row left open whether keypaste reads such a `{S:…}` as KeePassXC does or refuses it. On 2026-10-24 the founder chose KeePassXC's reading, keeping D-0465's refusal of a field the item lacks where KeePassXC gives an empty value, and everything else in D-0465 and D-0529 (D-0531).

## Diagnosis

- **KeePassXC.** From 2.7.10 on, `Entry::resolveMultiplePlaceholdersRecursive`, through `EntryPlaceholders::placeholderMatches` in 2.8.0, matches `({(?>[^{}]+?|(?1))+?})`: from an opening brace to its partner when every pair between them is balanced and none is empty, going on after the partner, or after the opening brace when it does not match. In 2.7.11 and 2.8.0 alike, `placeholderType` reads a match that starts `{S:`, in capitals only, as a custom attribute before anything else, 2.8's #13688 included, and `resolvePlaceholderRecursive` takes all of its inside after `S:` as the attribute's name, giving its value or nothing. So both read `{S:a{PASSWORD}b}` as the field `a{PASSWORD}b` and `{S:{S:x}}` as the field `{S:x}`, and `{S:a}b}` as the field `a` followed by `b}`. A `{S:` the pattern does not match from, as in `{S:a{b}`, `{S:a{PASSWORD}` or `{S:a{}{PASSWORD}b}`, is text, and matching goes on from the next brace, so the last two fill in the password. 2.7.0 to 2.7.9, Ubuntu 24.04's 2.7.6 among them, match from a brace to the next closing one instead.
- **keypaste.** `KeePassPlaceholders` paired each brace with the next one and restarted at an inner opening brace, so `{S:a{PASSWORD}b}` gave `{S:a`, the password and `b}`, and `{S:{S:x}}` filled in `x`. D-0529's check already matched braces as KeePassXC does and left a `{S:` alone.

## What changed

- `KeePassPlaceholders` finds once per value where KeePassXC's pattern matches each brace (`MatchedBraces`), and the scan, D-0529's check and an escape inside braces all read it. A `{S:` the pattern matches from is one placeholder naming its whole inside, so nothing in the name fills in; any other `{S:` is scanned as before. Every fill, the placeholders a terminal edit or a tag change plants and the fields a rename leaves naming nothing go through that scan (D-0465).
- keypaste releases a custom field only when its name is shaped like an environment variable (D-0388), so a braced name is never released and a value holding one is refused: "holds the KeePass placeholder {S:…}, which names no field of its item", or, when the item has the field, under the name the sanitizer shows, "{S:a PASSWORD b}, which names a field keypaste never releases".
- Inside braces that name no placeholder, as in `{x{S:a{b}c}y}`, which 2.7.11 fills in and 2.8 leaves as written, such a `{S:…}` is now a placeholder and refuses the value under D-0529; keypaste had found nothing there.

## Evidence

The batch's ci and app runs are pending. They are to show:

- `Keypaste.Core.Tests.KeePassPlaceholdersTests`: five values whose `{S:…}` name holds braces refuse as naming no field, plant nothing and do not name `API_KEY`; a braced field the item has is named and never released; four `{S:` values come out where KeePassXC's pattern ends them; and `{s:a{PASSWORD}b}` and `{x{S:a{b}c}y}` join D-0529's refusals.
- `scripts/verify-keepassxc-projects.sh`, in ci's `compat` jobs: `keypaste run` refuses `{S:a{PASSWORD}b}` with the field and without it, which keepassxc-cli from 2.7.11 on shows as the field's value and as nothing, and `{S:UNPAIRED_KEY}b}`, `{S:a{PASSWORD}` and `{S:a{}{PASSWORD}b}` reach the child as keepassxc-cli from 2.7.11 on shows them. keepassxc-cli 2.7.6 on Ubuntu is shown and not compared.

## Decisions

D-0531; D-0529 points to it.

## Limits and follow-ups

- No runner has KeePassXC 2.7.11, so its reading rests on its source; the gate compares keypaste with 2.8.0 on Windows and macOS.
- KeePassXC reads `{S:` in capitals only, while keypaste, as KeePass 2 does, fills in a `{s:…}` whose name holds no brace, which KeePassXC leaves as written. That predates this step.
- Source only.
