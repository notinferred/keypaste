#!/usr/bin/env bash
# Agent Activity shows what the app's session holds and what this session answered (4.3b, V-4.3b), across
# real processes: the shipped keypaste and keypaste-mcp, and tests/Keypaste.AppDriver holding the vault as
# launch composes the app, with its prompt window drawn on a headless display and clicked. The driver's
# `activity` prints what the screen's own view model reads from the authority and the audit file, and its
# `revoke` presses the screen's buttons.
#
# A real keypaste-mcp request waiting at the unlocked app is listed. After Approve its grant is listed and
# the history shows the prompted grant from the audit file the bridge wrote. Revoke and Revoke all make the
# same request ask again. A lock empties both lists, and the next session starts with nothing listed. A
# missing audit log reads as a session with no records yet, and an unreadable one is shown as unavailable.
#
# NEGATIVE CONTROL: this fails if a waiting request or a grant is not listed, if a revoked grant still
# answers, if history is missing the bridge's record or is shown for a log that cannot be read, or if a
# credential reaches the driver's output. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-activity-master-pw'
readonly SECRET='SENTINEL-AGENT-ACTIVITY-5c02d9'
readonly ENTRY='env/ci/DEPLOY_KEY'
readonly LABEL='ci-probe'

die() {
  echo "::error::$*" >&2
  for f in "${HOLD_OUT:-}" "${SHOWN:-}" "${OUT:-}" "${ERR:-}"; do
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
# Where the bridge writes by default and the app reads: the gate passes no --audit-log.
readonly AUDIT="$WORK/home/audit.jsonl"
readonly HOLD_OUT="$WORK/hold.txt"

HOLD_PID=""
exec {HOLD_IN}>/dev/null
exec {MCP_IN}>/dev/null
cleanup() {
  exec {MCP_IN}>&- 2>/dev/null || true
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

session_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/^holding session ([0-9a-f]+).*/\1/'; }
process_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/.* as process ([0-9]+).*/\1/'; }
prompts() { grep -c '^prompt client' "$HOLD_OUT" || true; }

# Starts a bridge whose standard input stays open until the next one starts.
start_bridge() {
  OUT="$WORK/$1-stdout.txt"
  ERR="$WORK/$1-stderr.txt"
  exec {MCP_IN}>&-
  # Without the driver's input, which it would otherwise inherit and hold open past quitting.
  exec {MCP_IN}> >(exec "$MCP" --vault "$VAULT" --client-label "$LABEL" >"$OUT" 2>"$ERR" {HOLD_IN}>&-)
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}' >&"$MCP_IN"
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}' >&"$MCP_IN"
}

ask() {
  printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$1,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci agent activity probe\",\"ttl_seconds\":60}}}" >&"$MCP_IN"
}

reply() {
  for _ in $(seq 1 150); do
    jq -e --argjson id "$1" 'select(.id == $id)' <"$OUT" >/dev/null 2>&1 && return 0
    sleep 0.2
  done
  die "request $1 got no reply"
}

released() {
  jq -e --argjson id "$1" 'select(.id == $id) | .result.isError == false' <"$OUT" >/dev/null || die "request $1 was not released"
}

refused() {
  jq -e --argjson id "$1" 'select(.id == $id) | .result.isError == true' <"$OUT" >/dev/null || die "request $1 was released"
}

last_audit() { tail -n 1 "$AUDIT" | jq -e "$@" >/dev/null; }

ACTIVITIES=0

# Asks the driver what Agent Activity shows, and keeps this reading's lines in $SHOWN.
activity() {
  ACTIVITIES=$((ACTIVITIES + 1))
  echo activity >&"$HOLD_IN"
  wait_for '^activity end' "$HOLD_OUT" "$ACTIVITIES"
  SHOWN="$WORK/activity-$ACTIVITIES.txt"
  awk -v n="$ACTIVITIES" '/^activity end/ { seen++; next } seen == n - 1 && /^(unavailable|waiting|grant|history)/' "$HOLD_OUT" >"$SHOWN"
}

shows() { grep -qE -- "$1" "$SHOWN" || die "Agent Activity does not show: $2"; }
omits() { grep -qE -- "$1" "$SHOWN" && die "Agent Activity shows: $2"; return 0; }

REVOKES=0
revoke() {
  REVOKES=$((REVOKES + 1))
  echo "revoke $1" >&"$HOLD_IN"
  wait_for '^revoked ' "$HOLD_OUT" "$REVOKES"
}

readonly LISTED="^grant n=1 client=ci-probe label=$LABEL entry=$ENTRY field=password left=ends in [0-9]+ s$"

# ---------------------------------------------------------------- a vault with something in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n' "$MASTER" | "$CLI" env set ci "DEPLOY_KEY=$SECRET" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"

exec {HOLD_IN}>&-
exec {HOLD_IN}> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
FIRST="$(session_of)"

# ------------------------------------------------ before any bridge has run there is no log to read
[ -e "$AUDIT" ] && die "an audit log exists before any bridge ran"
activity
omits '^unavailable ' "the authority as unreadable while it serves"
omits '^(waiting|grant) ' "a request or grant before any agent asked"
shows '^history-message The audit log has no records from this session yet\.' "a missing log as a session with no records yet"
omits '^history-message History unavailable' "a missing log as unavailable"

# ------------------------------------------ the waiting request, then its grant and its audit record
start_bridge first
ask 3
wait_for '^prompt client' "$HOLD_OUT" 1
activity
shows "^waiting n=1 client=ci-probe label=$LABEL entry=$ENTRY field=password left=answered for you in [0-9]+ s$" "the waiting request"
omits '^grant ' "a grant before Approve"

echo approve >&"$HOLD_IN"
reply 3
released 3
grep -q "$SECRET" "$OUT" || die "the approved reply does not carry the credential"
last_audit --arg s "$FIRST" '.decision == "granted" and .method == "prompt" and .session == $s'

activity
omits '^waiting ' "a request after it was answered"
shows "$LISTED" "the grant Approve gave"
shows "^history-heading 1 record of [0-9]+ in .*, this session$" "history kept to this session"
shows "^history .*DEPLOY_KEY.*granted.*prompt" "the prompted grant from the audit file"

ask 4
reply 4
released 4
last_audit '.method == "grant-cache"'
[ "$(prompts)" -eq 1 ] || die "a request inside a live grant raised a prompt"

# ------------------------------------------------------ Revoke all, then Revoke one, each ask again
revoke all
activity
omits '^grant ' "a grant after Revoke all"

ask 5
wait_for '^prompt client' "$HOLD_OUT" 2
echo approve >&"$HOLD_IN"
reply 5
released 5
last_audit '.method == "prompt" and .decision == "granted"'
activity
shows "$LISTED" "the grant after asking again"

revoke 1
activity
omits '^grant ' "a grant after Revoke"

ask 6
wait_for '^prompt client' "$HOLD_OUT" 3
echo deny >&"$HOLD_IN"
reply 6
refused 6
activity
shows "^history .*DEPLOY_KEY.*denied.*prompt" "the refusal from the audit file"
shows "^history-heading 4 records of [0-9]+ in .*, this session$" "every record of this session"

# ------------------------------------------------ a lock empties both lists; the next session has none
start_bridge second
ask 3
wait_for '^prompt client' "$HOLD_OUT" 4
activity
shows '^waiting n=1 ' "the request waiting before the lock"

echo lock >&"$HOLD_IN"
wait_for '^locked' "$HOLD_OUT"
reply 3
refused 3
last_audit --arg s "$FIRST" '.method == "vault-locked" and .session == $s'
activity
shows '^unavailable ' "a locked app as unable to read requests and grants"
omits '^(waiting|grant) ' "a request or grant after the lock"

echo unlock >&"$HOLD_IN"
wait_for 'holding session' "$HOLD_OUT" 2
SECOND="$(session_of)"
[ "$SECOND" != "$FIRST" ] || die "unlocking again reused session $FIRST"
activity
omits '^unavailable ' "the new session as unreadable"
omits '^(waiting|grant) ' "a request or grant from before the lock"
shows '^history-message The audit log has no records from this session yet\.' "the new session's history"
omits '^history(-heading)? ' "a record from before the lock in the new session's history"

# ----------------------------------------------------------------- an unreadable log is unavailable
exec {MCP_IN}>&-
for _ in $(seq 1 50); do rm -f "$AUDIT" 2>/dev/null && break; sleep 0.2; done
[ -e "$AUDIT" ] && die "could not remove the audit log"
mkdir "$AUDIT"
activity
shows "^history-message History unavailable: the audit log couldn't be read" "an unreadable log as unavailable"
omits '^history ' "history from a log that could not be read"

grep -q "$SECRET" "$HOLD_OUT" && die "a credential reached the app's output"

exec {HOLD_IN}>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

echo "ok: Agent Activity listed a real keypaste-mcp request waiting at the app and the grant Approve gave,"
echo "    showed this session's audit records, made revoked grants ask again, held nothing after a lock,"
echo "    read a missing log as no records yet and said an unreadable log was unavailable"
