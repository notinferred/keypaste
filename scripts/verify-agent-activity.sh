#!/usr/bin/env bash
# Agents shows what the app's session holds, and its History what agents asked for (4.3b, V-4.3b, N.3),
# across real processes: the shipped keypaste and keypaste mcp, and tests/Keypaste.AppDriver holding the vault
# as launch composes the app, with its prompt window drawn on a headless display and clicked. The driver's
# `activity` prints what the screens' own view models read from the authority and the audit file, and its
# `show-prompt` presses the screen's button.
#
# A real keypaste mcp request waiting at the unlocked app is listed. Show the prompt, pressed once the prompt
# has armed, brings it forward with Deny focused and its allows disarmed (D-0456), and Approve then releases
# only once it has armed again. The history shows the prompted release from the
# audit file. Each later request asks again. A lock empties the list, and the next
# unlock starts with nothing listed. A row's settings
# show what the vault gives the agent; a narrowing chosen there applies at once, to the vault and
# clients.toml, and a widening opens the app's confirmation window, changes nothing when refused, and once
# allowed makes the next request Automatic. Each change is one keypaste app line labelled with the agent.
# History reads a missing audit log as nothing asked yet, and says when one could not be read.
#
# NEGATIVE CONTROL: this fails if a waiting request is not listed, if Show the prompt leaves the
# prompt armed or focus off Deny, if a released request
# answers, if history is missing a request's record or is shown for a log that cannot be read, if a
# narrowing asks or a widening does not, if a refused widening changes anything or an allowed one does not
# release the next request unasked, or if a credential reaches the driver's output. These checks must never
# be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-activity-master-pw'
readonly SECRET='SENTINEL-AGENT-ACTIVITY-5c02d9'
readonly ENTRY='env/ci/DEPLOY_KEY'
readonly LABEL='ci-probe'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='HOLD_OUT SHOWN OUT ERR'
require jq

CLI="$(keypaste_bin)"
DRV="$(app_driver)"

WORK="$(mktemp -d)"
mkdir -p "$WORK/home"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
VAULT="$(native "$WORK/vault.kdbx")"
# Where the app writes and reads, and the bridge writes by default: the gate passes no --audit-log.
readonly AUDIT="$WORK/home/audit.jsonl"
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

prompts() { grep -c '^prompt client' "$HOLD_OUT" || true; }

# Starts a bridge whose standard input stays open until the next one starts.
start_bridge() {
  OUT="$WORK/$1-stdout.txt"
  ERR="$WORK/$1-stderr.txt"
  exec 8>&-
  # Without the driver's input, which it would otherwise inherit and hold open past quitting.
  exec 8> >(exec 7>&-; exec "$CLI" mcp --vault "$VAULT" --expose 'env/**' --client-label "$LABEL" >"$OUT" 2>"$ERR")
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}' >&8
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}' >&8
}

# Asks for $ENTRY's field $2, the password unless named.
ask() {
  local field="${2:-password}"
  printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$1,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"$field\",\"reason\":\"ci agent activity probe\"}}}" >&8
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
  echo activity >&7
  wait_for '^activity end' "$HOLD_OUT" "$ACTIVITIES"
  SHOWN="$WORK/activity-$ACTIVITIES.txt"
  awk -v n="$ACTIVITIES" '/^activity end/ { seen++; next } seen == n - 1 && /^(unavailable|waiting|history)/' "$HOLD_OUT" >"$SHOWN"
}

shows() { grep -qE -- "$1" "$SHOWN" || die "Agent Activity does not show: $2"; }
omits() { grep -qE -- "$1" "$SHOWN" && die "Agent Activity shows: $2"; return 0; }

SETTINGS=0
SHOWN_SETTINGS=""
# Opens the probe's settings from its row and keeps the line the driver reads off them in $SHOWN_SETTINGS.
settings_shown() {
  SETTINGS=$((SETTINGS + 1))
  echo "settings $LABEL" >&7
  wait_for '^settings agent=' "$HOLD_OUT" "$SETTINGS"
  SHOWN_SETTINGS="$(grep '^settings agent=' "$HOLD_OUT" | tail -1)"
}
settings_say() { grep -qF -- "$1" <<<"$SHOWN_SETTINGS" || die "the probe's settings do not show $2: $SHOWN_SETTINGS"; }

CHANGES=0
# Starts one change on the probe's settings; changed waits for its answer.
change() {
  CHANGES=$((CHANGES + 1))
  echo "setting $LABEL $*" >&7
}
changed() { wait_for '^setting-done ' "$HOLD_OUT" "$CHANGES"; }
confirmations() { grep -c '^access-prompt agent=' "$HOLD_OUT" || true; }

# The last line is the probe's settings change from the panel, with the decision and method given.
setting_line() {
  tail -n 1 "$AUDIT" | jq -e --arg l "$LABEL" --arg d "$1" --arg m "$2" \
    '.tool == "access_change" and .client.name == "keypaste app" and .client.label == $l and .decision == $d and .method == $m' >/dev/null \
    || die "the settings change was not recorded as $1/$2 under keypaste app: $(tail -n 1 "$AUDIT")"
}

# ---------------------------------------------------------------- a vault with something in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n%s\n' "$MASTER" "$SECRET" | "$CLI" add "$ENTRY" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"
# The entry is not tagged into a project, so the agent sees it only once a person lets it see env/**,
# confirmed at keypaste approve's terminal before the app holds the vault.
agent_settings "$CLI" "$VAULT" "$MASTER" "$LABEL --see env/**" || die "could not save the probe's settings in the vault"

exec 7>&-
exec 7> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" exec "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
FIRST="$(session_of)"

# ------------------------------------------------- before any agent has asked there is no log to read
[ -e "$AUDIT" ] && die "an audit log exists before any agent asked"
activity
omits '^unavailable ' "the authority as unreadable while it serves"
omits '^waiting ' "a request before any agent asked"
shows '^history-message Nothing has asked' "a missing log as nothing asked yet"
omits '^history ' "a record while the log is missing"

# ------------------------------------------ the waiting request, then its audit record
start_bridge first
ask 3
wait_for '^prompt client' "$HOLD_OUT" 1
activity
shows "^waiting n=1 client=ci-probe label=$LABEL entry=$ENTRY field=password left=answered for you in [0-9]+ s$" "the waiting request"

# Show the prompt, pressed once the prompt has armed, disarms it for a second with Deny focused; Approve,
# which waits for the allow to be pressable, then releases.
echo show-prompt >&7
wait_for '^prompt raised ' "$HOLD_OUT"
RAISED="$(grep '^prompt raised ' "$HOLD_OUT" | tail -1)"
[ "$RAISED" = 'prompt raised armed=false focus=Deny' ] || die "Show the prompt did not disarm the prompt with Deny focused: $RAISED"
activity
shows "^waiting n=1 client=ci-probe label=$LABEL entry=$ENTRY field=password " "the request still waiting after Show the prompt"

echo approve >&7
reply 3
released 3
grep -q "$SECRET" "$OUT" || die "the approved reply does not carry the credential"
last_audit --arg s "$FIRST" '.decision == "granted" and .method == "prompt" and .session == $s'

activity
omits '^waiting ' "a request after it was answered"
shows "^history .*DEPLOY_KEY.*Approved" "the prompted release from the audit file"

# ------------------------------------------------------------ the next request is asked again
ask 4
wait_for '^prompt client' "$HOLD_OUT" 2
echo approve >&7
reply 4
released 4
last_audit '.method == "prompt" and .decision == "granted"'

ask 5
wait_for '^prompt client' "$HOLD_OUT" 3
echo approve >&7
reply 5
released 5

ask 6
wait_for '^prompt client' "$HOLD_OUT" 4
echo deny >&7
reply 6
refused 6
activity
shows "^history .*DEPLOY_KEY.*Denied" "the refusal from the audit file"

# ------------------------------------------------ a lock empties both lists; the next session has none
# Another field, because the refusal above cools the password down for every bridge (D-0425).
start_bridge second
ask 3 username
wait_for '^prompt client' "$HOLD_OUT" 5
activity
shows '^waiting n=1 ' "the request waiting before the lock"

echo lock >&7
wait_for '^locked' "$HOLD_OUT"
reply 3
refused 3
last_audit --arg s "$FIRST" '.method == "vault-locked" and .session == $s'
activity
shows '^unavailable ' "a locked app as unable to read requests"
omits '^waiting ' "a request after the lock"

echo unlock >&7
wait_for 'holding session' "$HOLD_OUT" 2
SECOND="$(session_of)"
[ "$SECOND" != "$FIRST" ] || die "unlocking again reused session $FIRST"
activity
omits '^unavailable ' "the new session as unreadable"
omits '^waiting ' "a request from before the lock"
shows "^history .*DEPLOY_KEY.*Approved" "the file's records after a lock, which History reads whole"

# ---------------------------- a row's settings: a narrowing applies at once, a widening only once confirmed
start_bridge third
settings_shown
settings_say "agent=$LABEL mode=ask values=yes run=off see=env/** rows= held=none" "what the vault gives it"

change mode automatic
wait_for '^access-prompt agent=' "$HOLD_OUT" 1
grep '^access-prompt agent=' "$HOLD_OUT" | tail -1 \
  | grep -qF "agent=$LABEL asked=asked from this app's agent settings gains=by default: Automatic" \
  || die "the confirmation does not say who gains what, or where it was asked from"
echo deny >&7
changed
setting_line denied prompt
settings_shown
settings_say "mode=ask " "the mode a refused widening left"

change mode automatic
wait_for '^access-prompt agent=' "$HOLD_OUT" 2
echo allow >&7
changed
setting_line granted prompt
settings_shown
settings_say "mode=automatic " "the confirmed mode"

change mode ask
changed
[ "$(confirmations)" -eq 2 ] || die "a narrowing asked for confirmation"
setting_line granted narrowing
grep -A3 "^label = \"$LABEL\"" "$WORK/home/clients.toml" | grep -qF 'mode = "ask"' || die "the narrowing did not reach clients.toml"
settings_shown
settings_say "mode=ask " "the narrowing"
ask 7
wait_for '^prompt client' "$HOLD_OUT" 6
grep '^prompt client' "$HOLD_OUT" | tail -1 | grep -qF 'for=once or always' \
  || die "the request after the narrowing was not offered once or always"
echo once >&7
reply 7
released 7

change mode automatic
wait_for '^access-prompt agent=' "$HOLD_OUT" 3
echo allow >&7
changed
setting_line granted prompt
settings_shown
settings_say "mode=automatic " "the confirmed mode"
ask 8
reply 8
released 8
last_audit '.tool == "request_credential" and .decision == "granted" and .method == "automatic"' \
  || die "the request after Automatic was confirmed was not released as automatic"
[ "$(prompts)" -eq 6 ] || die "a request under Automatic raised a prompt"

# ----------------------------------------------------------------- an unreadable log is unavailable
# The app holds the log open from a session's first request until a lock, and Windows deletes no open file.
exec 8>&-
echo lock >&7
wait_for '^locked' "$HOLD_OUT" 2
for _ in $(seq 1 50); do rm -f "$AUDIT" 2>/dev/null && break; sleep 0.2; done
[ -e "$AUDIT" ] && die "could not remove the audit log"
mkdir "$AUDIT"
echo unlock >&7
wait_for 'holding session' "$HOLD_OUT" 3
activity
shows "^history-message That log couldn't be read" "an unreadable log said plainly"
omits '^history ' "history from a log that could not be read"

grep -q "$SECRET" "$HOLD_OUT" && die "a credential reached the app's output"

exec 7>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

echo "ok: Agents listed a real keypaste mcp request waiting at the app, brought its prompt forward disarmed with"
echo "    Deny focused, showed the audit records since the unlock, asked each"
echo "    later request again, held nothing after a lock,"
echo "    read a missing log as no records yet and said an unreadable log was unavailable"
