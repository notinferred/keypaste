#!/usr/bin/env bash
#
# verify-keepassxc-history.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md law 4.6, the entry-history half.
#
# verify-keepassxc-compat.sh proves a vault keypaste CREATES opens in KeePassXC, and
# verify-keepassxc-writeback.sh proves a vault keypaste MODIFIES still does. This one covers
# what docs/STEPS.md V.2a added: keypaste RESTORES an earlier revision, which rewrites the
# entry's fields, adds a history item and changes no UUID. KeePassXC must then show the
# restored value as current and every other revision still in the entry's history — the claim
# DECISIONS.md D-0014 rests on, now that keypaste can act on history rather than only write it.
#
# Usage:  scripts/verify-keepassxc-history.sh <history.kdbx>
# Env:    KP_COMPAT_PASSWORD   master password for the fixture       (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_RESTORER    path to the restore driver            (default: the Release build)
#
# The seeding is done by the SHIPPED binary, exactly as the other two gates do it, so init,
# prompting and the open-modify-save cycle are all under test here too (D-0012). Only the
# restore itself goes through tests/Keypaste.VaultRestorer, because no shipped binary a script
# can drive performs one (D-0228). V.2b put a restore on the entry pane, and a bash gate cannot
# drive a desktop window; what would retire the driver is a restore verb on the CLI (D-0230).
# The call it makes is the pane's own: Vault.RestoreRevision then Vault.Save.
#
# This builds its OWN database. The compat fixture is asserted against an exact tree and the
# write-back fixture against exact values; mutating either here would make the three
# order-dependent.

set -euo pipefail

die()  { printf '\nHISTORY GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

db=${1:-}
[ -n "$db" ] || die "usage: verify-keepassxc-history.sh <history.kdbx>"
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

# BOTH sides need \r stripped — see verify-keepassxc-writeback.sh.
kpxc() { printf '%s\n' "$pw" | "$cli" "$@" | tr -d '\r'; }

project=compat-history
key=ROTATED
entry="env/${project}/${key}"

mkdir -p "$(dirname "$db")"
rm -f "$db"          # re-runnable locally, not only on a fresh CI checkout

step "seed: the shipped binary writes four values into one entry"
printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$db"
for value in v1-first v2-second v3-third v4-current; do
  printf '%s\n%s\n' "$pw" "$value" | "$kp" env set "$project" "$key" --vault "$db"
done

# The last <UUID> BEFORE the first <History>, which is the entry's own rather than a revision's.
#
# The seen-flag is load-bearing, and `{exit}` is what it replaces (F.16). Leaving the stream
# early closes the pipe while the `tr` upstream is still writing, and GNU coreutils reports that
# as `tr: write error: Broken pipe` with a non-zero status, which `set -euo pipefail` then
# correctly propagates - killing the gate before it had checked anything. It failed on every
# ubuntu-24.04 run and on no windows-2025 or macos-15 run, so a local pass proved nothing.
#
# `head` is not the alternative: it closes the pipe the same way and only moves the broken pipe
# one process to the left. Consuming the whole stream is the fix, and the history reader below
# already reads it that way, so the two now agree.
uuid_before=$(kpxc export -f xml "$db" \
  | tr -d '\t' \
  | awk '/<History>/{seen=1} !seen && /<UUID>/{last=$0} END{print last}')
[ -n "$uuid_before" ] || die "could not read the entry's UUID out of the XML export"

# ---------------------------------------------------------------------------------------
# The restore. Index 1 is newest-first, so it names v2-second: neither the current value nor
# the oldest revision — a restore that quietly took either end would still look right otherwise.
# ---------------------------------------------------------------------------------------
step "keypaste restores the revision at index 1"
KEYPASTE_RESTORER_PASSWORD=$pw KEYPASTE_RESTORER_ENTRY=$entry "$restorer" "$db" 1 \
  || die "the restore driver failed"

# The container is re-checked after a restore-save, exactly as the write-back gate re-checks it
# after an update: a format or KDF shift on this path would round-trip through keypaste perfectly
# and be invisible anywhere else.
hdr=$(od -An -v -tx1 -N12 "$db" | tr -d ' \n' | tr 'A-Z' 'a-z')
[ "${hdr:0:16}" = "03d9a29a67fb4bb5" ] || die "not a KDBX file after a keypaste restore (signature ${hdr:0:16})"
[ "${hdr:20:2}" = "04" ]               || die "KDBX major version changed on restore: 0x${hdr:20:2}"

info=$(kpxc db-info "$db") || die "KeePassXC cannot open the file keypaste restored into"
grep -Eqi '^[[:space:]]*KDF:[[:space:]]*Argon2' <<<"$info" \
  || die "KDF is no longer Argon2 after a keypaste restore. Got: $(grep -i '^[[:space:]]*KDF:' <<<"$info" || echo '<no KDF line>')"

step "KeePassXC reads the restored value as current"
current=$(kpxc show -a Password "$db" "$entry") || die "keepassxc-cli show failed after a restore"
diff -u <(printf '%s\n' 'v2-second') <(printf '%s\n' "$current") \
  || die "KeePassXC does not see the value keypaste restored"

# ---------------------------------------------------------------------------------------
# The full history. `show` has no history flag and keepassxc-cli has no history verb, so the
# XML export is the only reader KeePassXC offers for what a restore did to the other revisions.
# Every value ever written must still be there, including the one the restore displaced.
# ---------------------------------------------------------------------------------------
step "KeePassXC reads every revision the entry has had"
xml=$(kpxc export -f xml "$db") || die "keepassxc-cli export -f xml failed"
grep -q '<History>' <<<"$xml" || die "the exported entry carries no <History> element at all"

history=$(awk '/<History>/{inside=1} inside' <<<"$xml")
for value in v1-first v2-second v3-third v4-current; do
  grep -qF ">${value}<" <<<"$history" \
    || die "'${value}' is missing from the history KeePassXC reads back"
done

step "the entry KeePassXC reads is the entry keypaste restored into"
uuid_after=$(tr -d '\t' <<<"$xml" | awk '/<History>/{seen=1} !seen && /<UUID>/{last=$0} END{print last}')
[ "$uuid_after" = "$uuid_before" ] \
  || die "the entry's UUID changed across the restore: '${uuid_before}' became '${uuid_after}'"

# ---------------------------------------------------------------------------------------
# NEGATIVE CONTROL.
#
# Everything above only means something if this gate is still capable of failing. See
# verify-keepassxc-compat.sh (v) for why this is the cheapest insurance in the repository.
# Never remove it.
# ---------------------------------------------------------------------------------------
step "NEGATIVE CONTROL: the comparisons must be able to fail"
if diff -q <(printf '%s\n' 'v2-second-CORRUPTED') <(printf '%s\n' "$current") >/dev/null 2>&1; then
  die "a deliberately corrupted expectation still matched — this gate is not gating"
fi

if grep -qF '>v0-never-written<' <<<"$history"; then
  die "a value nothing ever wrote was found in the history — the history search is not searching"
fi

# A revision the entry does not have must be refused, and refused without changing the file:
# a driver that silently restored the nearest index would make every assertion above vacuous.
before_refusal=$(od -An -v -tx1 "$db" | tr -d ' \n')
set +e
KEYPASTE_RESTORER_PASSWORD=$pw KEYPASTE_RESTORER_ENTRY=$entry "$restorer" "$db" 99 >/dev/null 2>&1
refused_rc=$?
set -e
[ "$refused_rc" -eq 1 ] || die "restoring a revision that does not exist exited ${refused_rc}, expected 1"
[ "$(od -An -v -tx1 "$db" | tr -d ' \n')" = "$before_refusal" ] \
  || die "a refused restore changed the vault file"

printf '\nHISTORY GATE PASSED: KeePassXC reads the restored value and every revision behind it.\n'
