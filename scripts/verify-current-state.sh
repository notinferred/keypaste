#!/usr/bin/env bash
# The owning session answers from the vault as last saved (U.3, V-U.3): the shipped keypaste and
# keypaste-mcp, and tests/Keypaste.AppDriver holding the vault as the app does, with --approving-prompt
# standing in for a person who approves and edit/relocate acting through the app's entries screen.
#
# One bridge keeps one connection, so a grant it was given can be reused. A password edited in the app
# is the next value released and is asked about again; an entry moved out of the exposure is refused,
# and moving it back asks again rather than serving the old grant. A save by the CLI between two
# requests makes the second refuse as vault-changed, the listing too, the file keeps the CLI's bytes
# even when the app then tries to save, and nothing is released until the app is unlocked again.
# Deletes, group renames and access changes run over a real pipe in CurrentStateTests.
#
# NEGATIVE CONTROL: this fails if an old value is released after an edit, a grant outlives the change
# to its entry, anything is released or listed after an external save before a re-unlock, or the app
# writes over the external save. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-current-master-pw'
readonly V1='SENTINEL-CURRENT-ONE-41d8'
readonly V2='SENTINEL-CURRENT-TWO-9c03'
readonly V3='SENTINEL-CURRENT-THREE-e7b5'
readonly ENTRY='env/ci/DEPLOY_KEY'

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
readonly OUT="$WORK/mcp-stdout.txt"
readonly ERR="$WORK/mcp-stderr.txt"

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
digest() { sha256sum "$WORK/vault.kdbx" | cut -d' ' -f1; }

# Does one act in the held app and prints the line the driver answered it with.
act() {
  local before
  before="$(wc -l <"$HOLD_OUT")"
  echo "$1" >&"$HOLD_IN"
  for _ in $(seq 1 150); do
    [ "$(wc -l <"$HOLD_OUT")" -gt "$before" ] && { tail -n 1 "$HOLD_OUT"; return 0; }
    sleep 0.2
  done
  die "the app never answered '$1'"
}

# Sends one tool call on the open bridge and waits for its reply.
call() {
  local id="$1" tool="$2" arguments="$3"
  printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"$tool\",\"arguments\":$arguments}}" >&"$MCP_IN"
  for _ in $(seq 1 150); do
    jq -e --argjson id "$id" 'select(.id == $id)' <"$OUT" >/dev/null 2>&1 && return 0
    sleep 0.2
  done
  die "call $id got no reply"
}

request() { call "$1" request_credential "{\"entry\":\"$2\",\"field\":\"password\",\"reason\":\"ci current-state probe\",\"ttl_seconds\":300}"; }

# The reply to $1 released $2 and nothing else, and its audit line says how, naming the session.
released() {
  local id="$1" value="$2" method="$3" what="$4"
  jq -e --argjson id "$id" --arg v "$value" 'select(.id == $id) | .result.isError == false and (tostring | contains($v))' <"$OUT" >/dev/null \
    || die "$what: $value was not released"
  for other in "$V1" "$V2" "$V3"; do
    [ "$other" = "$value" ] && continue
    jq -e --argjson id "$id" --arg v "$other" 'select(.id == $id) | tostring | contains($v)' <"$OUT" >/dev/null \
      && die "$what: $other was released instead"
  done
  tail -n 1 "$AUDIT" | jq -e --arg m "$method" --arg s "$(session_of)" \
    '.tool == "request_credential" and .decision == "granted" and .method == $m and .session == $s' >/dev/null \
    || die "$what: the release was not audited as $method by session $(session_of)"
}

# The reply to $1 released nothing, and its audit line is a denial by $2.
denied() {
  local id="$1" method="$2" what="$3" tool="${4:-request_credential}"
  jq -e --argjson id "$id" 'select(.id == $id) | .result.isError == true' <"$OUT" >/dev/null || die "$what: it was answered"
  for value in "$V1" "$V2" "$V3"; do
    jq -e --argjson id "$id" --arg v "$value" 'select(.id == $id) | tostring | contains($v)' <"$OUT" >/dev/null \
      && die "$what: a credential reached the client"
  done
  tail -n 1 "$AUDIT" | jq -e --arg t "$tool" --arg m "$method" '.tool == $t and .decision == "denied" and .method == $m' >/dev/null \
    || die "$what: it was not audited as a $method denial"
}

asked() { [ "$(grep -c '^approved' "$HOLD_OUT")" -eq "$1" ] || die "$2: a person was asked $(grep -c '^approved' "$HOLD_OUT") times, not $1"; }

# ---------------------------------------------------------------- a vault with something in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n' "$MASTER" | "$CLI" env set ci "DEPLOY_KEY=$V1" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"
printf '%s\n' "$MASTER" | "$CLI" add KEEP --group personal --generate --vault "$VAULT" >/dev/null \
  || die "could not make a group outside the exposure"

# --------------------------------------------------------------- the app holds it; a bridge attaches
exec {HOLD_IN}>&-
exec {HOLD_IN}> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" KEYPASTE_DRIVER_NEW_PASSWORD="$V2" \
  "$DRV" hold "$VAULT" --approving-prompt >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
FIRST="$(session_of)"

exec {MCP_IN}>&-
exec {MCP_IN}> >("$MCP" --vault "$VAULT" --audit-log "$AUDIT" --client-label ci-probe >"$OUT" 2>"$ERR" {HOLD_IN}>&-)
printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}' >&"$MCP_IN"
printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}' >&"$MCP_IN"

request 10 "$ENTRY"
released 10 "$V1" prompt "the first request"
request 11 "$ENTRY"
released 11 "$V1" grant-cache "the repeat request"
asked 1 "the repeat request"

# ------------------------------------------------ an edit in the app is the next value, asked again
line="$(act "edit $ENTRY")"
case "$line" in refused:*) die "the app refused the edit: $line" ;; esac
request 12 "$ENTRY"
released 12 "$V2" prompt "the request after the app's edit"
asked 2 "the request after the app's edit"

# ------------------------------- a move out of the exposure refuses; moving back asks again
line="$(act "relocate $ENTRY personal DEPLOY_KEY")"
case "$line" in refused:*) die "the app refused the move: $line" ;; esac
request 13 "$ENTRY"
denied 13 out-of-scope "the request after the entry moved away"
line="$(act "relocate personal/DEPLOY_KEY env/ci DEPLOY_KEY")"
case "$line" in refused:*) die "the app refused the move back: $line" ;; esac
request 14 "$ENTRY"
released 14 "$V2" prompt "the request after the entry moved back"
asked 3 "the request after the entry moved back"

# ------------------- another program saves: refused, the file kept, and nothing until a re-unlock
printf '%s\n' "$MASTER" | "$CLI" env set ci "DEPLOY_KEY=$V3" --vault "$VAULT" >/dev/null \
  || die "the CLI could not save the vault the app holds"
EXTERNAL="$(digest)"

request 15 "$ENTRY"
denied 15 vault-changed "the request after the CLI saved"
tail -n 1 "$AUDIT" | jq -e --arg s "$FIRST" '.session == $s' >/dev/null || die "the refusal does not name session $FIRST"
tail -n 1 "$AUDIT" | jq -e '.reason | contains("another program changed the vault file")' >/dev/null \
  || die "the refusal's audit line does not say the file changed"
call 16 list_entry_names '{}'
denied 16 vault-changed "the listing after the CLI saved" list_entry_names

line="$(act "edit $ENTRY")"
case "$line" in refused:*) ;; *) die "the app saved over the CLI's write: $line" ;; esac
[ "$(digest)" = "$EXTERNAL" ] || die "the vault file no longer holds the CLI's bytes"
request 17 "$ENTRY"
denied 17 vault-changed "the request after the app's refused save"
asked 3 "the requests after the CLI saved"

act lock >/dev/null
act unlock >/dev/null
wait_for 'holding session' "$HOLD_OUT" 2
[ "$(session_of)" != "$FIRST" ] || die "unlocking again reused session $FIRST"

request 18 "$ENTRY"
released 18 "$V3" prompt "the request after unlocking again"
asked 4 "the request after unlocking again"
[ "$(digest)" = "$EXTERNAL" ] || die "the vault file changed without anyone saving it"

exec {MCP_IN}>&-
exec {HOLD_IN}>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

echo "ok: an edit in the app was the next value released and was asked about again, a moved entry's grant"
echo "    was gone, and a CLI save was refused as vault-changed with its bytes kept until the app unlocked again"
