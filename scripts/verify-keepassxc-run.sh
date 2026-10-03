#!/usr/bin/env bash
#
# verify-keepassxc-run.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md §§1.4, 3.4 and law 4.6, for E.1a.
#
# KeePassXC makes the vault: entries tagged env:gate holding a usable variable, an expired one, a
# field no variable could be named and one KeePassXC then deletes into its recycle bin. `keypaste run`
# must refuse the set whole, naming the expired entry with its expiry, start no child and print no
# value. Neither the deleted entry (D-0248) nor the field is part of the set, and neither is named or
# injected. Once KeePassXC removes the expired entry, the next run injects exactly what is left.
#
# Expiry can only be written by KeePassXC, and keepassxc-cli has no option for it, so the vault is
# made by `keepassxc-cli import` from KeePass XML.
#
# Usage:  scripts/verify-keepassxc-run.sh <work-dir>
# Env:    KP_COMPAT_PASSWORD   master password for the fixture   (required)
#         KPXC_CLI             path to keepassxc-cli              (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary        (default: the Release build)

set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='RUN GATE FAILED: '

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-run.sh <work-dir>"
. "$(dirname "${BASH_SOURCE[0]}")/lib/kpxc.sh"
kp=$(keypaste_bin)

# The child is this script's own interpreter, for the reason verify-run-injection.sh gives.
child=${BASH:-/bin/bash}
[ -x "$child" ] || die "cannot locate the bash that is running this script"

rm -rf "$dir"
mkdir -p "$dir"
xml="$dir/run.xml"
db="$dir/run.kdbx"

valid='valid-value-4d2a'
expired='expired-value-4d2a'
badname='badname-value-4d2a'
recycled='recycled-value-4d2a'

# The child prints every variable the set could hold, so a value arriving where it should not is seen.
probe='printf "VALID=%s EXPIRED=%s RECYCLED=%s" "${VALID-unset}" "${EXPIRED-unset}" "${RECYCLED-unset}"'

step "KeePassXC makes the vault: one usable, one expired, one field no variable could be named, one deleted"
entry() { # uuid title field value expires expiry-time
  printf '<Entry><UUID>%s</UUID><Tags>env:gate</Tags><Times><Expires>%s</Expires><ExpiryTime>%s</ExpiryTime></Times>' "$1" "$5" "$6"
  printf '<String><Key>Title</Key><Value>%s</Value></String>' "$2"
  printf '<String><Key>%s</Key><Value ProtectInMemory="True">%s</Value></String></Entry>\n' "$3" "$4"
}
{
  printf '<?xml version="1.0" encoding="utf-8" standalone="yes"?>\n<KeePassFile><Meta><Generator>keypaste-run-gate</Generator><RecycleBinEnabled>True</RecycleBinEnabled></Meta><Root>\n'
  printf '<Group><UUID>AAAAAAAAAAAAAAAAAAAAAQ==</UUID><Name>Root</Name>\n'
  printf '<Group><UUID>AAAAAAAAAAAAAAAAAAAAAw==</UUID><Name>gate</Name>\n'
  entry AAAAAAAAAAAAAAAAAAAAEA== Valid VALID "$valid" True 2999-01-01T00:00:00Z
  entry AAAAAAAAAAAAAAAAAAAAEQ== Expired EXPIRED "$expired" True 2020-01-02T03:04:05Z
  entry AAAAAAAAAAAAAAAAAAAAEg== Odd bad-name "$badname" False 2999-01-01T00:00:00Z
  entry AAAAAAAAAAAAAAAAAAAAEw== Recycled RECYCLED "$recycled" False 2999-01-01T00:00:00Z
  printf '</Group></Group></Root></KeePassFile>\n'
} > "$xml"

printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$xml")" "$(native "$db")" >/dev/null \
  || die "keepassxc-cli could not import the fixture XML"
kpxc rm "$(native "$db")" gate/Recycled >/dev/null || die "keepassxc-cli rm failed on Recycled"

tree=$(kpxc ls -R -f "$(native "$db")") || die "keepassxc-cli cannot list the vault it made"
grep -qx 'gate/Expired' <<<"$tree" || die "KeePassXC did not keep Expired in gate. Tree: ${tree}"
grep -qx 'Recycle Bin/Recycled' <<<"$tree" || die "KeePassXC did not recycle Recycled. Tree: ${tree}"
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
grep -qF 'EXPIRED expired 2020-01-02 03:04:05Z (gate/Expired)' <<<"$err" || die "the expired entry was not named with its expiry. stderr: ${err}"
grep -qE 'bad-name|gate/Odd' <<<"$err" && die "a field no variable could be named was named, but it is not part of the set. stderr: ${err}"
grep -qE 'RECYCLED|Recycled' <<<"$err" && die "the recycled entry was named, but it is not part of the set. stderr: ${err}"
for value in "$valid" "$expired" "$badname" "$recycled"; do
  grep -qF "$value" <<<"$err$out" && die "a value was printed: ${value}"
done
echo "ok: exit 2, the expired entry named, no child, no value"

step "once KeePassXC removes the expired entry, the next run injects exactly what is left"
kpxc rm "$(native "$db")" gate/Expired >/dev/null || die "keepassxc-cli rm failed on Expired"

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
  | awk '/<Expires>/{last=$0} /<Value>Valid<\/Value>/{print last}')
[ "$valid_expires" = '<Expires>True</Expires>' ] \
  || die "VALID does not carry the future expiry its run just ignored (got '${valid_expires}')"

printf '\nRUN GATE PASSED: keypaste refuses an unusable set KeePassXC made, and injects it once repaired.\n'
