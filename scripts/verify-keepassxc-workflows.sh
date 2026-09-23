#!/usr/bin/env bash
# The supported vault workflows, on vaults KeePassXC made, through the shipped CLI and the desktop
# app, each result read back by KeePassXC (9.4, V-9.4). docs/FEATURES.md states KeePassXC
# compatibility from this gate alone (D-0307).
#
# KeePassXC imports one KeePass XML document three times: behind a password, behind a password and
# an XML keyfile, and behind the keyfile alone. The document carries what keypaste does not model —
# meta, group and entry CustomData, an unprotected and a protected custom string, tags, an auto-type
# association and a revision KeePassXC wrote — and KeePassXC then attaches a binary and a text file.
# Each vault is AES-KDF and KDBX 4.0, as KeePassXC writes them.
#
# On each vault: open, edit, restore a revision, organize, delete and recover, restore a backup,
# export, and change access. The app half runs through tests/Keypaste.AppDriver, which presses the
# commands the desktop's screens bind to. After every write KeePassXC opens the vault with its current
# factors, reads the value the workflow wrote, finds every unmodelled marker, exports both attachments
# byte for byte, and reports the cipher and KDF it chose. Every refusal leaves the vault byte-identical
# and keeps no backup. Creation runs once, through both front ends.
#
# NEGATIVE CONTROL: a vault KeePassXC stripped of an attachment, and one imported without its entry
# CustomData, must each fail the same check every workflow passes.
set -euo pipefail

die()  { printf '\nWORKFLOWS GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-workflows.sh <work-directory>"
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

drv=${KEYPASTE_APP_DRIVER:-}
if [ -z "$drv" ]; then
  drv=artifacts/bin/Keypaste.AppDriver/release/Keypaste.AppDriver
  [ -x "$drv" ] || drv="${drv}.exe"
fi
[ -x "$drv" ] || die "app driver not found at '$drv' (build tests/Keypaste.AppDriver, or set KEYPASTE_APP_DRIVER)"

rm -rf "$dir"
mkdir -p "$dir/home"
export KEYPASTE_HOME="$dir/home"

native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
bytes()  { od -An -v -tx1 "$1" | tr -d ' \n'; }
copies() { if [ -d "$1.backups" ]; then find "$1.backups" -maxdepth 1 -type f | wc -l | tr -d ' '; else echo 0; fi; }
header() { od -An -v -tx1 -N12 "$1" | tr -d ' \n' | tr 'A-Z' 'a-z'; }

# The factors the vault under test has now. Every reader and writer below uses them, so a change of
# access is followed by every later step.
cur_pw=$pw
cur_kf=
new_pw=

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

markers=(kp94-meta-value kp94-group-value kp94-entry-value kp94-plain-value kp94-secret-value kp94-window kp94-tag)

# Whether KeePassXC still sees everything keypaste does not model, on the entry at $2.
check_intact() {
  local db=$1 entry=$2 xml="$dir/intact.xml" marker name
  kx export "$db" -f xml >"$xml" 2>/dev/null || { echo "KeePassXC cannot open $db"; return 1; }
  # Outside <History> only: every edit copies the entry into its history, so a marker the edit dropped
  # from the entry itself would still be found in the revision.
  awk '/<History>/{past=1} !past{print} /<\/History>/{past=0}' "$xml" >"$xml.current"
  for marker in "${markers[@]}"; do
    grep -qF "$marker" "$xml.current" || { echo "KeePassXC no longer finds $marker outside history in $db"; return 1; }
  done
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

uuid() { printf '%-16.16s' "$1" | base64; }

cat >"$dir/seed.xml" <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta>
    <Generator>verify-keepassxc-workflows</Generator>
    <DatabaseName>kp94</DatabaseName>
    <CustomData><Item><Key>kp94.meta</Key><Value>kp94-meta-value</Value></Item></CustomData>
  </Meta>
  <Root>
    <Group>
      <UUID>$(uuid root)</UUID><Name>Root</Name>
      <Group>
        <UUID>$(uuid servers)</UUID><Name>servers</Name>
        <CustomData><Item><Key>kp94.group</Key><Value>kp94-group-value</Value></Item></CustomData>
        <Entry>
          <UUID>$(uuid database)</UUID>
          <Tags>kp94-tag</Tags>
          <String><Key>Title</Key><Value>database</Value></String>
          <String><Key>UserName</Key><Value>admin</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">pw-2</Value></String>
          <String><Key>kp94-plain</Key><Value>kp94-plain-value</Value></String>
          <String><Key>kp94-secret</Key><Value ProtectInMemory="True">kp94-secret-value</Value></String>
          <AutoType><Enabled>True</Enabled><DataTransferObfuscation>0</DataTransferObfuscation>
            <Association><Window>kp94-window</Window><KeystrokeSequence>{PASSWORD}{ENTER}</KeystrokeSequence></Association>
          </AutoType>
          <CustomData><Item><Key>kp94.entry</Key><Value>kp94-entry-value</Value></Item></CustomData>
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
        </Entry>
      </Group>
      <Group>
        <UUID>$(uuid archive)</UUID><Name>archive</Name>
        <Entry>
          <UUID>$(uuid occupied)</UUID>
          <String><Key>Title</Key><Value>occupied</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">o-1</Value></String>
        </Entry>
      </Group>
      <Group>
        <UUID>$(uuid env)</UUID><Name>env</Name>
        <Group>
          <UUID>$(uuid kp94)</UUID><Name>kp94</Name>
          <Entry>
            <UUID>$(uuid token)</UUID>
            <String><Key>Title</Key><Value>TOKEN</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">t-1</Value></String>
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
step "the intact check fails on a vault missing an attachment or its entry CustomData"
kpxc_import "$dir/control.kdbx" "$pw" "" "$dir/seed.xml"
container=$(kx db-info "$dir/control.kdbx" | grep -E '^(Cipher|KDF):')
intact "$dir/control.kdbx" servers/database
kx attachment-rm "$dir/control.kdbx" servers/database note.txt >/dev/null || die "KeePassXC could not remove an attachment"
check_intact "$dir/control.kdbx" servers/database >/dev/null && die "the intact check passed a vault missing an attachment"
grep -v 'kp94.entry' "$dir/seed.xml" >"$dir/seed-stripped.xml"
kpxc_import "$dir/control-stripped.kdbx" "$pw" "" "$dir/seed-stripped.xml"
check_intact "$dir/control-stripped.kdbx" servers/database >/dev/null && die "the intact check passed a vault missing its entry CustomData"
echo "    both controls fail the check"

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
  expect "$db" env/kp94/TOKEN Password t-2
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

  step "[$kind] organize: the app renames the group carrying CustomData and moves the entry out of it"
  did "the app's group rename" app group-rename "$db" servers hosts
  did "the app's rename and move" app relocate "$db" hosts/database archive database-old
  expect "$db" archive/database-old kp94-secret kp94-secret-value
  expect "$db" hosts/cache Password c-1
  [ "$(header "$db" | cut -c17-18)" = "00" ] || die "organizing raised the vault above KDBX 4.0"
  intact "$db" archive/database-old
  refused "the app moving onto an occupied name" "$db" app relocate "$db" hosts/cache archive occupied

  step "[$kind] delete and recover: the CLI recycles, the app restores; the app deletes and restores"
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
# Creation, through both front ends, read by KeePassXC.
# ---------------------------------------------------------------------------------------
step "create: keypaste init makes a vault KeePassXC opens and reads"
cur_pw=$pw cur_kf= new_pw=$pw
printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$dir/created-cli.kdbx" >/dev/null 2>&1 || die "keypaste init failed"
did "keypaste env set" kp_cli "$dir/created-cli.kdbx" env set kp94 TOKEN=created
expect "$dir/created-cli.kdbx" env/kp94/TOKEN Password created

step "create: the app makes a password-and-keyfile vault KeePassXC opens only with both"
cur_kf="$dir/next.keyx"
did "the app's create" app create "$dir/created-app.kdbx"
kx db-info "$dir/created-app.kdbx" >/dev/null 2>&1 || die "KeePassXC cannot open the vault the app created"
cur_kf=
kx db-info "$dir/created-app.kdbx" >/dev/null 2>&1 && die "KeePassXC opens the app's vault without its keyfile"
refused "the app creating over an existing vault" "$dir/created-cli.kdbx" app create "$dir/created-cli.kdbx"

printf '\nWORKFLOWS GATE PASSED: three KeePassXC vaults through the CLI and the app, creation, and every refusal byte-identical, on %s\n' \
  "$("$cli" --version | tr -d '\r')"
