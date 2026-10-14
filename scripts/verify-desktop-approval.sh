#!/usr/bin/env bash
# The app asks in its own prompt window (4.4, V-4.4), across real processes: the shipped keypaste and
# keypaste mcp, and tests/Keypaste.AppDriver holding the vault as launch composes the app, with the
# app's prompt window drawn on a headless display and clicked through its hit-testing.
#
# A real keypaste mcp request raises the prompt, which shows the client's label, the entry, the field and
# the lifetime. Approve returns the field, audited as a prompted grant naming the app's session and the
# label. Deny, closing the prompt, the client cancelling, the bridge going away, a lock and the gate's
# timeout each return a denial with the matching method and take the prompt down. The client cancelling
# and the bridge going away each leave exactly one line, cancelled, which the app records. Each case uses
# a new bridge, so no grant carries from one to the next, and Deny and closing each ask for a field of
# their own, because a refusal cools that field down for every bridge (D-0425). For an entry in one
# project environment the prompt offers Always for it: pressed, it holds the release while the app's
# confirmation asks to make that environment Automatic; refused, the request is released once and nothing
# changes; allowed, the next request for the entry is released with no prompt and recorded as automatic.
#
# NEGATIVE CONTROL: this fails if a request is released without a press of Approve, if Approve does not
# release it, if an audit line names the wrong method, session or label, if an abandoned request leaves
# no line or two, if a prompt stays up after its request is answered or abandoned, if Always for is not
# offered or releases before its confirmation is answered, or if a refused confirmation changes anything
# or an allowed one does not release the next request unasked. These checks must never be skipped or
# soft-passed.
set -euo pipefail

readonly MASTER='ci-approval-master-pw'
readonly SECRET='SENTINEL-DESKTOP-APPROVAL-8b41e6'
readonly ENTRY='env/ci/DEPLOY_KEY'
readonly LABEL='ci-probe'

# An entry with an env-named custom field, which may leave by name, and one that never does (C.5a).
readonly API_ENTRY='api/OpenAI'
readonly API_PASSWORD='SENTINEL-DESKTOP-OPENAI-PASSWORD-51c7'
readonly API_KEY='SENTINEL-DESKTOP-OPENAI-API-KEY-6d08'
readonly RECOVERY='SENTINEL-DESKTOP-RECOVERY-CODES-7e19'
# Entries in one environment of a project, for Always for: one whose confirmation is refused, one allowed.
readonly STAGING_ENTRY='env/ci/STAGING_KEY'
readonly RELEASE_ENTRY='env/ci/RELEASE_KEY'

readonly DENIAL='keypaste: DENIED. The "field" argument must be password, username, url, notes or a custom field named like an environment variable. This call was recorded in the audit log.'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='HOLD_OUT OUT ERR'
require jq

CLI="$(keypaste_bin)"
DRV="$(app_driver)"

WORK="$(mktemp -d)"
mkdir -p "$WORK/home"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
VAULT="$(native "$WORK/vault.kdbx")"
# The log in the owner's home, where the bridges here write too.
AUDIT="$(native "$WORK/home/audit.jsonl")"
readonly LOG="$WORK/home/audit.jsonl"
readonly HOLD_OUT="$WORK/hold.txt"

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

PROMPTS=0

# Writes one request_credential call to the open bridge.
request() {
  local id="$1" entry="$2" field="$3"
  jq -cn --argjson id "$id" --arg entry "$entry" --arg field "$field" \
    '{jsonrpc:"2.0",id:$id,method:"tools/call",params:{name:"request_credential",arguments:{entry:$entry,field:$field,reason:"ci desktop approval probe",ttl_seconds:60}}}' >&8
}

# Starts a bridge whose standard input stays open, asks for the credential, and waits for the prompt.
# The field, entry and exposure default to the password of $ENTRY under env/**.
raise_prompt() {
  local field="${2:-password}" entry="${3:-$ENTRY}" expose=(--expose "${4:-env/**}")
  OUT="$WORK/$1-stdout.txt"
  ERR="$WORK/$1-stderr.txt"
  exec 8>&-
  # Without the driver's input, which it would otherwise inherit and hold open past quitting.
  exec 8> >(exec 7>&-; exec "$CLI" mcp --vault "$VAULT" --audit-log "$AUDIT" --client-label "$LABEL" "${expose[@]}" >"$OUT" 2>"$ERR")
  BRIDGE_PID=$!
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}' >&8
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}' >&8
  request 3 "$entry" "$field"
  PROMPTS=$((PROMPTS + 1))
  wait_for '^prompt client' "$HOLD_OUT" "$PROMPTS"
}

# Waits up to 30 seconds for the reply to call $1 on the open bridge.
reply_to() {
  for _ in $(seq 1 150); do
    jq -e --argjson id "$1" 'select(.id == $id)' <"$OUT" >/dev/null 2>&1 && return 0
    sleep 0.2
  done
  die "call $1 got no reply"
}

# Waits up to $1 seconds for the request's reply, then lets the bridge go.
answered() {
  local seconds="$1"
  for _ in $(seq 1 $((seconds * 5))); do
    jq -e 'select(.id == 3)' <"$OUT" >/dev/null 2>&1 && break
    sleep 0.2
  done
  jq -e 'select(.id == 3)' <"$OUT" >/dev/null 2>&1 || die "the request got no reply"
  exec 8>&-
}

# The prompt that request raised has come down.
withdrawn() { wait_for '^prompt withdrawn' "$HOLD_OUT" "$PROMPTS"; }

# Waits up to 10 seconds for an abandoned request's line, then checks the log gained exactly that one: a
# cancelled denial in session $2 under the label, recorded by the app. $1 is the line count before it.
cancelled_once() {
  local before="$1" session="$2" what="$3"
  for _ in $(seq 1 50); do
    [ "$(lines_in "$LOG")" -gt "$before" ] && break
    sleep 0.2
  done
  lines_after "$LOG" "$before" | jq -e -s --arg s "$session" --arg l "$LABEL" 'length == 1 and .[0].tool == "request_credential"
      and .[0].decision == "denied" and .[0].method == "cancelled" and .[0].session == $s and .[0].client.label == $l' >/dev/null \
    || die "$what was not recorded once, as a cancelled denial in session $session: $(lines_after "$LOG" "$before")"
}

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
printf '%s\n%s\n' "$MASTER" "$SECRET" | "$CLI" add "$ENTRY" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"
printf '%s\n%s\n' "$MASTER" "$API_PASSWORD" | "$CLI" add "$API_ENTRY" --vault "$VAULT" >/dev/null \
  || die "could not store the entry with custom fields"
printf '%s\n%s\n%s\n' "$MASTER" "$API_KEY" "$RECOVERY" \
  | "$CLI" set "$API_ENTRY" --field OPENAI_API_KEY --field 'Recovery codes' --vault "$VAULT" >/dev/null \
  || die "could not set the entry's custom fields"
for entry in "$STAGING_ENTRY" "$RELEASE_ENTRY"; do
  printf '%s\n%s\n' "$MASTER" "$SECRET" | "$CLI" add "$entry" --vault "$VAULT" >/dev/null || die "could not store $entry"
done
printf '%s\n' "$MASTER" | "$CLI" env tag ci "$STAGING_ENTRY" -p staging --yes --vault "$VAULT" >/dev/null \
  || die "could not put $STAGING_ENTRY in ci/staging"
printf '%s\n' "$MASTER" | "$CLI" env tag ci "$RELEASE_ENTRY" --yes --vault "$VAULT" >/dev/null \
  || die "could not put $RELEASE_ENTRY in ci/dev"
# The first two entries are tagged into no project, so the agent sees them only once a person lets it see env/**
# and api/**, confirmed at keypaste agent's terminal before the app holds the vault.
agent_settings "$CLI" "$VAULT" "$MASTER" "$LABEL --see env/** --see api/**" || die "could not save the probe's settings in the vault"

exec 7>&-
exec 7> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" exec "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
FIRST="$(session_of)"

# ------------------------------------------------------------- Approve releases, and says so
raise_prompt approve
grep '^prompt client' "$HOLD_OUT" | tail -1 \
  | grep -qF "label=$LABEL entry=$ENTRY field=password for=once, or for 1 hour" \
  || die "the prompt does not show the label, entry, field and what the person can allow"
echo approve >&7
answered 30
withdrawn
jq -e 'select(.id == 3) | .result.isError == false' <"$OUT" >/dev/null || die "Approve did not release the credential"
grep -q "$SECRET" "$OUT" || die "the approved reply does not carry the credential"
tail -n 1 "$AUDIT" | jq -e --arg s "$FIRST" --arg l "$LABEL" \
  '.tool == "request_credential" and .decision == "granted" and .method == "prompt" and .session == $s and .client.label == $l' >/dev/null \
  || die "the approval was not audited as a prompted grant in session $FIRST under label $LABEL"
grep -q "$SECRET" "$AUDIT" && die "the credential reached the audit log"
grep -q "$SECRET" "$HOLD_OUT" && die "the credential reached the app's output"

# ------------------------------------------------------------- one custom field, by name (C.5a)
raise_prompt field-once OPENAI_API_KEY "$API_ENTRY" 'api/**'
grep '^prompt client' "$HOLD_OUT" | tail -1 \
  | grep -qF "label=$LABEL entry=$API_ENTRY field=OPENAI_API_KEY for=once, or for 1 hour" \
  || die "the prompt does not name the custom field"
echo once >&7
answered 30
withdrawn
jq -e --arg v "$API_KEY" 'select(.id == 3) | .result.isError == false and .result.structuredContent.value == $v' <"$OUT" >/dev/null \
  || die "Allow once did not return exactly the custom field's value"
tail -n 1 "$AUDIT" | jq -e --arg s "$FIRST" \
  '.decision == "granted" and .method == "prompt" and .args.field == "OPENAI_API_KEY" and .session == $s and .granted_seconds == 0' >/dev/null \
  || die "the custom field's release was not audited with its field as allowed once"

# Allowed for an hour on one connection, it serves that field alone: the password still draws a prompt,
# and a field that never leaves is refused by the bridge with no prompt at all.
raise_prompt field-hour OPENAI_API_KEY "$API_ENTRY" 'api/**'
echo approve >&7
reply_to 3
withdrawn
jq -e --arg v "$API_KEY" 'select(.id == 3) | .result.structuredContent.value == $v' <"$OUT" >/dev/null \
  || die "Allow for 1 hour did not return the custom field's value"
request 4 "$API_ENTRY" password
PROMPTS=$((PROMPTS + 1))
wait_for '^prompt client' "$HOLD_OUT" "$PROMPTS"
grep '^prompt client' "$HOLD_OUT" | tail -1 | grep -qF "entry=$API_ENTRY field=password" \
  || die "a password request under the custom field's grant did not draw its own prompt"
echo deny >&7
reply_to 4
withdrawn
id=5
for field in 'Recovery codes' otp KP2A_URL_1 URL; do
  request "$id" "$API_ENTRY" "$field"
  reply_to "$id"
  jq -e --arg t "$DENIAL" --argjson id "$id" 'select(.id == $id) | .result.isError == true and .result.content[0].text == $t' <"$OUT" >/dev/null \
    || die "a request for '$field' was not refused with the fixed denial"
  tail -n 1 "$AUDIT" | jq -e '.args.field == "invalid" and .method == "invalid-request" and .decision == "denied"' >/dev/null \
    || die "a request for '$field' was not audited as an invalid field"
  id=$((id + 1))
done
exec 8>&-
[ "$(grep -c '^prompt client' "$HOLD_OUT")" -eq "$PROMPTS" ] || die "a field that never leaves drew a prompt"
for value in "$API_PASSWORD" "$RECOVERY" "$SECRET"; do
  grep -q "$value" "$WORK/field-once-stdout.txt" "$OUT" && die "a custom-field request returned another field's value"
done
for value in "$API_PASSWORD" "$API_KEY" "$RECOVERY"; do
  grep -q "$value" "$AUDIT" "$HOLD_OUT" && die "a custom-field value reached the audit log or the app's output"
done

# ---------------------------------------------------------------- Deny and closing refuse
raise_prompt deny username
echo deny >&7
answered 30
withdrawn
denied_as prompt "$FIRST" "Deny"

raise_prompt close url
echo close >&7
answered 30
withdrawn
denied_as prompt "$FIRST" "closing the prompt"

# ------------------------------------------------ the client giving up takes the prompt down
logged=$(lines_in "$LOG")
raise_prompt cancelled
printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":3,"reason":"ci probe gave up"}}' >&8
withdrawn
exec 8>&-
cancelled_once "$logged" "$FIRST" "the cancelled request"
grep -q "$SECRET" "$OUT" && die "a cancelled request reached the client"

# A bridge whose standard input closes waits for its call to finish, so going away is being killed.
logged=$(lines_in "$LOG")
raise_prompt gone
if [ -r "/proc/$BRIDGE_PID/winpid" ]; then
  kill_process "$(cat "/proc/$BRIDGE_PID/winpid")"
else
  kill -9 "$BRIDGE_PID"
fi
withdrawn
exec 8>&-
cancelled_once "$logged" "$FIRST" "the request whose bridge went away"
grep -q "$SECRET" "$OUT" && die "a request whose bridge went away reached the client"

# --------------------- Always for allows once and then asks; refused it stays one release, allowed it is Automatic
raise_prompt always-refused password "$STAGING_ENTRY"
grep '^prompt client' "$HOLD_OUT" | tail -1 \
  | grep -qF "entry=$STAGING_ENTRY field=password for=once, or for 1 hour always=Always for ci:staging" \
  || die "the prompt does not offer Always for the entry's environment"
echo always >&7
wait_for '^access-prompt agent=' "$HOLD_OUT" 1
grep '^access-prompt agent=' "$HOLD_OUT" | tail -1 \
  | grep -qF "agent=$LABEL asked=asked from the approval prompt's Always for gains=ci:staging: Automatic" \
  || die "the confirmation does not say who gains what, or where it was asked from"
jq -e 'select(.id == 3)' <"$OUT" >/dev/null 2>&1 && die "the release came back before the change Always for asked for was answered"
echo deny >&7
answered 30
withdrawn
wait_for '^access-prompt withdrawn' "$HOLD_OUT" 1
jq -e 'select(.id == 3) | .result.isError == false' <"$OUT" >/dev/null || die "Always for did not release the request once"
tail -n 2 "$AUDIT" | jq -e -s --arg l "$LABEL" '.[0].tool == "access_change" and .[0].decision == "denied" and .[0].client.label == $l
    and .[1].tool == "request_credential" and .[1].decision == "granted" and .[1].method == "prompt" and .[1].granted_seconds == 0' >/dev/null \
  || die "a refused Always for was not recorded as one release and one refused change: $(tail -n 2 "$AUDIT")"

raise_prompt always-allowed password "$RELEASE_ENTRY"
grep '^prompt client' "$HOLD_OUT" | tail -1 | grep -qF "entry=$RELEASE_ENTRY field=password for=once, or for 1 hour always=Always for ci:dev" \
  || die "the prompt does not offer Always for ci:dev"
echo always >&7
wait_for '^access-prompt agent=' "$HOLD_OUT" 2
echo allow >&7
reply_to 3
withdrawn
wait_for '^access-prompt withdrawn' "$HOLD_OUT" 2
jq -e 'select(.id == 3) | .result.isError == false' <"$OUT" >/dev/null || die "Always for did not release the request"
tail -n 2 "$AUDIT" | jq -e -s '.[0].tool == "access_change" and .[0].decision == "granted" and .[0].method == "prompt"
    and .[1].tool == "request_credential" and .[1].decision == "granted" and .[1].method == "prompt"' >/dev/null \
  || die "an allowed Always for was not recorded as a confirmed change and a release: $(tail -n 2 "$AUDIT")"
request 4 "$RELEASE_ENTRY" password
reply_to 4
exec 8>&-
jq -e 'select(.id == 4) | .result.isError == false' <"$OUT" >/dev/null || die "the request after Always for was not released"
tail -n 1 "$AUDIT" | jq -e '.tool == "request_credential" and .decision == "granted" and .method == "automatic"' >/dev/null \
  || die "the request after Always for was not recorded as released automatically"
[ "$(grep -c '^prompt client' "$HOLD_OUT")" -eq "$PROMPTS" ] || die "a request under Automatic drew a prompt"

# -------------------------------------------------------------------- a lock refuses and withdraws
raise_prompt lock
echo lock >&7
wait_for '^locked' "$HOLD_OUT"
answered 30
withdrawn
denied_as vault-locked "$FIRST" "the lock"

# ------------------------------------------------ nobody answering is a denial when the window closes
echo unlock >&7
wait_for 'holding session' "$HOLD_OUT" 2
SECOND="$(session_of)"
[ "$SECOND" != "$FIRST" ] || die "unlocking again reused session $FIRST"

raise_prompt timeout
answered 60
withdrawn
denied_as timed-out "$SECOND" "the timeout"

exec 7>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

echo "ok: the app's prompt window showed a real keypaste mcp request with its label, released it only on Approve,"
echo "    and denied and withdrew it on Deny, closing, the client cancelling, the bridge going, a lock and the timeout"
