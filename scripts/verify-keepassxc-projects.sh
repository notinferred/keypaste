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
# Then a real `keypaste approve` holds the 4.1 vault and a real `keypaste mcp`, exposed to the entries,
# asks for passwords. An untagged-for-prod entry answered with the hour gets it, and so does
# env/billing/prod/DB. The entry tagged env:billing:prod, and one KeePassXC tagged env:billing:Prod
# through `keepassxc-cli merge`, are offered Allow once only: the hour is refused and once is honoured.
#
# A third vault KeePassXC makes holds fields on entries tagged env:billing, a staging entry also tagged
# env:billing:prod, and the same untagged entries. `keypaste run` gives a child exactly the tagged
# fields of the environment it names, never OLD_KEY or DB, `keypaste get` still reads OLD_KEY as an
# ordinary entry, `env export` writes a reference for each tagged key and `env diff` compares the
# environments' key names. A key on two entries, an expired member, {REF:…} in a value and a
# custom PASSWORD field each start nothing and name their entries, and a value holding {PASSWORD}
# reaches the child filled in from its own entry, exactly as KeePassXC shows it (P.3b1). Values using
# KeePassXC's \{…\} escape, whole, through {S:…}, inside braces and in part, reach the child as
# KeePassXC 2.7.11 gives them, compared with keepassxc-cli itself from 2.7.11 on (D-0519). A {S:…}
# whose name holds braces is refused, with a field so named or without, where keepassxc-cli from
# 2.7.11 on shows that one field's value or nothing, and a {S: that KeePassXC's pattern pairs with
# no brace reaches the child as text, as keepassxc-cli shows it (D-0531).
# `run --session` through a real `keypaste approve`, and through the app's own prompt window held by
# tests/Keypaste.AppDriver on an untouched copy, gets the same set after a prompt naming the source
# entries, and the staging set is asked once only. A scoped token's run of the dev set is audited
# with its source entries, and its staging set is refused because a member is also tagged
# env:billing:prod.
#
# A copy of the third vault, held by its own `keypaste approve`, is asked through a `keypaste mcp` on the
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
# A seventh vault KeePassXC makes from the first document holds agents' settings as ACAF records (G.8),
# which keypaste writes through `keypaste approve` and confirmed `keypaste clients` changes: one entry
# per agent in .keypaste/agents, each entry and both groups under the UUID their names derive (AcafIds),
# the vault staying KDBX 4.0. KeePassXC edits an entry, adds one and saves, keeping every record and tag
# byte for byte, and keypaste applies what it reads. Two copies then change apart: keypaste changes one
# agent in each and the same agent in both, creates the same agent in both, the second later each time,
# and tags an entry in the first; KeePassXC edits an entry in the second. `keepassxc-cli merge` either
# way leaves every change and tag and one entry per agent, the newer record of each agent changed or
# created in both whole with the older in its history, and a real `keypaste approve` applies the newer.
# keypaste then narrows both agents in one merged copy; merged either way again, each narrowing holds,
# with the record before it in the history. Two copies of the vault before keypaste wrote anything each
# create the same agent, and merged either way they hold one .keypaste group, one agents group and one
# entry, the newer record whole and the older in its history.
#
# NEGATIVE CONTROL: a corrupted expectation must fail the comparison the listing check rests on, a
# child holding OLD_KEY must fail the one the runs rest on, and the hour must be honoured for an entry
# no tag protects. The two copies' records of each agent changed or created in both must differ, so the
# merge can show which one won.
#
# Usage:  scripts/verify-keepassxc-projects.sh <work-directory>
# Env:    KP_COMPAT_PASSWORD   master password for the vaults        (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_APP_DRIVER  path to tests/Keypaste.AppDriver      (default: the Release build)
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='PROJECTS GATE FAILED: '
watchdog "${WATCHDOG_SECONDS:-600}" keypaste Keypaste.AppDriver keepassxc-cli

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

step "a real keypaste approve and keypaste mcp: the hour is refused for a protected tag and honoured otherwise, under a prod path too"
pipe="keypaste-projects-$$-$(date +%s)"
# The home's log, which every owner and bridge here writes; each phase reads the lines it gained.
audit="$KEYPASTE_HOME/audit.jsonl"
logged=$(lines_in "$audit")
agent_err="$dir/agent.err"
# projects-probe sees services/** and env/** by path, as a person confirmed at keypaste approve's
# terminal; with no settings it would see only project variables, never a password.
agent_settings "$kp" "$(native "$grouped")" "$pw" 'projects-probe --see services/** --see env/**' \
  || die "could not save projects-probe's settings in the KeePassXC vault"
# One answer per request, in order: the hour for the dev entry, once then the hour for the prod entry
# (once first, because refusing the hour cools that field down for every bridge, D-0425), the hour for
# the entry KeePassXC tagged through the merge, and the hour for env/billing/prod/DB.
printf '%s\nh\no\nh\nh\nh\n' "$pw" | "$kp" approve --vault "$(native "$grouped")" --approver "$pipe" --approval-timeout 30 \
  >/dev/null 2>"$agent_err" &
agent_pid=$!
for _ in $(seq 1 100); do
  grep -q 'listening on' "$agent_err" && break
  kill -0 "$agent_pid" 2>/dev/null || die "keypaste approve exited before it listened: $(cat "$agent_err")"
  sleep 0.2
done
grep -q 'listening on' "$agent_err" || die "keypaste approve never started listening"

ask() {
  local id=$1 entry=$2 out="$dir/ask-$1.out"
  : >"$out"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"projects-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$entry\",\"field\":\"password\",\"reason\":\"projects gate\",\"ttl_seconds\":60}}}"
    await_replies "${WAIT_SECONDS:-30}" "$out" "$id" || true
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
  || die "keypaste approve did not say three times that the entry is asked about every time: $(cat "$agent_err")"
lines_after "$audit" "$logged" | jq -e -s 'map(select(.method == "prompt")) | length == 5
                                           and .[0].decision == "granted" and .[1].decision == "granted"
                                           and .[2].decision == "denied" and .[3].decision == "denied" and .[4].decision == "granted"' >/dev/null \
  || die "the audit log does not show granted, granted, denied, denied, granted: $(lines_after "$audit" "$logged")"

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
[ "$(kp_on "$fields" get env/billing/OLD_KEY --reveal 2>/dev/null)" = old-c1b ] || die "keypaste get does not read env/billing/OLD_KEY as an ordinary entry"

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
refused two-entries 'DATABASE_URL is on more than one item (services/Database, services/Twin)' "$twin" 'db-c1b' ''
refused expired 'DATABASE_URL expired 2020-01-02 03:04:05Z (services/Database)' '' 'db-c1b' \
  '<Times><Expires>True</Expires><ExpiryTime>2020-01-02T03:04:05Z</ExpiryTime></Times>'
refused placeholder 'DATABASE_URL holds the KeePass placeholder {REF:…}, which keypaste does not fill in (services/Database)' \
  '' 'postgres://u:{REF:P@T:Stripe}@db-c1b' ''
custom="<Entry><UUID>$(uuid fcustom)</UUID><Tags>env:billing</Tags><String><Key>Title</Key><Value>Custom</Value></String><String><Key>Password</Key><Value ProtectInMemory=\"True\">custom-login-c1b</Value></String><String><Key>PASSWORD</Key><Value ProtectInMemory=\"True\">custom-field-c1b</Value></String></Entry>"
refused standard-name 'PASSWORD is a custom field named like a standard one, which keypaste never releases (services/Custom)' \
  "$custom" 'db-c1b' ''

step "P.3b1: a value holding {PASSWORD} reaches the child filled in from its own entry, as KeePassXC shows it"
make_fields filled '' 'postgres://u:{PASSWORD}@db-c1b' ''
shown=$(kx show "$dir/filled.kdbx" services/Database -a DATABASE_URL) || die "keepassxc-cli could not show the filled DATABASE_URL"
[ "$shown" = 'postgres://u:database-login-c1b@db-c1b' ] || die "KeePassXC shows DATABASE_URL as: ${shown}"
out=$(printf '%s\n' "$pw" | "$kp" run billing --vault "$dir/filled.kdbx" -- "$child" -c 'printf %s "${DATABASE_URL-unset}"' 2>"$dir/filled.err" | tr -d '\r') \
  || die "keypaste run failed on a value holding {PASSWORD}: $(cat "$dir/filled.err")"
[ "$out" = "$shown" ] || die "the child got DATABASE_URL as ${out}, and KeePassXC shows ${shown}"
grep -qF -- '-c1b' "$dir/filled.err" && die "keypaste run printed a value: $(cat "$dir/filled.err")"

step "P.3b1: values using KeePassXC's \\{…\\} escape reach the child as KeePassXC 2.7.11 gives them, nothing in an escape filled in"
escaped_keys=(ESCAPED_WHOLE ESCAPED_FIELD ESCAPED_PART)
escape="<Entry><UUID>$(uuid fescape)</UUID><Tags>env:billing</Tags><String><Key>Title</Key><Value>Escape</Value></String>"
escape+='<String><Key>Password</Key><Value ProtectInMemory="True">escape-login-c1b</Value></String>'
escape+='<String><Key>ESCAPED_WHOLE</Key><Value ProtectInMemory="True">\{PASSWORD\}</Value></String>'
escape+='<String><Key>ESCAPED_FIELD</Key><Value ProtectInMemory="True">x{S:ESCAPED_WHOLE}y</Value></String>'
escape+='<String><Key>ESCAPED_PART</Key><Value ProtectInMemory="True">a\{PASSWORD\}</Value></String></Entry>'
make_fields escaped "$escape" 'db-c1b' ''
# What Entry::resolveMultiplePlaceholdersRecursive gives each key from KeePassXC 2.7.11 on, one per line.
escaped_expected=$'{PASSWORD}\nx{PASSWORD}y\na\\{PASSWORD\\}'
out=$(printf '%s\n' "$pw" | "$kp" run billing --vault "$dir/escaped.kdbx" -- "$child" -c 'printf "%s\n" "${ESCAPED_WHOLE-unset}" "${ESCAPED_FIELD-unset}" "${ESCAPED_PART-unset}"' 2>"$dir/escaped.err" | tr -d '\r') \
  || die "keypaste run failed on values using the escape: $(cat "$dir/escaped.err")"
[ "$out" = "$escaped_expected" ] || die "the child got the escaped values as: ${out}"
grep -qF -- '-c1b' "$dir/escaped.err" && die "keypaste run printed a value: $(cat "$dir/escaped.err")"
shown=$(for key in "${escaped_keys[@]}"; do kx show "$dir/escaped.kdbx" services/Escape -a "$key" || exit 1; done) \
  || die "keepassxc-cli could not show the escaped values"
kpxc_version=$("$cli" --version | tr -d '\r')
[[ $kpxc_version =~ ([0-9]+)\.([0-9]+)\.([0-9]+) ]] || die "keepassxc-cli --version printed no version: ${kpxc_version}"
kpxc_number=$((BASH_REMATCH[1] * 10000 + BASH_REMATCH[2] * 100 + BASH_REMATCH[3]))
if ((kpxc_number >= 20711)); then
  [ "$shown" = "$escaped_expected" ] || die "keepassxc-cli ${kpxc_version} shows the escaped values as: ${shown}"
  printf '    keepassxc-cli %s and keypaste give each escaped value alike\n' "$kpxc_version"
else
  [ "$(head -n 1 <<<"$shown")" = '{PASSWORD}' ] \
    && die "keepassxc-cli ${kpxc_version} reads the \\{…\\} escape, which KeePassXC does only from 2.7.11"
  printf '    keepassxc-cli %s predates the %s escape (2.7.11), so keypaste is held to what 2.7.11 gives\n' "$kpxc_version" '\{…\}'
fi

step "P.3b1: an escape or placeholder inside braces that name no placeholder is refused, which KeePassXC 2.7.11 and 2.8 give differently"
differs='which KeePassXC 2.7 and 2.8 fill in differently inside braces that name no placeholder (services/Database)'
refused nested-escape "DATABASE_URL holds the KeePass placeholder \\{…\\}, ${differs}" '' '{\{PASSWORD\}}' ''
refused nested-placeholder "DATABASE_URL holds the KeePass placeholder {PASSWORD}, ${differs}" '' 'x{{PASSWORD}}y' ''
shown=$(kx show "$dir/nested-escape.kdbx" services/Database -a DATABASE_URL) || die "keepassxc-cli could not show the nested escape"
# 2.7.11 fills in the escape inside, and 2.8 classifies the inside again and leaves the braces as written.
if ((kpxc_number >= 20800)); then
  [ "$shown" = '{\{PASSWORD\}}' ] || die "keepassxc-cli ${kpxc_version} shows {\\{PASSWORD\\}} as ${shown}, not as written"
elif ((kpxc_number >= 20711)); then
  [ "$shown" = '{{PASSWORD}}' ] || die "keepassxc-cli ${kpxc_version} shows {\\{PASSWORD\\}} as ${shown}, not with its escape made braces"
fi
printf '    keepassxc-cli %s shows %s as %s\n' "$kpxc_version" '{\{PASSWORD\}}' "$shown"

step "F.69: a {S:…} whose name holds braces names one field, as KeePassXC reads it, and is refused, since keypaste releases no field so named"
braced_field='<String><Key>a{PASSWORD}b</Key><Value ProtectInMemory="True">braced-c1b</Value></String>'
refused braced-name 'DATABASE_URL holds the KeePass placeholder {S:a PASSWORD b}, which names a field keypaste never releases (services/Database)' \
  '' '{S:a{PASSWORD}b}' "$braced_field"
refused braced-missing 'DATABASE_URL holds the KeePass placeholder {S:…}, which names no field of its item (services/Database)' \
  '' '{S:a{PASSWORD}b}' ''
braced=$(kx show "$dir/braced-name.kdbx" services/Database -a DATABASE_URL) || die "keepassxc-cli could not show the braced name"
missing=$(kx show "$dir/braced-missing.kdbx" services/Database -a DATABASE_URL) || die "keepassxc-cli could not show the braced name with no field"
if ((kpxc_number >= 20711)); then
  [ "$braced" = braced-c1b ] || die "keepassxc-cli ${kpxc_version} shows {S:a{PASSWORD}b} as ${braced}, not as the field a{PASSWORD}b"
  [ -z "$missing" ] || die "keepassxc-cli ${kpxc_version} shows {S:a{PASSWORD}b} with no such field as ${missing}, not empty"
fi
printf '    keepassxc-cli %s shows {S:a{PASSWORD}b} as "%s", and with no such field as "%s"\n' "$kpxc_version" "$braced" "$missing"

step "F.69: a {S: whose brace KeePassXC pairs ends there, and one it pairs with none is text, reaching the child as KeePassXC gives it"
unpaired_keys=(UNPAIRED_TAIL UNPAIRED_OPEN UNPAIRED_EMPTY)
unpaired="<Entry><UUID>$(uuid funpaired)</UUID><Tags>env:billing</Tags><String><Key>Title</Key><Value>Unpaired</Value></String>"
unpaired+='<String><Key>Password</Key><Value ProtectInMemory="True">unpaired-login-c1b</Value></String>'
unpaired+='<String><Key>UNPAIRED_KEY</Key><Value ProtectInMemory="True">key-c1b</Value></String>'
unpaired+='<String><Key>UNPAIRED_TAIL</Key><Value ProtectInMemory="True">{S:UNPAIRED_KEY}b}</Value></String>'
unpaired+='<String><Key>UNPAIRED_OPEN</Key><Value ProtectInMemory="True">{S:a{PASSWORD}</Value></String>'
unpaired+='<String><Key>UNPAIRED_EMPTY</Key><Value ProtectInMemory="True">{S:a{}{PASSWORD}b}</Value></String></Entry>'
make_fields unpaired "$unpaired" 'db-c1b' ''
# What KeePassXC 2.7.11 and 2.8 give each key, one per line.
unpaired_expected=$'key-c1bb}\n{S:aunpaired-login-c1b\n{S:a{}unpaired-login-c1bb}'
out=$(printf '%s\n' "$pw" | "$kp" run billing --vault "$dir/unpaired.kdbx" -- "$child" -c 'printf "%s\n" "${UNPAIRED_TAIL-unset}" "${UNPAIRED_OPEN-unset}" "${UNPAIRED_EMPTY-unset}"' 2>"$dir/unpaired.err" | tr -d '\r') \
  || die "keypaste run failed on the unpaired values: $(cat "$dir/unpaired.err")"
[ "$out" = "$unpaired_expected" ] || die "the child got the unpaired values as: ${out}"
grep -qF -- '-c1b' "$dir/unpaired.err" && die "keypaste run printed a value: $(cat "$dir/unpaired.err")"
shown=$(for key in "${unpaired_keys[@]}"; do kx show "$dir/unpaired.kdbx" services/Unpaired -a "$key" || exit 1; done) \
  || die "keepassxc-cli could not show the unpaired values"
if ((kpxc_number >= 20711)); then
  [ "$shown" = "$unpaired_expected" ] || die "keepassxc-cli ${kpxc_version} shows the unpaired values as: ${shown}"
  printf '    keepassxc-cli %s and keypaste give each unpaired value alike\n' "$kpxc_version"
else
  printf '    keepassxc-cli %s pairs braces as KeePassXC did before 2.7.10, so keypaste is held to what 2.7.11 and 2.8 give\n' "$kpxc_version"
fi

step "C.5b: under the default exposure an agent lists the tagged entries with their variables and tags, gets a variable, never a password, and runs billing from services/"
exposed="$dir/exposed.kdbx"
make_fields exposed '' 'db-c1b' ''
expose_pipe="keypaste-exposed-$$-$(date +%s)"
expose_err="$dir/exposed-agent.err"
logged=$(lines_in "$audit")
expose_out="$dir/exposed.out"
trap 'cleanup; kill "${expose_pid:-}" 2>/dev/null || true' EXIT
# exposed-probe keeps the default view and may run commands, each still asked about, as a person
# confirmed at keypaste approve's terminal; with no settings its run would be refused unasked.
agent_settings "$kp" "$(native "$exposed")" "$pw" 'exposed-probe --run on' \
  || die "could not turn exposed-probe's commands on in the KeePassXC vault"
# The hour for STRIPE_SECRET_KEY, then once for the run; the password is refused before anyone is asked.
printf '%s\nh\no\n' "$pw" | "$kp" approve --vault "$(native "$exposed")" --approver "$expose_pipe" --approval-timeout 30 \
  >/dev/null 2>"$expose_err" &
expose_pid=$!
for _ in $(seq 1 100); do
  grep -q 'listening on' "$expose_err" && break
  kill -0 "$expose_pid" 2>/dev/null || die "keypaste approve exited before it listened: $(cat "$expose_err")"
  sleep 0.2
done
grep -q 'listening on' "$expose_err" || die "keypaste approve never started listening on the exposed vault"

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
} | "$kp" mcp --vault "$(native "$exposed")" --audit-log "$(native "$audit")" --approver "$expose_pipe" \
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
lines_after "$audit" "$logged" | jq -e -s '[.[] | select(.tool == "request_credential")] | .[0].decision == "granted" and .[1].method == "out-of-scope"' >/dev/null \
  || die "the audit log does not show the variable granted and the password out of scope: $(lines_after "$audit" "$logged")"
lines_after "$audit" "$logged" | jq -e -s 'any(.[]; .tool == "run" and .decision == "granted" and (.entries | sort) == ["services/Database", "services/Stripe"])' >/dev/null \
  || die "the run's audit line does not name the two services entries: $(lines_after "$audit" "$logged")"
[ "$(tr -d '\r' <"$expose_err" | grep -c 'an agent is asking for a credential')" = 1 ] \
  || die "keypaste approve was not asked exactly once for a credential: $(cat "$expose_err")"
[ "$(tr -d '\r' <"$expose_err" | grep -c 'an agent wants to run a command')" = 1 ] \
  || die "keypaste approve was not asked exactly once to run: $(cat "$expose_err")"
kill "$expose_pid" 2>/dev/null || true
wait "$expose_pid" 2>/dev/null || true
trap cleanup EXIT

step "C.1b: a token scoped to billing's dev and staging sets, made before an agent holds the vault"
token=$(printf '%s\n' "$pw" | "$kp" token create gate-ci --scope 'read:billing/dev/*,read:billing/staging/*' --vault "$(native "$fields")" 2>"$dir/token.err" | tr -d '\r') \
  || die "keypaste token create failed: $(cat "$dir/token.err")"
[ -n "$token" ] || die "keypaste token create printed no token: $(cat "$dir/token.err")"

step "C.1b: run --session through a real keypaste approve names the source entries, and a set holding a prod-tagged member is asked once only"
fields_pipe="keypaste-fields-$$-$(date +%s)"
fields_err="$dir/fields-agent.err"
cleanup_fields() {
  cleanup
  if [ -n "${fields_pid:-}" ]; then kill "$fields_pid" 2>/dev/null || true; fi
  exec 7>&- 2>/dev/null || true
}
trap cleanup_fields EXIT
# The hour for the dev run, then once for the staging run, whose Shared entry is also tagged env:billing:prod.
printf '%s\nh\no\n' "$pw" | "$kp" approve --vault "$(native "$fields")" --approver "$fields_pipe" --approval-timeout 30 \
  >/dev/null 2>"$fields_err" &
fields_pid=$!
for _ in $(seq 1 100); do
  grep -q 'listening on' "$fields_err" && break
  kill -0 "$fields_pid" 2>/dev/null || die "keypaste approve exited before it listened: $(cat "$fields_err")"
  sleep 0.2
done
grep -q 'listening on' "$fields_err" || die "keypaste approve never started listening on the fields vault"

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
logged=$(lines_in "$audit")
out=$(KEYPASTE_TOKEN="$token" "$kp" run --token env --vault "$(native "$fields")" --approver "$fields_pipe" billing -- "$child" -c "$probe" 2>"$dir/token-dev.err" | tr -d '\r') \
  || die "run --token failed: $(cat "$dir/token-dev.err") / $(cat "$fields_err")"
[ "$out" = "$dev_set" ] || die "the token child's environment was: ${out}"
set +e
out=$(KEYPASTE_TOKEN="$token" "$kp" run --token env --vault "$(native "$fields")" --approver "$fields_pipe" billing -p staging -- "$child" -c "$probe" 2>"$dir/token-staging.err")
status=$?
set -e
[ "$status" -ne 0 ] || die "a token without allow-prod ran a set holding a prod-tagged member"
[ -z "$out" ] || die "a child started for the refused token run: ${out}"
grep -qF 'holds an item of a protected environment' "$dir/token-staging.err" \
  || die "the refused token run does not say why: $(cat "$dir/token-staging.err")"
lines_after "$audit" "$logged" | jq -e -s 'map(select(.method == "token")) | length == 2
                                           and .[0].decision == "granted" and .[0].entries == ["services/Database", "services/Stripe"]
                                           and .[1].decision == "denied"' >/dev/null \
  || die "the token runs' audit lines do not name the source entries: $(lines_after "$audit" "$logged")"
grep -qF -- '-c1b' "$audit" && die "a value reached the audit log"

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
grep -qxF "asked: New value for STRIPE_KEY on services/Stripe, which billing/dev and billing/staging read. The old one stays in the item's history." <<<"$said" \
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

step "ACAF: keypaste keeps agents' settings in a vault KeePassXC made, one entry each in .keypaste/agents under the UUID its name derives, and the vault stays KDBX 4.0"
acaf="$dir/acaf.kdbx"
fresh="$dir/acaf-fresh.kdbx"
seed '' >"$dir/acaf.xml"
printf '%s\n%s\n' "$pw" "$pw" | "$cli" import -q -p "$(native "$dir/acaf.xml")" "$(native "$acaf")" \
  || die "keepassxc-cli could not import the ACAF seed"
cp "$acaf" "$fresh"
agent_settings "$kp" "$(native "$acaf")" "$pw" 'acaf-first --project billing --mode automatic' \
  'acaf-second --project billing --mode automatic' 'acaf-both --project billing --mode automatic' \
  || die "could not save the ACAF agents' settings in the KeePassXC vault"
[ "$(version "$acaf")" = 000004 ] || die "keeping agents' settings raised the vault above KDBX 4.0: $(version "$acaf")"

sha1() { if command -v sha1sum >/dev/null 2>&1; then sha1sum | cut -d' ' -f1; else shasum -a 1 | cut -d' ' -f1; fi; }
# RFC 9562's version 5 UUID of $1 in ACAF v1's namespace, as 32 hex digits: the UUID of the entry of the
# agent named $1, or, for a slash and a path, of keypaste's group at that path.
acaf_id() {
  local hash
  hash=$({ printf '919d7b746e775fb099eb74758c119d7d' | xxd -r -p; printf '%s' "$1"; } | sha1)
  printf '%s5%s%x%s\n' "${hash:0:12}" "${hash:13:3}" $(( (16#${hash:16:1} & 3) | 8 )) "${hash:17:15}"
}
# The UUID KeePassXC reads for the entry of the agent $2 in the vault $1, as 32 hex digits.
uuid_of() { kx show "$1" ".keypaste/agents/$2" -a Uuid | tr -d '{}-' | tr '[:upper:]' '[:lower:]'; }
# The UUID of every group named $2 in the vault $1, one per line, as KeePassXC's XML writes it.
groups_named() {
  kx export "$1" -f xml | awk -v name="<Name>$2</Name>" '
    /<UUID>/ { id = $0; sub(/^.*<UUID>/, "", id); sub(/<\/UUID>.*$/, "", id) }
    index($0, name) { print id }'
}
# Whether the vault $1 holds one .keypaste group and one agents group, each under the UUID its path derives.
groups_derived() {
  [ "$(groups_named "$1" .keypaste)" = "$(acaf_id /.keypaste | xxd -r -p | base64)" ] \
    && [ "$(groups_named "$1" agents)" = "$(acaf_id /.keypaste/agents | xxd -r -p | base64)" ]
}
# The names KeePassXC lists in .keypaste/agents of the vault $1, one per entry, sorted, on one line.
agents_in() { local listed; listed=$(kx ls "$1" .keypaste/agents) || return 1; sort <<<"$listed" | paste -sd' ' -; }
# The ACAF record KeePassXC reads in the entry for the agent $2 of the vault $1.
record_of() { kx show "$1" ".keypaste/agents/$2" -a acaf; }
canonical() { jq -cS . | tr -d '\r'; }
# Whether two records say the same, whatever the order of their members.
same() { jq -en --argjson a "$1" --argjson b "$2" '$a == $b' >/dev/null 2>&1; }
# The record of each revision KeePassXC keeps for the agent $2 of the vault $1, one per line, in canonical
# form. The whole export is read, since leaving the stream early breaks kx's pipe (F.16).
revisions_of() {
  local id
  id=$(uuid_of "$1" "$2" | xxd -r -p | base64) || die "KeePassXC cannot read the UUID of $2's entry"
  kx export "$1" -f xml | awk -v id="<UUID>$id</UUID>" '
    done { next }
    /<History>/ { inhist = 1; next }
    /<\/History>/ { inhist = 0; if (mine) done = 1; next }
    !inhist && /<UUID>/ { mine = index($0, id) > 0 }
    inhist && mine && index($0, "<Key>acaf</Key>") { want = 1 }
    want && (v = index($0, "<Value")) {
      text = substr($0, v); sub(/^<Value[^>]*>/, "", text); sub(/<\/Value>.*$/, "", text)
      gsub(/&quot;/, "\"", text); gsub(/&lt;/, "<", text); gsub(/&gt;/, ">", text); gsub(/&apos;/, "\047", text); gsub(/&amp;/, "\\&", text)
      print text; want = 0
    }' | canonical
}
merge_into() { # target, source
  printf '%s\n' "$pw" | "$cli" merge -q -s "$(native "$1")" "$(native "$2")" >/dev/null || die "keepassxc-cli could not merge $2 into $1"
}
# That the merged vault $1 holds one entry for the agent $2, under the UUID its name derives, with the newer
# record $4 whole and the older $3 in its history.
newer_kept() {
  same "$(record_of "$1" "$2")" "$4" || die "$(basename "$1"): $2 is not the newer copy's record whole: $(record_of "$1" "$2")"
  grep -qxF "$(canonical <<<"$3")" <<<"$(revisions_of "$1" "$2")" \
    || die "$(basename "$1"): $2's history does not hold the older copy's record: $(revisions_of "$1" "$2")"
  [ "$(uuid_of "$1" "$2")" = "$(acaf_id "$2")" ] || die "$(basename "$1"): $2's entry has the UUID $(uuid_of "$1" "$2")"
}

[ "$(agents_in "$acaf")" = "acaf-both acaf-first acaf-second" ] || die "KeePassXC lists the agents $(agents_in "$acaf")"
for agent in acaf-both acaf-first acaf-second; do
  same "$(record_of "$acaf" "$agent")" \
    '{"acaf":1,"see":["tag:env:*"],"values":true,"run":false,"mode":"allow-for-a-while","projects":[{"project":"billing","mode":"automatic"}]}' \
    || die "KeePassXC reads ${agent}'s record as $(record_of "$acaf" "$agent")"
  [ "$(uuid_of "$acaf" "$agent")" = "$(acaf_id "$agent")" ] || die "${agent}'s entry has the UUID $(uuid_of "$acaf" "$agent"), not $(acaf_id "$agent")"
done
groups_derived "$acaf" || die "the groups keypaste made are $(groups_named "$acaf" .keypaste) and $(groups_named "$acaf" agents), not their paths' UUIDs"

step "ACAF: KeePassXC edits an entry, adds one and saves, and every record and tag is kept byte for byte"
records_now() { local agent; for agent in acaf-both acaf-first acaf-second; do record_of "$1" "$agent"; done; }
tags_now() { local entry; for entry in services/Stripe services/Database services/Odd services/Other; do tags "$1" "$entry" | paste -sd' ' -; done; }
records_was=$(records_now "$acaf")
tags_was=$(tags_now "$acaf")
kx edit "$acaf" services/Other -u kpxc-edit >/dev/null || die "KeePassXC could not edit services/Other"
kx add "$acaf" services/Added -u kpxc-added >/dev/null || die "KeePassXC could not add services/Added"
[ "$(kx show "$acaf" services/Other -a UserName)" = kpxc-edit ] || die "KeePassXC did not save its edit"
[ "$(kx show "$acaf" services/Added -a UserName)" = kpxc-added ] || die "KeePassXC did not save the entry it added"
[ "$(records_now "$acaf")" = "$records_was" ] || die "KeePassXC's save changed a record: $(records_now "$acaf")"
[ "$(tags_now "$acaf")" = "$tags_was" ] || die "KeePassXC's save changed a tag: $(tags_now "$acaf")"
[ "$(agents_in "$acaf")" = "acaf-both acaf-first acaf-second" ] || die "after KeePassXC's save it lists the agents $(agents_in "$acaf")"
[ "$(uuid_of "$acaf" acaf-first)" = "$(acaf_id acaf-first)" ] || die "KeePassXC's save changed acaf-first's UUID"

held_pid=
holds=0
asks=0
trap 'cleanup_fields; kill "${held_pid:-}" 2>/dev/null || true' EXIT
# Holds the vault $1 with a real keypaste approve of the gate's home, answering its prompts with the keys in $2.
hold() {
  release
  holds=$((holds + 1))
  held=$1
  held_pipe="keypaste-acaf-$$-$(date +%s)-$holds"
  held_err="$dir/acaf-agent-$holds.err"
  printf '%s\n%b' "$pw" "$2" | "$kp" approve --vault "$(native "$held")" --approver "$held_pipe" --approval-timeout 30 \
    >/dev/null 2>"$held_err" &
  held_pid=$!
  for _ in $(seq 1 100); do
    grep -q 'listening on' "$held_err" && break
    kill -0 "$held_pid" 2>/dev/null || die "keypaste approve exited before it listened on ${held}: $(cat "$held_err")"
    sleep 0.2
  done
  grep -q 'listening on' "$held_err" || die "keypaste approve never started listening on ${held}"
}
release() {
  [ -n "$held_pid" ] || return 0
  kill "$held_pid" 2>/dev/null || true
  wait "$held_pid" 2>/dev/null || true
  held_pid=
}
# Asks the held vault through a real keypaste mcp labelled $1, registered as keypaste setup registers
# one, for Stripe's STRIPE_SECRET_KEY; the answer is left in reply.
ask_as() {
  local label=$1 id out call
  asks=$((asks + 1))
  id=$((asks + 1))
  out="$dir/acaf-$id.out"
  call=$(jq -cn --argjson id "$id" \
    '{jsonrpc:"2.0",id:$id,method:"tools/call",params:{name:"request_credential",arguments:{entry:"services/Stripe",field:"STRIPE_SECRET_KEY",reason:"ACAF gate",ttl_seconds:60}}}')
  : >"$out"
  {
    jq -cn --arg name "$label" '{jsonrpc:"2.0",id:1,method:"initialize",params:{protocolVersion:"2025-11-25",capabilities:{},clientInfo:{name:$name,version:"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "$call"
    await_replies "${WAIT_SECONDS:-30}" "$out" "$id" || true
  } | "$kp" mcp --vault "$(native "$held")" --expose '**' --allow-run --audit-log "$(native "$audit")" --approver "$held_pipe" \
        --client-label "$label" >"$out" 2>"$dir/acaf-$id.err" || die "keypaste mcp exited non-zero: $(cat "$dir/acaf-$id.err")"
  reply=$(tr -d '\r' <"$out" | jq -c --argjson id "$id" 'select(.id == $id)' | tr -d '\r')
}
released() { jq -e '.result.isError == false and .result.structuredContent.value == "sk_test_projects"' <<<"$reply" >/dev/null; }
refused() { jq -e '.result.isError == true' <<<"$reply" >/dev/null && ! grep -qF sk_test_projects <<<"$reply"; }
# How many of the agent's terminal lines match $1.
said_held() { tr -d '\r' <"$held_err" | grep -c "$1" || true; }
# The credential requests the owner recorded after the log held $1 lines: label, tool, decision, method.
answered() {
  lines_after "$audit" "$1" \
    | jq -c -s 'map(select(.tool == "request_credential") | [.client.label, .tool, .decision, .method])' | tr -d '\r'
}
# Every agent's record keypaste reads through the owner holding the vault, as name and record.
read_held() {
  "$kp" clients --json --vault "$(native "$held")" --approver "$held_pipe" 2>"$dir/acaf-policy.err" | tr -d '\r' \
    | jq -c '[.[] | select(.kind == "agent") | {name, record}] | sort'
}
# The same list from a name and a record's text for each.
records_list() { jq -cn '[$ARGS.positional as $p | range(0; $p | length; 2) | {name: $p[.], record: ($p[. + 1] | fromjson)}] | sort' --args "$@"; }

logged=$(lines_in "$audit")
hold "$acaf" ''
same "$(read_held)" "$(records_list acaf-both "$(record_of "$acaf" acaf-both)" acaf-first "$(record_of "$acaf" acaf-first)" acaf-second "$(record_of "$acaf" acaf-second)")" \
  || die "keypaste does not read the records KeePassXC saved: $(read_held) $(cat "$dir/acaf-policy.err")"
ask_as acaf-first
released || die "acaf-first, Automatic for billing, was not given STRIPE_SECRET_KEY after KeePassXC's save: ${reply}"
[ "$(said_held 'an agent is asking for a credential')" = 0 ] || die "an Automatic release put a prompt in front of the person"
[ "$(answered "$logged")" = '[["acaf-first","request_credential","granted","automatic"]]' ] \
  || die "the release was not recorded as acaf-first's Automatic one: $(lines_after "$audit" "$logged")"
release

step "ACAF: two copies change apart: one agent in each, the same agent in both, a tag in the first, and the same agent created in both"
first="$dir/acaf-first.kdbx"
second="$dir/acaf-second.kdbx"
cp "$acaf" "$first"
cp "$acaf" "$second"
agent_settings "$kp" "$(native "$first")" "$pw" 'acaf-first --values no' 'acaf-both --values no' 'acaf-twin --values no' \
  || die "could not change the first copy's agents"
said=$(kp_on "$first" env tag billing services/Other -p qa --yes 2>&1) || die "env tag on the first copy failed: ${said}"
# A merge orders an entry's versions by modification time, which the file keeps to the second.
sleep 2
agent_settings "$kp" "$(native "$second")" "$pw" 'acaf-second --project billing --mode ask' 'acaf-both --run on' \
  'acaf-twin --project billing --mode automatic' || die "could not change the second copy's agents"
kx edit "$second" services/Stripe -u kpxc-second >/dev/null || die "KeePassXC could not edit the second copy's services/Stripe"
first_only=$(record_of "$first" acaf-first)
both_first=$(record_of "$first" acaf-both)
twin_first=$(record_of "$first" acaf-twin)
second_only=$(record_of "$second" acaf-second)
both_second=$(record_of "$second" acaf-both)
twin_second=$(record_of "$second" acaf-twin)
jq -e '.values == false and .projects == [{"project":"billing","mode":"automatic"}]' <<<"$first_only" >/dev/null \
  || die "the first copy's acaf-first reads ${first_only}"
jq -e '.values == false and .run == false' <<<"$both_first" >/dev/null || die "the first copy's acaf-both reads ${both_first}"
jq -e '.mode == "allow-for-a-while" and .values == false and (has("projects") | not)' <<<"$twin_first" >/dev/null \
  || die "the first copy's acaf-twin reads ${twin_first}"
jq -e '.projects == [{"project":"billing","mode":"ask"}]' <<<"$second_only" >/dev/null || die "the second copy's acaf-second reads ${second_only}"
jq -e '.values == true and .run == true' <<<"$both_second" >/dev/null || die "the second copy's acaf-both reads ${both_second}"
jq -e '.values == true and .projects == [{"project":"billing","mode":"automatic"}]' <<<"$twin_second" >/dev/null \
  || die "the second copy's acaf-twin reads ${twin_second}"
same "$both_first" "$both_second" && die "the copies' records of acaf-both agree, so a merge cannot show which one won"
same "$twin_first" "$twin_second" && die "the copies' records of acaf-twin agree, so a merge cannot show which one won"
for copy in "$first" "$second"; do
  [ "$(version "$copy")" = 000004 ] || die "$(basename "$copy") left KDBX 4.0: $(version "$copy")"
  [ "$(uuid_of "$copy" acaf-twin)" = "$(acaf_id acaf-twin)" ] || die "$(basename "$copy") created acaf-twin under $(uuid_of "$copy" acaf-twin)"
done

step "ACAF: KeePassXC merges the copies both ways; each holds every change and one entry per agent, the newer record of acaf-both and acaf-twin whole with the older in its history"
into_first="$dir/acaf-into-first.kdbx"
into_second="$dir/acaf-into-second.kdbx"
cp "$first" "$into_first"
merge_into "$into_first" "$second"
cp "$second" "$into_second"
merge_into "$into_second" "$first"
for merged in "$into_first" "$into_second"; do
  name=$(basename "$merged")
  [ "$(version "$merged")" = 000004 ] || die "${name}: the merge left KDBX 4.0: $(version "$merged")"
  [ "$(agents_in "$merged")" = "acaf-both acaf-first acaf-second acaf-twin" ] || die "${name}: KeePassXC lists the agents $(agents_in "$merged")"
  same "$(record_of "$merged" acaf-first)" "$first_only" || die "${name}: acaf-first, changed in the first copy only, reads $(record_of "$merged" acaf-first)"
  same "$(record_of "$merged" acaf-second)" "$second_only" || die "${name}: acaf-second, changed in the second copy only, reads $(record_of "$merged" acaf-second)"
  newer_kept "$merged" acaf-both "$both_first" "$both_second"
  newer_kept "$merged" acaf-twin "$twin_first" "$twin_second"
  groups_derived "$merged" || die "${name}: KeePassXC holds the groups $(groups_named "$merged" .keypaste) and $(groups_named "$merged" agents)"
  [ "$(tags_now "$merged")" = $'env:billing finance\nenv:billing:prod\nenv:billing:Prod\nenv:billing:qa finance' ] \
    || die "${name}: KeePassXC reads the tags $(tags_now "$merged")"
  [ "$(kx show "$merged" services/Stripe -a UserName)" = kpxc-second ] || die "${name}: KeePassXC's edit in the second copy did not survive"
done

step "ACAF: keypaste reads one record per agent in each merge and applies the newer acaf-both and acaf-twin"
for merged in "$into_first" "$into_second"; do
  name=$(basename "$merged")
  logged=$(lines_in "$audit")
  # Once for acaf-second, whose billing row is Ask every time; nothing else may reach the person.
  hold "$merged" 'o\n'
  same "$(read_held)" "$(records_list acaf-both "$both_second" acaf-first "$first_only" acaf-second "$second_only" acaf-twin "$twin_second")" \
    || die "${name}: keypaste does not read the records KeePassXC merged: $(read_held) $(cat "$dir/acaf-policy.err")"
  ask_as acaf-first
  refused || die "${name}: acaf-first, given no values in the first copy, was released STRIPE_SECRET_KEY: ${reply}"
  ask_as acaf-second
  released || die "${name}: acaf-second, allowed once, was not released STRIPE_SECRET_KEY: ${reply}"
  ask_as acaf-both
  released || die "${name}: acaf-both, Automatic for billing in the newer copy, was not released STRIPE_SECRET_KEY: ${reply}"
  ask_as acaf-twin
  released || die "${name}: acaf-twin, Automatic for billing in the newer copy, was not released STRIPE_SECRET_KEY: ${reply}"
  [ "$(answered "$logged")" = '[["acaf-first","request_credential","denied","inject-only"],["acaf-second","request_credential","granted","prompt"],["acaf-both","request_credential","granted","automatic"],["acaf-twin","request_credential","granted","automatic"]]' ] \
    || die "${name}: the owner did not record what each record says: $(answered "$logged")"
  [ "$(said_held 'an agent is asking for a credential')" = 1 ] || die "${name}: keypaste approve was not asked exactly once: $(cat "$held_err")"
  [ "$(said_held 'this agent is set to Ask every time')" = 1 ] || die "${name}: acaf-second's prompt did not say it is asked every time: $(cat "$held_err")"
  release
done

step "ACAF: keypaste narrows acaf-twin and acaf-both in one merged copy, and merged either way again the narrowing holds in the one entry of each"
agent_settings "$kp" "$(native "$into_first")" "$pw" 'acaf-twin --values no' 'acaf-both --project billing --mode ask' \
  || die "could not narrow acaf-twin and acaf-both in the merged copy"
twin_narrowed=$(record_of "$into_first" acaf-twin)
both_narrowed=$(record_of "$into_first" acaf-both)
jq -e '.values == false and .projects == [{"project":"billing","mode":"automatic"}]' <<<"$twin_narrowed" >/dev/null \
  || die "the narrowed acaf-twin reads ${twin_narrowed}"
jq -e '.projects == [{"project":"billing","mode":"ask"}] and .run == true and .values == true' <<<"$both_narrowed" >/dev/null \
  || die "the narrowed acaf-both reads ${both_narrowed}"
again="$dir/acaf-into-second-again.kdbx"
cp "$into_second" "$again"
merge_into "$again" "$into_first"
merge_into "$into_first" "$into_second"
for merged in "$again" "$into_first"; do
  name=$(basename "$merged")
  logged=$(lines_in "$audit")
  [ "$(agents_in "$merged")" = "acaf-both acaf-first acaf-second acaf-twin" ] || die "${name}: KeePassXC lists the agents $(agents_in "$merged")"
  newer_kept "$merged" acaf-both "$both_second" "$both_narrowed"
  newer_kept "$merged" acaf-twin "$twin_second" "$twin_narrowed"
  # Once for acaf-both, now asked every time for billing.
  hold "$merged" 'o\n'
  ask_as acaf-both
  released || die "${name}: acaf-both, allowed once, was not released STRIPE_SECRET_KEY: ${reply}"
  ask_as acaf-twin
  refused || die "${name}: acaf-twin, given no values by the narrowing, was released STRIPE_SECRET_KEY: ${reply}"
  [ "$(answered "$logged")" = '[["acaf-both","request_credential","granted","prompt"],["acaf-twin","request_credential","denied","inject-only"]]' ] \
    || die "${name}: the owner did not record what each record says: $(answered "$logged")"
  [ "$(said_held 'this agent is set to Ask every time')" = 1 ] || die "${name}: acaf-both's prompt did not say it is asked every time: $(cat "$held_err")"
  release
done
grep -qF sk_test_projects "$audit" && die "a released value reached the audit log"
trap cleanup_fields EXIT

step "ACAF: two copies of a vault that held no agents' settings each create the same agent, and KeePassXC merges them both ways into one .keypaste/agents and one entry, the newer record whole"
new_first="$dir/acaf-new-first.kdbx"
new_second="$dir/acaf-new-second.kdbx"
cp "$fresh" "$new_first"
cp "$fresh" "$new_second"
[ -z "$(groups_named "$fresh" .keypaste)" ] || die "the vault KeePassXC made already holds a .keypaste group"
agent_settings "$kp" "$(native "$new_first")" "$pw" 'acaf-new --values no' || die "could not create acaf-new in the first fresh copy"
sleep 2
agent_settings "$kp" "$(native "$new_second")" "$pw" 'acaf-new --mode ask' || die "could not create acaf-new in the second fresh copy"
new_older=$(record_of "$new_first" acaf-new)
new_newer=$(record_of "$new_second" acaf-new)
jq -e '.values == false and .mode == "allow-for-a-while"' <<<"$new_older" >/dev/null || die "the first fresh copy's acaf-new reads ${new_older}"
jq -e '.values == true and .mode == "ask"' <<<"$new_newer" >/dev/null || die "the second fresh copy's acaf-new reads ${new_newer}"
cp "$new_first" "$dir/acaf-new-into-first.kdbx"
merge_into "$dir/acaf-new-into-first.kdbx" "$new_second"
cp "$new_second" "$dir/acaf-new-into-second.kdbx"
merge_into "$dir/acaf-new-into-second.kdbx" "$new_first"
for merged in "$dir/acaf-new-into-first.kdbx" "$dir/acaf-new-into-second.kdbx"; do
  name=$(basename "$merged")
  groups_derived "$merged" || die "${name}: KeePassXC holds the groups $(groups_named "$merged" .keypaste) and $(groups_named "$merged" agents)"
  [ "$(agents_in "$merged")" = acaf-new ] || die "${name}: KeePassXC lists the agents $(agents_in "$merged")"
  newer_kept "$merged" acaf-new "$new_older" "$new_newer"
done

step "NEGATIVE CONTROL: the listing and run comparisons must be able to fail"
[ "$listed" = "${expected}-CORRUPTED" ] && die "a deliberately corrupted expectation still matched — this gate is not gating"
out=$(printf '%s\n' "$pw" | OLD_KEY=old-c1b "$kp" run billing --vault "$fields" -- "$child" -c "$probe" 2>"$dir/run.err" | tr -d '\r') \
  || die "keypaste run with OLD_KEY inherited failed: $(cat "$dir/run.err")"
[ "$out" = "$dev_set" ] && die "a child holding OLD_KEY still matched the dev set — the run comparisons cannot see OLD_KEY"

printf '\n%s\n' "PROJECTS GATE PASSED: KeePassXC and keypaste agree on project tags, a protected tag makes a real agent ask every time, an untagged entry under env/ is neither a variable nor protected, a value's {PASSWORD} reaches a child as KeePassXC fills it in and one using KeePassXC's escape as 2.7.11 reads it, a {S:…} whose name holds braces is refused as the one field KeePassXC reads, a tag KeePassXC dropped with another change is flagged and restored, and agents' settings survive KeePassXC's saves and merge into one entry per agent, the newer record whole."
