#!/usr/bin/env bash
# PERMANENT COMPATIBILITY GATE: project tags KeePassXC writes are the ones keypaste reads, and the
# ones keypaste writes are the ones KeePassXC reads (C.1a); the fields of entries KeePassXC tagged
# are the project's variables (C.1b).
#
# KeePassXC imports a KeePass XML document whose entries carry the tags env:billing,
# env:billing:prod, env:billing:Prod and finance, under a group tagged env:billing:staging. KeePassXC
# keeps the group tag and so writes KDBX 4.1; a second vault from the same document without the
# group tag is KDBX 4.0. `keypaste env ls` names billing with dev, a protected prod and exactly the
# entries tagged into each, reports env:billing:Prod, and ignores finance and the group tag.
# `keypaste env tag` and `env untag` change an entry's tags; KeePassXC reads them with `show -a Tags`,
# the 4.0 vault stays 4.0, the 4.1 vault keeps its version and its group tag, and a refused tag leaves
# the file byte-identical.
#
# Then a real `keypaste agent` holds the 4.1 vault and a real `keypaste-mcp`, exposed to the entries,
# asks for passwords. An untagged-for-prod entry answered with the hour gets it. The entry tagged
# env:billing:prod, and one KeePassXC tagged env:billing:Prod through `keepassxc-cli merge`, are
# offered Allow once only: the hour is refused and once is honoured.
#
# A third vault KeePassXC makes holds billing's legacy LEGACY_TOKEN and fields on entries tagged
# env:billing, one also tagged env:billing:staging, and a staging entry also tagged env:billing:prod.
# `keypaste run` gives a child exactly the tagged fields and the legacy variable, `env export` writes a
# reference for each and `env diff` compares the environments' key names. A key on two entries, a
# legacy Api_Key beside a tagged API_KEY, an expired member, {PASSWORD} in a value and a custom
# PASSWORD field each start nothing and name their entries. `run --session` through a real
# `keypaste agent`, and through the app's own prompt window held by tests/Keypaste.AppDriver on an
# untouched copy, gets the same set after a prompt naming the source entries, and the staging set is
# asked once only. A scoped token's run of the dev set is audited with its source entries, and its
# staging set is refused because a member is also tagged env:billing:prod.
#
# A fourth vault KeePassXC makes from the same document takes the writers (C.1c). `env set` of a tagged
# key changes it on its own entry; a new key with no --entry creates env/billing/.env, tagged env:billing
# and holding it protected; `env pull` of a .env touching keys on two entries adds one revision to each;
# `env rm` removes a field and history keeps it; `keypaste run` then gives a child the new values. A tag
# change refused for want of --yes writes nothing, and one confirmed names its environment and fields
# first. The app, through tests/Keypaste.AppDriver on an untouched copy, adds, replaces, imports and
# removes the same way, and a declined tag change on its entry pane, having named what it reaches,
# writes nothing. KeePassXC reads every value, protection and tag written.
#
# NEGATIVE CONTROL: a corrupted expectation must fail the comparison the listing check rests on, and
# the hour must be honoured for an entry no tag protects.
#
# Usage:  scripts/verify-keepassxc-projects.sh <work-directory>
# Env:    KP_COMPAT_PASSWORD   master password for the vaults        (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_MCP_BIN     path to the keypaste-mcp binary       (default: the Release build)
#         KEYPASTE_APP_DRIVER  path to tests/Keypaste.AppDriver      (default: the Release build)
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='PROJECTS GATE FAILED: '

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-projects.sh <work-directory>"
. "$(dirname "${BASH_SOURCE[0]}")/lib/kpxc.sh"
require jq
kp=$(keypaste_bin)
mcp=$(keypaste_mcp)
drv=$(app_driver)

rm -rf "$dir"
mkdir -p "$dir/home"
export KEYPASTE_HOME="$dir/home"
unset KEYPASTE_VAULT KEYPASTE_KEYFILE

grouped="$dir/grouped.kdbx"
plain="$dir/plain.kdbx"
agent_pid=
cleanup() { if [ -n "$agent_pid" ]; then kill "$agent_pid" 2>/dev/null || true; fi; }
trap cleanup EXIT

version() { header "$1" | cut -c17-22; }
kp_on() {
  local db=$1; shift
  printf '%s\n' "$pw" | "$kp" "$@" --vault "$db" | tr -d '\r'
}
# One tag per line, sorted, read whole before anything matches on it.
tags() { local listed; listed=$(kx show "$1" "$2" -a Tags) || return 1; sort <<<"${listed//,/$'\n'}"; }

step "KeePassXC makes the vaults: tagged entries under a group tagged env:billing:staging, and the same without it"
seed() {
  cat <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-projects</Generator><DatabaseName>p</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid root)</UUID><Name>Root</Name>
      <Group>
        <UUID>$(uuid services)</UUID><Name>services</Name>
        $1
        <Entry>
          <UUID>$(uuid stripe)</UUID>
          <Tags>env:billing,finance</Tags>
          <CustomData><Item><Key>projects.entry</Key><Value>projects-entry-data</Value></Item></CustomData>
          <String><Key>Title</Key><Value>Stripe</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">stripe-login</Value></String>
          <String><Key>STRIPE_SECRET_KEY</Key><Value ProtectInMemory="True">sk_test_projects</Value></String>
          <String><Key>Region</Key><Value>eu</Value></String>
        </Entry>
        <Entry>
          <UUID>$(uuid database)</UUID>
          <Tags>env:billing:prod</Tags>
          <String><Key>Title</Key><Value>Database</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">database-password</Value></String>
        </Entry>
        <Entry>
          <UUID>$(uuid odd)</UUID>
          <Tags>env:billing:Prod</Tags>
          <String><Key>Title</Key><Value>Odd</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">odd-password</Value></String>
        </Entry>
        <Entry>
          <UUID>$(uuid other)</UUID>
          <Tags>finance</Tags>
          <String><Key>Title</Key><Value>Other</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">other-password</Value></String>
        </Entry>
      </Group>
    </Group>
  </Root>
</KeePassFile>
EOF
}
seed '<Tags>env:billing:staging</Tags>' >"$dir/grouped.xml"
seed '' >"$dir/plain.xml"
for name in grouped plain; do
  printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/$name.xml")" "$(native "$dir/$name.kdbx")" \
    || die "keepassxc-cli could not import the $name seed"
done
[ "$(version "$plain")" = 000004 ] || die "KeePassXC did not make the plain vault KDBX 4.0: $(version "$plain")"
grouped_version=$(version "$grouped")
exported=$(kx export "$grouped" -f xml) || die "KeePassXC cannot export the group-tagged vault"
[ "$(grep -c '<Tags>env:billing:staging</Tags>' <<<"$exported")" = 1 ] || die "KeePassXC did not keep the group tag"
printf '    KeePassXC wrote the group-tagged vault as %s and the other as 000004 (minor, major)\n' "$grouped_version"

step "env ls names billing, its dev and protected prod, exactly their entries, and reports env:billing:Prod"
listed=$(kp_on "$grouped" env ls 2>"$dir/ls.err") || die "keypaste env ls failed: $(cat "$dir/ls.err")"
expected=$'billing\n  dev\n    services/Stripe\n  prod  protected\n    services/Database'
[ "$listed" = "$expected" ] || die "env ls printed:
${listed}
not:
${expected}"
grep -qF "env:billing:Prod" "$dir/ls.err" || die "env ls did not report env:billing:Prod: $(cat "$dir/ls.err")"
grep -qF "services/Odd" "$dir/ls.err" || die "env ls did not name the entry carrying env:billing:Prod"
grep -qi 'staging\|finance' <<<"$listed$(cat "$dir/ls.err")" && die "env ls read the group tag or an ordinary tag"
json=$(kp_on "$grouped" env ls --json 2>/dev/null) || die "keypaste env ls --json failed"
jq -e '. == [{"project":"billing","legacy":false,"environments":[
          {"name":"dev","protected":false,"members":[{"path":"services/Stripe","group":"services","title":"Stripe"}]},
          {"name":"prod","protected":true,"members":[{"path":"services/Database","group":"services","title":"Database"}]}]}]' \
  <<<"$json" >/dev/null || die "env ls --json printed ${json}"

step "env tag and env untag change the entry's own tags, and KeePassXC reads them"
said=$(kp_on "$plain" env tag billing services/Stripe -p prod --yes 2>&1) || die "env tag failed: ${said}"
grep -qF 'STRIPE_SECRET_KEY' <<<"$said" || die "env tag did not name the field that joins: ${said}"
grep -qF 'Region' <<<"$said" && die "env tag named a field that is not env-named: ${said}"
[ "$(tags "$plain" services/Stripe | paste -sd' ' -)" = "env:billing env:billing:prod finance" ] \
  || die "KeePassXC reads the tags $(tags "$plain" services/Stripe | paste -sd' ' -)"
[ "$(version "$plain")" = 000004 ] || die "tagging raised the plain vault above KDBX 4.0: $(version "$plain")"
said=$(kp_on "$plain" env untag billing services/Stripe --yes 2>&1) || die "env untag failed: ${said}"
[ "$(tags "$plain" services/Stripe | paste -sd' ' -)" = "env:billing:prod finance" ] \
  || die "after untag KeePassXC reads the tags $(tags "$plain" services/Stripe | paste -sd' ' -)"

said=$(kp_on "$grouped" env tag billing services/Other -p qa --yes 2>&1) || die "env tag on the group-tagged vault failed: ${said}"
grep -qx 'env:billing:qa' <<<"$(tags "$grouped" services/Other)" || die "KeePassXC does not read the tag keypaste added"
[ "$(version "$grouped")" = "$grouped_version" ] || die "tagging changed the group-tagged vault's version"
exported=$(kx export "$grouped" -f xml) || die "KeePassXC cannot export the group-tagged vault"
[ "$(grep -c '<Tags>env:billing:staging</Tags>' <<<"$exported")" = 1 ] || die "the group tag did not survive"
said=$(kp_on "$grouped" env untag billing services/Other -p qa --yes 2>&1) || die "env untag on the group-tagged vault failed: ${said}"

for line in "env tag bill:ing services/Stripe --yes" "env tag billing services/Stripe -p Prod --yes" "env tag billing .keypaste/x --yes"; do
  was=$(bytes "$plain")
  set +e
  # shellcheck disable=SC2086 # the line is split into its words on purpose
  said=$(kp_on "$plain" $line 2>&1)
  rc=$?
  set -e
  [ "$rc" -ne 0 ] || die "keypaste ${line} was accepted"
  [ "$was" = "$(bytes "$plain")" ] || die "keypaste ${line} changed the vault"
  printf '    refused, bytes unchanged: %s\n' "$line"
done

step "KeePassXC tags an entry env:billing:Prod through a merge"
kx export "$grouped" -f xml | awk -v id="$(uuid other)" '
  /<History>/ { inside = 1 }
  !inside && index($0, "<UUID>" id "</UUID>") { mine = 1 }
  mine && !inside { sub(/<Tags>[^<]*<\/Tags>/, "<Tags>finance,env:billing:Prod</Tags>"); sub(/<LastModificationTime>[^<]*</, "<LastModificationTime>2037-01-01T00:00:00Z<") }
  /<\/History>/ { inside = 0 }
  mine && !inside && /<\/Entry>/ { mine = 0 }
  { print }' >"$dir/newer.xml"
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/newer.xml")" "$(native "$dir/newer.kdbx")" \
  || die "keepassxc-cli could not import the newer copy"
printf '%s\n' "$pw" | "$cli" merge -q -s "$(native "$grouped")" "$(native "$dir/newer.kdbx")" >/dev/null \
  || die "keepassxc-cli could not merge the newer copy"
grep -qx 'env:billing:Prod' <<<"$(tags "$grouped" services/Other)" || die "the merge did not bring env:billing:Prod"

step "a real keypaste agent and keypaste-mcp: the hour is refused for a protected tag and honoured otherwise"
pipe="keypaste-projects-$$-$(date +%s)"
audit="$dir/audit.jsonl"
agent_err="$dir/agent.err"
# One answer per request, in order: the hour for the dev entry, the hour then once for the prod
# entry, and the hour for the entry KeePassXC tagged through the merge.
printf '%s\nh\nh\no\nh\n' "$pw" | "$kp" agent --vault "$(native "$grouped")" --approver "$pipe" --approval-timeout 30 \
  >/dev/null 2>"$agent_err" &
agent_pid=$!
for _ in $(seq 1 100); do
  grep -q 'listening on' "$agent_err" && break
  kill -0 "$agent_pid" 2>/dev/null || die "keypaste agent exited before it listened: $(cat "$agent_err")"
  sleep 0.2
done
grep -q 'listening on' "$agent_err" || die "keypaste agent never started listening"

ask() {
  local id=$1 entry=$2 out="$dir/ask-$1.out"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"projects-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$entry\",\"field\":\"password\",\"reason\":\"projects gate\",\"ttl_seconds\":60}}}"
    sleep 8
  } | "$mcp" --vault "$(native "$grouped")" --expose 'services/**' --audit-log "$(native "$audit")" --approver "$pipe" \
        --client-label projects-probe >"$out" 2>"$dir/ask-$id.err" || die "keypaste-mcp exited non-zero: $(cat "$dir/ask-$id.err")"
  tr -d '\r' <"$out"
}
granted() { jq -e --argjson id "$1" 'select(.id == $id) | .result.isError == false' >/dev/null; }

ask 2 services/Stripe | granted 2 || die "the dev entry answered with the hour was not released (the hour is not honoured, so the control fails)"
ask 3 services/Database | granted 3 && die "the prod-tagged entry was released for the hour"
ask 4 services/Database | granted 4 || die "the prod-tagged entry answered once was not released"
ask 5 services/Other | granted 5 && die "the entry KeePassXC tagged env:billing:Prod was released for the hour"

[ "$(tr -d '\r' <"$agent_err" | grep -c 'it is asked about every time')" = 3 ] \
  || die "keypaste agent did not say three times that the entry is asked about every time: $(cat "$agent_err")"
jq -e -s 'map(select(.method == "prompt")) | length == 4
          and .[0].decision == "granted" and .[1].decision == "denied"
          and .[2].decision == "granted" and .[3].decision == "denied"' <"$audit" >/dev/null \
  || die "the audit log does not show granted, denied, granted, denied: $(cat "$audit")"

step "C.1b: KeePassXC makes a vault whose billing project is tagged entries' fields beside one legacy variable"
child=${BASH:-/bin/bash}
probe='printf "stripe=%s db=%s legacy=%s region=%s shared=%s" "${STRIPE_SECRET_KEY-unset}" "${DATABASE_URL-unset}" "${LEGACY_TOKEN-unset}" "${Region-unset}" "${SHARED_KEY-unset}"'
# $1: extra <Entry> elements for services; $2: the Database entry's DATABASE_URL; $3: its <Times>; $4: extra legacy entries.
fields_seed() {
  cat <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-projects</Generator><DatabaseName>f</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid froot)</UUID><Name>Root</Name>
      <Group>
        <UUID>$(uuid fenv)</UUID><Name>env</Name>
        <Group>
          <UUID>$(uuid fbilling)</UUID><Name>billing</Name>
          <Entry>
            <UUID>$(uuid flegacy)</UUID>
            <String><Key>Title</Key><Value>LEGACY_TOKEN</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">legacy-c1b</Value></String>
          </Entry>
          $4
        </Group>
      </Group>
      <Group>
        <UUID>$(uuid fservices)</UUID><Name>services</Name>
        <Entry>
          <UUID>$(uuid fstripe)</UUID>
          <Tags>env:billing,finance</Tags>
          <String><Key>Title</Key><Value>Stripe</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">stripe-login-c1b</Value></String>
          <String><Key>STRIPE_SECRET_KEY</Key><Value ProtectInMemory="True">stripe-c1b</Value></String>
          <String><Key>Region</Key><Value>eu-c1b</Value></String>
        </Entry>
        <Entry>
          <UUID>$(uuid fdatabase)</UUID>
          <Tags>env:billing</Tags>
          $3
          <String><Key>Title</Key><Value>Database</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">database-login-c1b</Value></String>
          <String><Key>DATABASE_URL</Key><Value ProtectInMemory="True">$2</Value></String>
        </Entry>
        <Entry>
          <UUID>$(uuid fshared)</UUID>
          <Tags>env:billing:staging,env:billing:prod</Tags>
          <String><Key>Title</Key><Value>Shared</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">shared-login-c1b</Value></String>
          <String><Key>SHARED_KEY</Key><Value ProtectInMemory="True">shared-c1b</Value></String>
        </Entry>
        $1
      </Group>
    </Group>
  </Root>
</KeePassFile>
EOF
}
make_fields() { # name, then fields_seed's arguments
  local name=$1; shift
  fields_seed "$@" >"$dir/$name.xml"
  printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/$name.xml")" "$(native "$dir/$name.kdbx")" \
    || die "keepassxc-cli could not import the $name seed"
}
fields="$dir/fields.kdbx"
make_fields fields '' 'db-c1b' '' ''
grep -qx 'env:billing' <<<"$(tags "$fields" services/Database)" || die "KeePassXC did not keep the Database entry's tag"
[ "$(kx show "$fields" services/Stripe -a STRIPE_SECRET_KEY)" = stripe-c1b ] || die "KeePassXC does not read the tagged field"
# The app's half runs on this copy, which no keypaste has written.
app_fields="$dir/fields-app.kdbx"
cp "$fields" "$app_fields"

step "C.1b: keypaste run gives the child exactly the tagged fields and the legacy variable"
out=$(printf '%s\n' "$pw" | "$kp" run billing --vault "$fields" -- "$child" -c "$probe" 2>"$dir/run.err" | tr -d '\r') \
  || die "keypaste run failed: $(cat "$dir/run.err")"
[ "$out" = "stripe=stripe-c1b db=db-c1b legacy=legacy-c1b region=unset shared=unset" ] || die "the child's environment was: ${out}"
out=$(printf '%s\n' "$pw" | "$kp" run billing -p staging --vault "$fields" -- "$child" -c "$probe" 2>"$dir/run.err" | tr -d '\r') \
  || die "keypaste run -p staging failed: $(cat "$dir/run.err")"
[ "$out" = "stripe=unset db=unset legacy=unset region=unset shared=shared-c1b" ] || die "the staging child's environment was: ${out}"

step "C.1b: env export writes a reference for each variable, and env diff compares the tagged environments"
exported=$(kp_on "$fields" env export billing --stdout 2>/dev/null) || die "keypaste env export failed"
for key in DATABASE_URL LEGACY_TOKEN STRIPE_SECRET_KEY; do
  grep -qF "kp://billing/dev/${key}" <<<"$exported" || die "env export wrote no reference for ${key}: ${exported}"
done
grep -qF 'Region' <<<"$exported" && die "env export wrote a field that is not env-named"
grep -qF -- '-c1b' <<<"$exported" && die "env export wrote a value"
diffed=$(kp_on "$fields" env diff billing dev staging 2>/dev/null) || die "keypaste env diff failed"
grep -qE 'SHARED_KEY +missing in dev' <<<"$diffed" || die "env diff did not find SHARED_KEY missing in dev: ${diffed}"
grep -qE 'STRIPE_SECRET_KEY +missing in staging' <<<"$diffed" || die "env diff did not find STRIPE_SECRET_KEY missing in staging: ${diffed}"

step "C.1b: each refusal starts nothing and names its entries"
refused() { # name, expected text, then fields_seed's arguments
  local name=$1 expected=$2 status err
  shift 2
  make_fields "$name" "$@"
  set +e
  out=$(printf '%s\n' "$pw" | "$kp" run billing --vault "$dir/$name.kdbx" -- "$child" -c "$probe" 2>"$dir/$name.err")
  status=$?
  set -e
  err=$(tr -d '\r' <"$dir/$name.err")
  [ "$status" = 2 ] || die "${name}: expected exit 2, got ${status}: ${err}"
  [ -z "$out" ] || die "${name}: a child started: ${out}"
  grep -qF "$expected" <<<"$err" || die "${name}: the refusal does not say '${expected}': ${err}"
  grep -qF -- '-c1b' <<<"$err" && die "${name}: a value was printed: ${err}"
  printf '    refused, nothing started: %s\n' "$name"
}
twin="<Entry><UUID>$(uuid ftwin)</UUID><Tags>env:billing</Tags><String><Key>Title</Key><Value>Twin</Value></String><String><Key>DATABASE_URL</Key><Value ProtectInMemory=\"True\">twin-c1b</Value></String></Entry>"
refused two-entries 'DATABASE_URL is on more than one entry (services/Database, services/Twin)' "$twin" 'db-c1b' '' ''
api="<Entry><UUID>$(uuid fapi)</UUID><Tags>env:billing</Tags><String><Key>Title</Key><Value>Api</Value></String><String><Key>API_KEY</Key><Value ProtectInMemory=\"True\">tagged-api-c1b</Value></String></Entry>"
legacy_api="<Entry><UUID>$(uuid flapi)</UUID><String><Key>Title</Key><Value>Api_Key</Value></String><String><Key>Password</Key><Value ProtectInMemory=\"True\">legacy-api-c1b</Value></String></Entry>"
refused case-pair "Api_Key differs only in case from 'API_KEY', which Windows treats as one variable (env/billing/Api_Key, services/Api)" "$api" 'db-c1b' '' "$legacy_api"
refused expired 'DATABASE_URL expired 2020-01-02 03:04:05Z (services/Database)' '' 'db-c1b' \
  '<Times><Expires>True</Expires><ExpiryTime>2020-01-02T03:04:05Z</ExpiryTime></Times>' ''
refused placeholder 'DATABASE_URL holds the KeePass placeholder {PASSWORD}, which keypaste does not resolve (services/Database)' \
  '' 'postgres://u:{PASSWORD}@db-c1b' '' ''
custom="<Entry><UUID>$(uuid fcustom)</UUID><Tags>env:billing</Tags><String><Key>Title</Key><Value>Custom</Value></String><String><Key>Password</Key><Value ProtectInMemory=\"True\">custom-login-c1b</Value></String><String><Key>PASSWORD</Key><Value ProtectInMemory=\"True\">custom-field-c1b</Value></String></Entry>"
refused standard-name 'PASSWORD is a custom field named like a standard one, which keypaste never releases (services/Custom)' \
  "$custom" 'db-c1b' '' ''

step "C.1b: a token scoped to billing's dev and staging sets, made before an agent holds the vault"
token=$(printf '%s\n' "$pw" | "$kp" token create gate-ci --scope 'read:billing/dev/*,read:billing/staging/*' --vault "$(native "$fields")" 2>"$dir/token.err" | tr -d '\r') \
  || die "keypaste token create failed: $(cat "$dir/token.err")"
[ -n "$token" ] || die "keypaste token create printed no token: $(cat "$dir/token.err")"

step "C.1b: run --session through a real keypaste agent names the source entries, and a set holding a prod-tagged member is asked once only"
fields_pipe="keypaste-fields-$$-$(date +%s)"
fields_err="$dir/fields-agent.err"
cleanup_fields() {
  cleanup
  if [ -n "${fields_pid:-}" ]; then kill "$fields_pid" 2>/dev/null || true; fi
  exec 7>&- 2>/dev/null || true
}
trap cleanup_fields EXIT
# The hour for the dev run, then once for the staging run, whose Shared entry is also tagged env:billing:prod.
printf '%s\nh\no\n' "$pw" | "$kp" agent --vault "$(native "$fields")" --approver "$fields_pipe" --approval-timeout 30 \
  >/dev/null 2>"$fields_err" &
fields_pid=$!
for _ in $(seq 1 100); do
  grep -q 'listening on' "$fields_err" && break
  kill -0 "$fields_pid" 2>/dev/null || die "keypaste agent exited before it listened: $(cat "$fields_err")"
  sleep 0.2
done
grep -q 'listening on' "$fields_err" || die "keypaste agent never started listening on the fields vault"

out=$("$kp" run --session billing --vault "$(native "$fields")" --approver "$fields_pipe" -- "$child" -c "$probe" 2>"$dir/session-dev.err" | tr -d '\r') \
  || die "run --session failed: $(cat "$dir/session-dev.err") / $(cat "$fields_err")"
[ "$out" = "stripe=stripe-c1b db=db-c1b legacy=legacy-c1b region=unset shared=unset" ] || die "the session child's environment was: ${out}"
from=$(grep -m1 '^  from ' <<<"$(tr -d '\r' <"$fields_err")" || true)
for entry in services/Database env/billing/LEGACY_TOKEN services/Stripe; do
  grep -qF "$entry" <<<"$from" || die "the agent's prompt does not name ${entry}: ${from}"
done

out=$("$kp" run --session billing -p staging --vault "$(native "$fields")" --approver "$fields_pipe" -- "$child" -c "$probe" 2>"$dir/session-staging.err" | tr -d '\r') \
  || die "run --session -p staging failed: $(cat "$dir/session-staging.err") / $(cat "$fields_err")"
[ "$out" = "stripe=unset db=unset legacy=unset region=unset shared=shared-c1b" ] || die "the staging session child's environment was: ${out}"
grep -qF 'services/Shared' <<<"$(tr -d '\r' <"$fields_err")" || die "the agent's prompt does not name services/Shared"
[ "$(tr -d '\r' <"$fields_err" | grep -c 'it is asked about every time')" = 1 ] \
  || die "the staging set holding a prod-tagged member was not asked once only: $(cat "$fields_err")"
grep -qF -- '-c1b' "$fields_err" && die "a value reached the agent's terminal"

step "C.1b: the token's dev run is audited with its source entries, and its staging set, holding a prod-tagged member, is refused"
out=$(KEYPASTE_TOKEN="$token" "$kp" run --token env --vault "$(native "$fields")" --approver "$fields_pipe" billing -- "$child" -c "$probe" 2>"$dir/token-dev.err" | tr -d '\r') \
  || die "run --token failed: $(cat "$dir/token-dev.err") / $(cat "$fields_err")"
[ "$out" = "stripe=stripe-c1b db=db-c1b legacy=legacy-c1b region=unset shared=unset" ] || die "the token child's environment was: ${out}"
set +e
out=$(KEYPASTE_TOKEN="$token" "$kp" run --token env --vault "$(native "$fields")" --approver "$fields_pipe" billing -p staging -- "$child" -c "$probe" 2>"$dir/token-staging.err")
status=$?
set -e
[ "$status" -ne 0 ] || die "a token without allow-prod ran a set holding a prod-tagged member"
[ -z "$out" ] || die "a child started for the refused token run: ${out}"
grep -qF 'holds an entry of a protected environment' "$dir/token-staging.err" \
  || die "the refused token run does not say why: $(cat "$dir/token-staging.err")"
home_audit="$dir/home/audit.jsonl"
jq -e -s 'map(select(.method == "token")) | length == 2
          and .[0].decision == "granted" and .[0].entries == ["services/Database", "env/billing/LEGACY_TOKEN", "services/Stripe"]
          and .[1].decision == "denied"' <"$home_audit" >/dev/null \
  || die "the token runs' audit lines do not name the source entries: $(cat "$home_audit")"
grep -qF -- '-c1b' "$home_audit" && die "a value reached the audit log"

step "C.1b: run --session through the app's prompt window on the untouched KeePassXC vault names the source entries, and the staging set is asked once only"
hold_out="$dir/hold.out"
# fd 7 is the driver's standard input: a line answers the prompt drawn, and closing it shuts the app down.
exec 7> >(KEYPASTE_DRIVER_PASSWORD="$pw" "$drv" hold "$(native "$app_fields")" >"$hold_out" 2>&1)
DIE_FILES=hold_out
wait_for 'holding session' "$hold_out" 1
grep -q 'not served' "$hold_out" && die "the app unlocked the KeePassXC vault and did not serve it"

# $1 names the case, $2 the prompt it must draw, $3 the answer, $4 what the child must print; the rest are run's arguments.
app_run() {
  local label=$1 drawn=$2 answer=$3 expected=$4 prompts line
  shift 4
  prompts=$(( $(grep -c '^env-prompt project=' "$hold_out" || true) + 1 ))
  "$kp" run --session "$@" --vault "$(native "$app_fields")" -- "$child" -c "$probe" \
    <<<"$pw" >"$dir/app-$label.out" 2>"$dir/app-$label.err" 7>&- &
  local run_pid=$!
  wait_for '^env-prompt project=' "$hold_out" "$prompts"
  line=$(grep '^env-prompt project=' "$hold_out" | tail -1 | tr -d '\r')
  case "$line" in
    *" $drawn") ;;
    *) die "${label}: the app's prompt does not end '${drawn}': ${line}" ;;
  esac
  echo "$answer" >&7
  wait "$run_pid" || die "${label}: run --session through the app failed: $(cat "$dir/app-$label.err")"
  [ "$(tr -d '\r' <"$dir/app-$label.out")" = "$expected" ] || die "${label}: the child's environment was: $(cat "$dir/app-$label.out")"
  grep -qi 'password' "$dir/app-$label.err" && die "${label}: the runner asked for a password"
  return 0
}
app_run dev 'keys=DATABASE_URL,LEGACY_TOKEN,STRIPE_SECRET_KEY entries=services/Database,env/billing/LEGACY_TOKEN,services/Stripe timed=True' \
  approve "stripe=stripe-c1b db=db-c1b legacy=legacy-c1b region=unset shared=unset" billing
app_run staging 'keys=SHARED_KEY entries=services/Shared timed=False' \
  once "stripe=unset db=unset legacy=unset region=unset shared=shared-c1b" billing -p staging
grep -qF -- '-c1b' "$hold_out" && die "a value reached the app's output"
exec 7>&-
wait_for '^shut down' "$hold_out" 1

step "C.1c: KeePassXC makes a vault for the writers, and an untouched copy for the app"
c1c="$dir/c1c.kdbx"
make_fields c1c '' 'db-c1b' '' ''
app_c1c="$dir/c1c-app.kdbx"
cp "$c1c" "$app_c1c"
c1c_probe='printf "stripe=%s db=%s new=%s app=%s" "${STRIPE_SECRET_KEY-unset}" "${DATABASE_URL-unset}" "${NEW_KEY-unset}" "${APP_KEY-unset}"'

# The revisions KeePassXC holds for the entry at the path $2 in the vault $1.
revisions_at() {
  local id
  id=$(kx show "$1" "$2" -a Uuid | tr -d '{}-' | xxd -r -p | base64) || die "KeePassXC cannot read the UUID of '$2'"
  kx export "$1" -f xml >"$dir/revisions.xml" || die "KeePassXC cannot export $1"
  awk -v id="<UUID>$id</UUID>" '
    /<History>/ { inhist = 1; if (mine) n = 0; next }
    /<\/History>/ { inhist = 0; if (mine) { print n; found = 1; exit } next }
    inhist { if (mine && /<Entry>/) n++; next }
    /<UUID>/ { mine = index($0, id) > 0 }
    END { if (!found) print 0 }' "$dir/revisions.xml"
}
# "protected" or "plain" for the current string called $2 in the vault $1, never a revision's.
protection_of() {
  local xml
  xml=$(kx export "$1" -f xml | awk '/<History>/{past=1} !past{print} /<\/History>/{past=0}') || die "KeePassXC cannot export $1"
  awk -v key="<Key>$2</Key>" '
    !want && (p = index($0, key)) { want = 1; $0 = substr($0, p + length(key)) }
    want && (v = index($0, "<Value")) {
      tag = substr($0, v); tag = substr(tag, 1, index(tag, ">"))
      print (index(tag, "ProtectInMemory=\"True\"") ? "protected" : "plain"); exit
    }' <<<"$xml"
}
history_holds() { kx export "$1" -f xml | awk '/<History>/{inside=1} inside{print} /<\/History>/{inside=0}' | grep -cF "$2" || true; }
c1c_run() { printf '%s\n' "$pw" | "$kp" run billing --vault "$1" -- "$child" -c "$c1c_probe" 2>"$dir/c1c-run.err" | tr -d '\r'; }
drive() { KEYPASTE_DRIVER_PASSWORD="$pw" KEYPASTE_DRIVER_NEW_PASSWORD="${value:-}" "$drv" "$@" | tr -d '\r'; }

step "C.1c: env set of a tagged key changes it on its own entry, protected, with one revision"
was=$(revisions_at "$c1c" services/Stripe)
said=$(kp_on "$c1c" env set billing STRIPE_SECRET_KEY=stripe-c1c 2>&1) || die "env set of a tagged key failed: ${said}"
grep -qF 'Updated STRIPE_SECRET_KEY on services/Stripe' <<<"$said" || die "env set did not say where it wrote: ${said}"
[ "$(kx show "$c1c" services/Stripe -a STRIPE_SECRET_KEY)" = stripe-c1c ] || die "KeePassXC does not read the value env set wrote on services/Stripe"
[ "$(protection_of "$c1c" STRIPE_SECRET_KEY)" = protected ] || die "the value env set wrote is not protected"
[ "$(revisions_at "$c1c" services/Stripe)" = $((was + 1)) ] || die "env set did not add exactly one revision to services/Stripe"
kx show "$c1c" env/billing/STRIPE_SECRET_KEY >/dev/null 2>&1 && die "env set made a legacy entry for a key a tagged entry holds"

step "C.1c: env set of a new key creates env/billing/.env, tagged env:billing, holding it protected"
said=$(kp_on "$c1c" env set billing NEW_KEY=new-c1c 2>&1) || die "env set of a new key failed: ${said}"
grep -qF 'Set NEW_KEY on env/billing/.env, created and tagged env:billing' <<<"$said" || die "env set did not say it created the home entry: ${said}"
[ "$(kx show "$c1c" env/billing/.env -a NEW_KEY)" = new-c1c ] || die "KeePassXC does not read NEW_KEY on env/billing/.env"
[ "$(protection_of "$c1c" NEW_KEY)" = protected ] || die "NEW_KEY is not protected"
[ "$(tags "$c1c" env/billing/.env | paste -sd' ' -)" = "env:billing" ] || die "KeePassXC reads the home entry's tags as $(tags "$c1c" env/billing/.env | paste -sd' ' -)"
[ "$(revisions_at "$c1c" env/billing/.env)" = 0 ] || die "creating the home entry left a revision"
kx show "$c1c" env/billing/NEW_KEY >/dev/null 2>&1 && die "env set made a legacy entry for a new key"
for refused_key in api_key KPXC_X URL; do
  was=$(bytes "$c1c")
  set +e
  said=$(kp_on "$c1c" env set billing "${refused_key}=nope-c1c" 2>&1)
  rc=$?
  set -e
  [ "$rc" -ne 0 ] || die "env set accepted the new key ${refused_key}, which no field of a project can be named"
  [ "$was" = "$(bytes "$c1c")" ] || die "the refused new key ${refused_key} changed the vault"
done

step "C.1c: env pull of a .env touching keys on two entries makes one revision on each, and run gives the child the new values"
printf 'STRIPE_SECRET_KEY=stripe-pull-c1c\nDATABASE_URL=db-pull-c1c\nNEW_KEY=new-pull-c1c\n' >"$dir/c1c.env"
stripe_was=$(revisions_at "$c1c" services/Stripe)
database_was=$(revisions_at "$c1c" services/Database)
home_was=$(revisions_at "$c1c" env/billing/.env)
said=$(kp_on "$c1c" env pull billing "$(native "$dir/c1c.env")" --yes --keep 2>&1) || die "env pull failed: ${said}"
grep -qF 'DATABASE_URL on services/Database' <<<"$said" || die "env pull did not name the entry each key goes to: ${said}"
[ "$(revisions_at "$c1c" services/Stripe)" = $((stripe_was + 1)) ] || die "env pull did not add exactly one revision to services/Stripe"
[ "$(revisions_at "$c1c" services/Database)" = $((database_was + 1)) ] || die "env pull did not add exactly one revision to services/Database"
[ "$(revisions_at "$c1c" env/billing/.env)" = $((home_was + 1)) ] || die "env pull did not add exactly one revision to env/billing/.env"
[ "$(kx show "$c1c" services/Database -a DATABASE_URL)" = db-pull-c1c ] || die "KeePassXC does not read the pulled DATABASE_URL"
[ "$(protection_of "$c1c" DATABASE_URL)" = protected ] || die "the pulled DATABASE_URL is not protected"
out=$(c1c_run "$c1c") || die "keypaste run after the writes failed: $(cat "$dir/c1c-run.err")"
[ "$out" = "stripe=stripe-pull-c1c db=db-pull-c1c new=new-pull-c1c app=unset" ] || die "the child's environment after the writes was: ${out}"

step "C.1c: env rm removes a field, and the entry's history keeps it"
said=$(kp_on "$c1c" env rm billing NEW_KEY --yes 2>&1) || die "env rm of a field failed: ${said}"
kx show "$c1c" env/billing/.env -a NEW_KEY >/dev/null 2>&1 && die "KeePassXC still reads the field env rm removed"
[ "$(history_holds "$c1c" new-pull-c1c)" -ge 1 ] || die "the removed field's value is not in the entry's history"

step "C.1c: a tag change names what it reaches before writing, and writes nothing unconfirmed"
was=$(bytes "$c1c")
set +e
said=$(kp_on "$c1c" env tag billing services/Database -p staging 2>&1)
rc=$?
set -e
[ "$rc" -ne 0 ] || die "env tag without a terminal or --yes was accepted"
[ "$was" = "$(bytes "$c1c")" ] || die "an unconfirmed tag change changed the vault"
said=$(kp_on "$c1c" env tag billing services/Database -p staging --yes 2>&1) || die "env tag --yes failed: ${said}"
[ "$(grep -nF 'billing/staging' <<<"$said" | head -1 | cut -d: -f1)" -lt "$(grep -nF 'tagged services/Database' <<<"$said" | cut -d: -f1)" ] \
  || die "env tag did not name the environment before it wrote: ${said}"
grep -qF 'Joining billing/staging: DATABASE_URL.' <<<"$said" || die "env tag did not name the field that joins: ${said}"
grep -qx 'env:billing:staging' <<<"$(tags "$c1c" services/Database)" || die "KeePassXC does not read the confirmed tag"
said=$(kp_on "$c1c" env untag billing services/Database -p staging --yes 2>&1) || die "env untag --yes failed: ${said}"
grep -qF 'Leaving billing/staging: DATABASE_URL.' <<<"$said" || die "env untag did not name the field that leaves: ${said}"

step "C.1c: the app adds, replaces, imports and removes keys on the untouched copy, and KeePassXC reads each"
value=app-stripe-c1c
said=$(drive env-set "$(native "$app_c1c")" billing dev STRIPE_SECRET_KEY) || die "the app's replace failed: ${said}"
[ "$(kx show "$app_c1c" services/Stripe -a STRIPE_SECRET_KEY)" = app-stripe-c1c ] || die "KeePassXC does not read the value the app replaced on services/Stripe"
value=app-key-c1c
said=$(drive env-add "$(native "$app_c1c")" billing dev APP_KEY) || die "the app's add failed: ${said}"
[ "$(kx show "$app_c1c" env/billing/.env -a APP_KEY)" = app-key-c1c ] || die "KeePassXC does not read APP_KEY on the home entry the app created"
[ "$(protection_of "$app_c1c" APP_KEY)" = protected ] || die "the app's new key is not protected"
[ "$(tags "$app_c1c" env/billing/.env | paste -sd' ' -)" = "env:billing" ] || die "the home entry the app created is not tagged env:billing"
value=app-entry-c1c
said=$(drive env-add "$(native "$app_c1c")" billing dev ENTRY_KEY --entry services/Database) || die "the app's add onto a chosen entry failed: ${said}"
[ "$(kx show "$app_c1c" services/Database -a ENTRY_KEY)" = app-entry-c1c ] || die "KeePassXC does not read ENTRY_KEY on the entry chosen in the app"
value=
printf 'DATABASE_URL=app-db-c1c\nAPP_KEY=app-key-pull-c1c\n' >"$dir/c1c-app.env"
database_was=$(revisions_at "$app_c1c" services/Database)
said=$(drive env-import "$(native "$app_c1c")" billing dev "$(native "$dir/c1c-app.env")") || die "the app's import failed: ${said}"
grep -qF 'previewed: DATABASE_URL  replaces the value on services/Database' <<<"$said" || die "the app's import preview did not name the entry: ${said}"
[ "$(kx show "$app_c1c" services/Database -a DATABASE_URL)" = app-db-c1c ] || die "KeePassXC does not read the DATABASE_URL the app imported"
[ "$(revisions_at "$app_c1c" services/Database)" = $((database_was + 1)) ] || die "the app's import did not add exactly one revision to services/Database"
out=$(c1c_run "$app_c1c") || die "keypaste run after the app's writes failed: $(cat "$dir/c1c-run.err")"
[ "$out" = "stripe=app-stripe-c1c db=app-db-c1c new=unset app=app-key-pull-c1c" ] || die "the child's environment after the app's writes was: ${out}"
said=$(drive env-rm "$(native "$app_c1c")" billing dev APP_KEY) || die "the app's remove failed: ${said}"
kx show "$app_c1c" env/billing/.env -a APP_KEY >/dev/null 2>&1 && die "KeePassXC still reads the field the app removed"
[ "$(history_holds "$app_c1c" app-key-pull-c1c)" -ge 1 ] || die "the field the app removed is not in the entry's history"

step "C.1c: a tag change on the app's entry pane names what it reaches, and declined writes nothing"
for act in tag-add tag-rm; do
  if [ "$act" = tag-rm ]; then
    said=$(drive tag-add "$(native "$app_c1c")" services/Database env:billing:staging) || die "the app's confirmed tag failed: ${said}"
    grep -qx 'env:billing:staging' <<<"$(tags "$app_c1c" services/Database)" || die "KeePassXC does not read the tag the app confirmed"
  fi
  was=$(bytes "$app_c1c")
  said=$(drive "$act" "$(native "$app_c1c")" services/Database env:billing:staging --decline) || die "the app's declined ${act} failed: ${said}"
  grep -qF 'asked: ' <<<"$said" || die "the app's ${act} asked nothing: ${said}"
  grep -qF 'billing/staging' <<<"$said" || die "the app's ${act} did not name the environment: ${said}"
  grep -qF 'DATABASE_URL' <<<"$said" || die "the app's ${act} did not name the field: ${said}"
  grep -qx 'declined' <<<"$said" || die "the app's ${act} was not declined: ${said}"
  [ "$was" = "$(bytes "$app_c1c")" ] || die "the app's declined ${act} changed the vault"
done
said=$(drive tag-rm "$(native "$app_c1c")" services/Database env:billing:staging) || die "the app's confirmed untag failed: ${said}"
grep -qx 'env:billing:staging' <<<"$(tags "$app_c1c" services/Database)" && die "KeePassXC still reads the tag the app removed"
grep -qF -- '-c1c' <<<"$(cat "$dir"/c1c-run.err)" && die "a value reached the runner's output"

step "NEGATIVE CONTROL: the listing comparison must be able to fail"
[ "$listed" = "${expected}-CORRUPTED" ] && die "a deliberately corrupted expectation still matched — this gate is not gating"

printf '\nPROJECTS GATE PASSED: KeePassXC and keypaste agree on project tags, and a protected tag makes a real agent ask every time.\n'
