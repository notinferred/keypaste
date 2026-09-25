#!/usr/bin/env bash
# The app asks in its own prompt window (4.4, V-4.4), across real processes: the shipped keypaste and
# keypaste-mcp, and tests/Keypaste.AppDriver holding the vault as launch composes the app, with the
# app's prompt window drawn on a headless display and clicked through its hit-testing.
#
# A real keypaste-mcp request raises the prompt, which shows the client's label, the entry, the field and
# the lifetime. Approve returns the field, audited as a prompted grant naming the app's session and the
# label. Deny, closing the prompt, the client cancelling, the bridge going away, a lock and the gate's
# timeout each return a denial with the matching method and take the prompt down. Each case uses a new
# bridge, so no grant or cooldown carries from one to the next.
#
# NEGATIVE CONTROL: this fails if a request is released without a press of Approve, if Approve does not
# release it, if an audit line names the wrong method, session or label, or if a prompt stays up after
# its request is answered or abandoned. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-approval-master-pw'
readonly SECRET='SENTINEL-DESKTOP-APPROVAL-8b41e6'
readonly ENTRY='env/ci/DEPLOY_KEY'
readonly LABEL='ci-probe'

die() {
  echo "::error::$*" >&2
  for f in "${HOLD_OUT:-}" "${OUT:-}" "${ERR:-}"; do
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

PROMPTS=0

# Starts a bridge whose standard input stays open, asks for the credential, and waits for the prompt.
raise_prompt() {
  OUT="$WORK/$1-stdout.txt"
  ERR="$WORK/$1-stderr.txt"
  exec {MCP_IN}>&-
  # Without the driver's input, which it would otherwise inherit and hold open past quitting.
  exec {MCP_IN}> >(exec "$MCP" --vault "$VAULT" --audit-log "$AUDIT" --client-label "$LABEL" >"$OUT" 2>"$ERR" {HOLD_IN}>&-)
  BRIDGE_PID=$!
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}' >&"$MCP_IN"
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}' >&"$MCP_IN"
  printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci desktop approval probe\",\"ttl_seconds\":60}}}" >&"$MCP_IN"
  PROMPTS=$((PROMPTS + 1))
  wait_for '^prompt client' "$HOLD_OUT" "$PROMPTS"
}

# Waits up to $1 seconds for the request's reply, then lets the bridge go.
answered() {
  local seconds="$1"
  for _ in $(seq 1 $((seconds * 5))); do
    jq -e 'select(.id == 3)' <"$OUT" >/dev/null 2>&1 && break
    sleep 0.2
  done
  jq -e 'select(.id == 3)' <"$OUT" >/dev/null 2>&1 || die "the request got no reply"
  exec {MCP_IN}>&-
}

# The prompt that request raised has come down.
withdrawn() { wait_for '^prompt withdrawn' "$HOLD_OUT" "$PROMPTS"; }

# The request was refused with no value, and its audit line is a denial with method $1 in session $2.
denied_as() {
  local method="$1" session="$2" what="$3"
  jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null || die "$what: the request was answered"
  grep -q "$SECRET" "$OUT" && die "$what: a credential reached the client"
  tail -n 1 "$AUDIT" | jq -e --arg m "$method" --arg s "$session" --arg l "$LABEL" \
    '.tool == "request_credential" and .decision == "denied" and .method == $m and .session == $s and .client.label == $l' >/dev/null \
    || die "$what: the request was not audited as a $method denial in session $session"
}

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

# ------------------------------------------------------------- Approve releases, and says so
raise_prompt approve
grep '^prompt client' "$HOLD_OUT" | tail -1 \
  | grep -qF "label=$LABEL entry=$ENTRY field=password for=once, or for 1 hour" \
  || die "the prompt does not show the label, entry, field and what the person can allow"
echo approve >&"$HOLD_IN"
answered 30
withdrawn
jq -e 'select(.id == 3) | .result.isError == false' <"$OUT" >/dev/null || die "Approve did not release the credential"
grep -q "$SECRET" "$OUT" || die "the approved reply does not carry the credential"
tail -n 1 "$AUDIT" | jq -e --arg s "$FIRST" --arg l "$LABEL" \
  '.tool == "request_credential" and .decision == "granted" and .method == "prompt" and .session == $s and .client.label == $l' >/dev/null \
  || die "the approval was not audited as a prompted grant in session $FIRST under label $LABEL"
grep -q "$SECRET" "$AUDIT" && die "the credential reached the audit log"
grep -q "$SECRET" "$HOLD_OUT" && die "the credential reached the app's output"

# ---------------------------------------------------------------- Deny and closing refuse
raise_prompt deny
echo deny >&"$HOLD_IN"
answered 30
withdrawn
denied_as prompt "$FIRST" "Deny"

raise_prompt close
echo close >&"$HOLD_IN"
answered 30
withdrawn
denied_as prompt "$FIRST" "closing the prompt"

# ------------------------------------------------ the client giving up takes the prompt down
raise_prompt cancelled
printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":3,"reason":"ci probe gave up"}}' >&"$MCP_IN"
withdrawn
exec {MCP_IN}>&-
for _ in $(seq 1 50); do
  tail -n 1 "$AUDIT" | jq -e '.method == "cancelled"' >/dev/null 2>&1 && break
  sleep 0.2
done
tail -n 1 "$AUDIT" | jq -e --arg l "$LABEL" \
  '.tool == "request_credential" and .decision == "denied" and .method == "cancelled" and .client.label == $l' >/dev/null \
  || die "the cancelled request was not audited as a cancelled denial"
grep -q "$SECRET" "$OUT" && die "a cancelled request reached the client"

# A bridge whose standard input closes waits for its call to finish, so going away is being killed.
raise_prompt gone
if [ -r "/proc/$BRIDGE_PID/winpid" ]; then
  kill_process "$(cat "/proc/$BRIDGE_PID/winpid")"
else
  kill -9 "$BRIDGE_PID"
fi
withdrawn
exec {MCP_IN}>&-
grep -q "$SECRET" "$OUT" && die "a request whose bridge went away reached the client"

# -------------------------------------------------------------------- a lock refuses and withdraws
raise_prompt lock
echo lock >&"$HOLD_IN"
wait_for '^locked' "$HOLD_OUT"
answered 30
withdrawn
denied_as vault-locked "$FIRST" "the lock"

# ------------------------------------------------ nobody answering is a denial when the window closes
echo unlock >&"$HOLD_IN"
wait_for 'holding session' "$HOLD_OUT" 2
SECOND="$(session_of)"
[ "$SECOND" != "$FIRST" ] || die "unlocking again reused session $FIRST"

raise_prompt timeout
answered 60
withdrawn
denied_as timed-out "$SECOND" "the timeout"

exec {HOLD_IN}>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

echo "ok: the app's prompt window showed a real keypaste-mcp request with its label, released it only on Approve,"
echo "    and denied and withdrew it on Deny, closing, the client cancelling, the bridge going, a lock and the timeout"
