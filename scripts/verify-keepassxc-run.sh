#!/usr/bin/env bash
#
# verify-keepassxc-run.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md §§1.4, 3.4 and law 4.6, for E.1a.
#
# KeePassXC makes the vault: an env set holding a usable entry, an expired one, one whose name
# cannot be exported and one KeePassXC then deletes into its recycle bin. `keypaste run` must refuse
# the set whole, naming the expired entry and the bad name with their reasons, start no child and
# print no value. The deleted entry is not part of the set (D-0248) and is never named or injected.
# Once KeePassXC removes the two unusable entries, the next run injects exactly what is left.
#
# Expiry can only be written by KeePassXC, and keepassxc-cli has no option for it, so the vault is
# made by `keepassxc-cli import` from KeePass XML.
#
# Usage:  scripts/verify-keepassxc-run.sh <work-dir>
# Env:    KP_COMPAT_PASSWORD   master password for the fixture   (required)
#         KPXC_CLI             path to keepassxc-cli              (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary        (default: the Release build)

set -euo pipefail

die()  { printf '\nRUN GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-run.sh <work-dir>"
pw=${KP_COMPAT_PASSWORD:-}
[ -n "$pw" ] || die "KP_COMPAT_PASSWORD is not set"
cli=${KPXC_CLI:-keepassxc-cli}

# Absence of the tool is a FAILURE, never a skip — see verify-keepassxc-compat.sh (i).
if ! command -v "$cli" >/dev/null 2>&1 && [ ! -x "$cli" ]; then
  die "keepassxc-cli not found (KPXC_CLI='${cli}'). This gate must never be skipped or soft-passed."
fi

kp=${KEYPASTE_BIN:-}
if [ -z "$kp" ]; then
  kp=artifacts/bin/Keypaste.Cli/release/keypaste
  [ -x "$kp" ] || kp="${kp}.exe"
fi
[ -x "$kp" ] || die "keypaste binary not found at '$kp' (build it, or set KEYPASTE_BIN)"

# The child is this script's own interpreter, for the reason verify-run-injection.sh gives.
child=${BASH:-/bin/bash}
[ -x "$child" ] || die "cannot locate the bash that is running this script"

native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

rm -rf "$dir"
mkdir -p "$dir"
xml="$dir/run.xml"
db="$dir/run.kdbx"

valid='valid-value-4d2a'
expired='expired-value-4d2a'
badname='badname-value-4d2a'
recycled='recycled-value-4d2a'

kpxc() { printf '%s\n' "$pw" | "$cli" "$@" | tr -d '\r'; }

# The child prints every variable the set could hold, so a value arriving where it should not is seen.
probe='printf "VALID=%s EXPIRED=%s RECYCLED=%s" "${VALID-unset}" "${EXPIRED-unset}" "${RECYCLED-unset}"'

step "KeePassXC makes the vault: one usable, one expired, one bad name, one deleted"
entry() { # uuid title value expires expiry-time
  printf '<Entry><UUID>%s</UUID><Times><Expires>%s</Expires><ExpiryTime>%s</ExpiryTime></Times>' "$1" "$4" "$5"
  printf '<String><Key>Title</Key><Value>%s</Value></String>' "$2"
  printf '<String><Key>Password</Key><Value ProtectInMemory="True">%s</Value></String></Entry>\n' "$3"
}
{
  printf '<?xml version="1.0" encoding="utf-8" standalone="yes"?>\n<KeePassFile><Meta><Generator>keypaste-run-gate</Generator><RecycleBinEnabled>True</RecycleBinEnabled></Meta><Root>\n'
  printf '<Group><UUID>AAAAAAAAAAAAAAAAAAAAAQ==</UUID><Name>Root</Name>\n'
  printf '<Group><UUID>AAAAAAAAAAAAAAAAAAAAAg==</UUID><Name>env</Name>\n'
  printf '<Group><UUID>AAAAAAAAAAAAAAAAAAAAAw==</UUID><Name>gate</Name>\n'
  entry AAAAAAAAAAAAAAAAAAAAEA== VALID "$valid" True 2999-01-01T00:00:00Z
  entry AAAAAAAAAAAAAAAAAAAAEQ== EXPIRED "$expired" True 2020-01-02T03:04:05Z
  entry AAAAAAAAAAAAAAAAAAAAEg== BAD-NAME "$badname" False 2999-01-01T00:00:00Z
  entry AAAAAAAAAAAAAAAAAAAAEw== RECYCLED "$recycled" False 2999-01-01T00:00:00Z
  printf '</Group></Group></Group></Root></KeePassFile>\n'
} > "$xml"

printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$xml")" "$(native "$db")" >/dev/null \
  || die "keepassxc-cli could not import the fixture XML"
kpxc rm "$(native "$db")" env/gate/RECYCLED >/dev/null || die "keepassxc-cli rm failed on RECYCLED"

tree=$(kpxc ls -R -f "$(native "$db")") || die "keepassxc-cli cannot list the vault it made"
grep -qx 'env/gate/EXPIRED' <<<"$tree" || die "KeePassXC did not keep EXPIRED in env/gate. Tree: ${tree}"
grep -qx 'Recycle Bin/RECYCLED' <<<"$tree" || die "KeePassXC did not recycle RECYCLED. Tree: ${tree}"
exported=$(kpxc export -f xml "$(native "$db")") || die "keepassxc-cli cannot export the vault it made"
# Matched whole: grep -q on the end of a pipe exits at the first match, and pipefail then fails on the writer it cut off.
grep -q '<Expires>True</Expires>' <<<"$exported" || die "the vault KeePassXC made carries no expiry"

step "keypaste run refuses the set whole, naming each entry and why, and starts nothing"
set +e
out=$(printf '%s\n' "$pw" | "$kp" run gate --vault "$db" -- "$child" -c "$probe" 2>"$dir/err")
status=$?
set -e
err=$(tr -d '\r' < "$dir/err")
out=$(tr -d '\r' <<<"$out")

[ "$status" = "2" ] || die "expected exit 2 for an unusable set, got ${status}. stderr: ${err}"
[ -z "$out" ] || die "a child started, or keypaste printed to stdout: ${out}"
grep -qF 'EXPIRED expired 2020-01-02 03:04:05Z' <<<"$err" || die "the expired entry was not named with its expiry. stderr: ${err}"
grep -qF 'BAD-NAME is not a valid environment variable name' <<<"$err" || die "the bad name was not named with its reason. stderr: ${err}"
grep -qF 'RECYCLED' <<<"$err" && die "the recycled entry was named, but it is not part of the set. stderr: ${err}"
for value in "$valid" "$expired" "$badname" "$recycled"; do
  grep -qF "$value" <<<"$err$out" && die "a value was printed: ${value}"
done
echo "ok: exit 2, both unusable entries named, no child, no value"

step "once KeePassXC removes them, the next run injects exactly what is left"
kpxc rm "$(native "$db")" env/gate/EXPIRED >/dev/null || die "keepassxc-cli rm failed on EXPIRED"
kpxc rm "$(native "$db")" env/gate/BAD-NAME >/dev/null || die "keepassxc-cli rm failed on BAD-NAME"

out=$(printf '%s\n' "$pw" | "$kp" run gate --vault "$db" -- "$child" -c "$probe" | tr -d '\r') \
  || die "keypaste run failed on the repaired set"
expected="VALID=${valid} EXPIRED=unset RECYCLED=unset"
[ "$out" = "$expected" ] || die "expected '${expected}', the child saw '${out}'"
echo "ok: the child saw VALID only"

# ---------------------------------------------------------------------------------------
# NEGATIVE CONTROL: the comparison must be able to fail, and an expiry in the future must not
# refuse, or a gate that refused everything would pass the first half.
# ---------------------------------------------------------------------------------------
step "NEGATIVE CONTROL: the comparisons must be able to fail"
[ "$out" = "VALID=${valid}-CORRUPTED EXPIRED=unset RECYCLED=unset" ] \
  && die "a deliberately corrupted expectation still matched — this gate is not gating"
valid_expires=$(kpxc export -f xml "$(native "$db")" | tr -d '\t' \
  | awk '/<Expires>/{last=$0} /<Value>VALID<\/Value>/{print last}')
[ "$valid_expires" = '<Expires>True</Expires>' ] \
  || die "VALID does not carry the future expiry its run just ignored (got '${valid_expires}')"

printf '\nRUN GATE PASSED: keypaste refuses an unusable set KeePassXC made, and injects it once repaired.\n'
