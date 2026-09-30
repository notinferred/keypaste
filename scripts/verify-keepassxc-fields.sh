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
# taken, and no listing carries a value. keypaste saves a field twice inside one second of the clock,
# and after KeePassXC merges in a newer copy of the entry, keypaste reads KeePassXC's value and the
# entry's history holds both values keypaste saved (F.27).
# On a second vault KeePassXC made, a real `keypaste agent` and `keypaste-mcp` release the
# env-named `OPENAI_API_KEY` alone, and refuse `Recovery codes`, `otp`, `KP2A_URL_1` and `URL`
# without asking anyone (C.5a).
#
# NEGATIVE CONTROL: a corrupted expectation, a changed byte and a plain field read as protected must
# each fail the comparison the checks here rest on.
#
# Usage:  scripts/verify-keepassxc-fields.sh <work-directory>
# Env:    KP_COMPAT_PASSWORD   master password for the vault         (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_MCP_BIN     path to the keypaste-mcp binary       (default: the Release build)
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='FIELDS GATE FAILED: '

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-fields.sh <work-directory>"
. "$(dirname "${BASH_SOURCE[0]}")/lib/kpxc.sh"

kp=$(keypaste_bin)
mcp=$(keypaste_mcp)

agent_pid=
trap 'if [ -n "$agent_pid" ]; then kill "$agent_pid" 2>/dev/null || true; fi' EXIT

rm -rf "$dir"
mkdir -p "$dir/home"
export KEYPASTE_HOME="$dir/home"
unset KEYPASTE_VAULT KEYPASTE_KEYFILE

db="$dir/a.kdbx"
entry=api/Stripe

# The master password, then one value per prompt, written with \n between them.
kp_with() {
  local values=$1; shift
  printf '%s\n%b' "$pw" "$values" | "$kp" "$@" --vault "$db" | tr -d '\r'
}

# The export with every <History> block removed, so a value an edit dropped cannot be found in the
# revision that edit made.
current_xml() { kx export "$db" -f xml | awk '/<History>/{past=1} !past{print} /<\/History>/{past=0}'; }
# Captured before it is searched: a `grep -q` that stops reading fails the pipeline under pipefail (F.27).
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

next_second() { local s; s=$(date +%s); while [ "$(date +%s)" = "$s" ]; do sleep 0.02; done; }

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

step "keypaste saves a field twice in one second; after KeePassXC merges a newer copy, keypaste reads KeePassXC's value and history keeps both"
attempts=10
for ((attempt = 1; ; attempt++)); do
  first="fields-first-$attempt" saved="fields-before-merge-$attempt"
  next_second
  started=$(date +%s)
  said=$(kp_with "${first}\n" set "$entry" --field MERGED 2>&1) || die "set --field MERGED failed: ${said}"
  said=$(kp_with "${saved}\n" set "$entry" --field MERGED 2>&1) || die "set --field MERGED failed: ${said}"
  [ "$(date +%s)" = "$started" ] && break
  [ "$attempt" -lt "$attempts" ] || die "${attempts} attempts could not save twice inside one second"
done
printf 'saved twice inside one second on attempt %d of %d\n' "$attempt" "$attempts"
history=$(history_xml)
grep -qF "$saved" <<<"$history" && die "the value about to be merged over is already in history"
kx export "$db" -f xml | awk -v id="$(uuid stripe)" -v saved="$saved" '
  /<History>/ { inside = 1 }
  !inside && index($0, "<UUID>" id "</UUID>") { mine = 1 }
  mine && !inside { sub(saved, "fields-after-merge"); sub(/<LastModificationTime>[^<]*</, "<LastModificationTime>2037-01-01T00:00:00Z<") }
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
history=$(history_xml)
grep -qF "$first" <<<"$history" || die "after the merge the history lost the first of the two values saved in one second"
grep -qF "$saved" <<<"$history" || die "after the merge the history lost the second of the two values saved in one second"

step "a real keypaste agent and keypaste-mcp release the env-named field KeePassXC wrote, and no other"
openai="$dir/openai.kdbx"
cat >"$dir/openai.xml" <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-fields</Generator><DatabaseName>openai</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid root)</UUID><Name>Root</Name>
      <Group>
        <UUID>$(uuid api)</UUID><Name>api</Name>
        <Entry>
          <UUID>$(uuid openai)</UUID>
          <CustomData><Item><Key>fields.entry</Key><Value>fields-entry-data</Value></Item></CustomData>
          <String><Key>Title</Key><Value>OpenAI</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">fields-openai-password</Value></String>
          <String><Key>OPENAI_API_KEY</Key><Value ProtectInMemory="True">fields-openai-api-key</Value></String>
          <String><Key>Recovery codes</Key><Value ProtectInMemory="True">fields-openai-recovery</Value></String>
          <String><Key>otp</Key><Value ProtectInMemory="True">otpauth://totp/OpenAI?secret=KRSXG5CTMVRXEZLU&amp;issuer=fields</Value></String>
          <String><Key>KP2A_URL_1</Key><Value>https://fields-openai-kp2a.example</Value></String>
        </Entry>
      </Group>
    </Group>
  </Root>
</KeePassFile>
EOF
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/openai.xml")" "$(native "$openai")" \
  || die "keepassxc-cli could not import the OpenAI entry"
[ "$(kx show "$openai" api/OpenAI -a OPENAI_API_KEY)" = fields-openai-api-key ] || die "KeePassXC does not read the OPENAI_API_KEY it wrote"

pipe="keypaste-fields-$$-$(date +%s)"
agent_err="$dir/agent.err"
audit="$dir/audit.jsonl"
# One answer: Allow once for OPENAI_API_KEY. No other request may reach the agent.
printf '%s\no\n' "$pw" | "$kp" agent --vault "$(native "$openai")" --approver "$pipe" --approval-timeout 30 \
  >/dev/null 2>"$agent_err" &
agent_pid=$!
for _ in $(seq 1 100); do
  grep -q 'listening on' "$agent_err" && break
  kill -0 "$agent_pid" 2>/dev/null || die "keypaste agent exited before it listened: $(cat "$agent_err")"
  sleep 0.2
done
grep -q 'listening on' "$agent_err" || die "keypaste agent never started listening"

ask_field() {
  local id=$1 field=$2 wait=$3 out="$dir/field-$1.out"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"fields-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"api/OpenAI\",\"field\":\"$field\",\"reason\":\"fields gate\",\"ttl_seconds\":60}}}"
    sleep "$wait"
  } | "$mcp" --vault "$(native "$openai")" --expose 'api/**' --audit-log "$(native "$audit")" --approver "$pipe" \
        --client-label fields-probe >"$out" 2>"$dir/field-$id.err" || die "keypaste-mcp exited non-zero: $(cat "$dir/field-$id.err")"
  tr -d '\r' <"$out"
}

ask_field 2 OPENAI_API_KEY 8 | jq -e 'select(.id == 2) | .result.isError == false and .result.structuredContent.value == "fields-openai-api-key"' >/dev/null \
  || die "Allow once did not return exactly the OPENAI_API_KEY KeePassXC wrote: $(cat "$dir/field-2.out")"
denial='keypaste: DENIED. The "field" argument must be password, username, url, notes or a custom field named like an environment variable. This call was recorded in the audit log.'
id=3
for field in 'Recovery codes' otp KP2A_URL_1 URL; do
  ask_field "$id" "$field" 3 | jq -e --arg t "$denial" --argjson id "$id" 'select(.id == $id) | .result.isError == true and .result.content[0].text == $t' >/dev/null \
    || die "a request for '$field' was not refused with the fixed denial: $(cat "$dir/field-$id.out")"
  id=$((id + 1))
done
[ "$(tr -d '\r' <"$agent_err" | grep -c 'an agent is asking for a credential')" = 1 ] \
  || die "keypaste agent was not asked exactly once: a field that never leaves reached a person"
jq -e -s 'length == 5 and .[0].args.field == "OPENAI_API_KEY" and .[0].decision == "granted"
          and (.[1:] | all(.args.field == "invalid" and .method == "invalid-request"))' <"$audit" >/dev/null \
  || die "the audit log does not show the release and four invalid fields: $(cat "$audit")"
for value in fields-openai-password fields-openai-recovery KRSXG5CTMVRXEZLU fields-openai-kp2a; do
  grep -q "$value" "$dir"/field-*.out "$audit" "$agent_err" && die "'$value' left the vault"
done
grep -q fields-openai-api-key "$audit" "$agent_err" && die "the released value reached the audit log or the agent's terminal"
kill "$agent_pid" 2>/dev/null || true
agent_pid=

step "NEGATIVE CONTROL: the comparisons must be able to fail"
if [ "$(kx show "$db" "$entry" -a Region)" = 'fields-plain-changed-CORRUPTED' ]; then
  die "a deliberately corrupted expectation still matched — this gate is not gating"
fi
[ "$(protection Region <<<"$current")" = protected ] && die "the protection reader calls a plain field protected"
printf 'x' >>"$dir/blob.out"
cmp -s "$dir/blob.bin" "$dir/blob.out" && die "the attachment comparison cannot see a changed byte"

printf '\nFIELDS GATE PASSED: KeePassXC reads the fields keypaste wrote with their protection, keeps its own, and keypaste reads what KeePassXC merged.\n'
