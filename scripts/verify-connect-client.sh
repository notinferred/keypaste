#!/usr/bin/env bash
# Connecting an MCP client from the app (2.6a, V-2.6a, G.2), across real processes: tests/Keypaste.AppDriver
# holds the vault as launch composes the app, with its prompt window drawn on a headless display and clicked,
# and presses Agent Activity's Connect section. Everything runs with HOME and USERPROFILE at a home of the
# gate's own and KEYPASTE_HOME at its .keypaste, the default home, so no real client's configuration is ever
# read or written. The command client is tests/Keypaste.FakeMcpClient, a stand-in named `claude` in an
# absolute PATH directory that answers Claude Code's `mcp add`, `remove` and `list` and keeps its servers in
# that home's .claude.json, where the app reads whether Claude Code holds keypaste. The file client is Cursor,
# whose ~/.cursor/mcp.json the gate seeds with another server, a top-level key and a comment. The bridge is
# the shipped Release keypaste, found on PATH as the app finds it and started as keypaste mcp.
#
# The first unlock chooses the vault for agents and the CLI (G.1): `keypaste use` prints it and `keypaste
# grants` reaches the app with no --vault. Cancelling a preview writes nothing. Running it registers exactly
# the command the preview showed, which names no vault and offers run with the exposure given, and the check
# starts that command, which finds the chosen vault, raises the app's prompt, and after Approve the audit
# file holds a prompted grant naming the app's session and the chosen label. With another vault chosen and
# no exposure given, Connect pins this one with --vault and registers run and every item in reach, and that
# bridge still reaches the app, which holds an agent with no settings of its own to the tagged entries; with
# two names listed the person picks, and Deny is recorded too. Removing leaves the client listing no
# keypaste. For Cursor the preview is the one edit of keypaste's entry and Cancel writes nothing; Run it
# changes no byte outside that entry and keeps the seed as the backup, the check reaches the app's prompt
# through what it registered, and Remove gives back the seed's bytes, leaves the backup alone, and a second
# Preview remove says Cursor is not connected. No configuration ever holds the master password, a keyfile,
# the session or a credential.
#
# NEGATIVE CONTROL: this fails if the first unlock chooses nothing, if the CLI without --vault does not reach
# the app, if a cancelled preview writes, if what is registered differs from what was shown, if a
# registration does not offer run or, with no exposure given, does not reach every item, if an agent with no
# settings is listed an untagged entry, if the chosen vault is written into the client or another vault is
# not, if the check does not reach the app's prompt through the registered bridge, if the audit record names
# another session or label, if removal leaves keypaste listed, if Cursor's preview or Cancel writes its
# file, if Run it changes a byte outside keypaste's entry or keeps no backup equal to the seed, if Cursor's
# registration does not reach the app's prompt, if removal does not give back the seed's bytes or touches
# the backup, if a second removal does not say Cursor is not connected, or if a secret reaches a client's
# configuration or the driver's output. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-connect-master-pw'
readonly SECRET='SENTINEL-CONNECT-CLIENT-7a41e2'
readonly OTHER_SECRET='SENTINEL-CONNECT-OTHER-3d90b6'
readonly LABEL='ci-connect'
readonly UNSET_LABEL='ci-connect-unset'
readonly CURSOR_LABEL='ci-cursor'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='HOLD_OUT CONFIG CURSOR'
WAIT_SECONDS=60
require jq

# Whether two paths name one file: Windows may spell a directory by its short 8.3 name.
same_file() {
  if command -v cygpath >/dev/null 2>&1; then
    [ "$(cygpath -m -l "$1" | tr '[:upper:]' '[:lower:]')" = "$(cygpath -m -l "$2" | tr '[:upper:]' '[:lower:]')" ]
  else
    [ "$1" -ef "$2" ]
  fi
}

CLI="$(keypaste_bin)"
DRV="$(app_driver)"
FAKE="$(resolve KEYPASTE_FAKE_CLIENT artifacts/bin/Keypaste.FakeMcpClient/release/claude)"

WORK="$(mktemp -d)"
mkdir -p "$WORK/user/.keypaste" "$WORK/user/.cursor"
HOME="$WORK/user"
USERPROFILE="$(native "$WORK/user")"
KEYPASTE_HOME="$(native "$WORK/user/.keypaste")"
export HOME USERPROFILE KEYPASTE_HOME
VAULT="$(native "$WORK/vault.kdbx")"
OTHER_VAULT="$(native "$WORK/other.kdbx")"
readonly AUDIT="$WORK/user/.keypaste/audit.jsonl"
readonly HOLD_OUT="$WORK/hold.txt"
readonly CONFIG="$WORK/user/.claude.json"
readonly CURSOR="$WORK/user/.cursor/mcp.json"
readonly SEED="$WORK/cursor-seed.json"
readonly STRIPPED="$WORK/cursor-stripped.json"
KEYPASTE_FAKE_CLIENT_CONFIG="$(native "$CONFIG")"
export KEYPASTE_FAKE_CLIENT_CONFIG

# The stand-in client and the bridge, first on PATH for the driver and everything it starts.
CLIENT_PATH="$(cd "$(dirname "$FAKE")" && pwd):$(cd "$(dirname "$CLI")" && pwd):$PATH"

HOLD_PID=""
# Fixed descriptors, as macOS's bash 3.2 needs (D-0398): 7 is the held app's input.
exec 7>/dev/null
cleanup() {
  exec 7>&- 2>/dev/null || true
  if [ -n "$HOLD_PID" ]; then kill_process "$HOLD_PID"; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

SENT=0
# Sends one line to the driver and waits for the line that ends what it prints for it.
say() {
  local ending="$1"
  shift
  local before
  before="$(grep -c -- "$ending" "$HOLD_OUT" || true)"
  echo "$*" >&7
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
  local file="$1"
  [ -f "$file" ] || return 0
  for secret in "$MASTER" "$SECRET" "$OTHER_SECRET" "$FIRST"; do
    grep -qF -- "$secret" "$file" && die "$file holds a secret or the session: $secret"
  done
  grep -qi 'keyfile' "$file" && die "$file names a keyfile"
  return 0
}

# Cursor's file with keypaste's member taken out: the lines from its name to its closing brace, as the
# app inserts it before the first server of a file laid out one member per line.
without_keypaste() {
  awk 'skip && /^    },$/ { skip = 0; next } skip { next } /^    "keypaste": [{]$/ { skip = 1; next } { print }' "$1"
}

# ---------------------------------------------------------------- a vault with two exposed entries
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n%s\n' "$MASTER" "$SECRET" | "$CLI" add env/ci/DEPLOY_KEY --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"
printf '%s\n%s\n' "$MASTER" "$OTHER_SECRET" | "$CLI" add env/other/TOKEN --vault "$VAULT" >/dev/null \
  || die "could not store the second credential"
# Two projects, whose tagged entries are what a bridge with the default exposure lists (C.5b).
for project in alpha beta; do
  printf '%s\n' "$MASTER" | "$CLI" env set "$project" "$(printf '%s' "$project" | tr a-z A-Z)_KEY=$OTHER_SECRET" --vault "$VAULT" >/dev/null 2>&1 \
    || die "could not store project ${project}'s variable"
done
# The untagged entries are seen only once a person lets an agent see env/**, confirmed at keypaste agent's
# terminal before the app holds the vault; an agent with no settings of its own still sees only tagged ones,
# however wide its connection is registered.
agent_settings "$CLI" "$VAULT" "$MASTER" "$LABEL --see env/**" "$CURSOR_LABEL --see env/**" \
  || die "could not save $LABEL's and $CURSOR_LABEL's settings in the vault"

exec 7>&-
exec 7> >(PATH="$CLIENT_PATH" KEYPASTE_DRIVER_PASSWORD="$MASTER" exec "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
FIRST="$(session_of)"

# ------------------------------------------ the first unlock chose the vault, and the CLI uses it unnamed
CHOSEN="$(env -u KEYPASTE_VAULT "$CLI" use 2>&1 | tr -d '\r\357\273\277')" || die "keypaste use printed no chosen vault: [$CHOSEN]"
same_file "$CHOSEN" "$VAULT" || die "the first unlock chose [$CHOSEN], not the vault it unlocked [$VAULT]"
GRANTS="$(env -u KEYPASTE_VAULT "$CLI" grants 2>&1)" || die "keypaste grants with no --vault failed: $GRANTS"
case "$GRANTS" in *"nothing holds"*) die "keypaste grants with no --vault did not reach the app holding the chosen vault: $GRANTS" ;; esac

# ---------------------------------------------------------------- a cancelled preview writes nothing
say '^preview end' connect claude-code "label=$LABEL" 'expose=env/ci/**'
PREVIEW="$(last_preview)"
[ "$(printf '%s' "$PREVIEW" | grep -c .)" = 2 ] || die "the preview is not the two commands Connect runs: [$PREVIEW]"
[ "$(printf '%s\n' "$PREVIEW" | sed -n 1p)" = 'claude mcp remove --scope user keypaste' ] \
  || die "the preview does not clear an earlier entry first"
ADD="$(printf '%s\n' "$PREVIEW" | sed -n 2p)"
case "$ADD" in
  *" --vault "*) die "the preview writes the chosen vault into the client: [$ADD]" ;;
  "claude mcp add --scope user --transport stdio keypaste -- "*" --client-label $LABEL --allow-run --expose env/ci/**") ;;
  *) die "the preview does not add the bridge with the label, run offered and the exposure: [$ADD]" ;;
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
clean_config "$CONFIG"

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

# ------------------------- with another vault chosen, Connect pins this one, and that bridge still reaches it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$OTHER_VAULT" >/dev/null 2>&1 || die "could not create the other vault"
"$CLI" use "$OTHER_VAULT" >/dev/null 2>&1 || die "keypaste use did not choose the other vault"
say '^preview end' connect claude-code "label=$UNSET_LABEL" 'expose='
case "$(last_preview | sed -n 2p)" in
  *" --vault "*vault.kdbx" --client-label $UNSET_LABEL --allow-run --expose **") ;;
  *) die "Connect did not pin the app's vault while another is chosen, offering run with every item in reach: [$(last_preview)]" ;;
esac
say '^message ' confirm
case "$(last_message)" in Connected.*) ;; *) die "reconnecting failed: $(last_message)" ;; esac
[ "$(registered)" = "$(last_preview | sed -n 2p | sed 's/.*keypaste -- //')" ] || die "the reconnection is not what the preview showed"
clean_config "$CONFIG"

# Through a bridge that reaches every item, an agent with no settings is listed the two projects' tagged
# entries, not the untagged ones, and asks for a variable.
say '^check-entry n=2' check
grep -qx 'check-entry n=1 env/alpha/.env' "$HOLD_OUT" || die "the picker does not list the first tagged entry"
grep -qx 'check-entry n=2 env/beta/.env' "$HOLD_OUT" || die "the picker does not list the second tagged entry"
grep -q '^check-entry n=3' "$HOLD_OUT" && die "the picker lists an entry an agent with no settings does not see"
say '^prompt client' pick 2
grep '^prompt client' "$HOLD_OUT" | tail -1 | grep -qF "entry=env/beta/.env field=BETA_KEY" || die "the prompt is not for the picked entry's variable"
say '^check end' deny
case "$(last_check)" in
  "Connected: the request for env/beta/.env reached you and was refused."*) ;;
  *) die "the check did not report the refusal: $(last_check)" ;;
esac
last_audit --arg s "$FIRST" --arg l "$UNSET_LABEL" '.decision == "denied" and .method == "prompt" and .session == $s and .client.label == $l' \
  || die "the audit file does not hold the refusal"

# ------------------------------------------------------------- removing leaves no keypaste in the client
say '^preview end' connect-remove claude-code
[ "$(last_preview)" = 'claude mcp remove --scope user keypaste' ] || die "the removal preview is not the client's own removal"
say '^message ' confirm
case "$(last_message)" in Removed.*) ;; *) die "Remove did not remove: $(last_message)" ;; esac
LISTED="$(PATH="$CLIENT_PATH" "$FAKE" mcp list)"
case "$LISTED" in *keypaste*) die "the client still lists keypaste after removal: $LISTED" ;; esac
clean_config "$CONFIG"

# ------------------------------------- Cursor: only keypaste's entry in its file, with the first original kept
cat >"$CURSOR" <<'JSON'
{
  // the person's own note, which no edit may lose
  "mcpServers": {
    "other": {
      "command": "other-server",
      "args": ["--port", "7"]
    }
  },
  "theme": "dark"
}
JSON
cp "$CURSOR" "$SEED"

say '^preview end' connect cursor "label=$CURSOR_LABEL" 'expose=env/ci/**'
EDIT="$(last_preview)"
[ "$(printf '%s' "$EDIT" | grep -c .)" = 1 ] || die "the Cursor preview is not the one edit Connect makes: [$EDIT]"
case "$EDIT" in
  "edit "*"mcp.json: set mcpServers.keypaste {"*"}; keeps the original as "*"mcp.json.keypaste-backup") ;;
  *) die "the Cursor preview is not the edit of keypaste's entry naming its backup: [$EDIT]" ;;
esac
ENTRY="${EDIT#*set mcpServers.keypaste }"
ENTRY="${ENTRY%; keeps the original as *}"
printf '%s' "$ENTRY" | jq -e --arg l "$CURSOR_LABEL" '.args | index("--client-label") as $i | .[$i:] == ["--client-label", $l, "--allow-run", "--expose", "env/ci/**"]' >/dev/null \
  || die "the Cursor preview does not register the chosen label, run offered and the exposure: [$ENTRY]"

say '^message ' cancel
[ "$(last_message)" = 'Cancelled. Nothing was changed.' ] || die "Cancel did not say it changed nothing"
[ "$(bytes "$CURSOR")" = "$(bytes "$SEED")" ] || die "a cancelled preview changed Cursor's file"
[ -e "$CURSOR.keypaste-backup" ] && die "a cancelled preview kept a backup"

say '^preview end' connect cursor "label=$CURSOR_LABEL" 'expose=env/ci/**'
[ "$(last_preview)" = "$EDIT" ] || die "the same choices previewed another edit"
say '^message ' confirm
case "$(last_message)" in
  "Connected. Cursor starts keypaste mcp for this vault;"*"Restart Cursor to load it.") ;;
  *) die "Run it did not connect Cursor: $(last_message)" ;;
esac
[ "$(bytes "$CURSOR.keypaste-backup")" = "$(bytes "$SEED")" ] || die "the backup is not Cursor's file as it was before the first edit"
without_keypaste "$CURSOR" >"$STRIPPED"
[ "$(bytes "$STRIPPED")" = "$(bytes "$SEED")" ] || die "Run it changed Cursor's file outside keypaste's entry"
[ "$(grep -v '^ *//' "$CURSOR" | jq -cS '.mcpServers.keypaste')" = "$(printf '%s' "$ENTRY" | jq -cS .)" ] \
  || die "Cursor's file does not hold the entry the preview showed"
clean_config "$CURSOR"

say '^prompt client' check
grep '^prompt client' "$HOLD_OUT" | tail -1 | grep -qF "client=keypaste-check label=$CURSOR_LABEL entry=env/ci/DEPLOY_KEY field=password" \
  || die "the prompt does not show Cursor's registration asking for the one listed entry"
say '^check end' approve
case "$(last_check)" in
  "Connected: you approved env/ci/DEPLOY_KEY"*) ;;
  *) die "the check through Cursor's registration did not report the approval: $(last_check)" ;;
esac
last_audit --arg s "$FIRST" --arg l "$CURSOR_LABEL" '.decision == "granted" and .method == "prompt" and .session == $s and .client.label == $l' \
  || die "the audit file does not hold the grant through Cursor's registration"

say '^preview end' connect-remove cursor
case "$(last_preview)" in
  "edit "*"mcp.json: remove mcpServers.keypaste") ;;
  *) die "the Cursor removal preview is not the edit that removes keypaste's entry: [$(last_preview)]" ;;
esac
say '^message ' confirm
case "$(last_message)" in Removed.*) ;; *) die "Remove did not remove keypaste from Cursor: $(last_message)" ;; esac
[ "$(bytes "$CURSOR")" = "$(bytes "$SEED")" ] || die "Remove did not give back Cursor's file as it was"
[ "$(bytes "$CURSOR.keypaste-backup")" = "$(bytes "$SEED")" ] || die "Remove changed the backup"
say '^preview end' connect-remove cursor
[ -z "$(last_preview)" ] || die "a second removal previewed a step: [$(last_preview)]"
[ "$(last_message)" = 'Cursor is not connected; there is nothing to remove.' ] \
  || die "a second removal did not say Cursor is not connected: $(last_message)"

grep -qF -- "$SECRET" "$HOLD_OUT" && die "a credential reached the app's output"
grep -qF -- "$OTHER_SECRET" "$HOLD_OUT" && die "a credential reached the app's output"

exec 7>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

echo "ok: the first unlock chose the vault and the CLI reached the app without --vault; a cancelled preview wrote"
echo "    nothing, Connect registered exactly the command shown with no vault in it, the check raised the app's"
echo "    prompt through that bridge and the audit file holds its prompted grant under the app's session and the"
echo "    chosen label; with another vault chosen Connect pinned this one, offering run with every item in reach,"
echo "    its bridge still reached the app, an agent with no settings was listed only the tagged entries and the"
echo "    picker asked for the chosen one's variable,"
echo "    and removal left the client listing no keypaste; Cursor's file changed only in keypaste's entry, its"
echo "    first original was kept, its registration reached the app's prompt, and removal gave back its bytes and"
echo "    then said it was not connected, with no secret or session in any configuration"
