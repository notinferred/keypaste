#!/usr/bin/env bash
#
# verify-keepassxc-recyclebin.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md law 4.6, the reversible-deletion half.
#
# verify-keepassxc-compat.sh proves a vault keypaste CREATES opens in KeePassXC,
# verify-keepassxc-writeback.sh proves a vault keypaste MODIFIES still does, and
# verify-keepassxc-history.sh proves a revision keypaste RESTORES is the one KeePassXC then
# reads. This one covers what docs/STEPS.md V.3a added: keypaste DELETES an entry into the
# KDBX recycle bin, lists what is in there, puts one back and removes one for good.
#
# Two things here are keypaste's claims about the FORMAT rather than about its own reader:
#
#   * The file becomes KDBX 4.1. PreviousParentGroup — where a recycled entry came from — is a
#     4.1 field, and upstream's GetMinKdbxVersion does not ask for 4.1 on its account, so a 4.0
#     save drops it and a restore after a reopen has nowhere to go. The KEYPASTE_KDBX_4_1_MOVES
#     guard adds it to the same floor upstream already applies to the other 4.1-only fields
#     (third_party/KeePassLib/UPSTREAM.md). The minor-version byte is asserted below because
#     nothing else would notice it silently going back to 0.
#
#   * A recycled entry gets no tombstone and a purged one does. A tombstone says an object was
#     deleted; a merge that believed one for a recycled entry would delete it in the other copy
#     of the vault.
#
# Usage:  scripts/verify-keepassxc-recyclebin.sh <recyclebin.kdbx>
# Env:    KP_COMPAT_PASSWORD   master password for the fixture       (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_RESTORER    path to the recovery driver           (default: the Release build)
#
# The seeding AND the deletion are done by the SHIPPED binary (D-0012): `keypaste rm` is what
# recycles. Only the listing, the restore and the purge go through tests/Keypaste.VaultRestorer,
# because no shipped surface performs one until V.3b puts a trash view on the desktop — the same
# argument D-0228 made for the history gate's driver.
#
# This builds its OWN database, for the reason verify-keepassxc-history.sh gives: the other
# fixtures are asserted against exact trees and values, and mutating one here would make the
# gates order-dependent.

set -euo pipefail

die()  { printf '\nRECYCLE BIN GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

db=${1:-}
[ -n "$db" ] || die "usage: verify-keepassxc-recyclebin.sh <recyclebin.kdbx>"
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

driver=${KEYPASTE_RESTORER:-}
if [ -z "$driver" ]; then
  driver=artifacts/bin/Keypaste.VaultRestorer/release/Keypaste.VaultRestorer
  [ -x "$driver" ] || driver="${driver}.exe"
fi
[ -x "$driver" ] || die "recovery driver not found at '$driver' (build keypaste.slnx, or set KEYPASTE_RESTORER)"

export KEYPASTE_RESTORER_PASSWORD=$pw

# BOTH sides need \r stripped — see verify-keepassxc-writeback.sh.
kpxc()  { printf '%s\n' "$pw" | "$cli" "$@" | tr -d '\r'; }
kpset() { printf '%s\n%s\n' "$pw" "$2" | "$kp" env set "$1" "$3" --vault "$db"; }
kprm()  { printf '%s\n' "$pw" | "$kp" rm "$1" --vault "$db" --yes; }

# The whole file, so a refusal can be shown to have written nothing at all.
bytes() { od -An -v -tx1 "$db" | tr -d ' \n'; }

# The identity trash-ls gives an entry, by the title it prints beside it.
trash_id() { "$driver" trash-ls "$db" | awk -v t="$1" '$3 == t { print $1 }'; }

project=compat-trash
entry="env/${project}/ROTATED"

mkdir -p "$(dirname "$db")"
rm -f "$db"          # re-runnable locally, not only on a fresh CI checkout

step "seed: the shipped binary writes four values into one entry, and three more entries"
printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$db"
for value in v1-first v2-second v3-third v4-current; do
  kpset "$project" "$value" ROTATED
done
kpset "$project" kept-value KEPT
kpset "$project" doomed-value GONE
kpset compat-trash-gone orphan-value DOOMED

# The last <UUID> BEFORE the first <History>, which is the entry's own rather than a revision's.
# The seen-flag rather than `{exit}` or `head`, because leaving the stream early breaks the pipe
# the `tr` upstream is writing into and `pipefail` then kills the gate (F.16).
entry_uuid() {
  kpxc export -f xml "$db" \
    | tr -d '\t' \
    | awk '/<History>/{seen=1} !seen && /<UUID>/{last=$0} END{print last}'
}

uuid_before=$(entry_uuid)
[ -n "$uuid_before" ] || die "could not read the entry's UUID out of the XML export"

# ---------------------------------------------------------------------------------------
# The deletion, by the shipped binary.
# ---------------------------------------------------------------------------------------
step "keypaste rm moves the entry to the recycle bin"
said=$(kprm "$entry" 2>&1) || die "keypaste rm failed: ${said}"
grep -qF 'recycle bin' <<<"$said" \
  || die "keypaste rm did not report a recycle. It said: ${said}"

# The container is re-checked after the save, exactly as the write-back and history gates do:
# a format or KDF shift on this path would round-trip through keypaste perfectly and be
# invisible anywhere else. The minor version is the new assertion, and the load-bearing one.
hdr=$(od -An -v -tx1 -N12 "$db" | tr -d ' \n' | tr 'A-Z' 'a-z')
[ "${hdr:0:16}" = "03d9a29a67fb4bb5" ] || die "not a KDBX file after a keypaste delete (signature ${hdr:0:16})"
[ "${hdr:20:2}" = "04" ]               || die "KDBX major version changed on delete: 0x${hdr:20:2}"
[ "${hdr:16:2}" = "01" ] \
  || die "KDBX minor version is 0x${hdr:16:2}, expected 0x01. Without 4.1 the file cannot carry PreviousParentGroup, so a restore after a reopen has lost where the entry came from."

info=$(kpxc db-info "$db") || die "KeePassXC cannot open the file keypaste deleted from"
grep -Eqi '^[[:space:]]*KDF:[[:space:]]*Argon2' <<<"$info" \
  || die "KDF is no longer Argon2 after a keypaste delete. Got: $(grep -i '^[[:space:]]*KDF:' <<<"$info" || echo '<no KDF line>')"

step "KeePassXC sees the entry in its recycle bin, and not where it was"
tree=$(kpxc ls -R -f "$db") || die "keepassxc-cli ls failed"
grep -qF 'Recycle Bin/ROTATED' <<<"$tree" \
  || die "KeePassXC does not see the entry in the recycle bin. Its tree: ${tree}"
grep -qF "${entry}" <<<"$tree" \
  && die "KeePassXC still sees the entry where it was, so nothing moved"
grep -qF "env/${project}/KEPT" <<<"$tree" \
  || die "the entry that was not deleted is missing too"

step "KeePassXC reads the recycled entry's value where it now lives"
current=$(kpxc show -a Password "$db" 'Recycle Bin/ROTATED') || die "keepassxc-cli show failed on the recycled entry"
diff -u <(printf '%s\n' 'v4-current') <(printf '%s\n' "$current") \
  || die "KeePassXC does not read the recycled entry's value"

step "the file records where the entry came from, and tombstones nothing"
xml=$(kpxc export -f xml "$db") || die "keepassxc-cli export -f xml failed"
grep -q '<PreviousParentGroup>' <<<"$xml" \
  || die "no PreviousParentGroup in the file. A 4.0 save drops it, so check the KEYPASTE_KDBX_4_1_MOVES guard."
grep -q '<DeletedObjects/>' <<<"$xml" \
  || die "recycling wrote a deleted-object tombstone. A merge would then delete the entry in the other copy of the vault."

# v4-current is the entry's current value and `show` has already read it back; the three
# behind it are what a delete used to take with it.
history=$(awk '/<History>/{inside=1} inside' <<<"$xml")
for value in v1-first v2-second v3-third; do
  grep -qF ">${value}<" <<<"$history" \
    || die "'${value}' is missing from the history of the recycled entry"
done

# ---------------------------------------------------------------------------------------
# Putting it back.
# ---------------------------------------------------------------------------------------
step "the trash lists the entry with the group it came from"
listed=$("$driver" trash-ls "$db") || die "trash-ls failed"
grep -qF "env/${project}  ROTATED" <<<"$listed" \
  || die "trash-ls does not report the entry and where it came from. It said: ${listed}"

id=$(trash_id ROTATED)
[ -n "$id" ] || die "trash-ls gave the entry no identity"

step "keypaste restores it, and KeePassXC reads it back where it was"
restored=$("$driver" trash-restore "$db" "$id") || die "the restore driver failed"
grep -qE '^restored +[0-9A-F]{32}$' <<<"$restored" \
  || die "the restore did not report putting the entry back where it came from. It said: ${restored}"

back=$(kpxc show -a Password "$db" "$entry") || die "keepassxc-cli show failed after a restore"
diff -u <(printf '%s\n' 'v4-current') <(printf '%s\n' "$back") \
  || die "KeePassXC does not see the restored entry where it belongs"

tree=$(kpxc ls -R -f "$db")
grep -qF 'Recycle Bin/ROTATED' <<<"$tree" \
  && die "the entry is still in the recycle bin after a restore"

step "the entry KeePassXC reads is the entry keypaste deleted"
uuid_after=$(entry_uuid)
[ "$uuid_after" = "$uuid_before" ] \
  || die "the entry's UUID changed across delete and restore: '${uuid_before}' became '${uuid_after}'"

history=$(kpxc export -f xml "$db" | awk '/<History>/{inside=1} inside')
for value in v1-first v2-second v3-third; do
  grep -qF ">${value}<" <<<"$history" \
    || die "'${value}' did not survive the round trip through the recycle bin"
done

# ---------------------------------------------------------------------------------------
# Removing one for good.
# ---------------------------------------------------------------------------------------
step "a purge removes the entry and tombstones it"
kprm "env/${project}/GONE" >/dev/null 2>&1 || die "keypaste rm failed on the entry to be purged"
gone_id=$(trash_id GONE)
[ -n "$gone_id" ] || die "the entry to be purged is not in the trash"

"$driver" trash-purge "$db" "$gone_id" >/dev/null || die "the purge driver failed"

xml=$(kpxc export -f xml "$db")
grep -qF '>doomed-value<' <<<"$xml" \
  && die "the purged entry's value is still in the file"
grep -q '<DeletedObjects/>' <<<"$xml" \
  && die "a purge wrote no tombstone, so a merge would bring the entry back"
grep -q '<DeletedObjects>' <<<"$xml" \
  || die "a purge wrote no DeletedObjects element at all"

# ---------------------------------------------------------------------------------------
# A restore whose group is gone. KeePassXC removes the group, which is how a vault actually
# arrives in this state: keypaste has no delete-group operation of its own.
# ---------------------------------------------------------------------------------------
step "an entry whose group KeePassXC removed comes back at the root"
kprm 'env/compat-trash-gone/DOOMED' >/dev/null 2>&1 || die "keypaste rm failed on the orphan entry"
kpxc rmdir "$db" env/compat-trash-gone >/dev/null || die "keepassxc-cli rmdir failed"

orphan_id=$(trash_id DOOMED)
[ -n "$orphan_id" ] || die "the orphan entry is not in the trash"

restored=$("$driver" trash-restore "$db" "$orphan_id") || die "the restore driver failed on the orphan"
grep -qE '^restored root ' <<<"$restored" \
  || die "a restore whose group is gone did not say so. It said: ${restored}"

orphan=$(kpxc show -a Password "$db" DOOMED) || die "KeePassXC cannot read the entry restored to the root"
diff -u <(printf '%s\n' 'orphan-value') <(printf '%s\n' "$orphan") \
  || die "the entry restored to the root does not hold its value"

# ---------------------------------------------------------------------------------------
# Emptying it.
# ---------------------------------------------------------------------------------------
step "emptying the bin takes everything in it"
kprm "env/${project}/KEPT" >/dev/null 2>&1 || die "keypaste rm failed on the last entry"
"$driver" trash-empty "$db" >/dev/null || die "the empty driver failed"

tree=$(kpxc ls -R -f "$db")
grep -qF 'Recycle Bin/KEPT' <<<"$tree" && die "the bin still holds an entry after being emptied"
grep -qF 'Recycle Bin/gone' <<<"$tree" && die "the bin still holds a group after being emptied"
grep -qF 'Recycle Bin/' <<<"$tree" || die "the recycle bin group itself disappeared"

# ---------------------------------------------------------------------------------------
# NEGATIVE CONTROL.
#
# Everything above only means something if this gate is still capable of failing. See
# verify-keepassxc-compat.sh (v) for why this is the cheapest insurance in the repository.
# Never remove it.
# ---------------------------------------------------------------------------------------
step "NEGATIVE CONTROL: the comparisons must be able to fail"
if diff -q <(printf '%s\n' 'v4-current-CORRUPTED') <(printf '%s\n' "$back") >/dev/null 2>&1; then
  die "a deliberately corrupted expectation still matched — this gate is not gating"
fi

if grep -qF '>v0-never-written<' <<<"$history"; then
  die "a value nothing ever wrote was found in the history — the history search is not searching"
fi

# An identity nothing answers to must be refused, and refused without changing the file: a
# driver that restored the nearest entry would make every assertion above vacuous.
before_refusal=$(bytes)
set +e
"$driver" trash-restore "$db" 0123456789ABCDEF0123456789ABCDEF >/dev/null 2>&1
refused_rc=$?
set -e
[ "$refused_rc" -eq 1 ] || die "restoring an identity nothing answers to exited ${refused_rc}, expected 1"
[ "$(bytes)" = "$before_refusal" ] || die "a refused restore changed the vault file"

# A restore that would produce two entries of one name is refused whole. Nothing in keypaste can
# resolve that pair afterwards: Find refuses it, a credential release denies it as ambiguous,
# and reading the env project throws (D-0091).
step "NEGATIVE CONTROL: a restore onto a name something else took is refused"
kprm "$entry" >/dev/null 2>&1 || die "keypaste rm failed on the re-deleted entry"
kpset "$project" a-new-value ROTATED >/dev/null

taken_id=$(trash_id ROTATED)
[ -n "$taken_id" ] || die "the re-deleted entry is not in the trash"

before_refusal=$(bytes)
set +e
occupied=$("$driver" trash-restore "$db" "$taken_id" 2>&1)
occupied_rc=$?
set -e
[ "$occupied_rc" -eq 1 ] \
  || die "restoring onto an occupied name exited ${occupied_rc}, expected 1. It said: ${occupied}"
[ "$(bytes)" = "$before_refusal" ] || die "a refused restore changed the vault file"

value=$(kpxc show -a Password "$db" "$entry")
diff -u <(printf '%s\n' 'a-new-value') <(printf '%s\n' "$value") \
  || die "the refused restore disturbed the entry that holds the name"

printf '\nRECYCLE BIN GATE PASSED: KeePassXC reads what keypaste recycled, restored and purged.\n'
