#!/usr/bin/env bash
#
# verify-keepassxc-keyfile.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md law 4.6, and the unlock half of law 2.
#
# verify-keepassxc-compat.sh proves a vault keypaste CREATES opens in KeePassXC,
# verify-keepassxc-writeback.sh proves a vault keypaste MODIFIES still does,
# verify-keepassxc-history.sh proves a revision keypaste RESTORES is the one KeePassXC reads,
# verify-keepassxc-recyclebin.sh proves a DELETED entry is recoverable,
# verify-keepassxc-backup.sh proves the copy of a replaced vault is a usable vault, and
# verify-keepassxc-organize.sh proves a RENAMED entry keeps what keypaste does not model.
# This one covers what docs/STEPS.md V.1a1 added: a vault KeePassXC protected with a KEYFILE is one
# keypaste opens, writes and hands back, in every form such a keyfile can take. It also covers
# V.1a2: `keypaste access` changes a vault's password and keyfile, and KeePassXC opens the result
# with the new factors and refuses the old ones.
#
# WHO MAKES WHAT, which is the whole honesty of this gate:
#
#   * KeePassXC creates every vault here, with `db-create`. The opening half of this gate is about
#     reading somebody else's vault, which a fixture keypaste keyed would prove nothing about; the
#     access half starts from KeePassXC's vaults too, so only the change is keypaste's.
#
#   * KeePassXC also AUTHORS the XML keyfile: pointed at a path that does not exist, db-create
#     writes a KeePass XML keyfile there. The other three forms are bytes this script makes,
#     because they are not a format anybody generates — they are a 32-byte file, a 64-character
#     hex file, and any file at all. KeePassXC is handed them and leaves them untouched, which is
#     asserted below, because a tool that rewrote the file it was given would invalidate the case.
#
#   * EVERY read and write on the keypaste side is the SHIPPED BINARY (D-0012), through the real
#     command line with --keyfile. A core test that called Vault.Open would prove the library and
#     not the product; V-V.1a1 says so in as many words.
#
# What this gate exists to catch, and what a unit test cannot:
#
#   * A FORM keypaste reads differently from KeePassXC. The four are decided by content, in an
#     order (XML, 32 bytes, 64 hex, else hashed) that VaultKeyfile copies out of KcpKeyFile. If
#     those two ever disagree, a vault opens in one program and not the other, and only a real
#     KeePassXC on the other side of the file notices.
#
#   * A KEYFILE-ONLY vault refused for want of a password. An empty password is not "no password":
#     KcpPassword hashes whatever it is given, so an empty one contributes SHA-256 of nothing and
#     produces a different composite key. Getting this wrong locks out every passwordless vault
#     KeePassXC ever wrote, and every one of them opens fine in KeePassXC, so nothing but this
#     gate would report it.
#
#   * A SAVE that quietly drops the second factor. keypaste writes through the composite key the
#     open database holds; if a save ever re-keyed the file from the password alone, the vault
#     would still open in keypaste and would stop needing the keyfile at all. So after every write
#     the vault is re-opened WITHOUT the keyfile and that has to fail.
#
#   * A REFUSAL that writes. Five refusals are exercised — no keyfile, a wrong keyfile, a wrong
#     password, a missing keyfile and the vault offered as its own keyfile — and the vault is
#     compared byte for byte across all of them.
#
#   * The hashed-any-file WARNING going to stdout. `keypaste get` gets piped and `keypaste run`
#     hands stdout to a child, so a sentence about key material on stdout is somebody's script
#     breaking. It is asserted present on stderr and absent from stdout.
#
# Usage:  scripts/verify-keepassxc-keyfile.sh <keyfile.kdbx>
# Env:    KP_COMPAT_PASSWORD   master password for the fixtures         (required)
#         KPXC_CLI             path to keepassxc-cli                    (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary              (default: the Release build)
#
# NEGATIVE CONTROL: this gate fails if keypaste opens a keyfile vault without its keyfile, if it
# cannot open one of the four forms, if it cannot open a keyfile-only vault with an empty password,
# if a value it wrote is not what KeePassXC reads back, if a save stops requiring the keyfile, if
# any refusal changes one byte of the vault, if KeePassXC rewrites a keyfile it was handed, or if
# the fragile-keyfile warning is missing from stderr or present on stdout. It also fails if
# KeePassXC cannot open a vault with the password or keyfile `keypaste access` set, still opens it
# with the ones replaced, sees a changed cipher or KDF, or if an access refusal writes the vault or
# a backup, or if the copy an access change kept does not open with the old password.

set -euo pipefail

die()  { printf '\nKEYFILE GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

db=${1:-}
[ -n "$db" ] || die "usage: verify-keepassxc-keyfile.sh <keyfile.kdbx>"
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

dir=$(dirname "$db")
stem=$(basename "$db" .kdbx)
mkdir -p "$dir"

# Windows builds of keepassxc-cli want native paths; the keypaste build under test takes either.
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

# BOTH sides need \r stripped — see verify-keepassxc-writeback.sh.
kpxc()     { printf '%s\n' "$pw" | "$cli" "$@" | tr -d '\r'; }
kpxc_only() { "$cli" "$@" | tr -d '\r'; }   # a keyfile-only vault: --no-password, nothing to type

# The whole file, so a refusal can be shown to have written nothing at all.
bytes() { od -An -v -tx1 "$1" | tr -d ' \n'; }

project=compat-keyfile

# ---------------------------------------------------------------------------------------
# The four forms, each in its own vault KeePassXC created.
# ---------------------------------------------------------------------------------------
rm -f "$dir/$stem"-*.kdbx
# The copies a previous run's saves kept are keyed to that run's keyfile, which is
# regenerated below. Left in place, the newest of them is still an older run's and refuses
# to open, so the directory goes with the vaults.
rm -rf "$dir/$stem"-*.kdbx.backups
# A generated .keyx lands read-only, so a re-run cannot simply overwrite it.
chmod u+w "$dir/$stem"-*.key* 2>/dev/null || true
rm -f "$dir/$stem"-*.key "$dir/$stem"-*.keyx "$dir/$stem"-*.txt

xml_kf="$dir/$stem-xml.keyx"
raw_kf="$dir/$stem-raw32.key"
hex_kf="$dir/$stem-hex64.key"
any_kf="$dir/$stem-any.txt"

step "make the three keyfile forms nobody generates, and let KeePassXC author the XML one"
head -c 32 /dev/urandom > "$raw_kf"
head -c 32 /dev/urandom | od -An -v -tx1 | tr -d ' \n' | head -c 64 > "$hex_kf"
printf 'an ordinary file somebody picked, which is exactly the problem\n' > "$any_kf"
[ "$(wc -c <"$raw_kf")" -eq 32 ] || die "the 32-byte keyfile is not 32 bytes"
[ "$(wc -c <"$hex_kf")" -eq 64 ] || die "the hex keyfile is not 64 characters"

for form in xml raw32 hex64 any; do
  case $form in
    xml)   kf=$xml_kf ;;
    raw32) kf=$raw_kf ;;
    hex64) kf=$hex_kf ;;
    any)   kf=$any_kf ;;
  esac
  vault="$dir/$stem-$form.kdbx"

  before=''
  [ "$form" = xml ] || before=$(bytes "$kf")

  step "KeePassXC creates a vault keyed to the $form keyfile"
  printf '%s\n%s\n' "$pw" "$pw" \
    | "$cli" db-create -q -p --set-key-file "$(native "$kf")" "$(native "$vault")" \
    || die "KeePassXC could not create the $form vault"
  [ -s "$vault" ] || die "KeePassXC created no $form vault"

  if [ -n "$before" ]; then
    [ "$before" = "$(bytes "$kf")" ] \
      || die "KeePassXC rewrote the $form keyfile it was handed, so this case proves nothing"
  else
    [ -s "$xml_kf" ] || die "KeePassXC did not author an XML keyfile at '$xml_kf'"
    grep -q '<KeyFile>' "$xml_kf" || die "what KeePassXC wrote at '$xml_kf' is not a KeePass XML keyfile"
  fi

  # What KeePassXC chose, before keypaste has ever opened the file.
  container_before=$(kpxc db-info --key-file "$(native "$kf")" "$(native "$vault")" \
    | grep -E '^(Cipher|KDF):') || die "KeePassXC cannot report on the $form vault it just made"

  step "the shipped keypaste writes into the $form vault, with --keyfile"
  # 2>&1: `env set` reports what it did on stderr, keeping stdout for a command's result.
  out=$(printf '%s\n' "$pw" | "$kp" env set "$project" "TOKEN=v-$form" --vault "$vault" --keyfile "$kf" 2>&1) \
    || die "keypaste could not write the $form vault"
  grep -Eq '^(Set|Updated) env' <<<"$out"     || die "keypaste did not report writing the $form vault. It said: ${out}"

  step "KeePassXC reads back what keypaste wrote into the $form vault"
  got=$(kpxc show -q -a Password --key-file "$(native "$kf")" "$(native "$vault")" "env/$project/TOKEN") \
    || die "KeePassXC cannot open the $form vault keypaste wrote"
  [ "$got" = "v-$form" ] \
    || die "KeePassXC reads '$got' out of the $form vault, not 'v-$form'"

  step "the $form vault still needs its keyfile after keypaste saved it"
  if printf '%s\n' "$pw" | "$kp" ls --vault "$vault" >/dev/null 2>&1; then
    die "the $form vault opened WITHOUT its keyfile after a keypaste save. The save dropped the second factor."
  fi

  # The container is re-checked after the save, as the write-back and history gates do: a format
  # or KDF shift on this path would round-trip through keypaste perfectly and be invisible.
  hdr=$(od -An -v -tx1 -N12 "$vault" | tr -d ' \n' | tr 'A-Z' 'a-z')
  [ "${hdr:0:16}" = "03d9a29a67fb4bb5" ] || die "the $form vault is not a KDBX file after a keypaste save"
  [ "${hdr:20:2}" = "04" ]               || die "KDBX major version changed on the $form vault: 0x${hdr:20:2}"

  # NOT "the KDF is Argon2". keypaste pins Argon2 on a vault it CREATES, which
  # verify-keepassxc-compat.sh asserts; ApplyKeypasteFormatSettings deliberately does not run on
  # the Open path. This vault is somebody else's — KeePassXC's db-create writes KDBX4 with
  # AES-KDF — and the claim that matters here is that keypaste left the container exactly as it
  # found it. Silently re-keying somebody's vault on first save is a worse bug than any KDF
  # preference, and nothing inside keypaste would notice it.
  step "keypaste left the $form vault's cipher and KDF as KeePassXC chose them"
  container_after=$(kpxc db-info --key-file "$(native "$kf")" "$(native "$vault")" \
    | grep -E '^(Cipher|KDF):') || die "KeePassXC cannot report on the $form vault after a save"
  [ "$container_before" = "$container_after" ] \
    || die "keypaste changed the $form vault's container. Before: ${container_before}. After: ${container_after}"
done

# ---------------------------------------------------------------------------------------
# A vault with NO password at all: the case an empty KcpPassword would silently break.
# ---------------------------------------------------------------------------------------
only_kf="$dir/$stem-only.keyx"
only="$dir/$stem-only.kdbx"

step "KeePassXC creates a vault protected by a keyfile ALONE"
"$cli" db-create -q --set-key-file "$(native "$only_kf")" "$(native "$only")" \
  || die "KeePassXC could not create the keyfile-only vault"

step "keypaste opens it with an empty line where the password goes"
printf '\n' | "$kp" env set "$project" "TOKEN=v-only" --vault "$only" --keyfile "$only_kf" >/dev/null 2>&1 \
  || die "keypaste could not write the keyfile-only vault. An empty password is not no password: see KeePassInterop.BuildKey."

got=$(kpxc_only show -q --no-password -a Password --key-file "$(native "$only_kf")" "$(native "$only")" "env/$project/TOKEN") \
  || die "KeePassXC cannot open the keyfile-only vault keypaste wrote"
[ "$got" = "v-only" ] \
  || die "KeePassXC reads '$got' out of the keyfile-only vault, not 'v-only'"

step "a typed password is still refused on a vault that has none"
if printf 'not-blank\n' | "$kp" ls --vault "$only" --keyfile "$only_kf" >/dev/null 2>&1; then
  die "the keyfile-only vault opened with a password it does not have"
fi

# ---------------------------------------------------------------------------------------
# Refusals, and the bytes they must not touch.
# ---------------------------------------------------------------------------------------
vault="$dir/$stem-xml.kdbx"
kf=$xml_kf
before=$(bytes "$vault")

refuse() {
  local what=$1; shift
  if printf '%s\n' "$pw" | "$kp" ls --vault "$vault" "$@" >/dev/null 2>&1; then
    die "$what was accepted"
  fi
  [ "$before" = "$(bytes "$vault")" ] || die "$what changed the vault"
}

step "five refusals, none of which writes a byte"
refuse "the right password with no keyfile at all"
refuse "the right password with the wrong keyfile" --keyfile "$raw_kf"
refuse "a keyfile that is not there"               --keyfile "$dir/$stem-absent.keyx"
refuse "the vault offered as its own keyfile"      --keyfile "$vault"

if printf 'wrong-%s\n' "$pw" | "$kp" ls --vault "$vault" --keyfile "$kf" >/dev/null 2>&1; then
  die "the wrong password was accepted with the right keyfile"
fi
[ "$before" = "$(bytes "$vault")" ] || die "a wrong password changed the vault"

step "a refusal says which factor was offered, and which refusal it was"
said=$(printf '%s\n' "$pw" | "$kp" ls --vault "$vault" --keyfile "$raw_kf" 2>&1 >/dev/null || true)
grep -qF 'keyfile' <<<"$said" \
  || die "a wrong keyfile was reported as a wrong password alone. It said: ${said}"

said=$(printf '%s\n' "$pw" | "$kp" ls --vault "$vault" --keyfile "$vault" 2>&1 >/dev/null || true)
grep -qF 'not a keyfile' <<<"$said" \
  || die "the vault offered as its own keyfile was not named as such. It said: ${said}"

# ---------------------------------------------------------------------------------------
# The warning about the form that one edit destroys.
# ---------------------------------------------------------------------------------------
step "the fragile-keyfile warning is on stderr, and never on stdout"
anyv="$dir/$stem-any.kdbx"
err=$(printf '%s\n' "$pw" | "$kp" ls --vault "$anyv" --keyfile "$any_kf" 2>&1 >/dev/null || true)
grep -qF 'locks the vault for good' <<<"$err" \
  || die "opening with an arbitrary hashed file said nothing about it. stderr was: ${err}"

out=$(printf '%s\n' "$pw" | "$kp" ls --vault "$anyv" --keyfile "$any_kf" 2>/dev/null || true)
grep -qF 'locks the vault for good' <<<"$out" \
  && die "the fragile-keyfile warning reached stdout, where a pipe or a child process would get it"

step "and a keyfile somebody actually made says nothing of the kind"
err=$(printf '%s\n' "$pw" | "$kp" ls --vault "$vault" --keyfile "$kf" 2>&1 >/dev/null || true)
grep -qF 'locks the vault for good' <<<"$err" \
  && die "an XML keyfile was reported as fragile"

# ---------------------------------------------------------------------------------------
# A backup of a keyfile vault is a keyfile vault.
# ---------------------------------------------------------------------------------------
step "the copy a save kept opens under the same two factors"
backup=$(ls -1 "$vault.backups"/* 2>/dev/null | tail -n 1 || true)
[ -n "$backup" ] || die "a save over the $stem-xml vault kept no copy"

kpxc db-info --key-file "$(native "$kf")" "$(native "$backup")" >/dev/null \
  || die "the copy kept beside a keyfile vault does not open under its keyfile"

if printf '%s\n' "$pw" | "$kp" ls --vault "$backup" >/dev/null 2>&1; then
  die "the copy kept beside a keyfile vault opened WITHOUT the keyfile"
fi

# ---------------------------------------------------------------------------------------
# keypaste access (V.1a2): keypaste changes the key, KeePassXC opens the result.
# ---------------------------------------------------------------------------------------
new_pw="next-$pw"
acc="$dir/$stem-access.kdbx"
kpxc_as() { local secret=$1; shift; printf '%s\n' "$secret" | "$cli" "$@" | tr -d '\r'; }
opens_in_kpxc() { kpxc_as "$1" db-info "${@:2}" >/dev/null 2>&1; }
copies() { ls -1 "$1.backups" 2>/dev/null | wc -l | tr -d ' '; }

step "KeePassXC creates a password-only vault for keypaste to change"
printf '%s\n%s\n' "$pw" "$pw" | "$cli" db-create -q -p "$(native "$acc")" \
  || die "KeePassXC could not create the access vault"
printf '%s\n' "$pw" | "$kp" env set "$project" "TOKEN=v-access" --vault "$acc" >/dev/null 2>&1 \
  || die "keypaste could not write the access vault"
container_before=$(kpxc db-info "$(native "$acc")" | grep -E '^(Cipher|KDF):') \
  || die "KeePassXC cannot report on the access vault"
old_copy_count=$(copies "$acc")

step "keypaste changes the master password; KeePassXC opens with the new one and refuses the old"
printf '%s\n%s\n%s\n' "$pw" "$new_pw" "$new_pw" | "$kp" access --password --vault "$acc" >/dev/null 2>&1 \
  || die "keypaste access --password failed"
opens_in_kpxc "$new_pw" "$(native "$acc")" || die "KeePassXC cannot open the vault with the password keypaste set"
opens_in_kpxc "$pw" "$(native "$acc")" && die "KeePassXC still opens the vault with the old password"
got=$(kpxc_as "$new_pw" show -q -a Password "$(native "$acc")" "env/$project/TOKEN") \
  || die "KeePassXC cannot read the entry after the password change"
[ "$got" = "v-access" ] || die "KeePassXC reads '$got' after the password change, not 'v-access'"
container_after=$(kpxc_as "$new_pw" db-info "$(native "$acc")" | grep -E '^(Cipher|KDF):')
[ "$container_before" = "$container_after" ] \
  || die "the password change altered the container. Before: ${container_before}. After: ${container_after}"

step "the copy the change kept opens in KeePassXC under the OLD password only"
[ "$(copies "$acc")" -gt "$old_copy_count" ] || die "the password change kept no copy of the vault it replaced"
kept=$(ls -1 "$acc.backups"/* | sort | tail -n 1)
opens_in_kpxc "$pw" "$(native "$kept")" || die "the kept copy does not open with the old password"
opens_in_kpxc "$new_pw" "$(native "$kept")" && die "the kept copy opens with the new password"

for form in xml raw32 hex64; do
  case $form in
    xml)   kf=$xml_kf ;;
    raw32) kf=$raw_kf ;;
    hex64) kf=$hex_kf ;;
  esac
  step "keypaste attaches the $form keyfile; KeePassXC needs it from then on"
  before_kf=$(bytes "$kf")
  printf '%s\n' "$new_pw" | "$kp" access --new-keyfile "$kf" --vault "$acc" ${cur_kf:+--keyfile "$cur_kf"} >/dev/null 2>&1 \
    || die "keypaste access --new-keyfile could not attach the $form keyfile"
  [ "$before_kf" = "$(bytes "$kf")" ] || die "keypaste rewrote the $form keyfile it attached"
  opens_in_kpxc "$new_pw" --key-file "$(native "$kf")" "$(native "$acc")" \
    || die "KeePassXC cannot open the vault with the $form keyfile keypaste attached"
  opens_in_kpxc "$new_pw" "$(native "$acc")" && die "KeePassXC opens the vault without the $form keyfile"
  if [ -n "${cur_kf:-}" ]; then
    opens_in_kpxc "$new_pw" --key-file "$(native "$cur_kf")" "$(native "$acc")" \
      && die "KeePassXC still opens the vault with the keyfile the $form one replaced"
  fi
  cur_kf=$kf
done

step "keypaste removes the keyfile; KeePassXC opens with the password alone"
printf '%s\n' "$new_pw" | "$kp" access --remove-keyfile --vault "$acc" --keyfile "$cur_kf" >/dev/null 2>&1 \
  || die "keypaste access --remove-keyfile failed"
opens_in_kpxc "$new_pw" "$(native "$acc")" || die "KeePassXC cannot open the vault after its keyfile was removed"

refuse_access() {
  local what=$1 target=$2 secret=$3; shift 3
  local was count
  was=$(bytes "$target")
  count=$(copies "$target")
  if printf '%s\n%s\n%s\n' "$secret" "$new_pw" "$new_pw" | "$kp" access --vault "$target" "$@" >/dev/null 2>&1; then
    die "$what was accepted"
  fi
  [ "$was" = "$(bytes "$target")" ] || die "$what changed the vault"
  [ "$count" = "$(copies "$target")" ] || die "$what kept a backup"
}

step "access refusals write neither the vault nor a backup"
refuse_access "attaching an arbitrary hashed file"    "$acc" "$new_pw" --new-keyfile "$any_kf"
refuse_access "attaching the vault as its own keyfile" "$acc" "$new_pw" --new-keyfile "$acc"
refuse_access "a wrong current password"               "$acc" "wrong-$new_pw" --password
refuse_access "removing the only factor of a keyfile-only vault" "$only" "" --remove-keyfile --keyfile "$only_kf"

step "a keyfile-only vault swaps its keyfile and stays keyfile-only"
printf '\n' | "$kp" access --new-keyfile "$raw_kf" --vault "$only" --keyfile "$only_kf" >/dev/null 2>&1 \
  || die "keypaste could not swap the keyfile of a keyfile-only vault"
kpxc_only db-info --no-password --key-file "$(native "$raw_kf")" "$(native "$only")" >/dev/null \
  || die "KeePassXC cannot open the keyfile-only vault with the keyfile keypaste attached"
kpxc_only db-info --no-password --key-file "$(native "$only_kf")" "$(native "$only")" >/dev/null 2>&1 \
  && die "KeePassXC still opens the keyfile-only vault with the keyfile it replaced"

# The vault under test is the XML one, so the caller's path names a file that exists afterwards.
cp -f "$vault" "$db"

printf '\nKEYFILE GATE PASSED: four forms, a keyfile-only vault, five refusals, a backup and access changes, on %s\n' \
  "$("$cli" --version | tr -d '\r')"
