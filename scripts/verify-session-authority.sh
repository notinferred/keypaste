#!/usr/bin/env bash
# The desktop owns the vault it unlocks (U.1, V-U.1), across real processes: the shipped keypaste and
# keypaste-mcp, and tests/Keypaste.AppDriver unlocking through the app's unlock screen and serving the
# vault as the app does.
#
# A real keypaste-mcp request reaches the app's session, is answered from its vault, and its audit line
# names that session. With the app locked the request is refused and audited. `keypaste agent` and a
# second app on the same vault are each refused with a message naming the app, before any password is
# asked for. Unlocking again starts a new session. A killed app leaves nothing holding the vault. The
# stale-session and unattached cases run over a real pipe in SessionAuthorityTests, because the shipped
# bridge always attaches.
#
# NEGATIVE CONTROL: this fails if the listing does not come back from the app's vault, if its audit line
# names no session or the wrong one, if a request is answered while the app is locked, or if either
# second owner opens the vault. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-session-master-pw'
readonly SECRET='SENTINEL-SESSION-PASSWORD-3e8a71'
readonly ENTRY='env/ci/DEPLOY_KEY'

die() {
  echo "::error::$*" >&2
  for f in "${HOLD_OUT:-}" "${OUT:-}" "${ERR:-}" "${AGENT_ERR:-}"; do
    if [ -n "$f" ] && [ -f "$f" ]; then echo "--- $f ---" >&2; cat "$f" >&2; fi
  done
  exit 1
}

command -v jq >/dev/null 2>&1 || die "jq is required and was not found; this gate must never be skipped"

resolve() {
  local candidate="$1"
  [ -x "$candidate" ] || candidate="${candidate}.exe"
  [ -x "$candidate" ] || die "not found: $1 (build first)"
  printf '%s' "$candidate"
}

native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

CLI="$(resolve "${KEYPASTE_BIN:-artifacts/bin/Keypaste.Cli/release/keypaste}")"
MCP="$(resolve "${KEYPASTE_MCP_BIN:-artifacts/bin/Keypaste.Mcp/release/keypaste-mcp}")"
DRV="$(resolve "${KEYPASTE_APP_DRIVER:-artifacts/bin/Keypaste.AppDriver/release/Keypaste.AppDriver}")"

WORK="$(mktemp -d)"
mkdir -p "$WORK/home"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
VAULT="$(native "$WORK/vault.kdbx")"
AUDIT="$(native "$WORK/audit.jsonl")"
readonly HOLD_OUT="$WORK/hold.txt"
readonly AGENT_ERR="$WORK/agent-stderr.txt"

HOLD_PID=""
exec {HOLD_IN}>/dev/null
cleanup() {
  exec {HOLD_IN}>&- 2>/dev/null || true
  if [ -n "$HOLD_PID" ]; then kill_process "$HOLD_PID"; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

kill_process() {
  if command -v taskkill >/dev/null 2>&1; then
    taskkill //F //PID "$1" >/dev/null 2>&1 || true
  else
    kill -9 "$1" 2>/dev/null || true
  fi
}

wait_for() {
  local pattern="$1" file="$2" count="${3:-1}"
  for _ in $(seq 1 150); do
    [ "$(grep -c -- "$pattern" "$file" 2>/dev/null || true)" -ge "$count" ] && return 0
    sleep 0.2
  done
  die "timed out waiting for '$pattern' in $file"
}

# The app's current session and process, from the latest line the driver printed.
session_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/^holding session ([0-9a-f]+).*/\1/'; }
process_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/.* as process ([0-9]+).*/\1/'; }

# One bridge process: initialize, list, then ask for the credential.
ask() {
  local out="$1" err="$2"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_entry_names","arguments":{}}}'
    sleep 2
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci session probe\",\"ttl_seconds\":60}}}"
    sleep 3
  } | "$MCP" --vault "$VAULT" --audit-log "$AUDIT" --client-label ci-probe >"$out" 2>"$err" \
    || die "keypaste-mcp exited non-zero"
}

# The last two audit lines: the listing, then the request.
last_lines() { tail -n 2 "$AUDIT"; }

# ---------------------------------------------------------------- a vault with something in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n' "$MASTER" | "$CLI" env set ci "DEPLOY_KEY=$SECRET" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"

# --------------------------------------------------------------------- the app unlocks and serves
exec {HOLD_IN}>&-
exec {HOLD_IN}> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
FIRST="$(session_of)"
[ -n "$FIRST" ] || die "the app printed no session"

OUT="$WORK/served-stdout.txt"
ERR="$WORK/served-stderr.txt"
ask "$OUT" "$ERR"

jq -e 'select(.id == 2) | .result.isError == false' <"$OUT" >/dev/null || die "the listing from the app's session was refused"
grep -q 'DEPLOY_KEY' "$OUT" || die "the listing did not come back from the app's vault"
jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null \
  || die "a credential was released although the app cannot yet approve"
grep -q "$SECRET" "$OUT" && die "a credential reached the client from the app's session"

last_lines | head -1 | jq -e --arg s "$FIRST" '.tool == "list_entry_names" and .decision == "granted" and .session == $s' >/dev/null \
  || die "the listing's audit line does not name the app's session $FIRST"
last_lines | tail -1 | jq -e --arg s "$FIRST" '.tool == "request_credential" and .decision == "denied" and .session == $s' >/dev/null \
  || die "the request's audit line does not name the app's session $FIRST"

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
echo lock >&"$HOLD_IN"
wait_for '^locked' "$HOLD_OUT"

OUT="$WORK/locked-stdout.txt"
ERR="$WORK/locked-stderr.txt"
ask "$OUT" "$ERR"

jq -e 'select(.id == 2) | .result.isError == true' <"$OUT" >/dev/null || die "a listing was answered while the app was locked"
jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null || die "a request was answered while the app was locked"
last_lines | jq -e -s 'all(.decision == "denied" and (has("session") | not))' >/dev/null \
  || die "the refusals while locked were not audited as denials reaching no session"

# ---------------------------------------------------------- unlocking again is a new session
echo unlock >&"$HOLD_IN"
wait_for 'holding session' "$HOLD_OUT" 2
SECOND="$(session_of)"
[ "$SECOND" != "$FIRST" ] || die "unlocking again reused session $FIRST"

OUT="$WORK/again-stdout.txt"
ERR="$WORK/again-stderr.txt"
ask "$OUT" "$ERR"
last_lines | head -1 | jq -e --arg s "$SECOND" '.decision == "granted" and .session == $s' >/dev/null \
  || die "the listing after unlocking again does not name the new session $SECOND"

# ------------------------------------------------------------- a killed app holds nothing
kill_process "$HOLD_PID"
HOLD_PID=""
exec {HOLD_IN}>&-

set +e
after="$(KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" open "$VAULT" 2>&1)"
after_exit=$?
set -e
[ "$after_exit" -eq 0 ] || die "the vault stayed held after the app holding it was killed: $after"

echo "ok: the app served its vault to a real keypaste-mcp under a named session, refused while locked,"
echo "    refused keypaste agent and a second app by name before any password, and held nothing once killed"
