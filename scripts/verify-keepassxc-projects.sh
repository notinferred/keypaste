#!/usr/bin/env bash
# PERMANENT COMPATIBILITY GATE: project tags KeePassXC writes are the ones keypaste reads, and the
# ones keypaste writes are the ones KeePassXC reads (C.1a); the fields of entries KeePassXC tagged
# are the project's variables (C.1b), and nothing else is: an untagged entry under env/ is an
# ordinary entry, and a path protects nothing (C.6, D-0416).
#
# KeePassXC imports a KeePass XML document whose entries carry the tags env:billing,
# env:billing:prod, env:billing:Prod and finance, under a group tagged env:billing:staging, beside the
# untagged env/billing/OLD_KEY and env/billing/prod/DB. KeePassXC keeps the group tag and so writes
# KDBX 4.1; a second vault from the same document without the group tag is KDBX 4.0. `keypaste env
# ls` names billing with dev, a protected prod and exactly the entries tagged into each, with no
# legacy mark, reports env:billing:Prod, and ignores finance, the group tag and the untagged entries.
# `keypaste env tag` and `env untag` change an entry's tags; KeePassXC reads them with `show -a Tags`,
# the 4.0 vault stays 4.0, the 4.1 vault keeps its version and its group tag, and a refused tag leaves
# the file byte-identical.
#
# Then a real `keypaste agent` holds the 4.1 vault and a real `keypaste mcp`, exposed to the entries,
# asks for passwords. An untagged-for-prod entry answered with the hour gets it, and so does
# env/billing/prod/DB. The entry tagged env:billing:prod, and one KeePassXC tagged env:billing:Prod
# through `keepassxc-cli merge`, are offered Allow once only: the hour is refused and once is honoured.
#
# A third vault KeePassXC makes holds fields on entries tagged env:billing, a staging entry also tagged
# env:billing:prod, and the same untagged entries. `keypaste run` gives a child exactly the tagged
# fields of the environment it names, never OLD_KEY or DB, `keypaste get` still reads OLD_KEY as an
# ordinary entry, `env export` writes a reference for each tagged key and `env diff` compares the
# environments' key names. A key on two entries, an expired member, {PASSWORD} in a value and a
# custom PASSWORD field each start nothing and name their entries. `run --session` through a real
# `keypaste agent`, and through the app's own prompt window held by tests/Keypaste.AppDriver on an
# untouched copy, gets the same set after a prompt naming the source entries, and the staging set is
# asked once only. A scoped token's run of the dev set is audited with its source entries, and its
# staging set is refused because a member is also tagged env:billing:prod.
#
# A copy of the third vault, held by its own `keypaste agent`, is asked through a `keypaste mcp` on the
# default exposure (C.5b): the listing names the three tagged entries with their variables and project
# tags and no untagged one, STRIPE_SECRET_KEY is released, Stripe's password is refused before anyone
# is asked, and billing's dev set runs from entries under services/.
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
# A fifth vault KeePassXC makes holds web, mapped in projects.json, and billing, whose dev and staging
# share Stripe, beside Mail in dev, an untagged Queue and Odd tagged env:billing:Prod (C.4). The app's
# Projects screen, through tests/Keypaste.AppDriver, lists both projects, where each value lives, the
# shared entry's two environments and the ignored tag. It adds Queue to staging and takes Mail out of
# dev, each having named what it reaches, replaces the shared value naming both environments, adds a
# key with no entry chosen and imports a .env; each declined or refused act leaves the file byte-
# identical, and KeePassXC reads every tag, value and protection written. `keypaste run` resolves the
# exported references to exactly dev's fields.
#
# A sixth vault KeePassXC makes from the first document has two versions KeePassXC itself writes through
# a merge (V.11): Stripe loses env:billing while its password changes, and Database loses
# env:billing:prod alone. The app's Settings › Recommendations, through tests/Keypaste.AppDriver, lists
# Stripe's tag and not Database's, prints no value, keeps a dismissal across processes, and Restore tag
# names what the tag reaches before putting it back; KeePassXC reads env:billing again, with one more
# revision.
#
# NEGATIVE CONTROL: a corrupted expectation must fail the comparison the listing check rests on, a
# child holding OLD_KEY must fail the one the runs rest on, and the hour must be honoured for an entry
# no tag protects.
#
# Usage:  scripts/verify-keepassxc-projects.sh <work-directory>
# Env:    KP_COMPAT_PASSWORD   master password for the vaults        (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_APP_DRIVER  path to tests/Keypaste.AppDriver      (default: the Release build)
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='PROJECTS GATE FAILED: '

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-projects.sh <work-directory>"
. "$(dirname "${BASH_SOURCE[0]}")/lib/kpxc.sh"
require jq
kp=$(keypaste_bin)
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

# Untagged entries in the env/<project> layout of keypaste before 0.5.0, which are ordinary entries now.
untagged() {
  cat <<EOF
      <Group>
        <UUID>$(uuid untagged-env)</UUID><Name>env</Name>
        <Group>
          <UUID>$(uuid untagged-billing)</UUID><Name>billing</Name>
          <Entry>
            <UUID>$(uuid old-key)</UUID>
            <String><Key>Title</Key><Value>OLD_KEY</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">old-c1b</Value></String>
          </Entry>
          <Group>
            <UUID>$(uuid untagged-prod)</UUID><Name>prod</Name>
            <Entry>
              <UUID>$(uuid prod-db)</UUID>
              <String><Key>Title</Key><Value>DB</Value></String>
              <String><Key>Password</Key><Value ProtectInMemory="True">prod-db-c1b</Value></String>
            </Entry>
          </Group>
        </Group>
      </Group>
EOF
}

step "KeePassXC makes the vaults: tagged entries under a group tagged env:billing:staging, and the same without it"
seed() {
  cat <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-projects</Generator><DatabaseName>p</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid root)</UUID><Name>Root</Name>
$(untagged)
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

step "env ls names billing, its dev and protected prod, exactly their entries and no untagged one, and reports env:billing:Prod"
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
jq -e '. == [{"project":"billing","environments":[
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

step "a real keypaste agent and keypaste mcp: the hour is refused for a protected tag and honoured otherwise, under a prod path too"
pipe="keypaste-projects-$$-$(date +%s)"
audit="$dir/audit.jsonl"
agent_err="$dir/agent.err"
# One answer per request, in order: the hour for the dev entry, once then the hour for the prod entry
# (once first, because refusing the hour cools that field down for every bridge, D-0425), the hour for
# the entry KeePassXC tagged through the merge, and the hour for env/billing/prod/DB.
printf '%s\nh\no\nh\nh\nh\n' "$pw" | "$kp" agent --vault "$(native "$grouped")" --approver "$pipe" --approval-timeout 30 \
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
  } | "$kp" mcp --vault "$(native "$grouped")" --expose 'services/**' --expose 'env/**' --audit-log "$(native "$audit")" --approver "$pipe" \
        --client-label projects-probe >"$out" 2>"$dir/ask-$id.err" || die "keypaste mcp exited non-zero: $(cat "$dir/ask-$id.err")"
  tr -d '\r' <"$out"
}
granted() { jq -e --argjson id "$1" 'select(.id == $id) | .result.isError == false' >/dev/null; }

ask 2 services/Stripe | granted 2 || die "the dev entry answered with the hour was not released (the hour is not honoured, so the control fails)"
ask 3 services/Database | granted 3 || die "the prod-tagged entry answered once was not released"
ask 4 services/Database | granted 4 && die "the prod-tagged entry was released for the hour"
ask 5 services/Other | granted 5 && die "the entry KeePassXC tagged env:billing:Prod was released for the hour"
ask 6 env/billing/prod/DB | granted 6 || die "the untagged env/billing/prod/DB answered with the hour was not released: a path protected it"

[ "$(tr -d '\r' <"$agent_err" | grep -c 'it is asked about every time')" = 3 ] \
  || die "keypaste agent did not say three times that the entry is asked about every time: $(cat "$agent_err")"
jq -e -s 'map(select(.method == "prompt")) | length == 5
          and .[0].decision == "granted" and .[1].decision == "granted"
          and .[2].decision == "denied" and .[3].decision == "denied" and .[4].decision == "granted"' <"$audit" >/dev/null \
  || die "the audit log does not show granted, granted, denied, denied, granted: $(cat "$audit")"

step "C.1b: KeePassXC makes a vault whose billing project is tagged entries' fields, beside the untagged entries"
child=${BASH:-/bin/bash}
probe='printf "stripe=%s db=%s old=%s prod_db=%s region=%s shared=%s" "${STRIPE_SECRET_KEY-unset}" "${DATABASE_URL-unset}" "${OLD_KEY-unset}" "${DB-unset}" "${Region-unset}" "${SHARED_KEY-unset}"'
dev_set="stripe=stripe-c1b db=db-c1b old=unset prod_db=unset region=unset shared=unset"
shared_set="stripe=unset db=unset old=unset prod_db=unset region=unset shared=shared-c1b"
# $1: extra <Entry> elements for services; $2: the Database entry's DATABASE_URL; $3: its <Times>.
fields_seed() {
  cat <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-projects</Generator><DatabaseName>f</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid froot)</UUID><Name>Root</Name>
$(untagged)
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
make_fields fields '' 'db-c1b' ''
grep -qx 'env:billing' <<<"$(tags "$fields" services/Database)" || die "KeePassXC did not keep the Database entry's tag"
[ "$(kx show "$fields" services/Stripe -a STRIPE_SECRET_KEY)" = stripe-c1b ] || die "KeePassXC does not read the tagged field"
# The app's half runs on this copy, which no keypaste has written.
app_fields="$dir/fields-app.kdbx"
cp "$fields" "$app_fields"

step "C.6: keypaste run gives each environment's child exactly its tagged fields, never an untagged entry, which get still reads"
out=$(printf '%s\n' "$pw" | "$kp" run billing --vault "$fields" -- "$child" -c "$probe" 2>"$dir/run.err" | tr -d '\r') \
  || die "keypaste run failed: $(cat "$dir/run.err")"
[ "$out" = "$dev_set" ] || die "the child's environment was: ${out}"
for environment in staging prod; do
  out=$(printf '%s\n' "$pw" | "$kp" run billing -p "$environment" --vault "$fields" -- "$child" -c "$probe" 2>"$dir/run.err" | tr -d '\r') \
    || die "keypaste run -p ${environment} failed: $(cat "$dir/run.err")"
  [ "$out" = "$shared_set" ] || die "the ${environment} child's environment was: ${out}"
done
[ "$(kp_on "$fields" get env/billing/OLD_KEY --show 2>/dev/null)" = old-c1b ] || die "keypaste get does not read env/billing/OLD_KEY as an ordinary entry"

step "C.1b: env export writes a reference for each variable, and env diff compares the tagged environments"
exported=$(kp_on "$fields" env export billing --stdout 2>/dev/null) || die "keypaste env export failed"
for key in DATABASE_URL STRIPE_SECRET_KEY; do
  grep -qF "kp://billing/dev/${key}" <<<"$exported" || die "env export wrote no reference for ${key}: ${exported}"
done
grep -qF 'OLD_KEY' <<<"$exported" && die "env export wrote a reference for the untagged OLD_KEY: ${exported}"
grep -qF 'Region' <<<"$exported" && die "env export wrote a field that is not env-named"
grep -qF -- '-c1b' <<<"$exported" && die "env export wrote a value"
diffed=$(kp_on "$fields" env diff billing dev staging 2>/dev/null) || die "keypaste env diff failed"
grep -qE 'SHARED_KEY +missing in dev' <<<"$diffed" || die "env diff did not find SHARED_KEY missing in dev: ${diffed}"
grep -qE 'STRIPE_SECRET_KEY +missing in staging' <<<"$diffed" || die "env diff did not find STRIPE_SECRET_KEY missing in staging: ${diffed}"
grep -qF 'OLD_KEY' <<<"$diffed" && die "env diff compared the untagged OLD_KEY: ${diffed}"

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
refused two-entries 'DATABASE_URL is on more than one entry (services/Database, services/Twin)' "$twin" 'db-c1b' ''
refused expired 'DATABASE_URL expired 2020-01-02 03:04:05Z (services/Database)' '' 'db-c1b' \
  '<Times><Expires>True</Expires><ExpiryTime>2020-01-02T03:04:05Z</ExpiryTime></Times>'
refused placeholder 'DATABASE_URL holds the KeePass placeholder {PASSWORD}, which keypaste does not resolve (services/Database)' \
  '' 'postgres://u:{PASSWORD}@db-c1b' ''
custom="<Entry><UUID>$(uuid fcustom)</UUID><Tags>env:billing</Tags><String><Key>Title</Key><Value>Custom</Value></String><String><Key>Password</Key><Value ProtectInMemory=\"True\">custom-login-c1b</Value></String><String><Key>PASSWORD</Key><Value ProtectInMemory=\"True\">custom-field-c1b</Value></String></Entry>"
refused standard-name 'PASSWORD is a custom field named like a standard one, which keypaste never releases (services/Custom)' \
  "$custom" 'db-c1b' ''

step "C.5b: under the default exposure an agent lists the tagged entries with their variables and tags, gets a variable, never a password, and runs billing from services/"
exposed="$dir/exposed.kdbx"
make_fields exposed '' 'db-c1b' ''
expose_pipe="keypaste-exposed-$$-$(date +%s)"
expose_err="$dir/exposed-agent.err"
expose_audit="$dir/exposed-audit.jsonl"
expose_out="$dir/exposed.out"
trap 'cleanup; kill "${expose_pid:-}" 2>/dev/null || true' EXIT
# The hour for STRIPE_SECRET_KEY, then once for the run; the password is refused before anyone is asked.
printf '%s\nh\no\n' "$pw" | "$kp" agent --vault "$(native "$exposed")" --approver "$expose_pipe" --approval-timeout 30 \
  >/dev/null 2>"$expose_err" &
expose_pid=$!
for _ in $(seq 1 100); do
  grep -q 'listening on' "$expose_err" && break
  kill -0 "$expose_pid" 2>/dev/null || die "keypaste agent exited before it listened: $(cat "$expose_err")"
  sleep 0.2
done
grep -q 'listening on' "$expose_err" || die "keypaste agent never started listening on the exposed vault"

replied() { # waits for the answer to one id
  for _ in $(seq 1 300); do
    jq -se --argjson id "$1" 'any(.[]; .id == $id)' <"$expose_out" >/dev/null 2>&1 && return 0
    sleep 0.2
  done
  return 1
}
credential() {
  jq -cn --argjson id "$1" --arg field "$2" \
    '{jsonrpc:"2.0",id:$id,method:"tools/call",params:{name:"request_credential",arguments:{entry:"services/Stripe",field:$field,reason:"C.5b gate",ttl_seconds:60}}}'
}
: >"$expose_out"
{
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"exposed-probe","version":"1.0.0"}}}'
  replied 1 || true
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_entry_names","arguments":{}}}'
  replied 2 || true
  credential 3 STRIPE_SECRET_KEY
  replied 3 || true
  credential 4 password
  replied 4 || true
  jq -cn --arg dir "$(native "$(cd "$dir" && pwd -P)")" \
    '{jsonrpc:"2.0",id:5,method:"tools/call",params:{name:"run",arguments:{command:["sh","-c","printf %s \"${STRIPE_SECRET_KEY:+stripe}${DATABASE_URL:+ db}${SHARED_KEY:+ shared}\""],directory:$dir,project:"billing",reason:"C.5b gate"}}}'
  replied 5 || true
} | "$kp" mcp --vault "$(native "$exposed")" --audit-log "$(native "$expose_audit")" --approver "$expose_pipe" \
      --client-label exposed-probe --allow-run >"$expose_out" 2>"$dir/exposed-mcp.err" \
  || die "keypaste mcp exited non-zero: $(cat "$dir/exposed-mcp.err")"
tr -d '\r' <"$expose_out" >"$expose_out.lf"

listing=$(jq -sc 'map(select(.id == 2))[0].result.structuredContent.entries' <"$expose_out.lf")
[ "$(jq -c 'map(.name) | sort' <<<"$listing")" = '["Database","Shared","Stripe"]' ] \
  || die "the default did not list exactly the tagged entries: ${listing}"
jq -e 'any(.[]; .name == "Stripe" and .group == "services" and .fields == ["STRIPE_SECRET_KEY"] and .tags == ["env:billing"])' <<<"$listing" >/dev/null \
  || die "Stripe was not listed with STRIPE_SECRET_KEY alone and env:billing: ${listing}"
grep -qF -- '-c1b' <<<"$listing" && die "the listing carried a value: ${listing}"
jq -se 'any(.[]; .id == 3 and .result.isError == false)' <"$expose_out.lf" >/dev/null \
  || die "the tagged entry's variable was not released: $(cat "$expose_out.lf")"
grep -qF 'stripe-c1b' "$expose_out.lf" || die "the released variable is not stripe-c1b"
jq -se 'any(.[]; .id == 4 and .result.isError == true)' <"$expose_out.lf" >/dev/null || die "the tagged entry's password was not refused"
grep -qF 'stripe-login-c1b' "$expose_out.lf" && die "the tagged entry's password was released"
jq -se 'any(.[]; .id == 5 and .result.isError == false and .result.structuredContent.stdout == "stripe db")' <"$expose_out.lf" >/dev/null \
  || die "billing's dev run did not get exactly its tagged fields: $(jq -sc 'map(select(.id == 5))' <"$expose_out.lf")"
jq -e -s '[.[] | select(.tool == "request_credential")] | .[0].decision == "granted" and .[1].method == "out-of-scope"' <"$expose_audit" >/dev/null \
  || die "the audit log does not show the variable granted and the password out of scope: $(cat "$expose_audit")"
jq -e -s 'any(.[]; .tool == "run" and .decision == "granted" and (.entries | sort) == ["services/Database", "services/Stripe"])' <"$expose_audit" >/dev/null \
  || die "the run's audit line does not name the two services entries: $(cat "$expose_audit")"
[ "$(tr -d '\r' <"$expose_err" | grep -c 'an agent is asking for a credential')" = 1 ] \
  || die "keypaste agent was not asked exactly once for a credential: $(cat "$expose_err")"
[ "$(tr -d '\r' <"$expose_err" | grep -c 'an agent wants to run a command')" = 1 ] \
  || die "keypaste agent was not asked exactly once to run: $(cat "$expose_err")"
kill "$expose_pid" 2>/dev/null || true
wait "$expose_pid" 2>/dev/null || true
trap cleanup EXIT

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
[ "$out" = "$dev_set" ] || die "the session child's environment was: ${out}"
from=$(grep -m1 '^  from ' <<<"$(tr -d '\r' <"$fields_err")" || true)
for entry in services/Database services/Stripe; do
  grep -qF "$entry" <<<"$from" || die "the agent's prompt does not name ${entry}: ${from}"
done
grep -qF 'env/billing' <<<"$from" && die "the agent's prompt names an untagged entry: ${from}"

out=$("$kp" run --session billing -p staging --vault "$(native "$fields")" --approver "$fields_pipe" -- "$child" -c "$probe" 2>"$dir/session-staging.err" | tr -d '\r') \
  || die "run --session -p staging failed: $(cat "$dir/session-staging.err") / $(cat "$fields_err")"
[ "$out" = "$shared_set" ] || die "the staging session child's environment was: ${out}"
grep -qF 'services/Shared' <<<"$(tr -d '\r' <"$fields_err")" || die "the agent's prompt does not name services/Shared"
[ "$(tr -d '\r' <"$fields_err" | grep -c 'it is asked about every time')" = 1 ] \
  || die "the staging set holding a prod-tagged member was not asked once only: $(cat "$fields_err")"
grep -qF -- '-c1b' "$fields_err" && die "a value reached the agent's terminal"

step "C.1b: the token's dev run is audited with its source entries, and its staging set, holding a prod-tagged member, is refused"
out=$(KEYPASTE_TOKEN="$token" "$kp" run --token env --vault "$(native "$fields")" --approver "$fields_pipe" billing -- "$child" -c "$probe" 2>"$dir/token-dev.err" | tr -d '\r') \
  || die "run --token failed: $(cat "$dir/token-dev.err") / $(cat "$fields_err")"
[ "$out" = "$dev_set" ] || die "the token child's environment was: ${out}"
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
          and .[0].decision == "granted" and .[0].entries == ["services/Database", "services/Stripe"]
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
app_run dev 'keys=DATABASE_URL,STRIPE_SECRET_KEY entries=services/Database,services/Stripe timed=True' \
  approve "$dev_set" billing
app_run staging 'keys=SHARED_KEY entries=services/Shared timed=False' \
  once "$shared_set" billing -p staging
grep -qF -- '-c1b' "$hold_out" && die "a value reached the app's output"
exec 7>&-
wait_for '^shut down' "$hold_out" 1

step "C.1c: KeePassXC makes a vault for the writers, and an untouched copy for the app"
c1c="$dir/c1c.kdbx"
make_fields c1c '' 'db-c1b' ''
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
kx show "$c1c" env/billing/STRIPE_SECRET_KEY >/dev/null 2>&1 && die "env set made an entry named for a key a tagged entry holds"

step "C.1c: env set of a new key creates env/billing/.env, tagged env:billing, holding it protected"
said=$(kp_on "$c1c" env set billing NEW_KEY=new-c1c 2>&1) || die "env set of a new key failed: ${said}"
grep -qF 'Set NEW_KEY on env/billing/.env, created and tagged env:billing' <<<"$said" || die "env set did not say it created the home entry: ${said}"
[ "$(kx show "$c1c" env/billing/.env -a NEW_KEY)" = new-c1c ] || die "KeePassXC does not read NEW_KEY on env/billing/.env"
[ "$(protection_of "$c1c" NEW_KEY)" = protected ] || die "NEW_KEY is not protected"
[ "$(tags "$c1c" env/billing/.env | paste -sd' ' -)" = "env:billing" ] || die "KeePassXC reads the home entry's tags as $(tags "$c1c" env/billing/.env | paste -sd' ' -)"
[ "$(revisions_at "$c1c" env/billing/.env)" = 0 ] || die "creating the home entry left a revision"
kx show "$c1c" env/billing/NEW_KEY >/dev/null 2>&1 && die "env set made an entry named for a new key"
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

step "C.4: KeePassXC makes a vault with web, mapped in projects.json, and billing, whose dev and staging share an entry"
c4_seed() {
  cat <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-projects</Generator><DatabaseName>c4</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid c4root)</UUID><Name>Root</Name>
      <Group>
        <UUID>$(uuid c4apps)</UUID><Name>apps</Name>
        <Entry>
          <UUID>$(uuid c4web)</UUID>
          <Tags>env:web</Tags>
          <String><Key>Title</Key><Value>Web</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">web-login-c4</Value></String>
          <String><Key>WEB_KEY</Key><Value ProtectInMemory="True">web-c4</Value></String>
        </Entry>
      </Group>
      <Group>
        <UUID>$(uuid c4services)</UUID><Name>services</Name>
        <Entry>
          <UUID>$(uuid c4stripe)</UUID>
          <Tags>env:billing,env:billing:staging</Tags>
          <String><Key>Title</Key><Value>Stripe</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">stripe-login-c4</Value></String>
          <String><Key>STRIPE_KEY</Key><Value ProtectInMemory="True">stripe-c4</Value></String>
        </Entry>
        <Entry>
          <UUID>$(uuid c4mail)</UUID>
          <Tags>env:billing</Tags>
          <String><Key>Title</Key><Value>Mail</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">mail-login-c4</Value></String>
          <String><Key>MAIL_KEY</Key><Value ProtectInMemory="True">mail-c4</Value></String>
        </Entry>
        <Entry>
          <UUID>$(uuid c4odd)</UUID>
          <Tags>env:billing:Prod</Tags>
          <String><Key>Title</Key><Value>Odd</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">odd-login-c4</Value></String>
          <String><Key>ODD_KEY</Key><Value ProtectInMemory="True">odd-c4</Value></String>
        </Entry>
        <Entry>
          <UUID>$(uuid c4queue)</UUID>
          <String><Key>Title</Key><Value>Queue</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">queue-login-c4</Value></String>
          <String><Key>QUEUE_KEY</Key><Value ProtectInMemory="True">queue-c4</Value></String>
        </Entry>
      </Group>
    </Group>
  </Root>
</KeePassFile>
EOF
}
c4_seed >"$dir/c4.xml"
c4="$(cd "$dir" && pwd)/c4.kdbx"
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/c4.xml")" "$(native "$c4")" || die "keepassxc-cli could not import the C.4 seed"
web_dir="$(cd "$dir" && pwd)/web"
mkdir -p "$web_dir" "$dir/c4-ref"
jq -n --arg vault "$(native "$c4")" --arg directory "$(native "$web_dir")" \
  '{projects: [{vault: $vault, project: "web", directory: $directory, command: "npm start"}]}' >"$KEYPASTE_HOME/projects.json"
c4_probe='printf "stripe=%s mail=%s queue=%s new=%s imported=%s web=%s" "${STRIPE_KEY-unset}" "${MAIL_KEY-unset}" "${QUEUE_KEY-unset}" "${NEW_KEY-unset}" "${IMPORTED_KEY-unset}" "${WEB_KEY-unset}"'
c4_run() { printf '%s\n' "$pw" | "$kp" run "$@" --vault "$(native "$c4")" -- "$child" -c "$c4_probe" 2>"$dir/c4-run.err" | tr -d '\r'; }

step "C.4: the screen lists both projects, where each value lives, the entry dev and staging share, and the tag that puts its entry in no project"
value=
said=$(drive projects "$(native "$c4")") || die "the screen's listing failed: ${said}"
for line in \
  "project web runs npm start in $(native "$web_dir")" \
  "project billing" \
  "entry billing/dev: services/Mail holds MAIL_KEY" \
  "entry billing/dev: services/Stripe holds STRIPE_KEY, also staging" \
  "entry billing/staging: services/Stripe holds STRIPE_KEY, also dev" \
  "entry web/dev: apps/Web holds WEB_KEY"; do
  grep -qxF -- "$line" <<<"$said" || die "the screen does not list '${line}': ${said}"
done
grep -F 'value billing/dev STRIPE_KEY: services/Stripe' <<<"$said" | grep -qF 'also staging' || die "the dev value does not name its entry and staging: ${said}"
grep -F 'value billing/staging STRIPE_KEY: services/Stripe' <<<"$said" | grep -qF 'also dev' || die "the staging value does not name its entry and dev: ${said}"
grep -qF 'ignored: services/Odd has the tag env:billing:Prod, which puts it in no project' <<<"$said" || die "the screen does not name the ignored tag: ${said}"
grep -qE '^entry [a-z]+/[a-z]+: services/(Odd|Queue) ' <<<"$said" && die "the screen lists an entry no well-formed tag puts in a project: ${said}"

step "C.4: adding Queue to staging names what it reaches; declined it writes nothing, and KeePassXC reads the confirmed tag"
was=$(bytes "$c4")
said=$(drive env-entry-add "$(native "$c4")" billing staging services/Queue --decline) || die "the declined add failed: ${said}"
grep -qxF 'asked: env:billing:staging puts services/Queue in billing/staging.' <<<"$said" || die "the add did not name the environment: ${said}"
grep -qxF 'asked: Joining billing/staging: QUEUE_KEY.' <<<"$said" || die "the add did not name the field that joins: ${said}"
grep -qx 'declined' <<<"$said" || die "the add was not declined: ${said}"
[ "$was" = "$(bytes "$c4")" ] || die "a declined add changed the vault"
said=$(drive env-entry-add "$(native "$c4")" billing staging services/Queue) || die "the add failed: ${said}"
[ "$(tags "$c4" services/Queue | paste -sd' ' -)" = "env:billing:staging" ] || die "KeePassXC reads Queue's tags as $(tags "$c4" services/Queue | paste -sd' ' -)"

step "C.4: taking Mail out of dev keeps the entry and its protected field; declined it writes nothing"
was=$(bytes "$c4")
said=$(drive env-entry-rm "$(native "$c4")" billing dev services/Mail --decline) || die "the declined removal failed: ${said}"
grep -qxF 'asked: Leaving billing/dev: MAIL_KEY.' <<<"$said" || die "the removal did not name the field that leaves: ${said}"
[ "$was" = "$(bytes "$c4")" ] || die "a declined removal changed the vault"
said=$(drive env-entry-rm "$(native "$c4")" billing dev services/Mail) || die "the removal failed: ${said}"
[ -z "$(tags "$c4" services/Mail)" ] || die "KeePassXC still reads a tag on Mail: $(tags "$c4" services/Mail | paste -sd' ' -)"
[ "$(kx show "$c4" services/Mail -a MAIL_KEY)" = mail-c4 ] || die "KeePassXC no longer reads Mail's field"
[ "$(protection_of "$c4" MAIL_KEY)" = protected ] || die "Mail's field lost its protection"

step "C.4: replacing the shared value names dev and staging; declined it writes nothing, and KeePassXC reads the new value"
was=$(bytes "$c4")
said=$(drive env-set "$(native "$c4")" billing dev STRIPE_KEY --decline) || die "the declined replace failed: ${said}"
grep -qxF "asked: New value for STRIPE_KEY on services/Stripe, which billing/dev and billing/staging read. The old one stays in the entry's history." <<<"$said" \
  || die "the replace did not name both environments: ${said}"
[ "$was" = "$(bytes "$c4")" ] || die "a declined replace changed the vault"
value=stripe-c4-new
said=$(drive env-set "$(native "$c4")" billing dev STRIPE_KEY) || die "the replace failed: ${said}"
[ "$(kx show "$c4" services/Stripe -a STRIPE_KEY)" = stripe-c4-new ] || die "KeePassXC does not read the replaced value"
[ "$(protection_of "$c4" STRIPE_KEY)" = protected ] || die "the replaced value is not protected"

step "C.4: a key added with no entry chosen lands protected on env/billing/.env, tagged env:billing; a name no field can have writes nothing"
value=new-c4
said=$(drive env-add "$(native "$c4")" billing dev NEW_KEY) || die "the add with no entry chosen failed: ${said}"
[ "$(kx show "$c4" env/billing/.env -a NEW_KEY)" = new-c4 ] || die "KeePassXC does not read NEW_KEY on env/billing/.env"
[ "$(protection_of "$c4" NEW_KEY)" = protected ] || die "NEW_KEY is not protected"
[ "$(tags "$c4" env/billing/.env | paste -sd' ' -)" = "env:billing" ] || die "the home entry is not tagged env:billing"
was=$(bytes "$c4")
set +e
said=$(drive env-add "$(native "$c4")" billing dev api_key)
rc=$?
set -e
[ "$rc" -ne 0 ] || die "the screen added the key api_key, which no field of a project can be named: ${said}"
[ "$was" = "$(bytes "$c4")" ] || die "the refused key changed the vault"

step "C.4: an imported .env is read back by KeePassXC"
value=
printf 'IMPORTED_KEY=imported-c4\n' >"$dir/c4.env"
said=$(drive env-import "$(native "$c4")" billing dev "$(native "$dir/c4.env")") || die "the import failed: ${said}"
[ "$(kx show "$c4" env/billing/.env -a IMPORTED_KEY)" = imported-c4 ] || die "KeePassXC does not read the imported key"
[ "$(protection_of "$c4" IMPORTED_KEY)" = protected ] || die "the imported key is not protected"

step "C.4: keypaste run resolves the exported references to exactly dev's fields; staging and web run their own"
said=$(drive env-export "$(native "$c4")" billing dev "$(native "$dir/c4-ref/.env.keypaste")") || die "the export failed: ${said}"
grep -qF 'Wrote 3 references' <<<"$said" || die "the export did not write dev's three references: ${said}"
grep -qF -- '-c4' "$dir/c4-ref/.env.keypaste" && die "the exported references hold a value"
out=$(c4_run --env-file "$(native "$dir/c4-ref/.env.keypaste")") || die "run --env-file failed: $(cat "$dir/c4-run.err")"
[ "$out" = "stripe=stripe-c4-new mail=unset queue=unset new=new-c4 imported=imported-c4 web=unset" ] || die "the exported references gave the child: ${out}"
out=$(c4_run -p staging billing) || die "the staging run failed: $(cat "$dir/c4-run.err")"
[ "$out" = "stripe=stripe-c4-new mail=unset queue=queue-c4 new=unset imported=unset web=unset" ] || die "the staging run gave the child: ${out}"
out=$(c4_run web) || die "web's run failed: $(cat "$dir/c4-run.err")"
[ "$out" = "stripe=unset mail=unset queue=unset new=unset imported=unset web=web-c4" ] || die "web's run gave the child: ${out}"

step "V.11: KeePassXC drops env:billing from Stripe as its password changes, and env:billing:prod from Database alone"
v11="$dir/v11.kdbx"
seed '' >"$dir/v11.xml"
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/v11.xml")" "$(native "$v11")" \
  || die "keepassxc-cli could not import the V.11 seed"
stripe_was=$(revisions_at "$v11" services/Stripe)
database_was=$(revisions_at "$v11" services/Database)
kx export "$v11" -f xml | awk -v stripe="$(uuid stripe)" -v database="$(uuid database)" '
  /<History>/ { inside = 1 }
  !inside && /<UUID>/ { mine = index($0, "<UUID>" stripe "</UUID>") ? "stripe" : index($0, "<UUID>" database "</UUID>") ? "database" : mine }
  mine == "stripe" && !inside { sub(/<Tags>[^<]*<\/Tags>/, "<Tags>finance</Tags>"); sub(/>stripe-login</, ">stripe-login-v11<") }
  mine == "database" && !inside { sub(/<Tags>[^<]*<\/Tags>/, "<Tags></Tags>") }
  mine != "" && !inside { sub(/<LastModificationTime>[^<]*</, "<LastModificationTime>2037-01-01T00:00:00Z<") }
  /<\/History>/ { inside = 0 }
  mine != "" && !inside && /<\/Entry>/ { mine = "" }
  { print }' >"$dir/v11-newer.xml"
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/v11-newer.xml")" "$(native "$dir/v11-newer.kdbx")" \
  || die "keepassxc-cli could not import the newer V.11 copy"
printf '%s\n' "$pw" | "$cli" merge -q -s "$(native "$v11")" "$(native "$dir/v11-newer.kdbx")" >/dev/null \
  || die "keepassxc-cli could not merge the newer V.11 copy"
[ "$(tags "$v11" services/Stripe | paste -sd' ' -)" = finance ] || die "the merge left Stripe with the tags $(tags "$v11" services/Stripe | paste -sd' ' -)"
[ -z "$(tags "$v11" services/Database)" ] || die "the merge left Database with the tags $(tags "$v11" services/Database | paste -sd' ' -)"
[ "$(revisions_at "$v11" services/Stripe)" = $((stripe_was + 1)) ] || die "KeePassXC's merge did not keep Stripe's earlier version"
[ "$(revisions_at "$v11" services/Database)" = $((database_was + 1)) ] || die "KeePassXC's merge did not keep Database's earlier version"

step "V.11: Recommendations lists the tag dropped with the password change and not the one removed alone, and no value"
said=$(drive tags-review "$(native "$v11")") || die "the review failed: ${said}"
grep -qF 'stripe-login' <<<"$said" && die "the review printed a value: ${said}"
[ "$(grep -c '^lost ' <<<"$said")" = 1 ] || die "the review did not list exactly one dropped tag: ${said}"
grep -qx 'lost entry=services/Stripe tag=env:billing state=needs-review' <<<"$said" || die "the review did not list Stripe's env:billing: ${said}"
grep -qx 'count 1' <<<"$said" || die "the review did not count one: ${said}"

step "V.11: a dismissal is kept across processes, and Review again lists the tag again"
said=$(drive tags-dismiss "$(native "$v11")" services/Stripe env:billing) || die "the dismissal failed: ${said}"
said=$(drive tags-review "$(native "$v11")") || die "the review failed: ${said}"
grep -qx 'lost entry=services/Stripe tag=env:billing state=dismissed' <<<"$said" || die "the dismissal was not kept: ${said}"
grep -qx 'count 0' <<<"$said" || die "a dismissed tag is still counted: ${said}"
said=$(drive tags-again "$(native "$v11")" services/Stripe env:billing) || die "Review again failed: ${said}"

step "V.11: Restore tag names what env:billing reaches, and KeePassXC reads it back with one more revision"
stripe_was=$(revisions_at "$v11" services/Stripe)
said=$(drive tag-restore "$(native "$v11")" services/Stripe env:billing) || die "the restore failed: ${said}"
grep -qF 'asked env:billing puts services/Stripe in billing/dev.' <<<"$said" || die "the restore did not name the environment first: ${said}"
grep -qF 'asked Joining billing/dev: STRIPE_SECRET_KEY.' <<<"$said" || die "the restore did not name the field that joins first: ${said}"
[ "$(tags "$v11" services/Stripe | paste -sd' ' -)" = "env:billing finance" ] || die "KeePassXC reads Stripe's tags as $(tags "$v11" services/Stripe | paste -sd' ' -)"
[ "$(revisions_at "$v11" services/Stripe)" = $((stripe_was + 1)) ] || die "the restore did not add exactly one revision"
[ "$(kx show "$v11" services/Stripe -a Password)" = stripe-login-v11 ] || die "the restore changed Stripe's password"
said=$(drive tags-review "$(native "$v11")") || die "the review failed: ${said}"
grep -qx 'count 0' <<<"$said" || die "a restored tag is still listed: ${said}"

step "NEGATIVE CONTROL: the listing and run comparisons must be able to fail"
[ "$listed" = "${expected}-CORRUPTED" ] && die "a deliberately corrupted expectation still matched — this gate is not gating"
out=$(printf '%s\n' "$pw" | OLD_KEY=old-c1b "$kp" run billing --vault "$fields" -- "$child" -c "$probe" 2>"$dir/run.err" | tr -d '\r') \
  || die "keypaste run with OLD_KEY inherited failed: $(cat "$dir/run.err")"
[ "$out" = "$dev_set" ] && die "a child holding OLD_KEY still matched the dev set — the run comparisons cannot see OLD_KEY"

printf '\nPROJECTS GATE PASSED: KeePassXC and keypaste agree on project tags, a protected tag makes a real agent ask every time, an untagged entry under env/ is neither a variable nor protected, and a tag KeePassXC dropped with another change is flagged and restored.\n'
