#!/usr/bin/env bash
#
# verify-keepassxc-backup.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md law 4.6, the damaged-file half of law 5.7.
#
# verify-keepassxc-compat.sh proves a vault keypaste CREATES opens in KeePassXC,
# verify-keepassxc-writeback.sh proves a vault keypaste MODIFIES still does,
# verify-keepassxc-history.sh proves a revision keypaste RESTORES is the one KeePassXC reads, and
# verify-keepassxc-recyclebin.sh proves a DELETED entry is recoverable. This one covers what
# docs/STEPS.md V.4a added: before replacing a vault, keypaste KEEPS the bytes it is replacing,
# and what it keeps is a vault KeePassXC opens and reads the previous values out of.
#
# What this gate exists to catch, and what a directory listing cannot:
#
#   * A backup that is the RIGHT SIZE and no use. The copy is asserted by opening it in real
#     KeePassXC and reading the value that was current before the save, not by its presence.
#
#   * A reader floor that does not travel. A vault that has recycled anything is KDBX 4.1
#     (D-0247), and a copy of it is too. The header bytes of BOTH files are read here, because
#     nothing else would notice a backup that silently stopped being the file it was copied from.
#
#   * Retention eating the evidence. The oldest goes and the newest stays, and the count is the
#     one the shipped binary believes in.
#
# Backups are ordinary KDBX files, so this gate needs NO driver: `keepassxc-cli` opens one
# directly and so does `keypaste --vault`. That is the whole claim of the design — the bytes are
# copied, never re-serialised — and a gate that needed a special reader would be evidence against
# it.
#
# Usage:  scripts/verify-keepassxc-backup.sh <backup.kdbx>
# Env:    KP_COMPAT_PASSWORD   master password for the fixture       (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#
# The seeding AND every save are done by the SHIPPED binary (D-0012): `keypaste env set` is what
# replaces the vault, so the backup under test is one a real command produced.
#
# The fifteen-minute floor (VaultBackups.Floor) is defeated WITHOUT a product knob: the stamp
# lives in the backup's file name, so renaming it is how this gate tells the floor that time has
# passed. There is deliberately no environment variable or flag that shortens it — a knob that
# turns backups off is a knob that gets turned off.
#
# This builds its OWN database, for the reason verify-keepassxc-history.sh gives: the other
# fixtures are asserted against exact trees and values, and mutating one here would make the
# gates order-dependent.

set -euo pipefail

die()  { printf '\nBACKUP GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

db=${1:-}
[ -n "$db" ] || die "usage: verify-keepassxc-backup.sh <backup.kdbx>"
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

# BOTH sides need \r stripped — see verify-keepassxc-writeback.sh.
kpxc()  { printf '%s\n' "$pw" | "$cli" "$@" | tr -d '\r'; }
kpset() { printf '%s\n%s\n' "$pw" "$2" | "$kp" env set "$1" "$3" --vault "$db"; }

# The whole file, so a refusal can be shown to have written nothing at all.
bytes() { od -An -v -tx1 "$db" | tr -d ' \n'; }

backups="${db}.backups"
stem=$(basename "$db" .kdbx)

# Newest first, by name. The stamp is fixed width, so an ordinal sort is chronological.
list()  { [ -d "$backups" ] && ls -1 "$backups" 2>/dev/null | grep -E "^${stem}\.[0-9]{8}T[0-9]{6}Z(-[0-9]+)?\.kdbx$" | sort -r || true; }
count() { list | grep -c . || true; }

# Tells the floor that time has passed, by rewriting the stamps it reads.
#
# The stamp lives in the file name, so this is a rename. Every backup is renumbered onto a fixed
# run of dates in 2020 — oldest onto the lowest — which puts them all far outside the floor while
# keeping their order, and a file already on its own slot is left alone. Deliberately arithmetic
# on a fixed sequence rather than `date -d`: that flag is GNU's, and this gate runs on macos-15.
age() {
  local name day target
  day=1
  for name in $(list | sort); do
    target=$(printf '%s.202001%02dT000000Z.kdbx' "$stem" "$day")
    [ "$name" = "$target" ] || mv "$backups/$name" "$backups/$target"
    day=$((day + 1))
  done
}

# A KDBX container check, on whichever file is named. Reads the raw header, because a silent
# format or KDF regression round-trips invisibly through everything else.
container() {
  local file=$1 what=$2 hdr info
  hdr=$(od -An -v -tx1 -N12 "$file" | tr -d ' \n' | tr 'A-Z' 'a-z')
  [ "${hdr:0:16}" = "03d9a29a67fb4bb5" ] || die "$what is not a KDBX file (signature ${hdr:0:16})"
  [ "${hdr:20:2}" = "04" ]               || die "$what is not KDBX 4: major 0x${hdr:20:2}"

  info=$(printf '%s\n' "$pw" | "$cli" db-info "$file" | tr -d '\r') \
    || die "KeePassXC cannot open $what"
  grep -Eqi '^[[:space:]]*KDF:[[:space:]]*Argon2' <<<"$info" \
    || die "$what is not Argon2. Got: $(grep -i '^[[:space:]]*KDF:' <<<"$info" || echo '<no KDF line>')"
}

project=compat-backup
entry="env/${project}/ROTATED"

mkdir -p "$(dirname "$db")"
rm -rf "$db" "$backups"   # re-runnable locally, not only on a fresh CI checkout

# ---------------------------------------------------------------------------------------
step "seed: creating a vault keeps nothing, because there is nothing yet to keep"
printf '%s
%s
' "$pw" "$pw" | "$kp" init "$db"

[ "$(count)" -eq 0 ]   || die "creating a vault left $(count) backups; first creation has nothing to preserve"

# ---------------------------------------------------------------------------------------
step "the first save over that vault keeps it as it was: empty"
kpset "$project" v1-first ROTATED

[ "$(count)" -eq 1 ] || die "the first save over an existing vault produced $(count) backups, expected 1"

empty=$(list | head -n1)
container "$backups/$empty" "the backup of the newly created vault"

# It held no entry at all, and that is the claim: the copy is the state before the save, not a
# second copy of the state after it.
if kpxc show -a Password "$backups/$empty" "$entry" >/dev/null 2>&1; then
  die "the backup of the empty vault already contains the entry the save added"
fi

# ---------------------------------------------------------------------------------------
step "a later save keeps the value it replaced"
age
kpset "$project" v2-second ROTATED

[ "$(count)" -eq 2 ] || die "a save outside the floor left $(count) backups, expected 2"

first=$(list | head -n1)
container "$db" "the vault after a save"
container "$backups/$first" "the backup that save took"

# ---------------------------------------------------------------------------------------
step "KeePassXC reads the PREVIOUS value out of the backup, and the new one out of the vault"
kept=$(kpxc show -a Password "$backups/$first" "$entry")   || die "keepassxc-cli show failed on the backup"
diff -u <(printf '%s
' 'v1-first') <(printf '%s
' "$kept")   || die "the backup does not hold the value the save replaced"

live=$(kpxc show -a Password "$db" "$entry") || die "keepassxc-cli show failed on the vault"
diff -u <(printf '%s
' 'v2-second') <(printf '%s
' "$live")   || die "the vault does not hold the value the save wrote"

# ---------------------------------------------------------------------------------------
step "keypaste reads its own backup back, with no special reader"
read_back=$(printf '%s
' "$pw" | "$kp" get "$entry" --vault "$backups/$first" --show | tr -d '\r')
grep -qF 'v1-first' <<<"$read_back"   || die "keypaste cannot read the previous value out of its own backup. Got: ${read_back}"

# ---------------------------------------------------------------------------------------
step "a second save inside the floor keeps nothing more"
kpset "$project" v3-third ROTATED
[ "$(count)" -eq 2 ]   || die "a save fifteen minutes inside the floor took another backup; $(count) are there"

# ---------------------------------------------------------------------------------------
step "a save outside the floor keeps another, and it holds its own generation"
age
kpset "$project" v4-current ROTATED
[ "$(count)" -eq 3 ] || die "a save outside the floor left $(count) backups, expected 3"

newest=$(list | head -n1)
container "$backups/$newest" "the third backup"
third=$(kpxc show -a Password "$backups/$newest" "$entry")   || die "keepassxc-cli show failed on the third backup"
diff -u <(printf '%s
' 'v3-third') <(printf '%s
' "$third")   || die "the third backup does not hold the value its own save replaced"

# ---------------------------------------------------------------------------------------
step "retention keeps the newest and drops the oldest"
retained=5
generation=5
while [ "$(count)" -lt "$retained" ]; do
  age
  kpset "$project" "v${generation}-gen" ROTATED
  generation=$((generation + 1))
  [ "$generation" -lt 20 ] || die "the retained count never reached $retained; it is stuck at $(count)"
done

[ "$(count)" -eq "$retained" ] || die "expected $retained backups before the roll, got $(count)"
oldest=$(list | tail -n1)

age
kpset "$project" v-rolled ROTATED

[ "$(count)" -eq "$retained" ] \
  || die "retention left $(count) backups, expected $retained"
[ ! -e "$backups/$oldest" ] \
  || die "the oldest backup survived the roll: $oldest"

rolled=$(list | head -n1)
container "$backups/$rolled" "the backup that rolled the oldest out"

# ---------------------------------------------------------------------------------------
step "a save that cannot take a backup does not happen"
before_refusal=$(bytes)
mv "$backups" "${backups}.held"
: > "$backups"                          # a regular file where the directory has to go

set +e
printf '%s\n%s\n' "$pw" blocked-value | "$kp" env set "$project" ROTATED --vault "$db" >/dev/null 2>&1
refused_rc=$?
set -e

rm -f "$backups"
mv "${backups}.held" "$backups"

[ "$refused_rc" -ne 0 ] || die "a save whose backup could not be written reported success"
[ "$(bytes)" = "$before_refusal" ] || die "a save refused for want of a backup changed the vault"
[ "$(count)" -eq "$retained" ] || die "a refused save pruned backups; $(count) remain of $retained"

refused=$(kpxc show -a Password "$db" "$entry") || die "keepassxc-cli show failed after the refusal"
diff -u <(printf '%s\n' 'v-rolled') <(printf '%s\n' "$refused") \
  || die "a refused save changed the value in the vault"

# ---------------------------------------------------------------------------------------
# NEGATIVE CONTROL.
#
# Everything above only means something if this gate is still capable of failing. See
# verify-keepassxc-compat.sh (v) for why this is the cheapest insurance in the repository.
# Never remove it.
# ---------------------------------------------------------------------------------------
step "NEGATIVE CONTROL: the comparisons must be able to fail"

if diff -q <(printf '%s\n' 'v1-first-CORRUPTED') <(printf '%s\n' "$kept") >/dev/null 2>&1; then
  die "a deliberately corrupted expectation still matched — this gate is not gating"
fi

if kpxc show -a Password "$backups/$rolled" 'env/compat-backup/NEVER-WRITTEN' >/dev/null 2>&1; then
  die "an entry nothing ever wrote was read out of a backup — the reader is not reading"
fi

# A backup must not merely be a file of the right size: prove the container check can refuse one.
printf 'not a vault' > "$backups/${stem}.19700101T000000Z.kdbx"
if (container "$backups/${stem}.19700101T000000Z.kdbx" "a planted non-vault") >/dev/null 2>&1; then
  die "a file that is not a KDBX passed the container check — a listing is all this gate proves"
fi
rm -f "$backups/${stem}.19700101T000000Z.kdbx"

printf '\nBACKUP GATE PASSED: KeePassXC opens what keypaste kept, and reads the values it replaced.\n'
