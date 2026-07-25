#!/usr/bin/env bash
#
# verify-keepassxc-compat.sh
#
# PERMANENT COMPATIBILITY GATE — CORE.md law 4.6:
#   "Compatibility is sacred: any KDBX file keypaste writes must open correctly in
#    KeePassXC. This is tested in CI against real KeePassXC."
#
# CORE.md cannot change, so neither can the existence of this gate. If it is failing,
# keypaste's vault writer is wrong. Do NOT relax an assertion, add a skip, mark the job
# continue-on-error, or drop an operating system to make it green. DECISIONS.md D-0008
# records why, and what the three non-negotiable properties are.
#
# Usage:  scripts/verify-keepassxc-compat.sh <vault.kdbx>
# Env:    KP_COMPAT_PASSWORD  master password of the fixture   (required)
#         KPXC_CLI            path to keepassxc-cli            (default: PATH lookup)
#
# Every expected value below is duplicated from tools/Keypaste.CompatFixture. That
# duplication is the change detector: expectations generated from the writer under test
# would agree with it forever and assert nothing.

set -euo pipefail

die()  { printf '\nCOMPAT GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

db=${1:-}
[ -n "$db" ] || die "usage: verify-keepassxc-compat.sh <vault.kdbx>"
pw=${KP_COMPAT_PASSWORD:-}
[ -n "$pw" ] || die "KP_COMPAT_PASSWORD is not set"
cli=${KPXC_CLI:-keepassxc-cli}

# (i) Absence of the tool is a FAILURE, never a skip.
#
# A gate that quietly no-ops when keepassxc-cli is missing is worse than no gate at all:
# it reports green forever. No path through this script exits 0 without having actually
# talked to KeePassXC.
if ! command -v "$cli" >/dev/null 2>&1 && [ ! -x "$cli" ]; then
  die "keepassxc-cli not found (KPXC_CLI='${cli}'). This gate must never be skipped or soft-passed."
fi
[ -f "$db" ] || die "vault not found: $db"

# keepassxc-cli reads exactly ONE line per prompt from stdin, with no isatty check and no
# retry, and writes the prompt to stderr — so stdout stays clean and piping is the supported
# non-interactive path. There is no --password-file and no KEEPASSXC_PASSWORD env var.
#
# The password is piped rather than passed as an argument on purpose: argv is world-readable
# via /proc on Linux.
#
# `tr -d '\r'` is not cosmetic. Qt writes CRLF on Windows; without stripping it, every
# exact-value diff below fails on windows-latest and nowhere else.
#
# -q/--quiet is deliberately NOT passed: it suppresses the failure reason along with the
# prompt, and stdout is already clean without it.
kpxc() {
  printf '%s\n' "$pw" | "$cli" "$@" | tr -d '\r'
}

step "keepassxc-cli under test"
"$cli" --version

# (ii-a) On-disk format, independent of any tool.
#
# The two KDBX signatures are little-endian uint32 values 0x9AA2D903 and 0xB54BFB67, so
# ON DISK the bytes read 03 d9 a2 9a 67 fb 4b b5 — byte-swapped from how the constants are
# written in the spec and in KdbxFormat.cs. They are followed by <minor uint16 LE> and
# <major uint16 LE>, giving 00 00 04 00 for KDBX 4.0.
#
# A silent downgrade to KDBX 3.1 would still round-trip and still open in KeePassXC — but
# 3.1 cannot carry Argon2, so it would quietly weaken every vault keypaste writes. Only a
# direct look at the bytes catches that.
#
# od(1) rather than xxd: Git for Windows ships od, not xxd.
step "container header (direct byte check)"
hdr=$(od -An -v -tx1 -N12 "$db" | tr -d ' \n' | tr 'A-Z' 'a-z')
printf 'first 12 bytes: %s\n' "$hdr"
[ "${#hdr}" -eq 24 ] || die "could not read 12 header bytes from $db"
[ "${hdr:0:16}" = "03d9a29a67fb4bb5" ] \
  || die "not a KDBX file — signature is ${hdr:0:16}, expected 03d9a29a67fb4bb5"
[ "${hdr:20:2}" = "04" ] \
  || die "expected KDBX 4.x — on-disk major version byte is 0x${hdr:20:2}"
printf 'KDBX major version 4 confirmed (minor byte 0x%s)\n' "${hdr:16:2}"

# (ii-b) KeePassXC's own view of the format.
#
# Argon2 is only representable in KDBX 4.x, so "KDF: Argon2*" is KeePassXC independently
# agreeing this is a real KDBX4 file.
#
# Matched by regex rather than exact string: the parenthetical parameters are formatted
# differently across KeePassXC versions ("Argon2d (2 rounds, 65536 KB)" on 2.7.10), and
# pinning that text would make the gate brittle against exactly the version drift it exists
# to survive. Herestrings, never pipes, so `set -o pipefail` plus grep's early exit cannot
# turn a successful match into a failure.
step "db-info (KeePassXC's view)"
info=$(kpxc db-info "$db") || die "keepassxc-cli db-info failed — the vault did not open at all"
printf '%s\n' "$info"
grep -Eqi '^[[:space:]]*KDF:[[:space:]]*Argon2' <<<"$info" \
  || die "KDF is not Argon2 — KDBX4 downgrade or AES-KDF fallback. Got: $(grep -i '^[[:space:]]*KDF:' <<<"$info" || echo '<no KDF line>')"
grep -Eqi '^[[:space:]]*Cipher:[[:space:]]*AES[-[:space:]]*256' <<<"$info" \
  || die "unexpected cipher. Got: $(grep -i '^[[:space:]]*Cipher:' <<<"$info" || echo '<no Cipher line>')"
echo "KDF is Argon2 and cipher is AES-256 — confirmed KDBX4."

# (iii) Structure. Groups come back with a trailing slash; the root group is not listed.
# Sorted on both sides: KeePassXC's child-iteration order is an implementation detail, and
# asserting it would turn an upstream refactor into a false compatibility failure. Missing
# or extra nodes are still caught exactly.
step "structure: ls -R -f"
actual_ls=$(kpxc ls -R -f "$db") || die "keepassxc-cli ls failed"
printf '%s\n' "$actual_ls"
actual_ls=$(printf '%s\n' "$actual_ls" | sed 's/[[:space:]]*$//' | sed '/^$/d' | LC_ALL=C sort)
expected_ls=$(printf '%s\n' \
  'compat/' \
  'compat/ascii' \
  'compat/nested/' \
  'compat/nested/deep' \
  'compat/unicode' \
  | LC_ALL=C sort)
diff -u <(printf '%s\n' "$expected_ls") <(printf '%s\n' "$actual_ls") \
  || die "vault structure does not match (left = expected, right = what KeePassXC read)"

# (iv) Field integrity, exact.
#
# With explicit repeated -a, keepassxc-cli prints BARE values, one per line, in the order
# requested, with no "Name:" labels — and protected attributes are shown in cleartext
# without needing -s. If Password ever comes back as the literal string PROTECTED, or as an
# empty line, this diff is what catches it.
step "field integrity: compat/ascii"
actual_fields=$(kpxc show -a Title -a UserName -a Password -a URL "$db" 'compat/ascii') \
  || die "keepassxc-cli show failed for compat/ascii"
expected_fields=$(printf '%s\n' \
  'ascii' \
  'compat-user' \
  'ascii-only-P@ssw0rd' \
  'https://example.invalid/keypaste')
diff -u <(printf '%s\n' "$expected_fields") <(printf '%s\n' "$actual_fields") \
  || die "field values do not survive the round trip through KeePassXC"

# Notes is queried on its own: a multi-line value inside a multi-attribute `show` would
# destroy the one-value-per-line alignment of every attribute after it.
step "field integrity: multi-line Notes"
actual_notes=$(kpxc show -a Notes "$db" 'compat/ascii') || die "keepassxc-cli show -a Notes failed"
expected_notes=$(printf '%s\n' \
  'first notes line' \
  'second line: , ; = " '"'"' punctuation')
diff -u <(printf '%s\n' "$expected_notes") <(printf '%s\n' "$actual_notes") \
  || die "multi-line Notes did not round-trip"

step "field integrity: nested group entry"
actual_deep=$(kpxc show -a Title -a UserName -a Password "$db" 'compat/nested/deep') \
  || die "keepassxc-cli show failed for compat/nested/deep"
expected_deep=$(printf '%s\n' 'deep' 'deep-user' 'deep-pass')
diff -u <(printf '%s\n' "$expected_deep") <(printf '%s\n' "$actual_deep") \
  || die "entry in a nested group did not round-trip"

# UTF-8 is asserted on every platform, including Windows. keepassxc-cli forces the Windows
# console code page to the system ANSI page, which can transcode non-ASCII on the way out —
# but under `shell: bash` with printf, this was verified to round-trip byte-for-byte. If it
# ever fails on windows-latest only, that is a harness property and not a defect in our
# file: narrow this one assertion to non-Windows and record it in DECISIONS.md rather than
# weakening anything above.
step "field integrity: unicode"
actual_uni=$(kpxc show -a Title -a UserName -a Password "$db" 'compat/unicode') \
  || die "keepassxc-cli show failed for compat/unicode"
expected_uni=$(printf '%s\n' 'unicode' 'ünïcode-user' 'pässwörd-ünïcode')
diff -u <(printf '%s\n' "$expected_uni") <(printf '%s\n' "$actual_uni") \
  || die "UTF-8 values did not survive the round trip"

# (v) NEGATIVE CONTROL.
#
# Everything above only means something if this gate is still capable of failing. Without
# this step, a keepassxc-cli that silently degraded to a no-op — or an assertion that ended
# up comparing two empty strings — would report green forever. That, not deletion, is the
# most likely way CORE.md law 4.6 actually dies. This is the cheapest insurance in the
# repository. Never remove it.
step "NEGATIVE CONTROL: a wrong password must be rejected"
set +e
wrong_out=$(printf '%s\n' "${pw}-DELIBERATELY-WRONG" | "$cli" ls -R -f "$db" 2>&1 | tr -d '\r')
wrong_rc=$?
set -e
printf '%s\n' "$wrong_out"
[ "$wrong_rc" -ne 0 ] \
  || die "keepassxc-cli exited 0 with a WRONG password. This gate is not testing anything — investigate before trusting any result above."
case "$wrong_out" in
  *'compat/ascii'*) die "a wrong password still produced entry names — the gate is not gating";;
esac
printf 'wrong password rejected (exit %s, no entry names emitted)\n' "$wrong_rc"

printf '\nCOMPAT GATE PASSED — %s opens correctly in KeePassXC %s\n' \
  "$db" "$("$cli" --version)"
