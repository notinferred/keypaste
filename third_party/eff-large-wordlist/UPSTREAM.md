# Vendored EFF long word list provenance

| Field | Value |
|---|---|
| Upstream file | <https://www.eff.org/files/2016/07/18/eff_large_wordlist.txt> |
| Upstream page | [EFF Large Wordlist for Passphrases](https://www.eff.org/document/passphrase-wordlists) |
| Announcement | [Deep Dive: EFF's New Wordlists for Random Passphrases](https://www.eff.org/deeplinks/2016/07/new-wordlists-random-passphrases) |
| Published | 2016-07-18 |
| Retrieved on | 2026-09-18 |
| Original work | Electronic Frontier Foundation; list compiled by Joseph Bonneau |
| Licence | CC-BY-4.0; see `LICENSE` |
| Size | 7,776 lines, 108,800 bytes |
| Line format | five dice digits, a tab, then the word, terminated by `\n` |

The file is vendored verbatim, with no local modifications. Only the word column is read;
`Keypaste.Core.WordList` splits each line on its tab and ignores the dice digits, which are
upstream's 6^5 enumeration in canonical order. Vendoring the word column alone was rejected:
the digest would then pin keypaste's transformation rather than EFF's bytes, and a re-merge
would diff against a file EFF never published.

`.gitattributes` marks this directory `-text` so the bytes survive a checkout on every platform.
A normalised line ending would change the digest and fail the check below on Windows only.

## The digest

**The SHA-256 pin lives in exactly one place, `Keypaste.Core.WordList.Digest`, and is not
restated here.** `WordList` hashes the embedded resource at first use and throws when it does
not match, so a shipped binary that has no repository to read still refuses a list it cannot
vouch for (D-0235). A pin written twice is a pin that can disagree with itself (D-0203, D-0207).

## Licence

EFF's [copyright policy](https://www.eff.org/copyright), retrieved 2026-09-18, states that any
and all original material on the EFF website may be freely distributed under the Creative
Commons Attribution 4.0 International License unless otherwise noted; neither the wordlist page
nor the announcement notes otherwise. The site footer also carries a legacy CC BY 3.0 US link;
the policy text is the authority, and CC-BY-4.0 is the more permissive reading for a
redistributor. Attribution is given in [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md).

The list is data, not code. keypaste's combined distribution remains AGPL-3.0-only.

## What the file actually proves

Asserted by [WordListTests](../../tests/Keypaste.Core.Tests/WordListTests.cs) against the file
on disk rather than against the shipped resource:

- 7,776 lines, all distinct words
- each word is 3 to 9 characters drawn from `a`-`z` and `-`
- four words carry an internal hyphen: `drop-down`, `felt-tip`, `t-shirt`, `yo-yo`

That last one is why the separator rule is derived from the list instead of written down: `-`
would make a six-word passphrase unsplittable, so any character the list itself contains is
refused as a separator (D-0239). Nothing else is claimed of the list. EFF documents
prefix-freedom for its *short* list #2, not for this one, and keypaste does not need it, a
separator always sitting between words.

## Re-vendoring a newer list

1. Download the upstream file again and diff it against this one.
2. Replace it verbatim, and update `Size` and `Retrieved on` above.
3. Update `Keypaste.Core.WordList.Digest` and, if the line count moved, `Count`.
4. Run `dotnet test tests/Keypaste.Core.Tests/Keypaste.Core.Tests.csproj -- --filter-class Keypaste.Core.Tests.WordListTests`.
   A changed size moves `WordList.BitsPerWord`, which is derived, so every stated entropy figure
   follows automatically. A word containing a new character narrows the separator rule.
