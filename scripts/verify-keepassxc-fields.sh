#!/usr/bin/env bash
# PERMANENT COMPATIBILITY GATE: the custom fields the shipped CLI writes are the ones KeePassXC reads (V.7a).
#
# KeePassXC imports a KeePass XML document holding one entry with a plain and a protected custom
# field, a tag, custom data, an `otp` attribute and a revision, and attaches a file. The custom data
# is also what makes KeePassXC write KDBX 4.0 rather than 3.1 under its default AES-KDF. The shipped CLI sets a new
# field, which it protects, and changes the plain one. KeePassXC reads both, its XML export marks the
# new one protected and leaves the changed one plain, and the entry keeps its other field, tag,
# custom data, attachment and `otp` in a file that is still KDBX 4.0. Setting three fields makes one revision.
# `otp`, `Password` and `password` are refused and the file stays byte-identical with no backup
# taken, and no listing carries a value. After KeePassXC merges in a newer copy of the entry,
# keypaste reads KeePassXC's value and the entry's history holds the one keypaste wrote.
#
# NEGATIVE CONTROL: a corrupted expectation, a changed byte and a plain field read as protected must
# each fail the comparison the checks here rest on.
#
# Usage:  scripts/verify-keepassxc-fields.sh <work-directory>
# Env:    KP_COMPAT_PASSWORD   master password for the vault         (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
set -euo pipefail

die()  { printf '\nFIELDS GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-fields.sh <work-directory>"
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

rm -rf "$dir"
mkdir -p "$dir/home"
export KEYPASTE_HOME="$dir/home"
unset KEYPASTE_VAULT KEYPASTE_KEYFILE

db="$dir/a.kdbx"
entry=api/Stripe

native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
bytes()  { od -An -v -tx1 "$1" | tr -d ' \n'; }
copies() { if [ -d "$1.backups" ]; then find "$1.backups" -maxdepth 1 -type f | wc -l | tr -d ' '; else echo 0; fi; }
header() { od -An -v -tx1 -N12 "$1" | tr -d ' \n' | tr '[:upper:]' '[:lower:]'; }
kx() {
  local sub=$1 target=$2; shift 2
  printf '%s\n' "$pw" | "$cli" "$sub" -q "$(native "$target")" "$@" | tr -d '\r'
}
uuid() { printf '%-16.16s' "$1" | base64; }

# The master password, then one value per prompt, written with \n between them.
kp_with() {
  local values=$1; shift
  printf '%s\n%b' "$pw" "$values" | "$kp" "$@" --vault "$db" | tr -d '\r'
}

# The export with every <History> block removed, so a value an edit dropped cannot be found in the
# revision that edit made.
current_xml() { kx export "$db" -f xml | awk '/<History>/{past=1} !past{print} /<\/History>/{past=0}'; }
history_xml() { kx export "$db" -f xml | awk '/<History>/{inside=1} inside{print} /<\/History>/{inside=0}'; }
revisions()   { history_xml | grep -c '<Entry>' || true; }

# "protected" or "plain" for the string called $2 in the XML on stdin, from its Value element's own tag.
protection() {
  awk -v key="<Key>$1</Key>" '
    !want && (p = index($0, key)) { want = 1; $0 = substr($0, p + length(key)) }
    want && (v = index($0, "<Value")) {
      tag = substr($0, v); tag = substr(tag, 1, index(tag, ">"))
      print (index(tag, "ProtectInMemory=\"True\"") ? "protected" : "plain"); exit
    }'
}

sentinels=(fields-login-pw fields-plain-value fields-other-secret JBSWY3DPEHPK3PXP fields-new-secret fields-plain-changed fields-three-a fields-three-b fields-three-c)

step "KeePassXC makes the vault: a plain and a protected field, a tag, custom data, otp, a revision and an attachment"
cat >"$dir/seed.xml" <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-fields</Generator><DatabaseName>a</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid root)</UUID><Name>Root</Name>
      <Group>
        <UUID>$(uuid api)</UUID><Name>api</Name>
        <Entry>
          <UUID>$(uuid stripe)</UUID>
          <Tags>fields-tag</Tags>
          <CustomData><Item><Key>fields.entry</Key><Value>fields-entry-data</Value></Item></CustomData>
          <String><Key>Title</Key><Value>Stripe</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">fields-login-pw</Value></String>
          <String><Key>Region</Key><Value>fields-plain-value</Value></String>
          <String><Key>Recovery</Key><Value ProtectInMemory="True">fields-other-secret</Value></String>
          <String><Key>otp</Key><Value ProtectInMemory="True">otpauth://totp/Stripe?secret=JBSWY3DPEHPK3PXP&amp;issuer=fields</Value></String>
          <History>
            <Entry>
              <UUID>$(uuid stripe)</UUID>
              <String><Key>Title</Key><Value>Stripe</Value></String>
              <String><Key>Password</Key><Value ProtectInMemory="True">fields-login-pw-0</Value></String>
            </Entry>
          </History>
        </Entry>
      </Group>
    </Group>
  </Root>
</KeePassFile>
EOF

head -c 4096 /dev/urandom >"$dir/blob.bin"
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/seed.xml")" "$(native "$db")" \
  || die "keepassxc-cli could not import the seed XML"
kx attachment-import "$db" "$entry" blob.bin "$(native "$dir/blob.bin")" >/dev/null \
  || die "keepassxc-cli could not attach a file"
[ "$(header "$db" | cut -c17-22)" = "000004" ] || die "KeePassXC did not make a KDBX 4.0 file: $(header "$db")"
[ "$(current_xml | protection Recovery)" = protected ] || die "KeePassXC did not keep Recovery protected"
[ "$(current_xml | protection Region)" = plain ] || die "KeePassXC did not keep Region plain"

step "the shipped CLI sets a new field and changes the plain one"
said=$(kp_with 'fields-new-secret\n' set "$entry" --field STRIPE_SECRET_KEY 2>&1) || die "set --field STRIPE_SECRET_KEY failed: ${said}"
said=$(kp_with 'fields-plain-changed\n' set "$entry" --field Region 2>&1) || die "set --field Region failed: ${said}"

step "KeePassXC reads both values, marks exactly the new one protected, and keeps everything else"
[ "$(kx show "$db" "$entry" -a STRIPE_SECRET_KEY)" = fields-new-secret ] || die "KeePassXC does not read the new field"
[ "$(kx show "$db" "$entry" -a Region)" = fields-plain-changed ] || die "KeePassXC does not read the changed plain field"
[ "$(kx show "$db" "$entry" -a Recovery)" = fields-other-secret ] || die "the other field changed"
current=$(current_xml)
[ "$(protection STRIPE_SECRET_KEY <<<"$current")" = protected ] || die "the new field is not protected in KeePassXC's export"
[ "$(protection Region <<<"$current")" = plain ] || die "the changed plain field became protected"
[ "$(protection Recovery <<<"$current")" = protected ] || die "the other field lost its protection"
grep -qF '<Tags>fields-tag</Tags>' <<<"$current" || die "the entry lost its tag"
grep -qF 'fields-entry-data' <<<"$current" || die "the entry lost its custom data"
grep -qF 'secret=JBSWY3DPEHPK3PXP' <<<"$current" || die "the entry lost its otp attribute"
rm -f "$dir/blob.out"
kx attachment-export "$db" "$entry" blob.bin "$(native "$dir/blob.out")" >/dev/null || die "KeePassXC cannot export the attachment"
cmp -s "$dir/blob.bin" "$dir/blob.out" || die "the attachment is not the bytes KeePassXC attached"
[ "$(header "$db" | cut -c17-22)" = "000004" ] || die "the vault is no longer KDBX 4.0: $(header "$db")"

step "setting three fields at once makes one revision"
before=$(revisions)
said=$(kp_with 'fields-three-a\nfields-three-b\nfields-three-c\n' set "$entry" --field A --field B --field C 2>&1) \
  || die "set with three --field failed: ${said}"
[ "$(revisions)" -eq $((before + 1)) ] || die "three fields made $(( $(revisions) - before )) revisions, not one"
for pair in A:a B:b C:c; do
  [ "$(kx show "$db" "$entry" -a "${pair%:*}")" = "fields-three-${pair#*:}" ] || die "KeePassXC does not read field ${pair%:*}"
done

step "otp, Password and password are refused and leave the file byte-identical with no backup"
for line in "set $entry --field otp" "set $entry --field Password" "set $entry --field password" "field rm $entry otp"; do
  was=$(bytes "$db")
  count=$(copies "$db")
  set +e
  # shellcheck disable=SC2086 # the line is split into its words on purpose
  said=$(kp_with 'refused-value\n' $line 2>&1)
  rc=$?
  set -e
  [ "$rc" -ne 0 ] || die "keypaste ${line} was accepted"
  [ "$was" = "$(bytes "$db")" ] || die "keypaste ${line} changed the vault"
  [ "$count" = "$(copies "$db")" ] || die "keypaste ${line} kept a backup"
  printf '    refused, bytes unchanged: %s\n' "$line"
done

step "no listing carries a value"
listed=$(kp_with '' field ls "$entry" 2>&1) || die "field ls failed: ${listed}"
grep -q '^STRIPE_SECRET_KEY  *protected$' <<<"$listed" || die "field ls does not name the new field as protected: ${listed}"
grep -q '^otp  *protected, read-only$' <<<"$listed" || die "field ls does not mark otp read-only: ${listed}"
for args in "field ls $entry --json" "ls" "ls --json"; do
  # shellcheck disable=SC2086 # the line is split into its words on purpose
  listed+=$(kp_with '' $args 2>&1) || die "keypaste ${args} failed"
done
for value in "${sentinels[@]}"; do
  grep -qF "$value" <<<"$listed" && die "a listing printed the value ${value}"
done

step "after KeePassXC merges a newer copy, keypaste reads KeePassXC's value and history keeps its own"
said=$(kp_with 'fields-before-merge\n' set "$entry" --field MERGED 2>&1) || die "set --field MERGED failed: ${said}"
history_xml | grep -qF fields-before-merge && die "the value about to be merged over is already in history"
kx export "$db" -f xml | awk -v id="$(uuid stripe)" '
  /<History>/ { inside = 1 }
  !inside && index($0, "<UUID>" id "</UUID>") { mine = 1 }
  mine && !inside { sub(/fields-before-merge/, "fields-after-merge"); sub(/<LastModificationTime>[^<]*</, "<LastModificationTime>2037-01-01T00:00:00Z<") }
  /<\/History>/ { inside = 0 }
  mine && !inside && /<\/Entry>/ { mine = 0 }
  { print }' >"$dir/newer.xml"
grep -qF fields-after-merge "$dir/newer.xml" || die "the newer copy does not hold the new value"
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/newer.xml")" "$(native "$dir/newer.kdbx")" \
  || die "keepassxc-cli could not import the newer copy"
printf '%s\n' "$pw" | "$cli" merge -q -s "$(native "$db")" "$(native "$dir/newer.kdbx")" >/dev/null \
  || die "keepassxc-cli could not merge the newer copy"
got=$(kp_with '' get "$entry" --field MERGED --show 2>/dev/null) || die "keypaste cannot read the merged field"
[ "$(tr -d '\r' <<<"$got")" = fields-after-merge ] || die "after the merge keypaste reads '${got}', not KeePassXC's value"
history_xml | grep -qF fields-before-merge || die "after the merge the history lost the value keypaste wrote"

step "NEGATIVE CONTROL: the comparisons must be able to fail"
if [ "$(kx show "$db" "$entry" -a Region)" = 'fields-plain-changed-CORRUPTED' ]; then
  die "a deliberately corrupted expectation still matched — this gate is not gating"
fi
[ "$(protection Region <<<"$current")" = protected ] && die "the protection reader calls a plain field protected"
printf 'x' >>"$dir/blob.out"
cmp -s "$dir/blob.bin" "$dir/blob.out" && die "the attachment comparison cannot see a changed byte"

printf '\nFIELDS GATE PASSED: KeePassXC reads the fields keypaste wrote with their protection, keeps its own, and keypaste reads what KeePassXC merged.\n'
