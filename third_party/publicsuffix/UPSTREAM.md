# Vendored Public Suffix List provenance

| Field | Value |
|---|---|
| Upstream file | <https://publicsuffix.org/list/public_suffix_list.dat>, the one place upstream asks the list be pulled from |
| Upstream repository | [publicsuffix/list](https://github.com/publicsuffix/list) |
| Version | `2026-10-28_13-15-55_UTC` |
| Commit | `9dfbe81931367b3f0af21035c36ae7eeb830dd88` |
| Retrieved on | 2026-10-29 |
| Original work | Mozilla Foundation and the Public Suffix List's contributors |
| Licence | MPL-2.0 for the list, `LICENSE` being upstream's text; `test_psl.txt` dedicated to the public domain under CC0 1.0, as its header says |

The list names the suffixes under which anyone may register a name, `com` and `co.uk` in its ICANN section and hosting platforms such as `github.io` in its private section, so a host's registrable domain is its public suffix and one more label. keypaste's browser fill matches a page to an item within one registrable domain, and `Keypaste.Core.Browser.PublicSuffixList` takes it from this file, embedded in `Keypaste.Core`, both sections read, with no package and no network ([DECISIONS.md](../../DECISIONS.md) D-0615, which holds PRODUCT §3.9's justification).

## Files

[update-public-suffix.sh](../../scripts/update-public-suffix.sh) writes all three. It downloads the list from publicsuffix.org and keeps it only when it is the file at the commit its `COMMIT` line names with its `VERSION` and `COMMIT` lines added, and takes the tests and the licence from that commit.

| File | Upstream SHA-256 | Change |
|---|---|---|
| `public_suffix_list.dat` | `1f96bae40adfc22ca2474d7c0099bbcfcb25aefd3e2547b5c2147c0dd2e3ebd9` | each rule that is not ASCII written as its A-labels |
| `test_psl.txt` | `8f50ad958916d6a8f79fba2363501475571acce752757f9126fe9d2f17dd920d` | each argument that is not ASCII written as its A-labels |
| `LICENSE` | `66a3107d5ad6a058aab753eaac2047ccb2ed0e39465dd0fe5844da3e300d5172` | none |

The list writes an internationalised rule in Unicode and is compared in Punycode. keypaste builds with `InvariantGlobalization`, under which .NET's IDN mapping does not follow the current standard, and compares A-labels only, so each such label is written as RFC 3492 Punycode behind `xn--` when the list is vendored. Upstream's linter holds every rule to lowercase NFC, which the script checks, so that is the label's A-label; nothing else in either file changes, comments included.

## Licence

MPL-2.0 is a file-level licence. The vendored list, with its change, remains under MPL-2.0, and its source form is this directory, published with keypaste's source; MPL-2.0 §3.3 permits distributing it within this AGPL-3.0-only work. Attribution is given in [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md). The list is data, not code. `test_psl.txt` is read only by the tests and no shipped binary contains it.

## What is asserted

`Keypaste.Core.Tests.Browser.PublicSuffixListTests` holds the embedded list to every case of `test_psl.txt`, so the upstream cases written in Unicode, converted here, give what upstream's own Punycode cases give, and to a rule of each section. `LoginLookupTests` holds the matcher's rules over it.

## Updating

1. Run `bash scripts/update-public-suffix.sh`, which needs `curl` and `python3`, before each release.
2. Review the diff of the three files and this one, and commit it as an ordinary change.

[require-public-suffix-fresh.sh](../../scripts/require-public-suffix-fresh.sh) refuses a release whose list's `VERSION` is more than 60 days old (D-0616).
