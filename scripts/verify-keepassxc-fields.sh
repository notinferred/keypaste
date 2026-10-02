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
# On a second vault KeePassXC made, `api/OpenAI` holds a password, a protected `OPENAI_API_KEY`,
# `Recovery codes`, `otp` and `KP2A_URL_1` (C.5a). A real `keypaste agent` and `keypaste-mcp` release
# `OPENAI_API_KEY` alone and refuse the other names without asking anyone, and the `run` tool naming
# its reference is asked about as the entry and the field. A rule naming the field releases it
# unprompted while the password reaches a person, and a rule naming `Recovery codes` releases
# nothing. `keypaste run --env-file` gives a child the field a reference names and starts nothing for
# `Recovery codes`. The app's own prompt window, held by tests/Keypaste.AppDriver on an untouched
# copy, names the field; Allow once releases it alone, and the hour serves no other field.
# `keypaste share` of the field and of its reference, through the site's Worker under `wrangler dev`
# on loopback, makes links share-crypto.js opens to that field alone, and `Recovery codes` uploads
# nothing.
#
# NEGATIVE CONTROL: a corrupted expectation, a changed byte and a plain field read as protected must
# each fail the comparison the checks here rest on.
#
# Usage:  scripts/verify-keepassxc-fields.sh <work-directory>
# Needs:  jq, curl, Node 22 or later, and `npm ci` run in site/
# Env:    KP_COMPAT_PASSWORD   master password for the vault         (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_MCP_BIN     path to the keypaste-mcp binary       (default: the Release build)
#         KEYPASTE_APP_DRIVER  path to tests/Keypaste.AppDriver      (default: the Release build)
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='FIELDS GATE FAILED: '

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-fields.sh <work-directory>"
. "$(dirname "${BASH_SOURCE[0]}")/lib/kpxc.sh"
require jq
require curl
require node

kp=$(keypaste_bin)
mcp=$(keypaste_mcp)
drv=$(app_driver)
site=$(cd "$(dirname "${BASH_SOURCE[0]}")/../site" && pwd)
wrangler="$site/node_modules/wrangler/bin/wrangler.js"
[ -f "$wrangler" ] || die "wrangler is not installed: run npm ci in site/"

agent_pid=
worker_pid=
# Waits for the agent to go, so the next one can claim the vault.
stop_agent() {
  [ -n "$agent_pid" ] || return 0
  kill "$agent_pid" 2>/dev/null || true
  for _ in $(seq 1 50); do
    kill -0 "$agent_pid" 2>/dev/null || break
    sleep 0.2
  done
  kill -9 "$agent_pid" 2>/dev/null || true
  agent_pid=
}
descendants() {
  local child
  for child in $(pgrep -P "$1" 2>/dev/null); do
    descendants "$child"
    echo "$child"
  done
}
# wrangler runs its CLI and workerd beneath it, so the whole tree goes.
stop_worker() {
  local tree
  [ -n "$worker_pid" ] || return 0
  if [ -r "/proc/$worker_pid/winpid" ]; then
    taskkill //F //T //PID "$(cat "/proc/$worker_pid/winpid")" >/dev/null 2>&1 || true
  else
    tree="$(descendants "$worker_pid") $worker_pid"
    # shellcheck disable=SC2086 # one process id per word
    kill $tree 2>/dev/null || true
    sleep 1
    # shellcheck disable=SC2086
    kill -9 $tree 2>/dev/null || true
  fi
  worker_pid=
}
# Fixed descriptors, as macOS's bash 3.2 needs (D-0398): 7 is the held app's input and 8 the open bridge's.
cleanup() {
  exec 8>&- 2>/dev/null || true
  exec 7>&- 2>/dev/null || true
  stop_agent
  stop_worker
}
trap cleanup EXIT

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
# The app's half runs on this copy, which no keypaste has written.
app_openai="$dir/openai-app.kdbx"
cp "$openai" "$app_openai"
api_key=fields-openai-api-key
others=(fields-openai-password fields-openai-recovery KRSXG5CTMVRXEZLU fields-openai-kp2a)
denial='keypaste: DENIED. The "field" argument must be password, username, url, notes or a custom field named like an environment variable. This call was recorded in the audit log.'

agents=0
# Starts keypaste agent on the OpenAI vault: $1 is what it reads after the master password, the rest its options.
start_agent() {
  local answers=$1
  shift
  stop_agent
  agents=$((agents + 1))
  pipe="keypaste-fields-$$-$(date +%s)-$agents"
  agent_err="$dir/agent-$agents.err"
  printf '%s\n%b' "$pw" "$answers" | "$kp" agent --vault "$(native "$openai")" --approver "$pipe" "$@" \
    >/dev/null 2>"$agent_err" &
  agent_pid=$!
  for _ in $(seq 1 100); do
    grep -q 'listening on' "$agent_err" && break
    kill -0 "$agent_pid" 2>/dev/null || die "keypaste agent exited before it listened: $(cat "$agent_err")"
    sleep 0.2
  done
  grep -q 'listening on' "$agent_err" || die "keypaste agent never started listening"
}
# The prompts the current agent drew.
asked() { tr -d '\r' <"$agent_err" | grep -c 'an agent is asking for a credential' || true; }

audit="$dir/audit.jsonl"
# Allow once for OPENAI_API_KEY, then Deny for the run tool. No other request may reach the agent.
start_agent 'o\nd\n' --approval-timeout 30

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

ask_field 2 OPENAI_API_KEY 8 | jq -e --arg v "$api_key" 'select(.id == 2) | .result.isError == false and .result.structuredContent.value == $v' >/dev/null \
  || die "Allow once did not return exactly the OPENAI_API_KEY KeePassXC wrote: $(cat "$dir/field-2.out")"
id=3
for field in 'Recovery codes' otp KP2A_URL_1 URL; do
  ask_field "$id" "$field" 3 | jq -e --arg t "$denial" --argjson id "$id" 'select(.id == $id) | .result.isError == true and .result.content[0].text == $t' >/dev/null \
    || die "a request for '$field' was not refused with the fixed denial: $(cat "$dir/field-$id.out")"
  id=$((id + 1))
done
[ "$(asked)" = 1 ] || die "keypaste agent was not asked exactly once: a field that never leaves reached a person"
jq -e -s 'length == 5 and .[0].args.field == "OPENAI_API_KEY" and .[0].decision == "granted"
          and (.[1:] | all(.args.field == "invalid" and .method == "invalid-request"))' <"$audit" >/dev/null \
  || die "the audit log does not show the release and four invalid fields: $(cat "$audit")"

step "the run tool naming kp:///api/OpenAI#OPENAI_API_KEY is asked about as the entry and its field"
mkdir -p "$dir/project"
run_dir=$(native "$(cd "$dir/project" && pwd -P)")
{
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"fields-probe","version":"1.0.0"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  jq -cn --arg dir "$run_dir" \
    '{jsonrpc:"2.0",id:7,method:"tools/call",params:{name:"run",arguments:{command:["sh","-c","printf %s \"$OPENAI_API_KEY\""],directory:$dir,env:{OPENAI_API_KEY:"kp:///api/OpenAI#OPENAI_API_KEY"},reason:"fields gate run"}}}'
  sleep 8
} | "$mcp" --vault "$(native "$openai")" --expose 'api/**' --allow-run --audit-log "$(native "$audit")" --approver "$pipe" \
      --client-label fields-probe >"$dir/run-tool.out" 2>"$dir/run-tool.err" || die "keypaste-mcp exited non-zero: $(cat "$dir/run-tool.err")"
jq -e 'select(.id == 7) | .result.isError == true' <"$dir/run-tool.out" >/dev/null || die "the denied run was not refused: $(cat "$dir/run-tool.out")"
LC_ALL=C grep -Eq '^  injects +OPENAI_API_KEY +api/OpenAI .{1,3} OPENAI_API_KEY +inject only' "$agent_err" \
  || die "the run's prompt does not name api/OpenAI · OPENAI_API_KEY: $(cat "$agent_err")"
jq -e -s 'length == 6 and .[5].tool == "run" and .[5].decision == "denied" and .[5].method == "prompt"' <"$audit" >/dev/null \
  || die "the run was not audited as a prompted denial: $(cat "$audit")"
for value in "${others[@]}"; do
  grep -q "$value" "$dir"/field-*.out "$dir/run-tool.out" "$audit" "$agent_err" && die "'$value' left the vault"
done
grep -q "$api_key" "$dir/run-tool.out" "$audit" "$agent_err" && die "the released value reached the denied run, the audit log or the agent's terminal"

step "a rule naming OPENAI_API_KEY releases it with nobody asked while the password reaches a person, and one naming Recovery codes releases nothing"
rule() { printf '[[allow]]\nclient          = "fields-probe"\nentries         = ["api/**"]\nfields          = ["%s"]\nmax_ttl_seconds = 300\n' "$2" >"$1"; }
rule "$dir/field-rule.toml" OPENAI_API_KEY
rule "$dir/recovery-rule.toml" 'Recovery codes'
audit="$dir/policy-audit.jsonl"
# Only the master password: a prompt the agent draws reads the end of its input, which denies.
start_agent '' --policy "$(native "$dir/field-rule.toml")" --approval-timeout 10
ask_field 10 OPENAI_API_KEY 4 | jq -e --arg v "$api_key" 'select(.id == 10) | .result.isError == false and .result.structuredContent.value == $v' >/dev/null \
  || die "a rule naming OPENAI_API_KEY did not release exactly the value KeePassXC wrote: $(cat "$dir/field-10.out")"
[ "$(asked)" = 0 ] || die "a rule naming OPENAI_API_KEY put a prompt in front of the person"
tail -n 1 "$audit" | jq -e '.decision == "granted" and .method == "policy" and .args.field == "OPENAI_API_KEY"' >/dev/null \
  || die "the rule's release was not audited as a policy release of that field: $(cat "$audit")"
ask_field 11 password 6 | jq -e 'select(.id == 11) | .result.isError == true' >/dev/null \
  || die "the password, outside the rule's fields, was released: $(cat "$dir/field-11.out")"
[ "$(asked)" = 1 ] || die "the password, outside the rule's fields, did not reach a person"

start_agent '' --policy "$(native "$dir/recovery-rule.toml")" --approval-timeout 10
grep -q 'NOT in force' "$agent_err" || die "a rule naming 'Recovery codes' was not reported as ignored: $(cat "$agent_err")"
ask_field 12 OPENAI_API_KEY 6 | jq -e 'select(.id == 12) | .result.isError == true' >/dev/null \
  || die "with the rule ignored, OPENAI_API_KEY was released unprompted: $(cat "$dir/field-12.out")"
[ "$(asked)" = 1 ] || die "with the rule ignored, OPENAI_API_KEY did not reach a person"
ask_field 13 'Recovery codes' 3 | jq -e --arg t "$denial" 'select(.id == 13) | .result.isError == true and .result.content[0].text == $t' >/dev/null \
  || die "a request for 'Recovery codes' was not refused with the fixed denial: $(cat "$dir/field-13.out")"
[ "$(asked)" = 1 ] || die "a request for 'Recovery codes' reached a person"
stop_agent
for value in "${others[@]}"; do
  grep -q "$value" "$dir"/field-1?.out "$audit" "$dir"/agent-*.err && die "'$value' left the vault under a rule"
done
grep -q "$api_key" "$audit" "$dir"/agent-*.err && die "the value a rule released reached the audit log or the agent's terminal"

step "keypaste run --env-file gives the child the field a reference names, and a reference to Recovery codes starts nothing"
child=${BASH:-/bin/bash}
printf 'OPENAI_API_KEY=kp:///api/OpenAI#OPENAI_API_KEY\n' >"$dir/field.env.keypaste"
got=$(printf '%s\n' "$pw" | "$kp" run --env-file "$(native "$dir/field.env.keypaste")" --vault "$(native "$openai")" \
  -- "$child" -c 'printf %s "$OPENAI_API_KEY"' 2>"$dir/env-file.err" | tr -d '\r') || die "run --env-file failed: $(cat "$dir/env-file.err")"
[ "$got" = "$api_key" ] || die "the child did not receive exactly the OPENAI_API_KEY KeePassXC wrote"
printf 'CODES=kp:///api/OpenAI#Recovery%%20codes\n' >"$dir/codes.env.keypaste"
if printf '%s\n' "$pw" | "$kp" run --env-file "$(native "$dir/codes.env.keypaste")" --vault "$(native "$openai")" \
  -- "$child" -c 'printf CHILD-RAN; printf %s "$CODES"' >"$dir/codes-run.out" 2>&1; then
  die "a reference naming 'Recovery codes' started its command"
fi
grep -q CHILD-RAN "$dir/codes-run.out" && die "a reference naming 'Recovery codes' started its command"
for value in "${others[@]}"; do
  grep -q "$value" "$dir/codes-run.out" "$dir/env-file.err" && die "'$value' reached the terminal through run --env-file"
done

step "a real keypaste-mcp asks the app's own prompt window on an untouched copy: Allow once releases the field alone, and the hour serves no other"
hold_out="$dir/hold.out"
app_audit="$dir/app-audit.jsonl"
DIE_FILES='hold_out bridge_out'
exec 7> >(KEYPASTE_DRIVER_PASSWORD="$pw" exec "$drv" hold "$(native "$app_openai")" >"$hold_out" 2>&1)
wait_for 'holding session' "$hold_out"
grep -q 'not served' "$hold_out" && die "the app unlocked the KeePassXC vault and did not serve it"
drawn=0

app_request() {
  jq -cn --argjson id "$1" --arg field "$2" \
    '{jsonrpc:"2.0",id:$id,method:"tools/call",params:{name:"request_credential",arguments:{entry:"api/OpenAI",field:$field,reason:"fields gate app",ttl_seconds:60}}}' >&8
}
# Opens a bridge on 8, without the app's input, asks it for the field $2 and waits for the app's prompt.
app_prompt() {
  bridge_out="$dir/app-$1.out"
  exec 8>&-
  exec 8> >(exec 7>&-; exec "$mcp" --vault "$(native "$app_openai")" --expose 'api/**' --audit-log "$(native "$app_audit")" \
    --client-label fields-probe >"$bridge_out" 2>"$dir/app-$1.err")
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"fields-probe","version":"1.0.0"}}}' >&8
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}' >&8
  app_request 3 "$2"
  drawn=$((drawn + 1))
  wait_for '^prompt client' "$hold_out" "$drawn"
}
app_reply() {
  for _ in $(seq 1 150); do
    jq -e --argjson id "$1" 'select(.id == $id)' <"$bridge_out" >/dev/null 2>&1 && return 0
    sleep 0.2
  done
  die "call $1 to the app's bridge got no reply"
}
withdrawn() { wait_for '^prompt withdrawn' "$hold_out" "$drawn"; }
last_prompt() { grep '^prompt client' "$hold_out" | tail -1 | tr -d '\r'; }

app_prompt once OPENAI_API_KEY
grep -qF "label=fields-probe entry=api/OpenAI field=OPENAI_API_KEY for=once, or for 1 hour" <<<"$(last_prompt)" \
  || die "the app's prompt does not name the field: $(last_prompt)"
echo once >&7
app_reply 3
withdrawn
jq -e --arg v "$api_key" 'select(.id == 3) | .result.isError == false and .result.structuredContent.value == $v' <"$bridge_out" >/dev/null \
  || die "Allow once in the app did not return exactly the OPENAI_API_KEY KeePassXC wrote"
tail -n 1 "$app_audit" | jq -e '.decision == "granted" and .method == "prompt" and .args.field == "OPENAI_API_KEY" and .granted_seconds == 0' >/dev/null \
  || die "the app's release was not audited with its field as allowed once: $(tail -n 1 "$app_audit")"

app_prompt hour OPENAI_API_KEY
echo approve >&7
app_reply 3
withdrawn
jq -e --arg v "$api_key" 'select(.id == 3) | .result.structuredContent.value == $v' <"$bridge_out" >/dev/null \
  || die "Allow for 1 hour in the app did not return the OPENAI_API_KEY KeePassXC wrote"
app_request 4 password
drawn=$((drawn + 1))
wait_for '^prompt client' "$hold_out" "$drawn"
grep -qF "entry=api/OpenAI field=password" <<<"$(last_prompt)" \
  || die "a password request under the field's hour did not draw its own prompt: $(last_prompt)"
echo deny >&7
app_reply 4
withdrawn
jq -e 'select(.id == 4) | .result.isError == true' <"$bridge_out" >/dev/null || die "the denied password was released"
id=5
for field in 'Recovery codes' otp KP2A_URL_1 URL; do
  app_request "$id" "$field"
  app_reply "$id"
  jq -e --arg t "$denial" --argjson id "$id" 'select(.id == $id) | .result.isError == true and .result.content[0].text == $t' <"$bridge_out" >/dev/null \
    || die "the app's bridge did not refuse '$field' with the fixed denial"
  tail -n 1 "$app_audit" | jq -e '.args.field == "invalid" and .method == "invalid-request" and .decision == "denied"' >/dev/null \
    || die "'$field' was not audited as an invalid field: $(tail -n 1 "$app_audit")"
  id=$((id + 1))
done
exec 8>&-
[ "$(grep -c '^prompt client' "$hold_out")" -eq "$drawn" ] || die "a field that never leaves drew a prompt in the app"
for value in "${others[@]}"; do
  grep -q "$value" "$dir"/app-*.out "$app_audit" "$hold_out" && die "'$value' left the vault through the app"
done
grep -q "$api_key" "$app_audit" "$hold_out" && die "the released value reached the audit log or the app's output"
exec 7>&-
wait_for '^shut down' "$hold_out"

step "share --field OPENAI_API_KEY and its reference form, through the site's Worker on loopback, make links share-crypto.js opens to that field alone"
origin=http://127.0.0.1:8787
worker_log="$dir/worker.log"
DIE_FILES='worker_log'
# The site's own Worker and configuration without its database bindings, so SHARE_DEV_MEMORY keeps shares in memory.
node -e '
  const fs = require("node:fs");
  const path = require("node:path");
  const [site, out] = process.argv.slice(1);
  const config = JSON.parse(fs.readFileSync(path.join(site, "wrangler.jsonc"), "utf8").replace(/^\s*\/\/.*$/gm, ""));
  delete config.hyperdrive;
  config.main = path.resolve(site, config.main);
  config.assets.directory = path.resolve(site, config.assets.directory);
  config.vars = { ...config.vars, SHARE_DEV_MEMORY: "1" };
  fs.writeFileSync(out, JSON.stringify(config, null, 2));
' "$(native "$site")" "$(native "$dir/wrangler.json")" || die "could not derive the Worker's loopback configuration from site/wrangler.jsonc"
WRANGLER_SEND_METRICS=false NO_COLOR=1 node "$(native "$wrangler")" dev --config "$(native "$dir/wrangler.json")" \
  --ip 127.0.0.1 --port 8787 --local-upstream 127.0.0.1:8787 >"$worker_log" 2>&1 &
worker_pid=$!
# An unknown share answers the gone 404 only once the memory store serves.
ready=
deadline=$((SECONDS + 120))
while [ "$SECONDS" -lt "$deadline" ]; do
  answer=$(curl -s -D - -o /dev/null --max-time 2 "$origin/api/share/AAAAAAAAAAAAAAAAAAAAAA" 2>/dev/null | tr -d '\r' || true)
  if grep -qi '^x-keypaste-share: gone$' <<<"$answer"; then ready=1; break; fi
  kill -0 "$worker_pid" 2>/dev/null || die "the Worker exited before it answered"
  sleep 0.5
done
[ -n "$ready" ] || die "the Worker on loopback never answered an unknown share as gone"

share() { printf '%s\n' "$pw" | KEYPASTE_SHARE_URL="$origin" "$kp" share "$@" --print --vault "$(native "$openai")"; }
by_field=$(share api/OpenAI --field OPENAI_API_KEY 2>"$dir/share-field.err" | tr -d '\r') \
  || die "share --field OPENAI_API_KEY failed: $(cat "$dir/share-field.err")"
by_reference=$(share 'kp:///api/OpenAI#OPENAI_API_KEY' 2>"$dir/share-reference.err" | tr -d '\r') \
  || die "share of kp:///api/OpenAI#OPENAI_API_KEY failed: $(cat "$dir/share-reference.err")"

# Opens a link as the viewer does, with share-crypto.js: the status call and its check tag, then the one view.
open_link() {
  node --input-type=module -e '
    import { pathToFileURL } from "node:url";
    const [shareCrypto, link] = process.argv.slice(1);
    const { checkPassphrase, deriveKey, openEnvelope, parseLink } = await import(pathToFileURL(shareCrypto).href);
    const url = new URL(link);
    const parsed = parseLink(url.hash);
    if (!parsed) throw new Error("not a whole link");
    const api = async (path, method) => {
      const response = await fetch(new URL(path, url.origin), { method });
      if (!response.ok) throw new Error(`${method} ${path} answered ${response.status}`);
      return response.json();
    };
    const meta = await api(`/api/share/${parsed.id}`, "GET");
    const cek = await deriveKey(meta, parsed.key, "");
    if (!(await checkPassphrase(meta, cek))) throw new Error("the check tag does not open");
    const opened = await api(`/api/share/${parsed.id}/open`, "POST");
    const payload = await openEnvelope(opened.envelope, cek);
    process.stdout.write(JSON.stringify({ title: payload.title, fields: payload.fields, views_left: opened.views_left }));
  ' "$(native "$site/public/s/share-crypto.js")" "$1"
}
for link in "$by_field" "$by_reference"; do
  case "$link" in "$origin/s/#"*) ;; *) die "a share made no link to the Worker on loopback" ;; esac
  opened=$(open_link "$link" 2>"$dir/open.err") || die "share-crypto.js could not open a link: $(cat "$dir/open.err")"
  jq -e --arg v "$api_key" '.title == "OpenAI" and (.fields | length) == 1 and .fields[0].name == "OPENAI_API_KEY"
                            and .fields[0].value == $v and .views_left == 0' <<<"$opened" >/dev/null \
    || die "a link opened to something other than OPENAI_API_KEY alone: $(jq -c '[.fields[].name]' <<<"$opened")"
  for value in "${others[@]}"; do
    grep -qF "$value" <<<"$opened" && die "a link carried '$value'"
  done
done

refused_share() {
  local said rc
  set +e
  said=$(share "$@" 2>&1)
  rc=$?
  set -e
  [ "$rc" -ne 0 ] || die "share $* was accepted"
  if grep -q fields-openai-recovery <<<"$said"; then die "the refused share $* printed the field it named"; fi
}
refused_share api/OpenAI --field 'Recovery codes'
refused_share 'kp:///api/OpenAI#Recovery%20codes'
created=$(sed -e $'s/\033\\[[0-9;]*m//g' "$worker_log" | grep -c 'POST /api/share 201' || true)
[ "$created" = 2 ] || die "the Worker made ${created} shares, not the two allowed: a refused field was uploaded or a share was not logged"
for value in "$api_key" "${others[@]}"; do
  grep -q "$value" "$worker_log" "$dir"/share-*.err "$KEYPASTE_HOME/audit.jsonl" && die "'$value' reached the Worker's log, the terminal or the audit log"
done
stop_worker
DIE_FILES=

step "NEGATIVE CONTROL: the comparisons must be able to fail"
if [ "$(kx show "$db" "$entry" -a Region)" = 'fields-plain-changed-CORRUPTED' ]; then
  die "a deliberately corrupted expectation still matched — this gate is not gating"
fi
[ "$(protection Region <<<"$current")" = protected ] && die "the protection reader calls a plain field protected"
printf 'x' >>"$dir/blob.out"
cmp -s "$dir/blob.bin" "$dir/blob.out" && die "the attachment comparison cannot see a changed byte"

printf '\nFIELDS GATE PASSED: KeePassXC reads the fields keypaste wrote with their protection, keeps its own, and keypaste reads what KeePassXC merged.\n'
