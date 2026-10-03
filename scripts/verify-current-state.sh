#!/usr/bin/env bash
# The owning session answers from the vault as last saved (U.3, V-U.3): the shipped keypaste and
# keypaste-mcp, and tests/Keypaste.AppDriver holding the vault as the app does, with --approving-prompt
# standing in for a person who approves and edit/relocate acting through the app's entries screen.
#
# One bridge keeps one connection, so a grant it was given can be reused. A password edited in the app
# is the next value released and is asked about again; an entry moved out of the exposure is refused,
# and moving it back asks again rather than serving the old grant. A save by another program between
# two requests makes the second refuse as vault-changed, the listing too, the file keeps that program's
# bytes even when the app then tries to save, the app's shell says so with Reload, and nothing is released
# until a person reloads, which answers in the same session and asks again (N.16, D-0412).
# The other program is the CLI under another KEYPASTE_HOME, which does not see this home's claim (T-29):
# under this home, a verb that saves is refused while the app holds the vault (N.10).
# Deletes, group renames and access changes run over a real pipe in CurrentStateTests.
#
# NEGATIVE CONTROL: this fails if an old value is released after an edit, a grant outlives the change
# to its entry, anything is released or listed after an external save before a reload, the app writes
# over the external save, shows no notice of it, or reloads into another session or without asking again. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-current-master-pw'
readonly V1='SENTINEL-CURRENT-ONE-41d8'
readonly V2='SENTINEL-CURRENT-TWO-9c03'
readonly V3='SENTINEL-CURRENT-THREE-e7b5'
readonly ENTRY='env/ci/DEPLOY_KEY'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='HOLD_OUT OUT ERR'
require jq

CLI="$(keypaste_bin)"
MCP="$(keypaste_mcp)"
DRV="$(app_driver)"

WORK="$(mktemp -d)"
mkdir -p "$WORK/home" "$WORK/other-home"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
OTHER_HOME="$(native "$WORK/other-home")"
VAULT="$(native "$WORK/vault.kdbx")"
AUDIT="$(native "$WORK/audit.jsonl")"
readonly HOLD_OUT="$WORK/hold.txt"
readonly OUT="$WORK/mcp-stdout.txt"
readonly ERR="$WORK/mcp-stderr.txt"

HOLD_PID=""
# Fixed descriptors, as macOS's bash 3.2 needs (D-0398): 7 is the held app's input and 8 the open bridge's.
exec 7>/dev/null
exec 8>/dev/null
cleanup() {
  exec 8>&- 2>/dev/null || true
  exec 7>&- 2>/dev/null || true
  if [ -n "$HOLD_PID" ]; then kill_process "$HOLD_PID"; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

digest() { sha256sum "$WORK/vault.kdbx" | cut -d' ' -f1; }

# Does one act in the held app and prints the line the driver answered it with.
act() {
  local before
  before="$(wc -l <"$HOLD_OUT")"
  echo "$1" >&7
  for _ in $(seq 1 150); do
    [ "$(wc -l <"$HOLD_OUT")" -gt "$before" ] && { tail -n 1 "$HOLD_OUT"; return 0; }
    sleep 0.2
  done
  die "the app never answered '$1'"
}

# Sends one tool call on the open bridge and waits for its reply.
call() {
  local id="$1" tool="$2" arguments="$3"
  printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"$tool\",\"arguments\":$arguments}}" >&8
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
printf '%s\n%s\n' "$MASTER" "$V1" | "$CLI" add "$ENTRY" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"
printf '%s\n' "$MASTER" | "$CLI" add KEEP --group personal --generate --vault "$VAULT" >/dev/null \
  || die "could not make a group outside the exposure"

# --------------------------------------------------------------- the app holds it; a bridge attaches
exec 7>&-
exec 7> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" KEYPASTE_DRIVER_NEW_PASSWORD="$V2" \
  exec "$DRV" hold "$VAULT" --approving-prompt >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
FIRST="$(session_of)"

exec 8>&-
exec 8> >(exec 7>&-; exec "$MCP" --vault "$VAULT" --audit-log "$AUDIT" --client-label ci-probe >"$OUT" 2>"$ERR")
printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}' >&8
printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}' >&8

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

# ---------------------- another program saves: refused, the file kept, and nothing until a reload
printf '%s\n%s\n' "$MASTER" "$V3" | KEYPASTE_HOME="$OTHER_HOME" "$CLI" set "$ENTRY" --vault "$VAULT" >/dev/null \
  || die "another program could not save the vault the app holds"
EXTERNAL="$(digest)"

request 15 "$ENTRY"
denied 15 vault-changed "the request after another program saved"
tail -n 1 "$AUDIT" | jq -e --arg s "$FIRST" '.session == $s' >/dev/null || die "the refusal does not name session $FIRST"
tail -n 1 "$AUDIT" | jq -e '.reason | contains("another program changed the vault file")' >/dev/null \
  || die "the refusal's audit line does not say the file changed"
call 16 list_entry_names '{}'
denied 16 vault-changed "the listing after another program saved" list_entry_names

line="$(act "edit $ENTRY")"
case "$line" in refused:*) ;; *) die "the app saved over the other program's write: $line" ;; esac
[ "$(digest)" = "$EXTERNAL" ] || die "the vault file no longer holds the other program's bytes"
request 17 "$ENTRY"
denied 17 vault-changed "the request after the app's refused save"
asked 3 "the requests after another program saved"

line="$(act notice)"
case "$line" in "notice Another program saved"*) ;; *) die "the app showed no notice of the other program's save: $line" ;; esac
case "$line" in *"was not saved, and reloading discards it."*) ;; *) die "the notice did not say the refused edit is discarded: $line" ;; esac

line="$(act reload)"
[ "$line" = "reloaded" ] || die "the app did not reload: $line"
[ "$(act notice)" = "notice none" ] || die "the notice outlived the reload"

request 18 "$ENTRY"
released 18 "$V3" prompt "the request after reloading"
tail -n 1 "$AUDIT" | jq -e --arg s "$FIRST" '.session == $s' >/dev/null || die "reloading did not keep session $FIRST"
asked 4 "the request after reloading"
[ "$(digest)" = "$EXTERNAL" ] || die "the vault file changed without anyone saving it"

exec 8>&-
exec 7>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

echo "ok: an edit in the app was the next value released and was asked about again, a moved entry's grant"
echo "    was gone, and another program's save was refused as vault-changed with its bytes kept until the app reloaded it"
