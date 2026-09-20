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
# and what it keeps is a vault KeePassXC opens and reads the previous values out of. V.4b added
# the other half: a backup PUT BACK is the vault KeePassXC then reads, the vault it replaced is
# kept, and an encrypted EXPORT opens in KeePassXC on its own.
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
#   * A restore that re-serialises. The restored vault is compared with the backup byte for
#     byte (`cmp -s`), because a vault that merely opens would hide a second KDBX writer.
#
# Backups are ordinary KDBX files, so READING one needs no driver: `keepassxc-cli` opens one
# directly and so does `keypaste --vault`. That is the whole claim of the design — the bytes are
# copied, never re-serialised — and a gate that needed a special reader would be evidence against
# it. The two ACTS are different: restoring and exporting happen in the desktop app, which a bash
# gate cannot press a button in, so they go through tests/Keypaste.VaultRestorer over the same
# public core calls the app makes (D-0254's argument, and D-0230's). The driver goes when a
# shipped command line performs them.
#
# Usage:  scripts/verify-keepassxc-backup.sh <backup.kdbx>
# Env:    KP_COMPAT_PASSWORD   master password for the fixture       (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_RESTORER    path to the restore/export driver     (default: the Release build)
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

restorer=${KEYPASTE_RESTORER:-}
if [ -z "$restorer" ]; then
  restorer=artifacts/bin/Keypaste.VaultRestorer/release/Keypaste.VaultRestorer
  [ -x "$restorer" ] || restorer="${restorer}.exe"
fi
[ -x "$restorer" ] || die "restore driver not found at '$restorer' (build keypaste.slnx, or set KEYPASTE_RESTORER)"

# The password travels in the environment, never in argv — see Keypaste.VaultRestorer.
drive() { KEYPASTE_RESTORER_PASSWORD="$pw" "$restorer" "$@" | tr -d '\r'; }

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
step "a restored backup is the vault KeePassXC reads, byte for byte, and what it replaced is kept"
before_roll="v$((generation - 1))-gen"

restored_out=$(drive backup-restore "$db" "$rolled") || die "the driver could not restore $rolled"
printf '%s\n' "$restored_out"

cmp -s "$db" "$backups/$rolled" || die "the restored vault is not the backup's bytes"
container "$db" "the restored vault"

restored=$(kpxc show -a Password "$db" "$entry") || die "keepassxc-cli show failed on the restored vault"
diff -u <(printf '%s\n' "$before_roll") <(printf '%s\n' "$restored") \
  || die "the restored vault does not hold the value the backup held"

[ "$(count)" -ge "$((retained + 1))" ] || die "the vault a restore replaced was not kept; $(count) backups remain"
[ "$(count)" -eq "$((retained + 1))" ] || die "a restore pruned, or kept more than one copy: $(count) backups"

preserved=$(sed -n 's/^preserved[[:space:]]*//p' <<<"$restored_out")
[ -n "$preserved" ] && [ -f "$backups/$preserved" ] || die "the driver named no preserved copy: '$preserved'"
container "$backups/$preserved" "the copy of the vault a restore replaced"

replaced=$(kpxc show -a Password "$backups/$preserved" "$entry") || die "keepassxc-cli show failed on the preserved copy"
diff -u <(printf '%s\n' 'v-rolled') <(printf '%s\n' "$replaced") \
  || die "the copy kept by a restore does not hold the value the restore replaced"

# ---------------------------------------------------------------------------------------
step "restoring again keeps nothing new, and the next save prunes"
again_out=$(drive backup-restore "$db" "$rolled") || die "the driver could not restore $rolled a second time"
grep -q '^already-kept' <<<"$again_out" || die "a vault already kept byte for byte was kept again: $again_out"
[ "$(count)" -eq "$((retained + 1))" ] || die "a second restore changed the backups: $(count)"

age
kpset "$project" v-after-restore ROTATED
[ "$(count)" -eq "$retained" ] || die "the save after a restore left $(count) backups, expected $retained"
rolled=$(list | head -n1)

# ---------------------------------------------------------------------------------------
step "an encrypted export opens in KeePassXC on its own, and refuses what it must"
export_to="$(dirname "$db")/${stem}-copy-gate.kdbx"
rm -f "$export_to"

drive vault-export "$db" "$export_to" || die "the driver could not export the vault"
cmp -s "$db" "$export_to" || die "the export is not the vault's bytes"
container "$export_to" "the export"

exported=$(kpxc show -a Password "$export_to" "$entry") || die "the export does not open in KeePassXC"
diff -u <(printf '%s\n' 'v-after-restore') <(printf '%s\n' "$exported") \
  || die "the export does not hold the vault's current value"

set +e
drive vault-export "$db" "$export_to" >/dev/null 2>&1
twice_rc=$?
drive vault-export "$db" "$backups/${stem}-copy-gate.kdbx" >/dev/null 2>&1
inside_rc=$?
set -e

[ "$twice_rc" -ne 0 ] || die "an export over an existing file reported success"
cmp -s "$db" "$export_to" || die "a refused export changed the file that was already there"
[ "$inside_rc" -ne 0 ] || die "an export into the backup directory reported success"
[ ! -e "$backups/${stem}-copy-gate.kdbx" ] || die "an export was written into the backup directory"
rm -f "$export_to"

# ---------------------------------------------------------------------------------------
step "a wrong password and a backup that is not a vault restore nothing"
before_refused_restore=$(bytes)

set +e
KEYPASTE_RESTORER_PASSWORD="not-$pw" "$restorer" backup-restore "$db" "$rolled" >/dev/null 2>&1
wrong_rc=$?
set -e

[ "$wrong_rc" -ne 0 ] || die "a restore under the wrong password reported success"
[ "$(bytes)" = "$before_refused_restore" ] || die "a restore refused for a wrong password changed the vault"

planted="${stem}.20991231T235959Z.kdbx"
printf 'not a vault' > "$backups/$planted"

set +e
drive backup-restore "$db" "$planted" >/dev/null 2>&1
planted_rc=$?
set -e

rm -f "$backups/$planted"

[ "$planted_rc" -ne 0 ] || die "a backup that is not a vault was restored"
[ "$(bytes)" = "$before_refused_restore" ] || die "a refused restore changed the vault"

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

# The byte comparison a restore is held to must be able to say no: the vault has been saved since
# the restore, so it is no longer any backup's bytes.
if cmp -s "$db" "$backups/$rolled"; then
  die "a vault saved since its restore still compares equal to a backup — cmp is not comparing"
fi

# A backup must not merely be a file of the right size: prove the container check can refuse one.
printf 'not a vault' > "$backups/${stem}.19700101T000000Z.kdbx"
if (container "$backups/${stem}.19700101T000000Z.kdbx" "a planted non-vault") >/dev/null 2>&1; then
  die "a file that is not a KDBX passed the container check — a listing is all this gate proves"
fi
rm -f "$backups/${stem}.19700101T000000Z.kdbx"

printf '\nBACKUP GATE PASSED: KeePassXC opens what keypaste kept, restored and exported, and reads the right values out of each.\n'
