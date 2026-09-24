#!/usr/bin/env bash
# Connecting an MCP client from the app (2.6a, V-2.6a), across real processes: tests/Keypaste.AppDriver holds
# the vault as launch composes the app, with its prompt window drawn on a headless display and clicked, and
# presses Agent Activity's Connect section. The client is tests/Keypaste.FakeMcpClient, a stand-in named
# `claude` that answers Claude Code's `mcp add`, `remove` and `list` and keeps its servers in a file this
# gate reads; it is on PATH only here, so no real client's configuration is ever written. The bridge is the
# shipped Release keypaste-mcp, found on PATH as the app finds it.
#
# Cancelling a preview writes nothing. Running it registers exactly the command the preview showed. The
# check starts that command, raises the app's prompt, and after Approve the audit file holds a prompted
# grant naming the app's session and the chosen label; with two names listed the person picks, and Deny is
# recorded too. Removing leaves the client listing no keypaste. Its configuration never holds the master
# password, a keyfile, the session or a credential.
#
# NEGATIVE CONTROL: this fails if a cancelled preview writes, if what is registered differs from what was
# shown, if the check does not reach the app's prompt through the registered bridge, if the audit record
# names another session or label, if removal leaves keypaste listed, or if a secret reaches the client's
# configuration or the driver's output. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-connect-master-pw'
readonly SECRET='SENTINEL-CONNECT-CLIENT-7a41e2'
readonly OTHER_SECRET='SENTINEL-CONNECT-OTHER-3d90b6'
readonly LABEL='ci-connect'

die() {
  echo "::error::$*" >&2
  for f in "${HOLD_OUT:-}" "${CONFIG:-}"; do
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
FAKE="$(resolve "${KEYPASTE_FAKE_CLIENT:-artifacts/bin/Keypaste.FakeMcpClient/release/claude}")"

WORK="$(mktemp -d)"
mkdir -p "$WORK/home"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
VAULT="$(native "$WORK/vault.kdbx")"
readonly AUDIT="$WORK/home/audit.jsonl"
readonly HOLD_OUT="$WORK/hold.txt"
readonly CONFIG="$WORK/client.json"
KEYPASTE_FAKE_CLIENT_CONFIG="$(native "$CONFIG")"
export KEYPASTE_FAKE_CLIENT_CONFIG

# The stand-in client and the bridge, first on PATH for the driver and everything it starts.
CLIENT_PATH="$(cd "$(dirname "$FAKE")" && pwd):$(cd "$(dirname "$MCP")" && pwd):$PATH"

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
  for _ in $(seq 1 300); do
    [ "$(grep -c -- "$pattern" "$file" 2>/dev/null || true)" -ge "$count" ] && return 0
    sleep 0.2
  done
  die "timed out waiting for '$pattern' in $file"
}

session_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/^holding session ([0-9a-f]+).*/\1/'; }
process_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/.* as process ([0-9]+).*/\1/'; }

SENT=0
# Sends one line to the driver and waits for the line that ends what it prints for it.
say() {
  local ending="$1"
  shift
  local before
  before="$(grep -c -- "$ending" "$HOLD_OUT" || true)"
  echo "$*" >&"$HOLD_IN"
  wait_for "$ending" "$HOLD_OUT" $((before + 1))
  SENT=$((SENT + 1))
}

# The preview the driver printed last, one line per command.
last_preview() {
  awk '/^preview end/ { block = buffer; buffer = ""; next } /^preview / { buffer = buffer substr($0, 9) "\n" } END { printf "%s", block }' "$HOLD_OUT"
}

last_message() { grep '^message ' "$HOLD_OUT" | tail -1 | cut -c9-; }
last_check() { grep '^check ' "$HOLD_OUT" | grep -v '^check end' | tail -1 | cut -c7-; }
last_audit() { tail -n 1 "$AUDIT" | jq -e "$@" >/dev/null; }

# What the stand-in client will start, as one line, from its own configuration.
registered() { jq -r '.mcpServers.keypaste | [.command] + .args | join(" ")' "$CONFIG"; }

clean_config() {
  [ -f "$CONFIG" ] || return 0
  for secret in "$MASTER" "$SECRET" "$OTHER_SECRET" "$FIRST"; do
    grep -qF -- "$secret" "$CONFIG" && die "the client's configuration holds a secret or the session: $secret"
  done
  grep -qi 'keyfile' "$CONFIG" && die "the client's configuration names a keyfile"
  return 0
}

# ---------------------------------------------------------------- a vault with two exposed entries
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n' "$MASTER" | "$CLI" env set ci "DEPLOY_KEY=$SECRET" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"
printf '%s\n' "$MASTER" | "$CLI" env set other "TOKEN=$OTHER_SECRET" --vault "$VAULT" >/dev/null \
  || die "could not store the second credential"

exec {HOLD_IN}>&-
exec {HOLD_IN}> >(PATH="$CLIENT_PATH" KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
FIRST="$(session_of)"

# ---------------------------------------------------------------- a cancelled preview writes nothing
say '^preview end' connect claude-code "label=$LABEL" 'expose=env/ci/**'
PREVIEW="$(last_preview)"
[ "$(printf '%s' "$PREVIEW" | grep -c .)" = 2 ] || die "the preview is not the two commands Connect runs: [$PREVIEW]"
[ "$(printf '%s\n' "$PREVIEW" | sed -n 1p)" = 'claude mcp remove --scope user keypaste' ] \
  || die "the preview does not clear an earlier entry first"
ADD="$(printf '%s\n' "$PREVIEW" | sed -n 2p)"
case "$ADD" in
  "claude mcp add --scope user --transport stdio keypaste -- "*" --vault "*" --client-label $LABEL --expose env/ci/**") ;;
  *) die "the preview does not add the bridge with the vault, the label and the exposure: [$ADD]" ;;
esac

say '^message ' cancel
[ "$(last_message)" = 'Cancelled. Nothing was changed.' ] || die "Cancel did not say it changed nothing"
[ -e "$CONFIG" ] && die "a cancelled preview wrote the client's configuration"

# ------------------------------------------------- running it registers exactly what the preview showed
say '^preview end' connect claude-code "label=$LABEL" 'expose=env/ci/**'
[ "$(last_preview)" = "$PREVIEW" ] || die "the same choices previewed something else"
say '^message ' confirm
case "$(last_message)" in Connected.*) ;; *) die "Run it did not connect: $(last_message)" ;; esac
[ "$(registered)" = "${ADD#*keypaste -- }" ] || die "the client registered [$(registered)], not what the preview showed"
clean_config

# ----------------------------------- the check reaches the app's prompt through the registered bridge
say '^prompt client' check
grep '^prompt client' "$HOLD_OUT" | tail -1 | grep -qF "client=keypaste-check label=$LABEL entry=env/ci/DEPLOY_KEY field=password" \
  || die "the prompt does not show the check, its label and the one listed entry"
say '^check end' approve
case "$(last_check)" in
  "Connected: you approved env/ci/DEPLOY_KEY"*) ;;
  *) die "the check did not report the approval: $(last_check)" ;;
esac
last_audit --arg s "$FIRST" --arg l "$LABEL" \
  '.tool == "request_credential" and .decision == "granted" and .method == "prompt" and .session == $s and .client.label == $l and .client.name == "keypaste-check"' \
  || die "the audit file does not hold a prompted grant naming the app's session and the chosen label"

# ------------------------------------------ with two names listed the person picks, and Deny is recorded
say '^preview end' connect claude-code "label=$LABEL" 'expose='
case "$(last_preview)" in *--expose*) die "an empty exposure still passed --expose" ;; esac
say '^message ' confirm
case "$(last_message)" in Connected.*) ;; *) die "reconnecting failed: $(last_message)" ;; esac
[ "$(registered)" = "$(last_preview | sed -n 2p | sed 's/.*keypaste -- //')" ] || die "the reconnection is not what the preview showed"
clean_config

say '^check-entry n=2' check
grep -qx 'check-entry n=1 env/ci/DEPLOY_KEY' "$HOLD_OUT" || die "the picker does not list the first entry"
grep -qx 'check-entry n=2 env/other/TOKEN' "$HOLD_OUT" || die "the picker does not list the second entry"
say '^prompt client' pick 2
grep '^prompt client' "$HOLD_OUT" | tail -1 | grep -qF "entry=env/other/TOKEN" || die "the prompt is not for the picked entry"
say '^check end' deny
case "$(last_check)" in
  "Connected: the request for env/other/TOKEN reached you and was refused."*) ;;
  *) die "the check did not report the refusal: $(last_check)" ;;
esac
last_audit --arg s "$FIRST" --arg l "$LABEL" '.decision == "denied" and .method == "prompt" and .session == $s and .client.label == $l' \
  || die "the audit file does not hold the refusal"

# ------------------------------------------------------------- removing leaves no keypaste in the client
say '^preview end' connect-remove claude-code
[ "$(last_preview)" = 'claude mcp remove --scope user keypaste' ] || die "the removal preview is not the client's own removal"
say '^message ' confirm
case "$(last_message)" in Removed.*) ;; *) die "Remove did not remove: $(last_message)" ;; esac
LISTED="$(PATH="$CLIENT_PATH" "$FAKE" mcp list)"
case "$LISTED" in *keypaste*) die "the client still lists keypaste after removal: $LISTED" ;; esac
clean_config

grep -qF -- "$SECRET" "$HOLD_OUT" && die "a credential reached the app's output"
grep -qF -- "$OTHER_SECRET" "$HOLD_OUT" && die "a credential reached the app's output"

exec {HOLD_IN}>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

echo "ok: from the unlocked app, a cancelled preview wrote nothing, Connect registered exactly the command shown,"
echo "    the check raised the app's prompt through that bridge and the audit file holds its prompted grant under"
echo "    the app's session and the chosen label, the picker asked for the chosen entry, and removal left the"
echo "    client listing no keypaste, with no secret or session in its configuration"
