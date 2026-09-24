#!/usr/bin/env bash
# The app starts, holds and ends the session authority without a terminal (4.4b, V-4.4b), across
# real processes: the shipped keypaste and keypaste-mcp, and tests/Keypaste.AppDriver composing the
# app's AppAuthority as launch does and printing the status that authority reports.
#
# Launched with no terminal process, the app reports the vault locked and a request is refused;
# unlocking lets a real keypaste-mcp request reach the app's session. Locking and quitting each leave
# the next request refused and nobody holding the vault. An app killed with a request waiting leaves no
# endpoint answering, and a relaunch reports the vault locked, refuses until unlocked, and then serves
# a new session on the same endpoint. With keypaste agent holding the vault first, the app's unlock is
# refused naming it, the status names it as the owner, and the agent keeps answering.
#
# NEGATIVE CONTROL: this fails if a request is answered by a locked, quit or killed app, if a status
# says serving without the session that answers, if anything still holds the vault after a lock or a
# quit, or if the app displaces keypaste agent. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-lifecycle-master-pw'
readonly SECRET='SENTINEL-LIFECYCLE-PASSWORD-5c20d4'
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
HOLD_OUT="$WORK/hold-1.txt"
readonly AGENT_ERR="$WORK/agent-stderr.txt"

HOLD_PID=""
AGENT_PID=""
exec {HOLD_IN}>/dev/null
exec {MCP_IN}>/dev/null
exec {AGENT_IN}>/dev/null
cleanup() {
  exec {MCP_IN}>&- 2>/dev/null || true
  exec {HOLD_IN}>&- 2>/dev/null || true
  exec {AGENT_IN}>&- 2>/dev/null || true
  if [ -n "$HOLD_PID" ]; then kill_process "$HOLD_PID"; fi
  if [ -n "$AGENT_PID" ]; then kill_process "$AGENT_PID"; fi
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

# The app's current session, process and endpoint, from the latest status the driver printed.
session_of() { grep '^status serving' "$HOLD_OUT" | tail -1 | sed -E 's/^status serving ([0-9a-f]+).*/\1/'; }
process_of() { grep '^status serving' "$HOLD_OUT" | tail -1 | sed -E 's/.* as process ([0-9]+) .*/\1/'; }
endpoint_of() { grep '^status serving' "$HOLD_OUT" | tail -1 | sed -E 's/.* on (.*)$/\1/' | tr -d '\r'; }
last_status() { grep '^status ' "$HOLD_OUT" | tail -1 | tr -d '\r'; }

# Starts the app as launch does, with its input held open so lines can drive it.
launch() {
  HOLD_OUT="$1"
  shift
  exec {HOLD_IN}>&-
  exec {HOLD_IN}> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" hold "$VAULT" "$@" >"$HOLD_OUT" 2>&1)
}

# Waits for the app's status to say it serves, for the given time; sets SESSION and HOLD_PID.
expect_serving() {
  local count="$1"
  wait_for '^status serving' "$HOLD_OUT" "$count"
  grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
  SESSION="$(session_of)"
  HOLD_PID="$(process_of)"
  [ -n "$SESSION" ] || die "the app's status named no session"
}

# One bridge process: initialize, list, then ask for the credential.
ask() {
  OUT="$1"
  ERR="$2"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_entry_names","arguments":{}}}'
    sleep 2
    if [ "${3:-}" != "--list-only" ]; then
      printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci lifecycle probe\",\"ttl_seconds\":60}}}"
    fi
    sleep 3
  } | "$MCP" --vault "$VAULT" --audit-log "$AUDIT" --client-label ci-probe >"$OUT" 2>"$ERR" {HOLD_IN}>&- {AGENT_IN}>&- \
    || die "keypaste-mcp exited non-zero"
}

# Both calls of the last ask were refused, and audited as denials reaching no session.
refused_by_nobody() {
  local what="$1"
  jq -e 'select(.id == 2) | .result.isError == true' <"$OUT" >/dev/null || die "$what: a listing was answered"
  jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null || die "$what: a request was answered"
  grep -q "$SECRET" "$OUT" && die "$what: a credential reached the client"
  tail -n 2 "$AUDIT" | jq -e -s 'length == 2 and all(.decision == "denied" and (has("session") | not))' >/dev/null \
    || die "$what: the refusals were not audited as denials reaching no session"
}

# The last ask's listing came back from the vault, and its audit line names session $1.
listed_by() {
  local session="$1" what="$2"
  jq -e 'select(.id == 2) | .result.isError == false' <"$OUT" >/dev/null || die "$what: the listing was refused"
  grep -q 'DEPLOY_KEY' "$OUT" || die "$what: the listing did not come back from the vault"
  grep '"list_entry_names"' "$AUDIT" | tail -n 1 | jq -e --arg s "$session" '.decision == "granted" and .session == $s' >/dev/null \
    || die "$what: the listing's audit line does not name session $session"
}

# Nothing holds the vault: another app opens it and lets it go.
nobody_holds() {
  local what="$1" opened
  set +e
  opened="$(KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" open "$VAULT" 2>&1 </dev/null)"
  local code=$?
  set -e
  [ "$code" -eq 0 ] || die "$what: the vault was still held: $opened"
}

# Starts a bridge whose input stays open, so its request is still waiting when the app goes.
start_request() {
  OUT="$1"
  ERR="$2"
  exec {MCP_IN}>&-
  exec {MCP_IN}> >("$MCP" --vault "$VAULT" --audit-log "$AUDIT" --client-label ci-probe >"$OUT" 2>"$ERR" {HOLD_IN}>&- {AGENT_IN}>&-)
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}' >&"$MCP_IN"
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}' >&"$MCP_IN"
  printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci lifecycle probe\",\"ttl_seconds\":60}}}" >&"$MCP_IN"
}

# Waits for the waiting request's reply, which must be a denial with no value, then lets the bridge go.
finish_denied() {
  local what="$1"
  for _ in $(seq 1 150); do
    jq -e 'select(.id == 3)' <"$OUT" >/dev/null 2>&1 && break
    sleep 0.2
  done
  jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null || die "$what: the waiting request was answered"
  grep -q "$SECRET" "$OUT" && die "$what: a credential reached the client"
  tail -n 1 "$AUDIT" | jq -e '.tool == "request_credential" and .decision == "denied"' >/dev/null \
    || die "$what: the waiting request was not audited as a denial"
  exec {MCP_IN}>&-
}

# ---------------------------------------------------------------- a vault with something in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n' "$MASTER" | "$CLI" env set ci "DEPLOY_KEY=$SECRET" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"

# -------------------------------------------- launched with no terminal, unlocking serves agents
launch "$WORK/hold-1.txt" --locked --held-prompt
wait_for '^status locked' "$HOLD_OUT"
ask "$WORK/launch-stdout.txt" "$WORK/launch-stderr.txt"
refused_by_nobody "the app just launched"

echo unlock >&"$HOLD_IN"
expect_serving 1
FIRST="$SESSION"
ask "$WORK/first-stdout.txt" "$WORK/first-stderr.txt" --list-only
listed_by "$FIRST" "the first unlock"

# ------------------------------------------------------------- locking leaves nobody answering
echo lock >&"$HOLD_IN"
wait_for '^status locked' "$HOLD_OUT" 2
ask "$WORK/locked-stdout.txt" "$WORK/locked-stderr.txt"
refused_by_nobody "the app locked"
nobody_holds "after the lock"

# -------------------------------------------- quitting answers what waits and leaves no owner
echo unlock >&"$HOLD_IN"
expect_serving 2
SECOND="$SESSION"
[ "$SECOND" != "$FIRST" ] || die "unlocking again reused session $FIRST"

start_request "$WORK/quit-stdout.txt" "$WORK/quit-stderr.txt"
wait_for '^asking' "$HOLD_OUT"
exec {HOLD_IN}>&-
wait_for '^shut down' "$HOLD_OUT"
finish_denied "quitting"
tail -n 1 "$AUDIT" | jq -e --arg s "$SECOND" '.method == "vault-locked" and .session == $s' >/dev/null \
  || die "quitting: the waiting request was not answered as vault-locked by session $SECOND"
wait_for '^status locked' "$HOLD_OUT" 3
HOLD_PID=""

ask "$WORK/quit-after-stdout.txt" "$WORK/quit-after-stderr.txt"
refused_by_nobody "the app quit"
nobody_holds "after quitting"

# ------------------------------------ a killed app leaves nothing answering, and a relaunch takes over
launch "$WORK/hold-2.txt" --held-prompt
expect_serving 1
THIRD="$SESSION"
ENDPOINT="$(endpoint_of)"

start_request "$WORK/crash-stdout.txt" "$WORK/crash-stderr.txt"
wait_for '^asking' "$HOLD_OUT"
kill_process "$HOLD_PID"
HOLD_PID=""
exec {HOLD_IN}>&-
finish_denied "the killed app"

if ! command -v taskkill >/dev/null 2>&1; then
  # .NET puts a Unix named pipe at this path; a killed server leaves it behind.
  STALE="${TMPDIR:-/tmp}"
  STALE="${STALE%/}/CoreFxPipe_$ENDPOINT"
  [ -S "$STALE" ] || die "the killed app left no socket at $STALE, so the takeover below would prove nothing"
fi

ask "$WORK/crash-after-stdout.txt" "$WORK/crash-after-stderr.txt"
refused_by_nobody "the app was killed"

launch "$WORK/hold-3.txt" --locked
wait_for '^status locked' "$HOLD_OUT"
ask "$WORK/relaunch-stdout.txt" "$WORK/relaunch-stderr.txt"
refused_by_nobody "the app relaunched and not unlocked"

echo unlock >&"$HOLD_IN"
expect_serving 1
FOURTH="$SESSION"
[ "$FOURTH" != "$THIRD" ] || die "the relaunched app reused the killed app's session $THIRD"
[ "$(endpoint_of)" = "$ENDPOINT" ] || die "the relaunched app serves $(endpoint_of), not the vault's endpoint $ENDPOINT"
ask "$WORK/relaunched-stdout.txt" "$WORK/relaunched-stderr.txt" --list-only
listed_by "$FOURTH" "the relaunched app"

exec {HOLD_IN}>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

# ----------------------------------------- keypaste agent holding the vault is named, not displaced
exec {AGENT_IN}>&-
exec {AGENT_IN}> >("$CLI" agent --vault "$VAULT" >/dev/null 2>"$AGENT_ERR" {HOLD_IN}>&- {MCP_IN}>&-)
printf '%s\n' "$MASTER" >&"$AGENT_IN"
wait_for 'listening on' "$AGENT_ERR"
AGENT_SESSION="$(grep 'listening on' "$AGENT_ERR" | sed -E 's/.* for session ([0-9a-f]+),.*/\1/')"

HOLD_OUT="$WORK/hold-4.txt"
set +e
KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1 </dev/null
refused_exit=$?
set -e
[ "$refused_exit" -eq 1 ] || die "the app did not refuse a vault keypaste agent holds (exit $refused_exit)"
AGENT_PID="$(sed -nE 's/^refused: .*keypaste agent \(process ([0-9]+)\).*/\1/p' "$HOLD_OUT" | head -1)"
[ -n "$AGENT_PID" ] || die "the app's refusal does not name keypaste agent"
grep -q "^owner: Keypaste agent (process $AGENT_PID) holds this vault" "$HOLD_OUT" \
  || die "the unlock screen does not name keypaste agent as the owner"
[ "$(last_status)" = "status held by keypaste agent (process $AGENT_PID)" ] \
  || die "the app's status is '$(last_status)', not the agent as owner"
grep -q '^status serving' "$HOLD_OUT" && die "the app served a vault keypaste agent holds"

ask "$WORK/agent-stdout.txt" "$WORK/agent-mcp-stderr.txt" --list-only
listed_by "$AGENT_SESSION" "keypaste agent after the app's refusal"

echo "ok: launched locked and refusing, the app served a real keypaste-mcp once unlocked; lock and quit each left"
echo "    nobody answering or holding the vault; a killed app answered nothing and a relaunch refused until it"
echo "    unlocked a new session on the same endpoint; and keypaste agent holding the vault was named and kept answering"
