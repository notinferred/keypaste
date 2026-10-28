#!/usr/bin/env bash
# PERMANENT COMPATIBILITY GATE: `keypaste import` copies a vault KeePassXC made into a keypaste
# vault, and KeePassXC reads the copy (D-0356).
#
# KeePassXC imports a KeePass XML document holding a group, an entry with a custom attribute and a
# revision, an untagged entry under env/acme, and an entry it then deletes into its recycle bin, and
# attaches a file to the first. The shipped CLI copies that vault into its own with --into, and
# KeePassXC then opens the target: the entry is under moved/…, with its attribute, its attachment byte
# for byte and its history, the env group is the plain group moved/env, which no project reads
# (D-0416), and nothing from the recycle bin came across. The source file's bytes never change, a dry
# run writes nothing, and an import into keypaste's reserved group is refused writing nothing.
#
# KeePassXC then exports a second vault as CSV, holding an item in its root group, one in a subgroup
# with a TOTP, an icon and its own times, and one it deleted into its recycle bin (9.1e). The shipped
# CLI imports the CSV, leaving the recycled item behind and naming it and the icon as not imported,
# and KeePassXC's CSV export of the target gives each other item the row the source's gave it, below
# the import's group, but for the icon.
#
# The shipped CLI then imports a Bitwarden export holding a bare base32 secret, an otpauth URI and a
# steam:// secret, three web addresses and a passkey (9.1b). keepassxc-cli show --totp gives each item
# the code keypaste gives it from the `otp` keypaste wrote, read within one 30-second period, and reads
# the first address as the URL, the others as KP2A_URL_1 and KP2A_URL_2, and the passkey's five
# KPEX_PASSKEY_* attributes as KeePassXC's own Bitwarden import writes that passkey.
#
# NEGATIVE CONTROL: a corrupted expectation, a changed byte, a changed CSV field and two items' codes
# must each fail the comparison the checks here rest on.
#
# Usage:  scripts/verify-keepassxc-import.sh <work-directory>
# Env:    KP_COMPAT_PASSWORD   master password for both vaults       (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='IMPORT GATE FAILED: '

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-import.sh <work-directory>"
. "$(dirname "${BASH_SOURCE[0]}")/lib/kpxc.sh"

kp=$(keypaste_bin)

rm -rf "$dir"
mkdir -p "$dir/home"
export KEYPASTE_HOME="$dir/home"
unset KEYPASTE_VAULT KEYPASTE_KEYFILE

src="$dir/a.kdbx"
target="$dir/mine.kdbx"

step "KeePassXC makes the source: a group, a custom attribute, a revision, an attachment, an env group and a recycled entry"
cat >"$dir/seed.xml" <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-import</Generator><DatabaseName>a</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid root)</UUID><Name>Root</Name>
      <Group>
        <UUID>$(uuid banking)</UUID><Name>Banking</Name>
        <Entry>
          <UUID>$(uuid checking)</UUID>
          <String><Key>Title</Key><Value>checking</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">import-pw-2</Value></String>
          <String><Key>account-number</Key><Value ProtectInMemory="True">import-attribute-value</Value></String>
          <History>
            <Entry>
              <UUID>$(uuid checking)</UUID>
              <String><Key>Title</Key><Value>checking</Value></String>
              <String><Key>Password</Key><Value ProtectInMemory="True">import-pw-1</Value></String>
            </Entry>
          </History>
        </Entry>
        <Entry>
          <UUID>$(uuid doomed)</UUID>
          <String><Key>Title</Key><Value>doomed</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">doomed-pw</Value></String>
        </Entry>
      </Group>
      <Group>
        <UUID>$(uuid env)</UUID><Name>env</Name>
        <Group>
          <UUID>$(uuid acme)</UUID><Name>acme</Name>
          <Entry>
            <UUID>$(uuid oldkey)</UUID>
            <String><Key>Title</Key><Value>OLD_KEY</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">import-old-key</Value></String>
          </Entry>
        </Group>
      </Group>
    </Group>
  </Root>
</KeePassFile>
EOF

head -c 4096 /dev/urandom >"$dir/statement.bin"
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/seed.xml")" "$(native "$src")" \
  || die "keepassxc-cli could not import the seed XML"
kx attachment-import "$src" Banking/checking statement.bin "$(native "$dir/statement.bin")" >/dev/null \
  || die "keepassxc-cli could not attach a file"
kx rm "$src" Banking/doomed >/dev/null || die "keepassxc-cli could not recycle an entry"
kx ls "$src" -R -f | grep -q 'Recycle Bin/doomed' || die "KeePassXC did not put the deleted entry in its recycle bin"

source_hash=$(bytes "$src")

step "the shipped binary makes the target"
printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$target" >/dev/null || die "keypaste init failed"

step "a dry run shows the rows and writes nothing"
before=$(bytes "$target")
said=$(printf '%s\n%s\n' "$pw" "$pw" | "$kp" import "$src" --vault "$target" --into moved --dry-run 2>&1) \
  || die "the dry run failed: ${said}"
grep -qF ' moved/Banking' <<<"$said" || die "the dry run did not show Banking landing at moved/Banking: ${said}"
grep -qF ' moved/env' <<<"$said" || die "the dry run did not show env landing at moved/env: ${said}"
grep -Eq 'Recycle Bin +1  skipped' <<<"$said" || die "the dry run did not show the recycle bin skipped: ${said}"
grep -qF 'nothing was written (--dry-run)' <<<"$said" || die "the dry run did not say it wrote nothing: ${said}"
[ "$(bytes "$target")" = "$before" ] || die "the dry run changed the target"

step "an import into keypaste's reserved group is refused and writes nothing"
set +e
said=$(printf '%s\n%s\n' "$pw" "$pw" | "$kp" import "$src" --vault "$target" --into .keypaste 2>&1)
rc=$?
set -e
[ "$rc" -eq 1 ] || die "an import into .keypaste exited ${rc}, expected 1: ${said}"
grep -qF "keypaste's own group" <<<"$said" || die "the refusal did not name the reserved group: ${said}"
[ "$(bytes "$target")" = "$before" ] || die "a refused import changed the target"

step "keypaste copies the source into the target under moved"
said=$(printf '%s\n%s\n' "$pw" "$pw" | "$kp" import "$src" --vault "$target" --into moved 2>&1) \
  || die "keypaste import failed: ${said}"
{ grep -qF '2 items' <<<"$said" && grep -qF '0 projects' <<<"$said" && grep -qF 'copied into moved' <<<"$said"; } \
  || die "the import did not report the copy: ${said}"

step "KeePassXC opens the target and finds the entry, its attribute, its attachment and its history"
tree=$(kx ls "$target" -R -f) || die "KeePassXC cannot open the target keypaste wrote"
grep -qx 'moved/Banking/checking' <<<"$tree" || die "KeePassXC does not see moved/Banking/checking. Its tree: ${tree}"
grep -q 'doomed' <<<"$tree" && die "the recycled entry was copied. Its tree: ${tree}"
grep -q 'Recycle Bin' <<<"$tree" && die "the source's recycle bin was copied. Its tree: ${tree}"
grep -qx 'moved/env/acme/OLD_KEY' <<<"$tree" || die "KeePassXC does not see the env group copied as a plain group under moved. Its tree: ${tree}"
grep -qx 'env/acme/OLD_KEY' <<<"$tree" && die "the env group kept its own path. Its tree: ${tree}"

attribute=$(kx show "$target" moved/Banking/checking -a account-number) || die "KeePassXC cannot read the custom attribute"
[ "$attribute" = 'import-attribute-value' ] || die "the custom attribute reads '${attribute}'"

password=$(kx show "$target" moved/Banking/checking -a Password) || die "KeePassXC cannot read the password"
[ "$password" = 'import-pw-2' ] || die "the password reads '${password}'"

rm -f "$dir/statement.out"
kx attachment-export "$target" moved/Banking/checking statement.bin "$(native "$dir/statement.out")" >/dev/null \
  || die "KeePassXC cannot export the attachment from the copy"
cmp -s "$dir/statement.bin" "$dir/statement.out" || die "the copied attachment is not the bytes KeePassXC attached"

xml=$(kx export "$target" -f xml) || die "KeePassXC cannot export the target"
history=$(awk '/<History>/{inside=1} inside' <<<"$xml")
grep -qF '>import-pw-1<' <<<"$history" || die "the copied entry lost its history"

step "keypaste imports KeePassXC's CSV export, and KeePassXC reads back each item's fields, TOTP and times"
csv_src="$dir/csv.kdbx"
cat >"$dir/csv-seed.xml" <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-import</Generator><DatabaseName>csv</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid csvroot)</UUID><Name>Root</Name>
      <Entry>
        <UUID>$(uuid csvtop)</UUID>
        <Times><CreationTime>2021-02-03T04:05:06Z</CreationTime><LastModificationTime>2022-03-04T05:06:07Z</LastModificationTime></Times>
        <String><Key>Title</Key><Value>csv-top</Value></String>
        <String><Key>Password</Key><Value ProtectInMemory="True">csv-pw-top</Value></String>
      </Entry>
      <Entry>
        <UUID>$(uuid csvdoomed)</UUID>
        <String><Key>Title</Key><Value>csv-doomed</Value></String>
        <String><Key>Password</Key><Value ProtectInMemory="True">csv-pw-doomed</Value></String>
      </Entry>
      <Group>
        <UUID>$(uuid csvwork)</UUID><Name>Work</Name>
        <Group>
          <UUID>$(uuid csvdev)</UUID><Name>Dev</Name>
          <Entry>
            <UUID>$(uuid csvsite)</UUID>
            <IconID>12</IconID>
            <Times><CreationTime>2023-04-05T06:07:08Z</CreationTime><LastModificationTime>2024-05-06T07:08:09Z</LastModificationTime></Times>
            <String><Key>Title</Key><Value>csv-site</Value></String>
            <String><Key>UserName</Key><Value>csv-user</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">csv-pw-site</Value></String>
            <String><Key>URL</Key><Value>https://csv.import.example</Value></String>
            <String><Key>Notes</Key><Value>csv-notes</Value></String>
            <String><Key>otp</Key><Value ProtectInMemory="True">otpauth://totp/csv-site:csv-user?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&amp;period=30&amp;digits=8</Value></String>
          </Entry>
        </Group>
      </Group>
    </Group>
  </Root>
</KeePassFile>
EOF
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/csv-seed.xml")" "$(native "$csv_src")" \
  || die "keepassxc-cli could not import the CSV seed XML"
kx rm "$csv_src" csv-doomed >/dev/null || die "keepassxc-cli could not recycle csv-doomed"
kx export "$csv_src" -f csv >"$dir/keepassxc.csv" || die "keepassxc-cli could not export the vault as CSV"
grep -qF '"Root/Recycle Bin","csv-doomed",' "$dir/keepassxc.csv" \
  || die "KeePassXC's CSV does not hold csv-doomed in its recycle bin: $(cat "$dir/keepassxc.csv")"

# The row of a KeePassXC CSV export, on stdin, whose Title is $1, without its Group column.
row_of() { grep -F ",\"$1\"," | sed 's/^"[^"]*",//'; }
# A row without its Icon column, the seventh once the Group column is gone.
sans_icon() { awk 'BEGIN { FS = OFS = "\",\"" } { $7 = ""; print }'; }

grep -qF '"Root/Work/Dev","csv-site",' "$dir/keepassxc.csv" \
  || die "KeePassXC's CSV does not put csv-site in Root/Work/Dev: $(cat "$dir/keepassxc.csv")"
exported_site=$(row_of csv-site <"$dir/keepassxc.csv") || die "KeePassXC's CSV holds no csv-site"
case "$exported_site" in
  *'"otpauth://totp/'*'"12","2024-05-06T07:08:09Z","2023-04-05T06:07:08Z"') ;;
  *) die "KeePassXC's CSV does not hold the TOTP, icon and times the seed gave csv-site: ${exported_site}" ;;
esac

said=$(printf '%s\n' "$pw" | "$kp" import "$dir/keepassxc.csv" --vault "$target" --into kpxc-csv 2>&1) \
  || die "keypaste import of the KeePassXC CSV failed: ${said}"
{ grep -qF 'KeePassXC CSV' <<<"$said" && grep -qF '2 items' <<<"$said" && grep -qF 'copied into kpxc-csv' <<<"$said"; } \
  || die "the CSV import did not report the copy: ${said}"
grep -qF 'Work/Dev/csv-site: icon' <<<"$said" || die "the CSV import did not name the icon as not imported: ${said}"
grep -qF 'item Recycle Bin/csv-doomed' <<<"$said" || die "the CSV import did not name the recycled item as not imported: ${said}"

tree=$(kx ls "$target" -R -f) || die "KeePassXC cannot open the target after the CSV import"
grep -qx 'kpxc-csv/csv-top' <<<"$tree" || die "KeePassXC does not see csv-top in the import's group. Its tree: ${tree}"
grep -qx 'kpxc-csv/Work/Dev/csv-site' <<<"$tree" || die "KeePassXC does not see kpxc-csv/Work/Dev/csv-site. Its tree: ${tree}"
grep -q '^kpxc-csv/Root' <<<"$tree" && die "the CSV's root group was copied as a group. Its tree: ${tree}"
grep -q 'csv-doomed' <<<"$tree" && die "the CSV's recycled item was copied. Its tree: ${tree}"

kx export "$target" -f csv >"$dir/copied.csv" || die "keepassxc-cli could not export the target as CSV"
for title in csv-top csv-site; do
  exported=$(row_of "$title" <"$dir/keepassxc.csv") || die "KeePassXC's CSV holds no ${title}"
  copied=$(row_of "$title" <"$dir/copied.csv") || die "KeePassXC's CSV export of the target holds no ${title}"
  [ "$(sans_icon <<<"$copied")" = "$(sans_icon <<<"$exported")" ] \
    || die "KeePassXC reads ${title} back as ${copied} where its export held ${exported}"
  printf '    %s: KeePassXC reads back the row it exported\n' "$title"
done
copied_site=$(row_of csv-site <"$dir/copied.csv")
case "$copied_site" in
  *'"0","2024-05-06T07:08:09Z","2023-04-05T06:07:08Z"') ;;
  *) die "the copied csv-site does not hold the default icon and the export's times: ${copied_site}" ;;
esac

step "the source file was never written"
[ "$(bytes "$src")" = "$source_hash" ] || die "keypaste import changed the source file"
kx ls "$src" -R -f | grep -qx 'Banking/checking' || die "KeePassXC no longer reads the source"

step "keypaste imports a Bitwarden export, and KeePassXC computes each TOTP and reads each web address and the passkey keypaste wrote"
cat >"$dir/bitwarden.json" <<'EOF'
{
  "encrypted": false,
  "folders": [ { "id": "folder-work", "name": "Work" } ],
  "items": [
    {
      "id": "item-sha1", "folderId": "folder-work", "type": 1, "name": "Sha1",
      "login": {
        "uris": [
          { "match": null, "uri": "https://one.import.example" },
          { "match": null, "uri": "https://two.import.example" },
          { "match": null, "uri": "https://three.import.example" }
        ],
        "username": "import-bw-user", "password": "import-bw-pw", "totp": "gezd gnbv gy3t qojq gezd gnbv gy3t qojq",
        "fido2Credentials": [
          {
            "credentialId": "a3e15f8b-27c1-42ae-90cf-a615ad87854f", "keyType": "public-key", "keyAlgorithm": "ECDSA", "keyCurve": "P-256",
            "keyValue": "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgmr4GQQjerojFuf0ZouOuUllMvAwxZSZAfB6gwDYcLiehRANCAAT0WR5zVSp6ieusvjkLkzaGc7fjGBmwpiuLPxR_d-ZjqMI9L2DKh-takp6wGt2x0n4jzr1KA352NZg0vjZX9CHh",
            "rpId": "webauthn.io", "userHandle": "aTFtdmFnOHYtS2dxVEJ0by1rSFpLWGg0enlTVC1iUVJReDZ5czJXa3c2aw", "userName": "KPXC_BITWARDEN",
            "counter": "0", "rpName": "webauthn.io", "userDisplayName": "KPXC_BITWARDEN", "discoverable": "true"
          }
        ]
      }
    },
    {
      "id": "item-sha256", "folderId": "folder-work", "type": 1, "name": "Sha256",
      "login": { "totp": "otpauth://totp/Sha256?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA&algorithm=SHA256&digits=8" }
    },
    {
      "id": "item-steam", "folderId": "folder-work", "type": 1, "name": "Steam",
      "login": { "totp": "steam://JBSWY3DPEHPK3PXP" }
    }
  ]
}
EOF
said=$(printf '%s\n' "$pw" | "$kp" import "$dir/bitwarden.json" --vault "$target" --into bitwarden 2>&1) \
  || die "keypaste import of the Bitwarden export failed: ${said}"
{ grep -qF '3 items' <<<"$said" && grep -qF 'copied into bitwarden' <<<"$said"; } || die "the Bitwarden import did not report the copy: ${said}"

# keypaste's current code for bitwarden/Work/$1.
kp_totp() { printf '%s\n' "$pw" | "$kp" get "bitwarden/Work/$1" totp --reveal --vault "$target" 2>"$dir/totp.err" | tr -d '\r'; }

# Both codes for bitwarden/Work/$1, read back to back inside one 30-second period; a pair read across a period's end is read once more.
same_totp() {
  local title=$1 start ours theirs
  for _ in 1 2; do
    start=$(date +%s)
    ours=$(kp_totp "$title") || die "keypaste get bitwarden/Work/${title} totp failed: $(cat "$dir/totp.err")"
    theirs=$(kx show "$target" "bitwarden/Work/$title" -t) || die "keepassxc-cli show --totp failed for bitwarden/Work/${title}"
    if [ $((start / 30)) -eq $(($(date +%s) / 30)) ]; then
      if [ -z "$ours" ] || [ "$ours" != "$theirs" ]; then
        die "keypaste gives bitwarden/Work/${title} the code '${ours}' where KeePassXC gives '${theirs}'"
      fi
      printf '    %s: both give the same %s-character code\n' "$title" "${#ours}"
      return 0
    fi
  done
  die "keypaste's and KeePassXC's codes for bitwarden/Work/${title} could not be read within one period twice running"
}
for title in Sha1 Sha256 Steam; do
  same_totp "$title"
done

# The passkey is KeePassXC's own Bitwarden test passkey, read as its testBitwardenPasskey expects its own import to write it.
pem='-----BEGIN PRIVATE KEY-----MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgmr4GQQjerojFuf0ZouOuUllMvAwxZSZAfB6gwDYcLiehRANCAAT0WR5zVSp6ieusvjkLkzaGc7fjGBmwpiuLPxR/d+ZjqMI9L2DKh+takp6wGt2x0n4jzr1KA352NZg0vjZX9CHh-----END PRIVATE KEY-----'
for pair in 'URL https://one.import.example' 'KP2A_URL_1 https://two.import.example' 'KP2A_URL_2 https://three.import.example' 'Password import-bw-pw' \
  'KPEX_PASSKEY_CREDENTIAL_ID o-FfiyfBQq6Qz6YVrYeFTw' "KPEX_PASSKEY_PRIVATE_KEY_PEM ${pem}" 'KPEX_PASSKEY_RELYING_PARTY webauthn.io' \
  'KPEX_PASSKEY_USERNAME KPXC_BITWARDEN' 'KPEX_PASSKEY_USER_HANDLE aTFtdmFnOHYtS2dxVEJ0by1rSFpLWGg0enlTVC1iUVJReDZ5czJXa3c2aw'; do
  read -r field wanted <<<"$pair"
  value=$(kx show "$target" bitwarden/Work/Sha1 -a "$field") || die "KeePassXC cannot read ${field} of the imported item"
  [ "$value" = "$wanted" ] || die "KeePassXC reads ${field} of the imported item as '${value}'"
done

step "NEGATIVE CONTROL: the comparisons must be able to fail"
[ "$(kp_totp Sha1)" = "$(kx show "$target" bitwarden/Work/Steam -t)" ] && die "the TOTP comparison matched two items' codes"
if [ "$attribute" = 'import-attribute-value-CORRUPTED' ]; then
  die "a deliberately corrupted expectation still matched — this gate is not gating"
fi
[ "$(sans_icon <<<"$copied_site")" = "$(sans_icon <<<"${copied_site/csv-user/csv-userX}")" ] \
  && die "the CSV row comparison cannot see a changed field"
printf 'x' >>"$dir/statement.out"
cmp -s "$dir/statement.bin" "$dir/statement.out" && die "the attachment comparison cannot see a changed byte"

printf '\nIMPORT GATE PASSED: KeePassXC reads what keypaste copied, with its attribute, attachment and history, the source is untouched, and KeePassXC computes the TOTPs and reads the web addresses keypaste wrote from a Bitwarden export.\n'
