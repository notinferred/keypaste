#!/usr/bin/env bash
# PERMANENT COMPATIBILITY GATE: project tags KeePassXC writes are the ones keypaste reads, and the
# ones keypaste writes are the ones KeePassXC reads (C.1a). This is the tags half; the fields that
# tagged entries release are C.1b's.
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
# NEGATIVE CONTROL: a corrupted expectation must fail the comparison the listing check rests on, and
# the hour must be honoured for an entry no tag protects.
#
# Usage:  scripts/verify-keepassxc-projects.sh <work-directory>
# Env:    KP_COMPAT_PASSWORD   master password for the vaults        (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_MCP_BIN     path to the keypaste-mcp binary       (default: the Release build)
set -euo pipefail

die()  { printf '\nPROJECTS GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-projects.sh <work-directory>"
pw=${KP_COMPAT_PASSWORD:-}
[ -n "$pw" ] || die "KP_COMPAT_PASSWORD is not set"
cli=${KPXC_CLI:-keepassxc-cli}

# Absence of the tool is a FAILURE, never a skip — see verify-keepassxc-compat.sh (i).
if ! command -v "$cli" >/dev/null 2>&1 && [ ! -x "$cli" ]; then
  die "keepassxc-cli not found (KPXC_CLI='${cli}'). This gate must never be skipped or soft-passed."
fi
command -v jq >/dev/null 2>&1 || die "jq is required and was not found; this gate must never be skipped"

binary() {
  local candidate=$1
  [ -x "$candidate" ] || candidate="${candidate}.exe"
  [ -x "$candidate" ] || die "not found: $1 (build it first)"
  printf '%s' "$candidate"
}
kp=$(binary "${KEYPASTE_BIN:-artifacts/bin/Keypaste.Cli/release/keypaste}")
mcp=$(binary "${KEYPASTE_MCP_BIN:-artifacts/bin/Keypaste.Mcp/release/keypaste-mcp}")

rm -rf "$dir"
mkdir -p "$dir/home"
export KEYPASTE_HOME="$dir/home"
unset KEYPASTE_VAULT KEYPASTE_KEYFILE

grouped="$dir/grouped.kdbx"
plain="$dir/plain.kdbx"
agent_pid=
cleanup() { if [ -n "$agent_pid" ]; then kill "$agent_pid" 2>/dev/null || true; fi; }
trap cleanup EXIT

native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
bytes()  { od -An -v -tx1 "$1" | tr -d ' \n'; }
version() { od -An -v -tx1 -N12 "$1" | tr -d ' \n' | cut -c17-22; }
kx() {
  local sub=$1 target=$2; shift 2
  printf '%s\n' "$pw" | "$cli" "$sub" -q "$(native "$target")" "$@" | tr -d '\r'
}
kp_on() {
  local db=$1; shift
  printf '%s\n' "$pw" | "$kp" "$@" --vault "$db" | tr -d '\r'
}
uuid() { printf '%-16.16s' "$1" | base64; }
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
said=$(kp_on "$plain" env tag billing services/Stripe -p prod 2>&1) || die "env tag failed: ${said}"
grep -qF 'STRIPE_SECRET_KEY' <<<"$said" || die "env tag did not name the field that joins: ${said}"
grep -qF 'Region' <<<"$said" && die "env tag named a field that is not env-named: ${said}"
[ "$(tags "$plain" services/Stripe | paste -sd' ' -)" = "env:billing env:billing:prod finance" ] \
  || die "KeePassXC reads the tags $(tags "$plain" services/Stripe | paste -sd' ' -)"
[ "$(version "$plain")" = 000004 ] || die "tagging raised the plain vault above KDBX 4.0: $(version "$plain")"
said=$(kp_on "$plain" env untag billing services/Stripe 2>&1) || die "env untag failed: ${said}"
[ "$(tags "$plain" services/Stripe | paste -sd' ' -)" = "env:billing:prod finance" ] \
  || die "after untag KeePassXC reads the tags $(tags "$plain" services/Stripe | paste -sd' ' -)"

said=$(kp_on "$grouped" env tag billing services/Other -p qa 2>&1) || die "env tag on the group-tagged vault failed: ${said}"
grep -qx 'env:billing:qa' <<<"$(tags "$grouped" services/Other)" || die "KeePassXC does not read the tag keypaste added"
[ "$(version "$grouped")" = "$grouped_version" ] || die "tagging changed the group-tagged vault's version"
exported=$(kx export "$grouped" -f xml) || die "KeePassXC cannot export the group-tagged vault"
[ "$(grep -c '<Tags>env:billing:staging</Tags>' <<<"$exported")" = 1 ] || die "the group tag did not survive"
said=$(kp_on "$grouped" env untag billing services/Other -p qa 2>&1) || die "env untag on the group-tagged vault failed: ${said}"

for line in "env tag bill:ing services/Stripe" "env tag billing services/Stripe -p Prod" "env tag billing .keypaste/x"; do
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

step "NEGATIVE CONTROL: the listing comparison must be able to fail"
[ "$listed" = "${expected}-CORRUPTED" ] && die "a deliberately corrupted expectation still matched — this gate is not gating"

printf '\nPROJECTS GATE PASSED: KeePassXC and keypaste agree on project tags, and a protected tag makes a real agent ask every time.\n'
