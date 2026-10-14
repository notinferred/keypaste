#!/usr/bin/env bash
# The desktop owns the vault it unlocks (U.1, V-U.1), across real processes: the shipped keypaste and
# keypaste mcp, and tests/Keypaste.AppDriver unlocking through the app's unlock screen and serving the
# vault as the app does.
#
# A real keypaste mcp request reaches the app's session, is put to the person in the app's prompt, and
# the app records the listing and the refusal in its own home's log, naming that session, while the
# bridge, logging elsewhere, records nothing. With the app locked the requests are refused and the bridge
# records them, the app nothing. `keypaste agent` and a second app on the same vault are each refused with
# a message naming the app, before any password is asked for. Unlocking again starts a new session, in
# which the app applies the vault's agent settings as `keypaste agent` does: what the agent is Automatic
# for is released unasked and recorded as such. A killed app leaves nothing holding the vault. The
# stale-session and unattached cases run over a real pipe in SessionAuthorityTests, because the shipped
# bridge always attaches.
#
# NEGATIVE CONTROL: this fails if the listing does not come back from the app's vault, if the app's lines
# name no session or the wrong one, if the bridge also records a request the app answered, if a request
# is answered while the app is locked, if either second owner opens the vault, or if the app asks about
# or misrecords what the agent is Automatic for. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-session-master-pw'
readonly SECRET='SENTINEL-SESSION-PASSWORD-3e8a71'
readonly ENTRY='env/ci/DEPLOY_KEY'
readonly AUTO='SENTINEL-SESSION-AUTOMATIC-5c1d09'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='HOLD_OUT OUT ERR AGENT_ERR'
require jq

CLI="$(keypaste_bin)"
DRV="$(app_driver)"

WORK="$(mktemp -d)"
mkdir -p "$WORK/home"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
VAULT="$(native "$WORK/vault.kdbx")"
# The bridge logs apart from the app, so each log shows which process recorded what.
AUDIT="$(native "$WORK/audit.jsonl")"
readonly BRIDGE_LOG="$WORK/audit.jsonl"
readonly APP_LOG="$WORK/home/audit.jsonl"
readonly HOLD_OUT="$WORK/hold.txt"
readonly AGENT_ERR="$WORK/agent-stderr.txt"

HOLD_PID=""
# Fixed descriptors, as macOS's bash 3.2 needs (D-0398): 7 is the held app's input.
exec 7>/dev/null
cleanup() {
  exec 7>&- 2>/dev/null || true
  if [ -n "$HOLD_PID" ]; then kill_process "$HOLD_PID"; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

# One bridge process: initialize, list, then ask for the credential. Given $3, the request reaches the
# app's prompt, the $3th this app has raised, and the person denies it there.
ask() {
  local out="$1" err="$2" prompt="${3:-}"
  : >"$out"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_entry_names","arguments":{}}}'
    await_replies "${WAIT_SECONDS:-30}" "$out" 2 || true
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci session probe\",\"ttl_seconds\":60}}}"
    if [ -n "$prompt" ]; then
      wait_for '^prompt client' "$HOLD_OUT" "$prompt"
      echo deny >&7
    fi
    await_replies "${WAIT_SECONDS:-30}" "$out" 3 || true
  } | "$CLI" mcp --vault "$VAULT" --expose 'env/**' --audit-log "$AUDIT" --client-label ci-probe >"$out" 2>"$err" \
    || die "keypaste mcp exited non-zero"
}

# One bridge process asking for ci's AUTO_KEY, which ci-probe is Automatic for.
ask_automatic() {
  local out="$1" err="$2"
  : >"$out"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"request_credential","arguments":{"entry":"env/ci/.env","field":"AUTO_KEY","reason":"ci automatic probe","ttl_seconds":60}}}'
    await_replies "${WAIT_SECONDS:-30}" "$out" 2 || true
  } | "$CLI" mcp --vault "$VAULT" --expose 'env/**' --audit-log "$AUDIT" --client-label ci-probe >"$out" 2>"$err" \
    || die "keypaste mcp exited non-zero"
}

# The lines each log gained since the phase began.
app_lines() { lines_after "$APP_LOG" "$app_before"; }
bridge_lines() { lines_after "$BRIDGE_LOG" "$bridge_before"; }
before_phase() { app_before=$(lines_in "$APP_LOG"); bridge_before=$(lines_in "$BRIDGE_LOG"); }

# ---------------------------------------------------------------- a vault with something in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n%s\n' "$MASTER" "$SECRET" | "$CLI" add "$ENTRY" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"
project_var "$CLI" "$VAULT" "$MASTER" ci AUTO_KEY "$AUTO" || die "could not store ci's AUTO_KEY"
# ci-probe sees env/**, and ci is Automatic for it, as a person confirmed both at keypaste agent's terminal.
agent_settings "$CLI" "$VAULT" "$MASTER" 'ci-probe --see env/**' 'ci-probe --project ci --mode automatic' \
  || die "could not save ci-probe's settings in the vault"

# --------------------------------------------------------------------- the app unlocks and serves
exec 7>&-
exec 7> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" exec "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
FIRST="$(session_of)"
[ -n "$FIRST" ] || die "the app printed no session"

OUT="$WORK/served-stdout.txt"
ERR="$WORK/served-stderr.txt"
before_phase
ask "$OUT" "$ERR" 1

jq -e 'select(.id == 2) | .result.isError == false' <"$OUT" >/dev/null || die "the listing from the app's session was refused"
grep -q 'DEPLOY_KEY' "$OUT" || die "the listing did not come back from the app's vault"
jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null \
  || die "a credential was released although the person denied it"
grep -q "$SECRET" "$OUT" && die "a credential reached the client from the app's session"

app_lines | jq -e -s --arg s "$FIRST" 'length == 2
    and .[0].tool == "list_entry_names" and .[0].decision == "granted" and .[0].session == $s
    and .[1].tool == "request_credential" and .[1].decision == "denied" and .[1].method == "prompt" and .[1].session == $s' >/dev/null \
  || die "the app did not record the listing and the request once each, naming its session $FIRST: $(app_lines)"
[ -z "$(bridge_lines)" ] || die "the bridge recorded requests the app answered: $(bridge_lines)"

# ------------------------------------------------------------------- a second owner is refused
set +e
# Bounded, so an agent that wrongly starts is stopped and reported rather than listening forever.
printf '%s\n' "$MASTER" | timeout 30 "$CLI" agent --vault "$VAULT" >/dev/null 2>"$AGENT_ERR"
agent_exit=$?
set -e
[ "$agent_exit" -ne 0 ] || die "keypaste agent started on a vault the app holds"
grep -q "already unlocked in the keypaste desktop app (process $HOLD_PID)" "$AGENT_ERR" \
  || die "keypaste agent's refusal does not name the app holding the vault"
grep -q 'Master password' "$AGENT_ERR" && die "keypaste agent asked for the password of a vault the app holds"
grep -q 'listening on' "$AGENT_ERR" && die "keypaste agent listened on a vault the app holds"

set +e
second="$(KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" open "$VAULT" 2>&1)"
second_exit=$?
set -e
[ "$second_exit" -eq 1 ] || die "a second app opened the vault the first holds (exit $second_exit): $second"
printf '%s' "$second" | grep -q "already unlocked in the keypaste desktop app (process $HOLD_PID)" \
  || die "the second app's refusal does not name the app holding the vault: $second"

# ----------------------------------------------------------------------- locked means refused
echo lock >&7
wait_for '^locked' "$HOLD_OUT"

OUT="$WORK/locked-stdout.txt"
ERR="$WORK/locked-stderr.txt"
before_phase
ask "$OUT" "$ERR"

jq -e 'select(.id == 2) | .result.isError == true' <"$OUT" >/dev/null || die "a listing was answered while the app was locked"
jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null || die "a request was answered while the app was locked"
bridge_lines | jq -e -s 'length == 2 and all(.decision == "denied" and (has("session") | not))' >/dev/null \
  || die "the bridge did not record the two refusals while locked as denials reaching no session: $(bridge_lines)"
[ -z "$(app_lines)" ] || die "the locked app recorded requests: $(app_lines)"

# ---------------------------------------------------------- unlocking again is a new session
echo unlock >&7
wait_for 'holding session' "$HOLD_OUT" 2
SECOND="$(session_of)"
[ "$SECOND" != "$FIRST" ] || die "unlocking again reused session $FIRST"

OUT="$WORK/again-stdout.txt"
ERR="$WORK/again-stderr.txt"
before_phase
ask "$OUT" "$ERR" 2
app_lines | head -1 | jq -e --arg s "$SECOND" '.tool == "list_entry_names" and .decision == "granted" and .session == $s' >/dev/null \
  || die "the app's line for the listing after unlocking again does not name the new session $SECOND"

OUT="$WORK/automatic-stdout.txt"
ERR="$WORK/automatic-stderr.txt"
before_phase
ask_automatic "$OUT" "$ERR"
jq -e --arg v "$AUTO" 'select(.id == 2) | .result.isError == false and .result.structuredContent.value == $v' <"$OUT" >/dev/null \
  || die "the app did not release what ci-probe is Automatic for"
[ "$(grep -c '^prompt client' "$HOLD_OUT" || true)" -eq 2 ] || die "the app asked about a request ci-probe is Automatic for"
app_lines | tail -1 | jq -e --arg s "$SECOND" '.tool == "request_credential" and .decision == "granted" and .method == "automatic" and .args.field == "AUTO_KEY" and .session == $s' >/dev/null \
  || die "the app did not record the Automatic release as automatic in session $SECOND: $(app_lines)"
[ -z "$(bridge_lines)" ] || die "the bridge recorded a release the app answered: $(bridge_lines)"
grep -q "$AUTO" "$APP_LOG" && die "the app's log carries the Automatic release's value"

# ------------------------------------------------------------- a killed app holds nothing
kill_process "$HOLD_PID"
HOLD_PID=""
exec 7>&-

set +e
after="$(KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" open "$VAULT" 2>&1)"
after_exit=$?
set -e
[ "$after_exit" -eq 0 ] || die "the vault stayed held after the app holding it was killed: $after"

echo "ok: the app served its vault to a real keypaste mcp under a named session, refused while locked,"
echo "    refused keypaste agent and a second app by name before any password, and held nothing once killed"
