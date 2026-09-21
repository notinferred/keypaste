#!/usr/bin/env bash
#
# verify-keepassxc-organize.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md law 4.6, the organization half.
#
# verify-keepassxc-compat.sh proves a vault keypaste CREATES opens in KeePassXC,
# verify-keepassxc-writeback.sh proves a vault keypaste MODIFIES still does,
# verify-keepassxc-history.sh proves a revision keypaste RESTORES is the one KeePassXC then reads,
# verify-keepassxc-recyclebin.sh proves a DELETE is recoverable, and verify-keepassxc-backup.sh
# proves the whole-file copies. This one covers what docs/STEPS.md V.5a added: keypaste CREATES and
# RENAMES a group, RENAMES an entry and MOVES one between groups.
#
# Two things here are keypaste's claims about the FORMAT rather than about its own reader:
#
#   * The file is still KDBX 4.0. This is the exact inverse of the recycle-bin gate's assertion,
#     and it is the load-bearing one. PreviousParentGroup — where an object was moved out of —
#     raises a written file to 4.1 through the KEYPASTE_KDBX_4_1_MOVES guard (D-0247), which costs
#     every reader below KeePassXC 2.7 and KeePass 2.48. Recycling pays that; tidying a folder must
#     not, so an ordinary move records nothing about where the entry came from. If somebody makes a
#     move stamp that field, the minor-version byte below is what notices.
#
#   * A rename and a move write no deleted-object tombstone. A tombstone says an object was
#     deleted; a merge that believed one for a moved entry would delete it in the other copy of the
#     vault.
#
# The entry's UUID is compared across the rename and the move, because the difference between
# mutating an entry and re-adding it under a new name is invisible in every field keypaste models
# and takes the attachments, custom strings and history with it.
#
# Usage:  scripts/verify-keepassxc-organize.sh <organize.kdbx>
# Env:    KP_COMPAT_PASSWORD   master password for the fixture       (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_RESTORER    path to the recovery driver           (default: the Release build)
#
# The seeding is done by the SHIPPED binary (D-0012). Only the four organize acts go through
# tests/Keypaste.VaultRestorer, because no CLI verb performs them and V.5b's desktop controls are a
# bash gate cannot press — the same argument D-0228 made for the history gate's driver, reaffirmed
# in D-0254.
#
# This builds its OWN database, for the reason verify-keepassxc-history.sh gives: the other
# fixtures are asserted against exact trees and values, and mutating one here would make the
# gates order-dependent.

set -euo pipefail

die()  { printf '\nORGANIZE GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

db=${1:-}
[ -n "$db" ] || die "usage: verify-keepassxc-organize.sh <organize.kdbx>"
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
[ -x "$driver" ] || die "organize driver not found at '$driver' (build keypaste.slnx, or set KEYPASTE_RESTORER)"

export KEYPASTE_RESTORER_PASSWORD=$pw

# BOTH sides need \r stripped — see verify-keepassxc-writeback.sh.
kpxc()  { printf '%s\n' "$pw" | "$cli" "$@" | tr -d '\r'; }
kpset() { printf '%s\n%s\n' "$pw" "$2" | "$kp" env set "$1" "$3" --vault "$db"; }

# The whole file, so a refusal can be shown to have written nothing at all.
bytes() { od -An -v -tx1 "$db" | tr -d ' \n'; }

# The last <UUID> BEFORE the first <History>, which is the entry's own rather than a revision's.
# The seen-flag rather than `{exit}` or `head`, because leaving the stream early breaks the pipe
# the `tr` upstream is writing into and `pipefail` then kills the gate (F.16).
entry_uuid() {
  kpxc export -f xml "$db" \
    | tr -d '\t' \
    | awk '/<History>/{seen=1} !seen && /<UUID>/{last=$0} END{print last}'
}

# The KDBX container: signature, major version, and the minor version this gate exists for.
assert_kdbx_40() {
  local hdr what=$1
  hdr=$(od -An -v -tx1 -N12 "$db" | tr -d ' \n' | tr 'A-Z' 'a-z')
  [ "${hdr:0:16}" = "03d9a29a67fb4bb5" ] || die "not a KDBX file after ${what} (signature ${hdr:0:16})"
  [ "${hdr:20:2}" = "04" ]               || die "KDBX major version changed on ${what}: 0x${hdr:20:2}"
  [ "${hdr:16:2}" = "00" ] \
    || die "KDBX minor version is 0x${hdr:16:2}, expected 0x00 after ${what}. Organizing a vault must not stamp PreviousParentGroup: that raises the file to 4.1 and costs every reader below KeePassXC 2.7 (D-0247). Only recycling may do that."
}

# A refused organize act: the exact outcome name, exit 1, and a file nobody touched.
refuses() {
  local expected=$1 before said rc
  shift
  before=$(bytes)
  set +e
  said=$("$driver" "$@" 2>&1)
  rc=$?
  set -e
  [ "$rc" -eq 1 ] || die "'$*' exited ${rc}, expected 1. It said: ${said}"
  grep -qF "refused: ${expected}" <<<"$said" \
    || die "'$*' was not refused as ${expected}. It said: ${said}"
  [ "$(bytes)" = "$before" ] || die "the refused '$*' changed the vault file"
}

project=compat-organize
other=compat-shipping

mkdir -p "$(dirname "$db")"
rm -f "$db"          # re-runnable locally, not only on a fresh CI checkout

# ---------------------------------------------------------------------------------------
# Seeded by the shipped binary.
# ---------------------------------------------------------------------------------------
step "seed: the shipped binary writes an env project with history, a second project and a plain entry"
printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$db"
for value in v1-first v2-second v3-third v4-current; do
  kpset "$project" "$value" TOKEN
done
kpset "$project" kept-value KEPT
kpset "$other" other-value OTHER
printf '%s\n%s\n' "$pw" 'plain-pass' | "$kp" add "keys/spare" --vault "$db" >/dev/null

uuid_before=$(entry_uuid)
[ -n "$uuid_before" ] || die "could not read the entry's UUID out of the XML export"

assert_kdbx_40 "the seed"

# ---------------------------------------------------------------------------------------
# Renaming a group, which is how an env project is renamed.
# ---------------------------------------------------------------------------------------
step "keypaste renames the group, and the file is still KDBX 4.0"
said=$("$driver" group-rename "$db" "env/${project}" invoicing) || die "group-rename failed: ${said}"
grep -qE '^renamed +env/invoicing$' <<<"$said" \
  || die "group-rename did not report the new path. It said: ${said}"

assert_kdbx_40 "a group rename"

info=$(kpxc db-info "$db") || die "KeePassXC cannot open the file keypaste renamed a group in"
grep -Eqi '^[[:space:]]*KDF:[[:space:]]*Argon2' <<<"$info" \
  || die "KDF is no longer Argon2 after a group rename. Got: $(grep -i '^[[:space:]]*KDF:' <<<"$info" || echo '<no KDF line>')"

step "KeePassXC sees the group at its new path, with everything that was under it"
tree=$(kpxc ls -R -f "$db") || die "keepassxc-cli ls failed"
grep -qF 'env/invoicing/TOKEN' <<<"$tree" \
  || die "KeePassXC does not see the entry at the renamed group's path. Its tree: ${tree}"
grep -qF 'env/invoicing/KEPT' <<<"$tree" \
  || die "the other entry in the renamed group is missing"
grep -qF "env/${project}/" <<<"$tree" \
  && die "KeePassXC still sees the group under its old name, so nothing was renamed"
grep -qF "env/${other}/OTHER" <<<"$tree" \
  || die "the project that was not renamed is missing"

step "KeePassXC reads the entry's value at the new path"
current=$(kpxc show -a Password "$db" 'env/invoicing/TOKEN') || die "keepassxc-cli show failed at the new path"
diff -u <(printf '%s\n' 'v4-current') <(printf '%s\n' "$current") \
  || die "KeePassXC does not read the entry's value at the renamed path"

step "the shipped binary resolves the project by its new name and not its old one"
listed=$(printf '%s\n' "$pw" | "$kp" env ls --vault "$db") || die "keypaste env ls failed"
grep -qF 'invoicing' <<<"$listed" || die "keypaste does not list the renamed project. It said: ${listed}"
grep -qF "$project" <<<"$listed" && die "keypaste still lists the project under its old name"

got=$(printf '%s\n' "$pw" | "$kp" get env/invoicing/TOKEN --show --vault "$db") \
  || die "keypaste get failed at the renamed path"
grep -qF 'v4-current' <<<"$got" || die "keypaste does not read the value at the renamed path. It said: ${got}"

# ---------------------------------------------------------------------------------------
# Creating a group, renaming an entry, moving one.
# ---------------------------------------------------------------------------------------
step "keypaste creates an empty group that KeePassXC lists"
said=$("$driver" group-create "$db" "" archive) || die "group-create failed: ${said}"
grep -qE '^created +archive$' <<<"$said" || die "group-create did not report the new path. It said: ${said}"

tree=$(kpxc ls -R -f "$db")
grep -qE '^archive/$' <<<"$tree" \
  || die "KeePassXC does not list the empty group keypaste created. Its tree: ${tree}"

step "keypaste renames the entry, then moves it, and KeePassXC follows both"
said=$("$driver" entry-rename "$db" env/invoicing TOKEN API_TOKEN) || die "entry-rename failed: ${said}"
grep -qE '^renamed +env/invoicing/API_TOKEN$' <<<"$said" \
  || die "entry-rename did not report the new name. It said: ${said}"

said=$("$driver" entry-move "$db" env/invoicing API_TOKEN archive) || die "entry-move failed: ${said}"
grep -qE '^moved +archive/API_TOKEN$' <<<"$said" \
  || die "entry-move did not report the new name. It said: ${said}"

assert_kdbx_40 "a rename and a move"

tree=$(kpxc ls -R -f "$db")
grep -qF 'archive/API_TOKEN' <<<"$tree" || die "KeePassXC does not see the moved entry. Its tree: ${tree}"
grep -qF 'env/invoicing/TOKEN' <<<"$tree" && die "KeePassXC still sees the entry where it was"

moved=$(kpxc show -a Password "$db" 'archive/API_TOKEN') || die "keepassxc-cli show failed on the moved entry"
diff -u <(printf '%s\n' 'v4-current') <(printf '%s\n' "$moved") \
  || die "the moved entry does not hold its value"

# ---------------------------------------------------------------------------------------
# What the file says about the entry that was renamed and moved.
# ---------------------------------------------------------------------------------------
step "it is the same entry, with its history, and the file records no move and no deletion"
uuid_after=$(entry_uuid)
[ "$uuid_after" = "$uuid_before" ] \
  || die "the entry's UUID changed across a rename and a move, so it was re-added rather than mutated. Its attachments, custom strings and history do not survive that."

xml=$(kpxc export -f xml "$db") || die "keepassxc-cli export -f xml failed"
grep -q '<PreviousParentGroup>' <<<"$xml" \
  && die "an organize act stamped PreviousParentGroup. That raises the file to KDBX 4.1 for a reason only a deletion may raise it (D-0247)."
grep -q '<DeletedObjects/>' <<<"$xml" \
  || die "renaming or moving wrote a deleted-object tombstone. A merge would then delete the entry in the other copy of the vault."

history=$(awk '/<History>/{inside=1} inside' <<<"$xml")
for value in v1-first v2-second v3-third; do
  grep -qF ">${value}<" <<<"$history" \
    || die "'${value}' is missing from the history of the renamed and moved entry"
done

# ---------------------------------------------------------------------------------------
# Both directions of law 4.6: a group KeePassXC made, renamed by keypaste.
#
# This runs AFTER every header assertion above, because KeePassXC's own writer may raise the file
# to 4.1 on its own account and that would fail an assertion about keypaste for a reason that has
# nothing to do with keypaste.
# ---------------------------------------------------------------------------------------
step "keypaste renames a group KeePassXC made, and KeePassXC reads the result"
printf '%s\n' "$pw" | "$cli" mkdir "$db" toolmade >/dev/null || die "keepassxc-cli mkdir failed"

said=$("$driver" group-rename "$db" toolmade ours) || die "keypaste could not rename a KeePassXC-made group: ${said}"
grep -qE '^renamed +ours$' <<<"$said" || die "the rename did not report the new path. It said: ${said}"

tree=$(kpxc ls -R -f "$db")
grep -qE '^ours/$' <<<"$tree" || die "KeePassXC does not see the group keypaste renamed. Its tree: ${tree}"

info=$(kpxc db-info "$db") || die "KeePassXC cannot open the file after renaming its own group"
grep -Eqi '^[[:space:]]*KDF:[[:space:]]*Argon2' <<<"$info" || die "KDF changed after renaming a KeePassXC-made group"

# ---------------------------------------------------------------------------------------
# The recycle bin, which the refusals below need to exist to mean anything — and which is the
# positive control for every 4.0 assertion above: the same guard that leaves an organized file at
# 4.0 must still raise a recycled one to 4.1, or those assertions are checking nothing.
# ---------------------------------------------------------------------------------------
step "recycling raises the same file to KDBX 4.1, which is what makes the 4.0 assertions mean something"
kpset invoicing doomed-value DOOMED >/dev/null
printf '%s\n' "$pw" | "$kp" rm env/invoicing/DOOMED --vault "$db" --yes >/dev/null

hdr=$(od -An -v -tx1 -N12 "$db" | tr -d ' \n' | tr 'A-Z' 'a-z')
[ "${hdr:16:2}" = "01" ] \
  || die "recycling did not raise the file to KDBX 4.1 (minor 0x${hdr:16:2}). The assertions that organizing leaves it at 4.0 are then checking nothing."

# ---------------------------------------------------------------------------------------
# Refusals. Each one names the refusal it expects and proves the file was never opened for
# writing — a gate that only checked that something failed would pass with every refusal
# collapsed into one.
# ---------------------------------------------------------------------------------------
step "a name something else already answers to is refused, and writes nothing"
kpset invoicing second-value TOKEN_TAKEN >/dev/null
refuses DestinationOccupied entry-rename "$db" env/invoicing KEPT TOKEN_TAKEN

step "a destination that does not exist is refused, and creates nothing on the way"
refuses DestinationMissing entry-move "$db" env/invoicing KEPT "archive/2026/q1"
tree=$(kpxc ls -R -f "$db")
grep -qF 'archive/2026' <<<"$tree" && die "a refused move created part of its destination path"

step "the recycle bin is not a destination: a move is not a delete"
refuses DestinationMissing entry-move "$db" env/invoicing KEPT "Recycle Bin"

step "a name no vault could address is refused"
refuses NameRefused group-rename "$db" env/invoicing "with/slash"

step "the env root is reserved, and so is the recycle bin's name"
refuses NameReserved group-create "$db" "" env
refuses NameReserved group-create "$db" "" "Recycle Bin"
refuses NameReserved group-rename "$db" archive env

step "a variable name nothing could export is refused"
refuses EnvNameRefused entry-rename "$db" env/invoicing KEPT lower-case

step "two variables differing only in case are refused where they would be created"
refuses EnvNameCollides entry-rename "$db" env/invoicing TOKEN_TAKEN Kept

step "the entries the refusals named are exactly as they were"
value=$(kpxc show -a Password "$db" 'env/invoicing/KEPT')
diff -u <(printf '%s\n' 'kept-value') <(printf '%s\n' "$value") \
  || die "the refusals disturbed the entry they named"

# ---------------------------------------------------------------------------------------
# NEGATIVE CONTROL.
#
# Everything above only means something if this gate is still capable of failing. See
# verify-keepassxc-compat.sh (v) for why this is the cheapest insurance in the repository.
# Never remove it.
# ---------------------------------------------------------------------------------------
step "NEGATIVE CONTROL: the comparisons must be able to fail"
if diff -q <(printf '%s\n' 'v4-current-CORRUPTED') <(printf '%s\n' "$moved") >/dev/null 2>&1; then
  die "a deliberately corrupted expectation still matched — this gate is not gating"
fi

if grep -qF '>v0-never-written<' <<<"$history"; then
  die "a value nothing ever wrote was found in the history — the history search is not searching"
fi

# The byte comparison every refusal above rests on must be able to see a change.
before_control=$(bytes)
kpset invoicing control-value CONTROL >/dev/null
[ "$(bytes)" != "$before_control" ] \
  || die "the vault file did not change after a write — the byte comparison cannot detect one, so every refusal above proved nothing"

# And a rename the rules allow must actually go through, or a gate whose refusals all pass because
# everything is refused would look exactly like this one.
said=$("$driver" entry-rename "$db" env/invoicing CONTROL CONTROL_RENAMED) \
  || die "a rename nothing forbids was refused — every refusal above would then prove nothing. It said: ${said}"
kpxc show -a Password "$db" 'env/invoicing/CONTROL_RENAMED' >/dev/null \
  || die "KeePassXC cannot read the control rename"

printf '\nORGANIZE GATE PASSED: KeePassXC reads what keypaste created, renamed and moved, and the file is still KDBX 4.0.\n'
