# Vendored common-password list provenance

| Field | Value |
|---|---|
| Upstream file | <https://github.com/danielmiessler/SecLists/blob/913b327317496d062bcc7cace524aaad8a693be2/Passwords/Common-Credentials/10k-most-common.txt> |
| Upstream repository | [danielmiessler/SecLists](https://github.com/danielmiessler/SecLists) |
| Commit | `913b327317496d062bcc7cace524aaad8a693be2`, 2026-09-08, the file's latest change when retrieved |
| Retrieved on | 2026-10-21 |
| Original work | Daniel Miessler and the SecLists contributors |
| Licence | MIT; see `LICENSE`, retrieved from the same commit |
| Upstream size | 10,001 lines, 73,026 bytes, `\n` line endings, printable ASCII |
| Vendored file | `10k-most-common.txt.gz`, 37,879 bytes |

The file is the 10,000 most common passwords SecLists lists, with the one line its latest commit added. It is vendored gzipped, not modified: `10k-most-common.txt.gz` is upstream's bytes compressed at gzip level 9 with no file name and a zero timestamp, so it decompresses to upstream's file byte for byte, and anyone can rebuild it from the commit above. It is compressed because it ships inside every keypaste binary.

## The digest

**The SHA-256 pin of the decompressed file lives in exactly one place, `Keypaste.Core.Recommendations.CommonPasswords.Digest`, and is not restated here.** `CommonPasswords` hashes what the embedded resource decompresses to the first time a password is rated and throws when it does not match, so a shipped binary rates against no list it cannot vouch for (D-NEW-C5), as the word list is pinned (D-0235).

## Licence

SecLists' `LICENSE` at the commit above is the MIT License, copyright (c) 2018 Daniel Miessler. Attribution is given in [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md). The list is data, not code; keypaste's combined distribution remains AGPL-3.0-only.

## What the file is asserted to hold

[CommonPasswordsTests](../../tests/Keypaste.Core.Tests/CommonPasswordsTests.cs) reads this file, not the shipped resource: it decompresses to the pinned digest, and holds `CommonPasswords.Count` distinct lines. KeePassLib's `PopularPasswords` lowercases each line and keeps it by length; `PasswordHealthCheckTests` shows a password on the list rating weak.

## Re-vendoring a newer list

1. Download the upstream file at a newer commit and diff it against what this file decompresses to.
2. Compress it as above, replace `10k-most-common.txt.gz`, and update the commit, dates and sizes here and in THIRD_PARTY_NOTICES.md.
3. Update `CommonPasswords.Digest` and, if the line count moved, `CommonPasswords.Count`.
