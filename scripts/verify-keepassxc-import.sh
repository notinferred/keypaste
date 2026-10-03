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
# NEGATIVE CONTROL: a corrupted expectation must fail the comparison every check here rests on.
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
{ grep -qF '2 entries' <<<"$said" && grep -qF '0 projects' <<<"$said" && grep -qF 'copied into moved' <<<"$said"; } \
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

step "the source file was never written"
[ "$(bytes "$src")" = "$source_hash" ] || die "keypaste import changed the source file"
kx ls "$src" -R -f | grep -qx 'Banking/checking' || die "KeePassXC no longer reads the source"

step "NEGATIVE CONTROL: the comparisons must be able to fail"
if [ "$attribute" = 'import-attribute-value-CORRUPTED' ]; then
  die "a deliberately corrupted expectation still matched — this gate is not gating"
fi
printf 'x' >>"$dir/statement.out"
cmp -s "$dir/statement.bin" "$dir/statement.out" && die "the attachment comparison cannot see a changed byte"

printf '\nIMPORT GATE PASSED: KeePassXC reads what keypaste copied, with its attribute, attachment and history, and the source is untouched.\n'
