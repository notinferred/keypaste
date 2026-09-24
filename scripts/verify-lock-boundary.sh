#!/usr/bin/env bash
# A lock is one transition across real processes (U.2, V-U.2): the shipped keypaste and keypaste-mcp,
# and tests/Keypaste.AppDriver holding the vault as the app does, with --held-prompt putting a request
# that needs a person in front of one who never answers.
#
# A real keypaste-mcp request waits at the app's session; a manual lock answers it as a vault-locked
# denial, and its audit line names the session it reached. Unlocking again starts a session where the
# same request waits again rather than being answered from anything before the lock. Quitting the app
# with a request waiting answers it the same way. Where a signal can reach a native process, a request
# waiting at `keypaste agent`'s prompt is answered the same way when the agent is sent SIGTERM. Idle
# and an expired sleep take the same transition; they run over a real pipe with a manual clock in
# LockBoundaryTests, because this gate cannot wait out an idle timeout or suspend the machine.
#
# NEGATIVE CONTROL: this fails if a waiting request is released, dropped without a reply, or audited as
# anything but a vault-locked denial of the session it reached, or if a later unlock answers it from
# before the lock. These checks must never be skipped or soft-passed; only the agent's signal step is
# skipped on Windows, and it says so.
set -euo pipefail

readonly MASTER='ci-lock-master-pw'
readonly SECRET='SENTINEL-LOCK-PASSWORD-7c2d95'
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
AGENT_PID=""
exec {HOLD_IN}>/dev/null
exec {MCP_IN}>/dev/null
cleanup() {
  exec {MCP_IN}>&- 2>/dev/null || true
  exec {HOLD_IN}>&- 2>/dev/null || true
  if [ -n "$HOLD_PID" ]; then kill_process "$HOLD_PID"; fi
  if [ -n "$AGENT_PID" ]; then kill -9 "$AGENT_PID" 2>/dev/null || true; fi
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

# Starts a bridge whose standard input stays open, so its request is still waiting when the lock comes.
start_request() {
  OUT="$1"
  ERR="$2"
  exec {MCP_IN}>&-
  # Without the driver's input, which it would otherwise inherit and hold open past quitting.
  exec {MCP_IN}> >("$MCP" --vault "$VAULT" --audit-log "$AUDIT" --client-label ci-probe >"$OUT" 2>"$ERR" {HOLD_IN}>&-)
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}' >&"$MCP_IN"
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}' >&"$MCP_IN"
  printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci lock probe\",\"ttl_seconds\":60}}}" >&"$MCP_IN"
}

# Waits for the request's reply, then lets the bridge go.
finish_request() {
  for _ in $(seq 1 150); do
    jq -e 'select(.id == 3)' <"$OUT" >/dev/null 2>&1 && break
    sleep 0.2
  done
  jq -e 'select(.id == 3)' <"$OUT" >/dev/null 2>&1 || die "the waiting request got no reply"
  exec {MCP_IN}>&-
}

# The request was refused with no value, and its audit line is a vault-locked denial of $1.
denied_as_locked() {
  local session="$1" what="$2"
  jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null || die "$what: the waiting request was answered"
  grep -q "$SECRET" "$OUT" && die "$what: a credential reached the client"
  tail -n 1 "$AUDIT" | jq -e --arg s "$session" \
    '.tool == "request_credential" and .decision == "denied" and .method == "vault-locked" and .session == $s' >/dev/null \
    || die "$what: the waiting request was not audited as a vault-locked denial of session $session"
}

# ---------------------------------------------------------------- a vault with something in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n' "$MASTER" | "$CLI" env set ci "DEPLOY_KEY=$SECRET" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"

# ---------------------------------------------------- a manual lock denies the waiting request
exec {HOLD_IN}>&-
exec {HOLD_IN}> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" hold "$VAULT" --held-prompt >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
FIRST="$(session_of)"

start_request "$WORK/manual-stdout.txt" "$WORK/manual-stderr.txt"
wait_for '^asking' "$HOLD_OUT"
echo lock >&"$HOLD_IN"
wait_for '^locked' "$HOLD_OUT"
finish_request
grep -q '^withdrawn' "$HOLD_OUT" || die "the manual lock did not withdraw the waiting prompt"
denied_as_locked "$FIRST" "manual lock"

# ------------------------------- a new unlock asks again, and quitting denies what is waiting
echo unlock >&"$HOLD_IN"
wait_for 'holding session' "$HOLD_OUT" 2
SECOND="$(session_of)"
[ "$SECOND" != "$FIRST" ] || die "unlocking again reused session $FIRST"

start_request "$WORK/quit-stdout.txt" "$WORK/quit-stderr.txt"
wait_for '^asking' "$HOLD_OUT" 2
exec {HOLD_IN}>&-
wait_for '^shut down' "$HOLD_OUT"
finish_request
wait_for '^withdrawn' "$HOLD_OUT" 2
denied_as_locked "$SECOND" "quitting"
HOLD_PID=""

# ------------------------------------------- stopping keypaste agent denies what is waiting
if command -v taskkill >/dev/null 2>&1; then
  echo "skip: keypaste agent's SIGTERM step, because Git Bash cannot deliver a signal to a native Windows process; CI runs it on Linux"
else
  mkfifo "$WORK/agent.in"
  "$CLI" agent --vault "$VAULT" <"$WORK/agent.in" >/dev/null 2>"$AGENT_ERR" &
  AGENT_PID=$!
  exec {AGENT_IN}>"$WORK/agent.in"
  printf '%s\n' "$MASTER" >&"$AGENT_IN"
  wait_for 'listening on' "$AGENT_ERR"
  AGENT_SESSION="$(grep 'listening on' "$AGENT_ERR" | sed -E 's/.* for session ([0-9a-f]+),.*/\1/')"

  start_request "$WORK/agent-stdout.txt" "$WORK/agent-mcp-stderr.txt"
  wait_for 'an agent is asking for a credential' "$AGENT_ERR"
  kill -TERM "$AGENT_PID"
  set +e
  wait "$AGENT_PID"
  agent_exit=$?
  set -e
  AGENT_PID=""
  exec {AGENT_IN}>&-
  finish_request
  [ "$agent_exit" -eq 0 ] || die "keypaste agent did not stop cleanly on SIGTERM (exit $agent_exit)"
  grep -q 'withdrawn before you answered' "$AGENT_ERR" || die "SIGTERM did not withdraw the agent's prompt"
  grep -q 'every grant is gone' "$AGENT_ERR" || die "keypaste agent did not report that it locked"
  denied_as_locked "$AGENT_SESSION" "stopping keypaste agent"
  echo "ok: SIGTERM withdrew the request waiting at keypaste agent's prompt and it was audited as vault-locked"
fi

echo "ok: a request waiting at the app's session was denied as vault-locked by a manual lock and by quitting,"
echo "    and a new unlock asked again rather than answering from before the lock"
