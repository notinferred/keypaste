#!/usr/bin/env bash
# Refreshes the vendored Public Suffix List, its tests and its licence, which a person runs before each release and
# commits like any other change (D-0615); release.yml refuses a tag whose copy is more than 60 days old (D-0616).
#
# The list comes only from publicsuffix.org, as upstream asks, and is kept only when it is the file at the commit its
# COMMIT line names with its VERSION and COMMIT lines added. Each internationalised rule, and each argument of
# test_psl.txt, becomes its A-labels: keypaste builds with InvariantGlobalization, whose IDN mapping is not current, and
# compares A-labels only. A rule's labels are already lowercase NFC, which upstream's linter requires, so each label
# that is not ASCII is RFC 3492 Punycode behind `xn--`, and a label that is not NFC refuses the update.
#
# Usage: bash scripts/update-public-suffix.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

readonly DIR=third_party/publicsuffix
readonly SERVED=https://publicsuffix.org/list/public_suffix_list.dat

die() { echo "update-public-suffix: $*" >&2; exit 1; }

for tool in curl python3; do command -v "$tool" >/dev/null 2>&1 || die "$tool is required"; done

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum < "$1" | cut -d' ' -f1
  else shasum -a 256 < "$1" | cut -d' ' -f1
  fi
}

fetch() { curl -fsSL --proto '=https' --retry 3 -o "$2" "$1" || die "could not download $1"; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fetch "$SERVED" "$work/served.dat"
version="$(sed -n 's|^// VERSION: \([0-9]\{4\}-[0-9]\{2\}-[0-9]\{2\}_[0-9]\{2\}-[0-9]\{2\}-[0-9]\{2\}_UTC\)$|\1|p' "$work/served.dat")"
commit="$(sed -n 's|^// COMMIT: \([0-9a-f]\{40\}\)$|\1|p' "$work/served.dat")"
[ "$(printf '%s\n' "$version" | grep -c .)" = 1 ] || die "$SERVED names no single VERSION"
[ "$(printf '%s\n' "$commit" | grep -c .)" = 1 ] || die "$SERVED names no single COMMIT"

raw="https://raw.githubusercontent.com/publicsuffix/list/$commit"
fetch "$raw/public_suffix_list.dat" "$work/committed.dat"
fetch "$raw/tests/test_psl.txt" "$work/test_psl.txt"
fetch "$raw/LICENSE" "$work/LICENSE"

# publicsuffix.org serves the commit's file with its VERSION and COMMIT lines and a blank line after them.
sed '/^\/\/ VERSION: /,/^$/d' "$work/served.dat" | cmp -s - "$work/committed.dat" \
  || die "the list publicsuffix.org serves is not the file at $commit with its VERSION and COMMIT lines"

python3 -I - "$work/served.dat" "$work/list.out" "$work/test_psl.txt" "$work/tests.out" <<'PY'
import re
import sys
import unicodedata


def a_label(label):
    if label.isascii():
        return label
    if not unicodedata.is_normalized("NFC", label) or label != label.lower():
        sys.exit(f"update-public-suffix: '{label}' is not a lowercase NFC label")
    converted = "xn--" + label.encode("punycode").decode("ascii")
    if len(converted) > 63:
        sys.exit(f"update-public-suffix: '{label}' is longer than a label may be")
    return converted


def a_labels(name):
    return ".".join(a_label(label) for label in name.split("."))


def convert(source, target, change):
    with open(source, encoding="utf-8", newline="") as read:
        text = read.read()
    with open(target, "w", encoding="utf-8", newline="") as write:
        write.write("".join(change(line) for line in text.splitlines(keepends=True)))


def rule(line):
    body = line.rstrip("\r\n")
    if body.startswith("//") or not body.strip():
        return line
    if any(character.isspace() for character in body):
        sys.exit(f"update-public-suffix: the rule '{body}' holds whitespace")
    exception = body.startswith("!")
    return ("!" if exception else "") + a_labels(body[1:] if exception else body) + line[len(body):]


convert(sys.argv[1], sys.argv[2], rule)
convert(sys.argv[3], sys.argv[4], lambda line: re.sub(r"'([^']*)'", lambda m: "'" + a_labels(m.group(1)) + "'", line))
PY

LC_ALL=C grep -n '^[^/].*[^ -~]' "$work/list.out" && die "a rule is still not ASCII"
LC_ALL=C grep -n '^check.*[^ -~]' "$work/tests.out" && die "a test is still not ASCII"

mkdir -p "$DIR"
cp "$work/list.out" "$DIR/public_suffix_list.dat"
cp "$work/tests.out" "$DIR/test_psl.txt"
cp "$work/LICENSE" "$DIR/LICENSE"

served_sha="$(sha256_of "$work/served.dat")"
tests_sha="$(sha256_of "$work/test_psl.txt")"
licence_sha="$(sha256_of "$work/LICENSE")"
today="$(date -u +%Y-%m-%d)"
sed -i.bak \
  -e "s#^| Version | .*#| Version | \`$version\` |#" \
  -e "s#^| Commit | .*#| Commit | \`$commit\` |#" \
  -e "s#^| Retrieved on | .*#| Retrieved on | $today |#" \
  -e "s#^| \`public_suffix_list.dat\` | \`[0-9a-f]*\` |#| \`public_suffix_list.dat\` | \`$served_sha\` |#" \
  -e "s#^| \`test_psl.txt\` | \`[0-9a-f]*\` |#| \`test_psl.txt\` | \`$tests_sha\` |#" \
  -e "s#^| \`LICENSE\` | \`[0-9a-f]*\` |#| \`LICENSE\` | \`$licence_sha\` |#" \
  "$DIR/UPSTREAM.md"
rm -f "$DIR/UPSTREAM.md.bak"

echo "public suffix list $version at $commit, SHA-256 $served_sha as served; review and commit $DIR"
