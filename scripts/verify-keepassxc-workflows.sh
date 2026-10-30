#!/usr/bin/env bash
# The supported vault workflows, on vaults KeePassXC made, through the shipped CLI and the desktop
# app, each result read back by KeePassXC (9.4, V-9.4). docs/FEATURES.md states KeePassXC
# compatibility from this gate alone (D-0307).
#
# KeePassXC imports one KeePass XML document three times: behind a password, behind a password and
# an XML keyfile, and behind the keyfile alone. The document carries what keypaste does not model —
# meta, group and entry CustomData, unprotected and protected custom strings, KeePassXC's own otp,
# tags, auto-type settings (a group's sequence, a group that turns it off, an item's sequence and window
# associations, one a regular expression and one holding a placeholder) and a revision KeePassXC wrote —
# and KeePassXC then attaches a binary and a text file. The app reports the sequence keypaste would type
# for a set of window titles, typing nothing, when KeePassXC has made each vault and after the last save.
# Each vault is AES-KDF and KDBX 4.0, as KeePassXC writes them.
#
# On each vault: open, edit, restore a revision, set and remove custom fields and tag and untag from
# the CLI and from the app, organize, delete and recover, restore a backup, export, and change access. On a fourth, the app makes one item from each template (N.4). The app half runs through tests/Keypaste.AppDriver, which presses the
# commands the desktop's screens bind to. After every write KeePassXC opens the vault with its current
# factors, reads the value the workflow wrote, finds every unmodelled marker, exports both attachments
# byte for byte, and reports the cipher and KDF it chose. Every refusal leaves the vault byte-identical
# and keeps no backup. Creation runs once, through both front ends.
#
# KeePassXC imports a document with no custom data as KDBX 3.1. The app unlocks it without writing, and the CLI's
# first save writes KDBX 4.0 with KeePassXC's cipher and KDF and keeps the 3.1 file whole as its one backup, which
# KeePassXC still reads.
#
# keepassxc-cli cannot choose a cipher, so the app driver writes a Twofish vault with the vendored library, as
# KeePass with its Twofish plugin writes one. The shipped CLI saves it and KeePassXC reads it as Twofish; KeePassXC
# then saves an entry of its own into it, keeping Twofish; the CLI reads that entry, and the app reads it and saves.
#
# The document also carries KeePassXC-Browser's data as KeePassXC 2.8.0 writes it (8.1d, 9.4's limit): a pairing in the
# meta CustomData (KPXC_BROWSER_<id> holding the extension's identification key, and its _CREATED_ stamp), an entry's
# remembered Allow list (KeePassXC-Browser Settings) and an entry's BrowserHideEntry. After every write each item reads back
# from KeePassXC with its key and value as imported. On a fifth vault, after keypaste's CLI and app have saved it, keypaste's
# owner, holding it as the app does, refuses that pairing when tests/Keypaste.FakeBrowser presents it to keypaste's relay, and
# answers a database hash of its own.
#
# NEGATIVE CONTROL: a vault KeePassXC stripped of an attachment, one imported without its entry CustomData, and one imported
# without KeePassXC-Browser's pairing must each fail the same check every workflow passes, and KeePassXC's AES vault must fail
# the Twofish check.
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='WORKFLOWS GATE FAILED: '

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-workflows.sh <work-directory>"
. "$(dirname "${BASH_SOURCE[0]}")/lib/kpxc.sh"
kp=$(keypaste_bin)
drv=$(app_driver)
fake=$(resolve KEYPASTE_FAKE_BROWSER artifacts/bin/Keypaste.FakeBrowser/release/Keypaste.FakeBrowser)
require jq

rm -rf "$dir"
mkdir -p "$dir/home"
export KEYPASTE_HOME="$dir/home"

# The factors the vault under test has now. Every reader and writer below uses them, so a change of
# access is followed by every later step.
cur_pw=$pw
cur_kf=
new_pw=

# Replaces kpxc.sh's kx, which opens every vault with the gate's password alone.
kx() {
  local sub=$1 db=$2; shift 2
  local factors=(-q)
  [ -n "$cur_kf" ] && factors+=(--key-file "$(native "$cur_kf")")
  [ -n "$cur_pw" ] || factors+=(--no-password)
  printf '%s\n' "$cur_pw" | "$cli" "$sub" "${factors[@]}" "$(native "$db")" "$@" | tr -d '\r'
}

# Every prompt a verb can ask, in order: the current password, then a new one twice.
kp_cli() {
  local db=$1; shift
  printf '%s\n%s\n%s\n' "$cur_pw" "$new_pw" "$new_pw" | "$kp" "$@" --vault "$db" ${cur_kf:+--keyfile "$cur_kf"}
}

app() {
  KEYPASTE_DRIVER_PASSWORD=$cur_pw KEYPASTE_DRIVER_KEYFILE=${cur_kf:+$(native "$cur_kf")} \
    KEYPASTE_DRIVER_NEW_PASSWORD=$new_pw "$drv" "$@"
}

value() { kx show "$1" "$2" -a "$3"; }

expect() {
  local db=$1 entry=$2 field=$3 want=$4 got
  got=$(value "$db" "$entry" "$field") || die "KeePassXC cannot read $field of '$entry'"
  [ "$got" = "$want" ] || die "KeePassXC reads $field '$got' of '$entry', not '$want'"
}

markers=(kp94-meta-value kp94-group-value kp94-entry-value kp94-plain-value kp94-secret-value kp94-window kp94-tag KP7BOTPJBSWY3DPE
  kp4a1-group kp4a1-item kp4a1-regex kp4a1-placeholder kp4a1-title)

# KeePassXC-Browser's items in KeePassXC's export $1 outside <History>, one "<Key>..</Key><Value>..</Value>" line each,
# sorted: a save may reorder a CustomData's items or stamp them when the file becomes KDBX 4.1, and neither changes an item.
browser_data() {
  tr -d '\r\n\t' <"$1" \
    | sed 's#<Item>#\n#g; s#</Item>#\n#g' \
    | sed 's#<LastModificationTime>[^<]*</LastModificationTime>##' \
    | grep -E '^<Key>(KPXC_BROWSER_|_CREATED_|KeePassXC-Browser Settings|BrowserHideEntry)' \
    | sort
}

# KeePassXC's export $1 outside <History>, written as $1.current: every edit copies the entry into its history, so a
# marker the edit dropped from the entry itself would still be found in the revision.
outside_history() { awk '/<History>/{past=1} !past{print} /<\/History>/{past=0}' "$1" >"$1.current"; }

# Whether KeePassXC still sees everything keypaste does not model, on the entry at $2.
check_intact() {
  local db=$1 entry=$2 xml="$dir/intact.xml" marker name
  kx export "$db" -f xml >"$xml" 2>/dev/null || { echo "KeePassXC cannot open $db"; return 1; }
  outside_history "$xml"
  for marker in "${markers[@]}"; do
    grep -qF "$marker" "$xml.current" || { echo "KeePassXC no longer finds $marker outside history in $db"; return 1; }
  done
  [ "$(browser_data "$xml.current")" = "$browser_baseline" ] \
    || { echo "KeePassXC no longer reads KeePassXC-Browser's pairing and settings as imported in $db"; return 1; }
  grep -qi '>false<' <<<"$(awk '/<Name>review<\/Name>/{group=1} group && /<EnableAutoType>/{print; exit}' "$xml.current")" \
    || { echo "KeePassXC no longer reads Auto-Type as off for the review group in $db"; return 1; }
  for name in blob.bin note.txt; do
    rm -f "$dir/attachment.out"
    kx attachment-export "$db" "$entry" "$name" "$(native "$dir/attachment.out")" >/dev/null 2>&1 \
      || { echo "KeePassXC cannot export $name from '$entry' in $db"; return 1; }
    cmp -s "$dir/attachment.out" "$dir/$name" || { echo "$name in '$entry' is not the bytes KeePassXC attached"; return 1; }
  done
  [ "$(kx db-info "$db" | grep -E '^(Cipher|KDF):')" = "$container" ] \
    || { echo "the cipher or KDF of $db is not the one KeePassXC chose"; return 1; }
  [ "$(header "$db" | cut -c21-22)" = "04" ] || { echo "$db is no longer KDBX 4"; return 1; }
}

intact() { local why; why=$(check_intact "$@") || die "$why"; }

# "protected" or "plain" for the custom string called $1 on the entry, outside <History>, in the vault $2.
protection() {
  kx export "$2" -f xml | awk '/<History>/{past=1} !past{print} /<\/History>/{past=0}' >"$dir/protection.xml"
  awk -v key="<Key>$1</Key>" '
    !want && (p = index($0, key)) { want = 1; $0 = substr($0, p + length(key)) }
    want && (v = index($0, "<Value")) {
      tag = substr($0, v); tag = substr(tag, 1, index(tag, ">"))
      print (index(tag, "ProtectInMemory=\"True\"") ? "protected" : "plain"); exit
    }' "$dir/protection.xml"
}

# A refusal is exit 1 from the driver (3 is a driver that could not arrange the act) and any failure
# from the CLI, and either way the vault's bytes and backup count are what they were.
refused() {
  local what=$1 db=$2; shift 2
  local was count rc=0
  was=$(bytes "$db")
  count=$(copies "$db")
  "$@" >"$dir/refusal.out" 2>&1 || rc=$?
  [ "$rc" -ne 0 ] || die "$what was accepted"
  if [ "$1" = app ] && [ "$rc" -ne 1 ]; then
    die "$what: the driver exited $rc: $(cat "$dir/refusal.out")"
  fi
  [ "$was" = "$(bytes "$db")" ] || die "$what changed the vault's bytes"
  [ "$count" = "$(copies "$db")" ] || die "$what kept a backup"
  printf '    refused, bytes unchanged: %s\n' "$what"
}

did() { local what=$1; shift; "$@" >"$dir/did.out" 2>&1 || die "$what failed: $(cat "$dir/did.out")"; }

# The sequence the app would type into each window, read from the settings KeePassXC imported, typing nothing (P.4a1).
autotype_report() {
  local db=$1 got want
  want=$(cat <<'WANT'
servers/database "kp94-window": association 1 {PASSWORD}{ENTER}
servers/database "KP94-WINDOW": association 1 {PASSWORD}{ENTER}
servers/database "kp4a1-regex 42": association 2 {USERNAME}{ENTER}
servers/database "Sign in: database kp4a1-placeholder - Browser": association 3 {C:kp4a1-title}{PASSWORD}{ENTER}
servers/database "another window": group {USERNAME}{TAB}{PASSWORD}{C:kp4a1-group}{ENTER}
servers/cache "another window": item {C:kp4a1-item}{PASSWORD}{ENTER}
review/plain "another window": off: Auto-Type is off for a group this item is in.
archive/occupied "another window": default {PASSWORD}{ENTER}
WANT
  )
  got=$(app autotype "$db" servers/database kp94-window servers/database KP94-WINDOW servers/database "kp4a1-regex 42" \
    servers/database "Sign in: database kp4a1-placeholder - Browser" servers/database "another window" \
    servers/cache "another window" review/plain "another window" archive/occupied "another window" 2>&1 | tr -d '\r') \
    || die "the app's Auto-Type report failed: $got"
  [ "$got" = "$want" ] || die "the app reports other sequences than KeePassXC's settings give: $got"
}

# The number of revisions KeePassXC holds for the entry with the UUID $2 in the vault $1.
# The export goes to a file first: awk stops reading once it has counted, and under pipefail the
# rest of a large export written into a closed pipe fails the gate (F.26).
revisions() {
  kx export "$1" -f xml >"$dir/revisions.xml" || die "KeePassXC cannot export $1"
  awk -v id="<UUID>$2</UUID>" '
    /<History>/ { inhist = 1; if (mine) n = 0; next }
    /<\/History>/ { inhist = 0; if (mine) { print n; found = 1; exit } next }
    inhist { if (mine && /<Entry>/) n++; next }
    /<UUID>/ { mine = index($0, id) > 0 }
    END { if (!found) print 0 }' "$dir/revisions.xml"
}

# The UUID KeePassXC holds for the entry at the path $2 in the vault $1, spelled as uuid spells one.
uuid_at() { value "$1" "$2" Uuid | tr -d '{}-' | xxd -r -p | base64; }

# The number of revisions KeePassXC holds for the entry at the path $2 in the vault $1.
revisions_at() {
  local id
  id=$(uuid_at "$1" "$2") || die "KeePassXC cannot read the UUID of '$2'"
  revisions "$1" "$id"
}

# C.2's notes: two keys, a GitHub token on a line of its own, a PEM block and a sentence. The token
# is assembled here so no literal token sits in the repository.
gh_token="ghp_$(printf 'kp2c%.0s' 1 2 3 4 5 6 7 8 9)"

# The identification key KeePassXC-Browser paired with KeePassXC under kp81d-keepassxc, which the extension presents again.
kpxc_key=$(head -c 32 /dev/urandom | base64 | tr -d '\n')
kept_notes=$'-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC\n-----END PRIVATE KEY-----\nRecovery codes are in the safe.'

cat >"$dir/seed.xml" <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta>
    <Generator>verify-keepassxc-workflows</Generator>
    <DatabaseName>kp94</DatabaseName>
    <CustomData>
      <Item><Key>kp94.meta</Key><Value>kp94-meta-value</Value></Item>
      <Item><Key>KPXC_BROWSER_kp81d-keepassxc</Key><Value>$kpxc_key</Value></Item>
      <Item><Key>_CREATED_kp81d-keepassxc</Key><Value>10/24/26 10:00 AM</Value></Item>
    </CustomData>
  </Meta>
  <Root>
    <Group>
      <UUID>$(uuid root)</UUID><Name>Root</Name>
      <Group>
        <UUID>$(uuid servers)</UUID><Name>servers</Name>
        <DefaultAutoTypeSequence>{USERNAME}{TAB}{PASSWORD}{C:kp4a1-group}{ENTER}</DefaultAutoTypeSequence>
        <CustomData><Item><Key>kp94.group</Key><Value>kp94-group-value</Value></Item></CustomData>
        <Entry>
          <UUID>$(uuid database)</UUID>
          <Tags>kp94-tag</Tags>
          <String><Key>Title</Key><Value>database</Value></String>
          <String><Key>UserName</Key><Value>admin</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">pw-2</Value></String>
          <String><Key>kp94-plain</Key><Value>kp94-plain-value</Value></String>
          <String><Key>kp94-secret</Key><Value ProtectInMemory="True">kp94-secret-value</Value></String>
          <String><Key>kp7b-change</Key><Value>kp7b-change-value</Value></String>
          <String><Key>kp7b-remove</Key><Value ProtectInMemory="True">kp7b-remove-value</Value></String>
          <String><Key>otp</Key><Value ProtectInMemory="True">otpauth://totp/kp94?secret=KP7BOTPJBSWY3DPE&amp;issuer=kp94</Value></String>
          <AutoType><Enabled>True</Enabled><DataTransferObfuscation>0</DataTransferObfuscation>
            <Association><Window>kp94-window</Window><KeystrokeSequence>{PASSWORD}{ENTER}</KeystrokeSequence></Association>
            <Association><Window>//^kp4a1-regex [0-9]+//</Window><KeystrokeSequence>{USERNAME}{ENTER}</KeystrokeSequence></Association>
            <Association><Window>*{TITLE} kp4a1-placeholder*</Window><KeystrokeSequence>{C:kp4a1-title}{PASSWORD}{ENTER}</KeystrokeSequence></Association>
          </AutoType>
          <CustomData>
            <Item><Key>kp94.entry</Key><Value>kp94-entry-value</Value></Item>
            <Item><Key>KeePassXC-Browser Settings</Key><Value>{"Allow":["kp81d.example"],"Deny":[],"Realm":""}</Value></Item>
          </CustomData>
          <History>
            <Entry>
              <UUID>$(uuid database)</UUID>
              <String><Key>Title</Key><Value>database</Value></String>
              <String><Key>Password</Key><Value ProtectInMemory="True">pw-1</Value></String>
            </Entry>
          </History>
        </Entry>
        <Entry>
          <UUID>$(uuid cache)</UUID>
          <String><Key>Title</Key><Value>cache</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">c-1</Value></String>
          <AutoType><Enabled>True</Enabled><DataTransferObfuscation>0</DataTransferObfuscation><DefaultSequence>{C:kp4a1-item}{PASSWORD}{ENTER}</DefaultSequence></AutoType>
          <CustomData><Item><Key>BrowserHideEntry</Key><Value>true</Value></Item></CustomData>
        </Entry>
      </Group>
      <Group>
        <UUID>$(uuid archive)</UUID><Name>archive</Name>
        <Entry>
          <UUID>$(uuid occupied)</UUID>
          <String><Key>Title</Key><Value>occupied</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">o-1</Value></String>
          <History>
            <Entry>
              <UUID>$(uuid occupied)</UUID>
              <String><Key>Title</Key><Value>occupied</Value></String>
              <String><Key>Password</Key><Value ProtectInMemory="True">o-0</Value></String>
            </Entry>
          </History>
        </Entry>
      </Group>
      <Group>
        <UUID>$(uuid review)</UUID><Name>review</Name><EnableAutoType>false</EnableAutoType>
        <Entry>
          <UUID>$(uuid stripe)</UUID>
          <String><Key>Title</Key><Value>stripe</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">s-1</Value></String>
          <String><Key>Notes</Key><Value>STRIPE_SECRET_KEY=sk_test_1
export OPENAI_API_KEY=sk-proj-2
$gh_token
$kept_notes</Value></String>
        </Entry>
        <Entry>
          <UUID>$(uuid plain)</UUID>
          <String><Key>Title</Key><Value>plain</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">p-1</Value></String>
          <String><Key>Notes</Key><Value>Recovery codes are in the safe.</Value></String>
        </Entry>
      </Group>
      <Group>
        <UUID>$(uuid env)</UUID><Name>env</Name>
        <Group>
          <UUID>$(uuid kp94)</UUID><Name>kp94</Name>
          <Entry>
            <UUID>$(uuid token)</UUID>
            <Tags>env:kp94</Tags>
            <String><Key>Title</Key><Value>deploy</Value></String>
            <String><Key>TOKEN</Key><Value ProtectInMemory="True">t-1</Value></String>
          </Entry>
        </Group>
      </Group>
    </Group>
  </Root>
</KeePassFile>
EOF

head -c 4096 /dev/urandom >"$dir/blob.bin"
printf 'kp94 text attachment\r\nsecond line\n' >"$dir/note.txt"
printf 'an ordinary file, keyed by its hash\n' >"$dir/any.txt"

# XML keyfiles KeePassXC writes itself, for keypaste to attach.
for name in next next2; do
  printf '%s\n%s\n' "$pw" "$pw" | "$cli" db-create -q -p --set-key-file "$(native "$dir/$name.keyx")" \
    "$(native "$dir/$name-throwaway.kdbx")" || die "KeePassXC could not write the $name keyfile"
done

# $1 vault, $2 password or empty, $3 keyfile or empty, $4 seed document.
kpxc_import() {
  local db=$1 secret=$2 kf=$3 seed=$4 args=(-q)
  [ -n "$secret" ] && args+=(-p)
  [ -n "$kf" ] && args+=(--set-key-file "$(native "$kf")")
  printf '%s\n%s\n' "$secret" "$secret" | "$cli" import "${args[@]}" "$(native "$seed")" "$(native "$db")" \
    || die "KeePassXC could not import $seed into $db"
  cur_pw=$secret
  cur_kf=$kf
  kx attachment-import "$db" servers/database blob.bin "$(native "$dir/blob.bin")" >/dev/null \
    && kx attachment-import "$db" servers/database note.txt "$(native "$dir/note.txt")" >/dev/null \
    || die "KeePassXC could not attach files in $db"
}

# ---------------------------------------------------------------------------------------
# NEGATIVE CONTROL first: the check below must be able to fail.
# ---------------------------------------------------------------------------------------
step "the intact check fails on a vault missing an attachment, its entry CustomData or KeePassXC-Browser's pairing"
kpxc_import "$dir/control.kdbx" "$pw" "" "$dir/seed.xml"
container=$(kx db-info "$dir/control.kdbx" | grep -E '^(Cipher|KDF):')
kx export "$dir/control.kdbx" -f xml >"$dir/baseline.xml" || die "KeePassXC cannot export the control vault"
outside_history "$dir/baseline.xml"
browser_baseline=$(browser_data "$dir/baseline.xml.current")
[ "$(printf '%s\n' "$browser_baseline" | wc -l | tr -d ' ')" = 4 ] \
  || die "KeePassXC did not import KeePassXC-Browser's four items: $browser_baseline"
grep -qF "<Key>KPXC_BROWSER_kp81d-keepassxc</Key><Value>$kpxc_key</Value>" <<<"$browser_baseline" \
  || die "KeePassXC did not import the pairing's key as written"
intact "$dir/control.kdbx" servers/database
kx attachment-rm "$dir/control.kdbx" servers/database note.txt >/dev/null || die "KeePassXC could not remove an attachment"
check_intact "$dir/control.kdbx" servers/database >/dev/null && die "the intact check passed a vault missing an attachment"
grep -v 'kp94.entry' "$dir/seed.xml" >"$dir/seed-stripped.xml"
kpxc_import "$dir/control-stripped.kdbx" "$pw" "" "$dir/seed-stripped.xml"
check_intact "$dir/control-stripped.kdbx" servers/database >/dev/null && die "the intact check passed a vault missing its entry CustomData"
grep -v 'KPXC_BROWSER_kp81d' "$dir/seed.xml" >"$dir/seed-unpaired.xml"
kpxc_import "$dir/control-unpaired.kdbx" "$pw" "" "$dir/seed-unpaired.xml"
check_intact "$dir/control-unpaired.kdbx" servers/database >/dev/null && die "the intact check passed a vault missing KeePassXC-Browser's pairing"
echo "    all three controls fail the check"

exercise() {
  local kind=$1 secret=$2 kf=$3
  local db="$dir/$kind.kdbx" original="$dir/$kind.kpxc" copy="$dir/$kind-copy.kdbx" first listed
  new_pw=

  step "[$kind] KeePassXC makes the vault"
  kpxc_import "$db" "$secret" "$kf" "$dir/seed.xml"
  container=$(kx db-info "$db" | grep -E '^(Cipher|KDF):') || die "KeePassXC cannot report on the $kind vault"
  grep -qx 'KDF: AES (1000000 rounds)' <<<"$container" || die "KeePassXC did not make an AES-KDF vault: $container"
  [ "$(header "$db" | cut -c17-22)" = "000004" ] || die "KeePassXC did not make KDBX 4.0"
  cp "$db" "$original"
  intact "$db" servers/database

  step "[$kind] auto-type: the app reports the sequence it would type into each window, typing nothing"
  autotype_report "$db"
  cmp -s "$db" "$original" || die "the app's Auto-Type report wrote to the vault"

  step "[$kind] open: the CLI lists it and the app unlocks it"
  listed=$(kp_cli "$db" ls | tr -d '\r') || die "keypaste ls cannot open the $kind vault"
  grep -qx '  database' <<<"$listed" || die "keypaste ls does not list servers/database"
  did "the app's unlock" app open "$db"
  if [ -n "$cur_kf" ]; then
    local held=$cur_kf
    cur_kf=
    refused "the CLI opening without its keyfile" "$db" kp_cli "$db" ls
    refused "the app opening without its keyfile" "$db" app open "$db"
    cur_kf="$dir/next.keyx"
    refused "the CLI opening with the wrong keyfile" "$db" kp_cli "$db" ls
    refused "the app opening with the wrong keyfile" "$db" app open "$db"
    cur_kf=$held
  else
    cur_pw="wrong-$pw"
    refused "the CLI opening with a wrong password" "$db" kp_cli "$db" ls
    refused "the app opening with a wrong password" "$db" app open "$db"
    cur_pw=$secret
  fi

  step "[$kind] edit: the CLI updates a variable KeePassXC made, and keeps KeePassXC's bytes as the first backup"
  did "keypaste env set" kp_cli "$db" env set kp94 TOKEN=t-2
  expect "$db" env/kp94/deploy TOKEN t-2
  first=$(ls -1 "$db.backups")
  [ "$(copies "$db")" = 1 ] || die "the first save kept $(copies "$db") backups, not one"
  cmp -s "$db.backups/$first" "$original" || die "the first backup is not the vault KeePassXC wrote"
  intact "$db" servers/database
  refused "the CLI adding over an existing entry" "$db" kp_cli "$db" add servers/database

  step "[$kind] edit: the app saves the entry carrying everything keypaste does not model"
  new_pw=pw-3
  did "the app's edit" app edit "$db" servers/database "kp94 notes" "https://kp94.example"
  new_pw=
  expect "$db" servers/database Password pw-3
  expect "$db" servers/database Notes "kp94 notes"
  expect "$db" servers/database URL "https://kp94.example"
  intact "$db" servers/database

  # A restore makes the entry the revision whole, as KeePass does: KeePassXC wrote this one before it
  # attached anything, so the current entry loses what came later, and the state it replaced is the
  # newest revision, which a second restore puts back.
  step "[$kind] history: the app restores the revision KeePassXC wrote, then the state it replaced"
  did "the app's revision restore" app revision-restore "$db" servers/database oldest
  expect "$db" servers/database Password pw-1
  value "$db" servers/database kp94-secret >/dev/null 2>&1 \
    && die "the restored revision kept a custom field KeePassXC added after it"
  did "the app's revision restore" app revision-restore "$db" servers/database 0
  expect "$db" servers/database Password pw-3
  expect "$db" servers/database kp94-secret kp94-secret-value
  intact "$db" servers/database

  step "[$kind] fields: the CLI sets a protected and a plain field on the entry KeePassXC made, and removes one"
  new_pw=kp7a-secret-value
  did "keypaste set --field" kp_cli "$db" set servers/database --field kp7a-secret
  new_pw=kp7a-plain-value
  did "keypaste set --field --plain" kp_cli "$db" set servers/database --field kp7a-plain --plain
  new_pw=
  expect "$db" servers/database kp7a-secret kp7a-secret-value
  expect "$db" servers/database kp7a-plain kp7a-plain-value
  did "keypaste field rm" kp_cli "$db" field rm servers/database kp7a-plain
  value "$db" servers/database kp7a-plain >/dev/null 2>&1 && die "KeePassXC still finds the field keypaste removed"
  refused "the CLI writing a field named Password" "$db" kp_cli "$db" set servers/database --field Password
  intact "$db" servers/database

  step "[$kind] tags: the CLI tags the entry KeePassXC made into a project and untags it"
  did "keypaste env tag" kp_cli "$db" env tag kp94 servers/database -p prod --yes
  listed=$(value "$db" servers/database Tags) || die "KeePassXC cannot read the entry's tags"
  grep -qx 'env:kp94:prod' <<<"${listed//,/$'\n'}" || die "KeePassXC does not read the tag keypaste added: ${listed}"
  did "keypaste env untag" kp_cli "$db" env untag kp94 servers/database -p prod --yes
  listed=$(value "$db" servers/database Tags) || die "KeePassXC cannot read the entry's tags"
  grep -qx 'env:kp94:prod' <<<"${listed//,/$'\n'}" && die "KeePassXC still reads the tag keypaste removed: ${listed}"
  refused "the CLI writing a malformed project tag" "$db" kp_cli "$db" env tag kp94 servers/database -p Prod --yes
  intact "$db" servers/database

  step "[$kind] fields and tags in the app: add a protected field, change, protect and remove others, and tag"
  new_pw=kp7b-added-value
  did "the app's field add" app field-add "$db" servers/database kp7b-added protected
  new_pw=kp7b-changed-value
  did "the app's field replace" app field-set "$db" servers/database kp7b-change
  new_pw=
  [ "$(protection kp7b-change "$db")" = plain ] || die "replacing a plain field's value in the app changed its protection"
  did "the app's protection switch" app field-protect "$db" servers/database kp7b-change
  did "the app's field remove" app field-rm "$db" servers/database kp7b-remove
  expect "$db" servers/database kp7b-added kp7b-added-value
  expect "$db" servers/database kp7b-change kp7b-changed-value
  value "$db" servers/database kp7b-remove >/dev/null 2>&1 && die "KeePassXC still finds the field the app removed"
  [ "$(protection kp7b-added "$db")" = protected ] || die "KeePassXC does not read the field the app added as protected"
  [ "$(protection kp7b-change "$db")" = protected ] || die "KeePassXC does not read the field the app protected as protected"
  did "the app's tag add" app tag-add "$db" servers/database env:kp94:prod
  listed=$(value "$db" servers/database Tags) || die "KeePassXC cannot read the entry's tags"
  grep -qx 'env:kp94:prod' <<<"${listed//,/$'\n'}" || die "KeePassXC does not read the tag the app added: ${listed}"
  did "the app's tag remove" app tag-rm "$db" servers/database env:kp94:prod
  listed=$(value "$db" servers/database Tags) || die "KeePassXC cannot read the entry's tags"
  grep -qx 'env:kp94:prod' <<<"${listed//,/$'\n'}" && die "KeePassXC still reads the tag the app removed: ${listed}"
  new_pw=refused-value
  refused "the app adding a field named otp" "$db" app field-add "$db" servers/database otp protected
  new_pw=
  [ "$(header "$db" | cut -c17-22)" = "000004" ] || die "the app's field and tag edits moved the vault off KDBX 4.0"
  intact "$db" servers/database

  step "[$kind] notes: the app flags keys left in notes, remembers a dismissal, refuses a clash and moves them into fields"
  review() {
    app notes-review "$db" >"$dir/review.out" 2>&1 || die "the app's notes review failed: $(cat "$dir/review.out")"
    grep -qF -e sk_test_1 -e sk-proj-2 -e "$gh_token" "$dir/review.out" && die "the app's notes review printed a value"
    grep -qx "count $1" "$dir/review.out" || die "the app does not count $1 keys to review: $(cat "$dir/review.out")"
  }
  review 3
  for want in "key=STRIPE_SECRET_KEY kind=assignment" "key=OPENAI_API_KEY kind=assignment" "key=GITHUB_TOKEN kind=token"; do
    grep -qxF "finding entry=review/stripe $want state=needs-review" "$dir/review.out" \
      || die "the app does not flag $want on review/stripe: $(cat "$dir/review.out")"
  done
  [ "$(grep -c '^finding ' "$dir/review.out")" = 3 ] || die "the app flags other than three keys: $(cat "$dir/review.out")"
  did "the app's dismissal" app notes-dismiss "$db" review/stripe OPENAI_API_KEY
  review 2
  grep -qxF "finding entry=review/stripe key=OPENAI_API_KEY kind=assignment state=dismissed" "$dir/review.out" \
    || die "a later unlock forgot the dismissal: $(cat "$dir/review.out")"
  did "the app's review again" app notes-restore "$db" review/stripe OPENAI_API_KEY
  review 3
  new_pw=kp-c2-other-value
  did "keypaste set --field" kp_cli "$db" set review/stripe --field GITHUB_TOKEN
  new_pw=
  refused "the app moving a key onto a field holding another value" "$db" app notes-move "$db" review/stripe
  did "keypaste field rm" kp_cli "$db" field rm review/stripe GITHUB_TOKEN
  listed=$(revisions "$db" "$(uuid stripe)")
  did "the app's move" app notes-move "$db" review/stripe
  expect "$db" review/stripe STRIPE_SECRET_KEY sk_test_1
  expect "$db" review/stripe OPENAI_API_KEY sk-proj-2
  expect "$db" review/stripe GITHUB_TOKEN "$gh_token"
  for field in STRIPE_SECRET_KEY OPENAI_API_KEY GITHUB_TOKEN; do
    [ "$(protection "$field" "$db")" = protected ] || die "KeePassXC does not read $field as protected"
  done
  expect "$db" review/stripe Notes "$kept_notes"
  expect "$db" review/plain Notes "Recovery codes are in the safe."
  [ "$(revisions "$db" "$(uuid stripe)")" = $((listed + 1)) ] || die "the move did not add exactly one revision"
  kx export "$db" -f xml >"$dir/history.xml" || die "KeePassXC cannot export $db"
  grep -qF 'STRIPE_SECRET_KEY=sk_test_1' "$dir/history.xml" || die "KeePassXC finds no revision holding the old notes"
  review 0
  intact "$db" servers/database

  step "[$kind] organize: the app renames the group carrying CustomData and moves the entry out of it"
  did "the app's group rename" app group-rename "$db" servers hosts
  did "the app's rename and move" app relocate "$db" hosts/database archive database-old
  expect "$db" archive/database-old kp94-secret kp94-secret-value
  expect "$db" hosts/cache Password c-1
  [ "$(header "$db" | cut -c17-18)" = "00" ] || die "organizing raised the vault above KDBX 4.0"
  intact "$db" archive/database-old
  refused "the app moving onto an occupied name" "$db" app relocate "$db" hosts/cache archive occupied

  step "[$kind] delete and recover: the CLI recycles, the app restores; the app deletes and restores; the app restores under another title a name KeePassXC took"
  did "keypaste rm" kp_cli "$db" rm archive/database-old --yes
  listed=$(kx ls "$db" -R -f)
  grep -qx 'Recycle Bin/database-old' <<<"$listed" || die "KeePassXC does not find the entry in its recycle bin"
  did "the app's trash restore" app trash-restore "$db" database-old
  intact "$db" archive/database-old
  did "the app's delete" app delete "$db" hosts/cache
  listed=$(kx ls "$db" -R -f)
  grep -qx 'Recycle Bin/cache' <<<"$listed" || die "KeePassXC does not find the app's deletion in its recycle bin"
  did "the app's trash restore" app trash-restore "$db" cache
  expect "$db" hosts/cache Password c-1
  did "keypaste rm" kp_cli "$db" rm archive/occupied --yes
  kx add "$db" archive/occupied -u kpxc >/dev/null || die "KeePassXC could not add an entry"
  refused "the app restoring onto a name KeePassXC took" "$db" app trash-restore "$db" occupied
  did "the app's restore under another title" app trash-restore-as "$db" occupied
  expect "$db" "archive/occupied (restored)" Password o-1
  [ "$(uuid_at "$db" "archive/occupied (restored)")" = "$(uuid occupied)" ] \
    || die "KeePassXC finds another entry than the deleted one at 'archive/occupied (restored)'"
  [ "$(revisions_at "$db" "archive/occupied (restored)")" = 1 ] \
    || die "the restore under another title did not keep exactly the one revision the entry was deleted with"
  expect "$db" archive/occupied UserName kpxc
  listed=$(kx ls "$db" -R -f)
  grep -qx 'Recycle Bin/occupied' <<<"$listed" && die "KeePassXC still finds the restored entry in its recycle bin"
  intact "$db" archive/database-old

  step "[$kind] backup: the app restores the backup that holds KeePassXC's bytes, and exports"
  cur_pw="wrong-$pw"
  refused "the app restoring a backup with a wrong password" "$db" app backup-restore "$db" "$first"
  cur_pw=$secret
  did "the app's backup restore" app backup-restore "$db" "$first"
  cmp -s "$db" "$original" || die "the restored vault is not the bytes KeePassXC wrote"
  intact "$db" servers/database
  did "the app's export" app export "$db" "$copy"
  intact "$copy" servers/database

  step "[$kind] access: each change is read back by KeePassXC under the new factors only"
  refused "the CLI attaching an arbitrary hashed file" "$db" kp_cli "$db" access --new-keyfile "$dir/any.txt"
  refused "the app attaching an arbitrary hashed file" "$db" app access "$db" --attach "$(native "$dir/any.txt")"
  case $kind in
    password)
      new_pw="cli-$pw"
      did "keypaste access --password" kp_cli "$db" access --password
      cur_pw=$new_pw
      opens_only "$db"
      did "the app attaching a keyfile" app access "$db" --attach "$(native "$dir/next.keyx")"
      cur_kf="$dir/next.keyx"
      opens_only "$db"
      did "the app removing the keyfile" app access "$db" --remove-keyfile
      cur_kf=
      opens_only "$db"
      new_pw="app-$pw"
      did "the app changing the password" app access "$db" --password
      cur_pw=$new_pw
      ;;
    keyfile)
      did "the app replacing the keyfile" app access "$db" --attach "$(native "$dir/next.keyx")"
      cur_kf="$dir/next.keyx"
      opens_only "$db"
      did "keypaste access --remove-keyfile" kp_cli "$db" access --remove-keyfile
      cur_kf=
      opens_only "$db"
      did "keypaste access --new-keyfile" kp_cli "$db" access --new-keyfile "$dir/next2.keyx"
      cur_kf="$dir/next2.keyx"
      opens_only "$db"
      new_pw="app-$pw"
      did "the app changing the password" app access "$db" --password
      cur_pw=$new_pw
      ;;
    keyfile-only)
      refused "the CLI removing the only factor" "$db" kp_cli "$db" access --remove-keyfile
      refused "the app removing the only factor" "$db" app access "$db" --remove-keyfile
      did "the app swapping the keyfile" app access "$db" --attach "$(native "$dir/next.keyx")"
      cur_kf="$dir/next.keyx"
      opens_only "$db"
      did "keypaste swapping the keyfile" kp_cli "$db" access --new-keyfile "$dir/next2.keyx"
      cur_kf="$dir/next2.keyx"
      opens_only "$db"
      new_pw="app-$pw"
      did "the app setting a password and removing the keyfile" app access "$db" --password --remove-keyfile
      cur_pw=$new_pw
      cur_kf=
      ;;
  esac
  opens_only "$db"
  intact "$db" servers/database
  new_pw=

  step "[$kind] auto-type: after keypaste's saves the app reports the same sequences"
  autotype_report "$db"
}

# The vault opens with the current factors and not with the password or keyfile it had before.
opens_only() {
  local db=$1 held_pw=$cur_pw held_kf=$cur_kf
  kx db-info "$db" >/dev/null 2>&1 || die "KeePassXC cannot open $db with its new factors"
  if [ -n "$held_kf" ]; then
    cur_kf=
    kx db-info "$db" >/dev/null 2>&1 && die "KeePassXC opens $db without the keyfile it now needs"
  fi
  cur_kf=$held_kf
  if [ -n "$held_pw" ]; then
    cur_pw="not-$held_pw"
    kx db-info "$db" >/dev/null 2>&1 && die "KeePassXC opens $db with a wrong password"
  fi
  cur_pw=$held_pw
  intact "$db" servers/database
}

exercise password "$pw" ""
exercise keyfile "$pw" "$dir/keyfile.keyx"
exercise keyfile-only "" "$dir/keyfile-only.keyx"

# ---------------------------------------------------------------------------------------
# New items from templates, through the app, on a vault KeePassXC made (N.4).
# ---------------------------------------------------------------------------------------
# ---------------------------------------------------------------------------------------
# KeePassXC-Browser's data (8.1d): keypaste keeps it and grants nothing from it.
# ---------------------------------------------------------------------------------------
step "browser data: after keypaste's saves, keypaste's owner refuses KeePassXC's pairing and answers a hash of its own"
db="$dir/browser.kdbx"
kpxc_import "$db" "$pw" "" "$dir/seed.xml"
container=$(kx db-info "$db" | grep -E '^(Cipher|KDF):') || die "KeePassXC cannot report on the browser vault"
new_pw=
did "keypaste env set" kp_cli "$db" env set kp94 TOKEN=t-browser
new_pw=pw-browser
did "the app's edit" app edit "$db" servers/database "kp81d notes" "https://kp81d.example"
new_pw=
intact "$db" servers/database

hold_out="$dir/hold.txt"
exec 7> >(KEYPASTE_DRIVER_PASSWORD=$cur_pw exec "$drv" hold "$(native "$db")" >"$hold_out" 2>&1)
wait_for 'holding session' "$hold_out"
grep -q 'not served' "$hold_out" && die "the app unlocked the browser vault and did not serve it"
KEYPASTE_VAULT=$(native "$db") "$fake" "$kp" chrome --id-key "$kpxc_key" --within 60 get-databasehash test-associate=kp81d-keepassxc \
  >"$dir/keypaste-browser.txt" 2>&1 || die "keypaste's relay exited with an error: $(cat "$dir/keypaste-browser.txt")"
own_hash=$(sed -n 2p "$dir/keypaste-browser.txt" | jq -er '.hash') || die "keypaste answered no database hash: $(cat "$dir/keypaste-browser.txt")"
sed -n 3p "$dir/keypaste-browser.txt" | jq -e '.errorCode == "8"' >/dev/null \
  || die "keypaste's owner did not refuse KeePassXC's pairing: $(sed -n 3p "$dir/keypaste-browser.txt")"
exec 7>&-
wait_for '^shut down' "$hold_out"
intact "$db" servers/database
echo "    refused with error 8; keypaste's hash is $own_hash"

step "templates: the app makes one item from each template, and KeePassXC reads each whole, with no revision"
db="$dir/templates.kdbx"
cur_pw=$pw cur_kf= new_pw=
kpxc_import "$db" "$pw" "" "$dir/seed.xml"
new_pw=n4-login-pw
did "the app's new login" app item-new "$db" login servers n4-login --username n4-user --url https://n4.example --notes "n4 login notes" --tag n4
new_pw=sk-n4-api-key
did "the app's new API key" app item-new "$db" apikey servers n4-openai --key N4_API_KEY --tag env:kp94
new_pw=n4-db-pw
did "the app's new database" app item-new "$db" database servers n4-db --host db.n4.example:5432 --username n4-dbuser
new_pw=n4-server-pw
did "the app's new server" app item-new "$db" server servers n4-server --host n4.example --username root
new_pw=
did "the app's new secure note" app item-new "$db" securenote servers n4-note --notes "n4 secure note"

expect "$db" servers/n4-login UserName n4-user
expect "$db" servers/n4-login Password n4-login-pw
expect "$db" servers/n4-login URL https://n4.example
expect "$db" servers/n4-login Notes "n4 login notes"
expect "$db" servers/n4-openai N4_API_KEY sk-n4-api-key
[ "$(protection N4_API_KEY "$db")" = protected ] || die "KeePassXC does not read the API key the app made as protected"
expect "$db" servers/n4-db Host db.n4.example:5432
expect "$db" servers/n4-db UserName n4-dbuser
expect "$db" servers/n4-db Password n4-db-pw
expect "$db" servers/n4-server Host n4.example
expect "$db" servers/n4-server UserName root
expect "$db" servers/n4-server Password n4-server-pw
[ "$(protection Host "$db")" = plain ] || die "KeePassXC does not read the host the app wrote as a plain field"
expect "$db" servers/n4-note Notes "n4 secure note"
listed=$(value "$db" servers/n4-login Tags) || die "KeePassXC cannot read the login's tags"
grep -qx 'n4' <<<"${listed//,/$'\n'}" || die "KeePassXC does not read the tag the app gave the login: ${listed}"
listed=$(value "$db" servers/n4-openai Tags) || die "KeePassXC cannot read the API key's tags"
grep -qx 'env:kp94' <<<"${listed//,/$'\n'}" || die "KeePassXC does not read the project tag the app gave the API key: ${listed}"
for title in n4-login n4-openai n4-db n4-server n4-note; do
  [ "$(revisions_at "$db" "servers/$title")" = 0 ] || die "KeePassXC finds a revision of servers/$title, which the app made in one write"
done

new_pw=refused-value
refused "the app making an item whose title the folder already has" "$db" app item-new "$db" login servers n4-login
refused "the app making an API key named otp" "$db" app item-new "$db" apikey servers n4-otp --key otp
refused "the app making an API key whose name is not a variable's" "$db" app item-new "$db" apikey servers n4-spaced --key "api key"
refused "the app making an item with no title" "$db" app item-new "$db" login servers ""
new_pw=
[ "$(header "$db" | cut -c17-22)" = "000004" ] || die "the app's new items moved the vault off KDBX 4.0"
intact "$db" servers/database

# ---------------------------------------------------------------------------------------
# KDBX 3.1, which KeePassXC writes under its default AES-KDF when nothing needs KDBX 4.
# ---------------------------------------------------------------------------------------
step "KDBX 3.1: the CLI's first save writes KDBX 4 with KeePassXC's cipher and KDF and keeps the 3.1 file as its backup"
db="$dir/kdbx31.kdbx"
cur_pw=$pw cur_kf= new_pw=
cat >"$dir/seed31.xml" <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-workflows</Generator><DatabaseName>kp31</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid root31)</UUID><Name>Root</Name>
      <Group>
        <UUID>$(uuid env31)</UUID><Name>env</Name>
        <Group>
          <UUID>$(uuid kp31)</UUID><Name>kp31</Name>
          <Entry>
            <UUID>$(uuid deploy31)</UUID>
            <Tags>env:kp31</Tags>
            <String><Key>Title</Key><Value>deploy</Value></String>
            <String><Key>TOKEN</Key><Value ProtectInMemory="True">t31-1</Value></String>
          </Entry>
        </Group>
      </Group>
    </Group>
  </Root>
</KeePassFile>
EOF
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/seed31.xml")" "$(native "$db")" \
  || die "KeePassXC could not import the KDBX 3.1 seed"
[ "$(header "$db" | cut -c17-24)" = "01000300" ] || die "KeePassXC did not write KDBX 3.1 from a document with no custom data: $(header "$db")"
container=$(kx db-info "$db" | grep -E '^(Cipher|KDF):') || die "KeePassXC cannot report on the KDBX 3.1 vault"
cp "$db" "$dir/kdbx31.kpxc"
did "the app's unlock" app open "$db"
cmp -s "$db" "$dir/kdbx31.kpxc" || die "the app's unlock wrote to the KDBX 3.1 vault"
did "keypaste env set" kp_cli "$db" env set kp31 TOKEN=t31-2
[ "$(header "$db" | cut -c17-24)" = "00000400" ] || die "the CLI's first save did not write KDBX 4.0: $(header "$db")"
listed=$(kx db-info "$db" | grep -E '^(Cipher|KDF):') || die "KeePassXC cannot report on the vault the CLI converted"
[ "$listed" = "$container" ] || die "the CLI's save changed the cipher or KDF KeePassXC chose, from '$container' to '$listed'"
expect "$db" env/kp31/deploy TOKEN t31-2
[ "$(copies "$db")" = 1 ] || die "the first save kept $(copies "$db") backups, not one"
first=$(ls -1 "$db.backups")
cmp -s "$db.backups/$first" "$dir/kdbx31.kpxc" || die "the backup is not the KDBX 3.1 file KeePassXC wrote"
expect "$db.backups/$first" env/kp31/deploy TOKEN t31-1
did "the app's unlock" app open "$db"

# ---------------------------------------------------------------------------------------
# Twofish, which keepassxc-cli cannot choose (D-0292).
# ---------------------------------------------------------------------------------------
# Whether KeePassXC reads the vault at $1 as Twofish. A vault it cannot open fails the gate, so the AES control
# cannot pass by not opening. Captured first, so grep -q cannot close the pipe on it.
twofish() { local info; info=$(kx db-info "$1") || die "KeePassXC cannot open $1"; grep -Eq '^Cipher:[[:space:]]*Twofish' <<<"$info"; }

step "Twofish: KeePassXC reads a Twofish vault the CLI saved, the CLI reads one KeePassXC saved, and the app saves it"
db="$dir/twofish.kdbx"
cur_pw=$pw cur_kf= new_pw=
twofish "$dir/control.kdbx" && die "the Twofish check passes KeePassXC's AES vault"
new_pw=tf-1
did "the driver writing a Twofish vault" app twofish-create "$db"
new_pw=
twofish "$db" || die "KeePassXC does not read the driver's vault as Twofish"
did "keypaste env set" kp_cli "$db" env set tf TOKEN=tf-2
twofish "$db" || die "the CLI's save did not keep Twofish"
[ "$(header "$db" | cut -c21-22)" = "04" ] || die "the CLI's save of the Twofish vault is not KDBX 4"
expect "$db" env/tf/deploy TOKEN tf-2
kx add "$db" env/tf/from-kpxc -g -L 24 -l -U -n >/dev/null || die "KeePassXC could not add an entry to the Twofish vault"
twofish "$db" || die "KeePassXC's save did not keep Twofish"
written=$(value "$db" env/tf/from-kpxc Password) || die "KeePassXC cannot read the password it generated"
[ -n "$written" ] || die "KeePassXC generated an empty password, so nothing would be compared"
listed=$(kp_cli "$db" get env/tf/from-kpxc --reveal | tr -d '\r') || die "keypaste get cannot open the Twofish vault KeePassXC saved"
[ "$listed" = "$written" ] || die "the CLI does not read the password KeePassXC wrote into the Twofish vault"
did "the app's unlock" app open "$db"
new_pw=tf-app
did "the app's edit" app edit "$db" env/tf/from-kpxc "written by the app" "https://tf.example"
new_pw=
twofish "$db" || die "the app's save did not keep Twofish"
expect "$db" env/tf/from-kpxc Password tf-app
expect "$db" env/tf/deploy TOKEN tf-2

# ---------------------------------------------------------------------------------------
# Creation, through both front ends, read by KeePassXC.
# ---------------------------------------------------------------------------------------
step "create: keypaste init makes a vault KeePassXC opens and reads"
cur_pw=$pw cur_kf= new_pw=$pw
printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$dir/created-cli.kdbx" >/dev/null 2>&1 || die "keypaste init failed"
did "keypaste env set" kp_cli "$dir/created-cli.kdbx" env set kp94 TOKEN=created
expect "$dir/created-cli.kdbx" env/kp94/.env TOKEN created
expect "$dir/created-cli.kdbx" env/kp94/.env Tags env:kp94

step "create: the app makes a password-and-keyfile vault KeePassXC opens only with both"
cur_kf="$dir/next.keyx"
did "the app's create" app create "$dir/created-app.kdbx"
kx db-info "$dir/created-app.kdbx" >/dev/null 2>&1 || die "KeePassXC cannot open the vault the app created"
cur_kf=
kx db-info "$dir/created-app.kdbx" >/dev/null 2>&1 && die "KeePassXC opens the app's vault without its keyfile"
refused "the app creating over an existing vault" "$dir/created-cli.kdbx" app create "$dir/created-cli.kdbx"

printf '\nWORKFLOWS GATE PASSED: three KeePassXC vaults through the CLI and the app, KeePassXC-Browser data kept, refused by keypaste and answered by KeePassXC on a fourth, one item from each template on a fifth, a KDBX 3.1 conversion, a Twofish vault both ways, creation, and every refusal byte-identical, on %s\n' \
  "$("$cli" --version | tr -d '\r')"
