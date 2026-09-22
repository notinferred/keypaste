#!/usr/bin/env bash
#
# verify-keepassxc-xml-attach.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md law 4.6.
#
# `keypaste access --new-keyfile` attaches a KeePass XML keyfile that KeePassXC wrote, and KeePassXC
# then opens the vault with that keyfile and refuses it without. A build whose XML keyfile loader
# falls back to hashing the file keys the vault to the wrong material, which the NativeAOT binary
# did while trimming removed the types that loader reads; keypaste refuses the keyfile in such a
# build (D-0294), and this gate counts that as a failure too, because a refusal is not support. It
# runs first in the AOT job so the attach is checked even when the fuller keyfile gate cannot start.
#
# Usage:  scripts/verify-keepassxc-xml-attach.sh <work-dir>
# Env:    KP_COMPAT_PASSWORD   master password for the fixture   (required)
#         KPXC_CLI             path to keepassxc-cli              (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary        (default: the Release build)
#
# NEGATIVE CONTROL: it fails if keypaste refuses the XML keyfile, if KeePassXC cannot open the
# vault with it, or if KeePassXC opens the vault without it.

set -euo pipefail

die()  { printf '\nXML ATTACH GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-xml-attach.sh <work-dir>"
pw=${KP_COMPAT_PASSWORD:-}
[ -n "$pw" ] || die "KP_COMPAT_PASSWORD is not set"
cli=${KPXC_CLI:-keepassxc-cli}
if ! command -v "$cli" >/dev/null 2>&1 && [ ! -x "$cli" ]; then
  die "keepassxc-cli not found (KPXC_CLI='${cli}'). This gate must never be skipped or soft-passed."
fi

kp=${KEYPASTE_BIN:-}
if [ -z "$kp" ]; then
  kp=artifacts/bin/Keypaste.Cli/release/keypaste
  [ -x "$kp" ] || kp="${kp}.exe"
fi
[ -x "$kp" ] || die "keypaste binary not found at '$kp' (build it, or set KEYPASTE_BIN)"

native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

rm -rf "$dir"
mkdir -p "$dir"
keyfile="$dir/attached.keyx"
vault="$dir/attached.kdbx"

step "KeePassXC authors an XML keyfile"
printf '%s\n%s\n' "$pw" "$pw" \
  | "$cli" db-create -q -p --set-key-file "$(native "$keyfile")" "$(native "$dir/authoring.kdbx")" >/dev/null \
  || die "KeePassXC could not author an XML keyfile"
grep -q '<KeyFile>' "$keyfile" || die "what KeePassXC wrote at '$keyfile' is not an XML keyfile"

step "keypaste creates a password vault and attaches the keyfile"
printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$vault" >/dev/null 2>&1 || die "keypaste init failed"
said=$(printf '%s\n' "$pw" | "$kp" access --new-keyfile "$keyfile" --vault "$vault" 2>&1) \
  || die "keypaste refused to attach the XML keyfile KeePassXC wrote (this build cannot read XML keyfiles; see D-0294). It said: ${said}"

step "KeePassXC opens the vault with the keyfile, and refuses it without"
printf '%s\n' "$pw" | "$cli" db-info -q --key-file "$(native "$keyfile")" "$(native "$vault")" >/dev/null 2>&1 \
  || die "KeePassXC cannot open the vault with the XML keyfile keypaste attached; keypaste keyed it with other material"
if printf '%s\n' "$pw" | "$cli" db-info -q "$(native "$vault")" >/dev/null 2>&1; then
  die "KeePassXC opens the vault without the keyfile keypaste attached"
fi

printf '\nXML ATTACH GATE PASSED on %s\n' "$("$cli" --version | tr -d '\r')"
