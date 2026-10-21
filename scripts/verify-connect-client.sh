#!/usr/bin/env bash
# Connecting an AI tool from the app (2.6a, V-2.6a, G.2, N.3, G.3), across real processes: tests/Keypaste.AppDriver
# holds the vault as launch composes the app, with its prompt window drawn on a headless display and clicked,
# and presses the rows of Agents' AI tools. Everything runs with HOME and USERPROFILE at a home of the gate's
# own and KEYPASTE_HOME at its .keypaste, the default home, so no real tool's configuration is ever read or
# written. The command tool is tests/Keypaste.FakeMcpClient, a stand-in named `claude` in an absolute PATH
# directory that answers Claude Code's `--version`, `mcp add`, `remove` and `list` and keeps its servers in
# that home's .claude.json, where the app reads whether Claude Code holds keypaste. The file tool is Cursor,
# whose ~/.cursor/mcp.json the gate seeds with another server, a top-level key and a comment. The bridge is
# the shipped Release keypaste, found on PATH as the app finds it and started as keypaste mcp. A runner image
# may hold other tools of the catalogue, so only the rows of these two are asserted.
#
# The first unlock chooses the vault for agents and the CLI (G.1): `keypaste use` prints it and `keypaste
# grants` reaches the app with no --vault. Agents finds both tools and offers Connect on each. Cancelling a plan
# writes nothing. Running it registers exactly the command the plan showed, under the tool's own id, naming no
# vault and offering run with every item in reach, and the tool's row then says Connected. The check starts that
# command, which finds the chosen vault, raises the app's prompt for the one item Claude Code's settings let it
# see, and after Approve the audit file holds a prompted grant naming the app's session and the tool's id.
# keypaste doctor --json then exits 0, finding the versions, the vault, the app holding it and Claude Code's
# registration working and noting Cursor as not connected, and changes no byte under the gate's home, the audit
# file's included (D-0467). On Linux and macOS, keypaste alone, under a pseudo-terminal from `script` with no
# colour, exits 0 and prints its version, the chosen vault, the app holding it unlocked, Claude Code
# connected and the help as its next step, changing no byte under that home (D-0471). With another vault
# chosen, Connect again pins this one with --vault and that bridge still reaches the app. Disconnect leaves the
# tool listing no keypaste and its row offering Connect. For Cursor, which has no settings of its own, the plan
# is the one edit of keypaste's entry and Cancel writes nothing; Run it changes no byte outside that entry and
# keeps the seed as the backup, the check is listed only the projects' tagged items, as the defaults give, and
# asks for the one picked; Disconnect gives back the seed's bytes, leaves the backup alone, and its row offers
# Connect again. No configuration ever holds the master password, a keyfile, the session or a credential. Once
# the app has shut down, doctor notes that nothing holds the vault it held and still exits 0.
#
# NEGATIVE CONTROL: this fails if the first unlock chooses nothing, if the CLI without --vault does not reach
# the app, if a tool is not found or its state is wrong before or after Run it or Disconnect, if a cancelled
# plan writes, if what is registered differs from what was shown, if a registration is not under the tool's id,
# does not offer run or does not reach every item, if an agent with no settings is listed an untagged item, if
# the chosen vault is written into the tool or another vault is not, if the check does not reach the app's
# prompt through the registered bridge, if the audit record names another session or name, if doctor exits
# non-zero, finds the versions, the vault, the app or Claude Code's registration anything but working or Cursor
# anything but a note, or changes a byte under the gate's home or a line of the audit file, if keypaste alone on
# a pseudo-terminal exits non-zero, misses its version, a row or that next step, or changes a byte or a line of
# the audit file, if Disconnect leaves keypaste listed, if Cursor's plan or Cancel writes its file, if Run it
# changes a byte outside keypaste's entry or keeps no backup equal to the seed, if Disconnect does not give back
# the seed's bytes or touches the backup, if a secret reaches a tool's configuration or the driver's output, or
# if doctor once the app has shut down does not note that nothing holds the vault or exits non-zero. These
# checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-connect-master-pw'
readonly SECRET='SENTINEL-CONNECT-CLIENT-7a41e2'
readonly OTHER_SECRET='SENTINEL-CONNECT-OTHER-3d90b6'
readonly CLAUDE='claude-code'
readonly CURSOR_ID='cursor'

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

# The state a new `tools` prints for a tool: connect, connected, cannot-tell or problem. The blocks already
# printed are counted from the output, since this runs in the subshell of the caller's substitution.
tool_state() {
  local before
  before="$(grep -c '^tools end' "$HOLD_OUT" || true)"
  echo tools >&7
  wait_for '^tools end' "$HOLD_OUT" $((before + 1))
  awk -v n="$before" -v id="$1" '/^tools end/ { seen++; next } seen == n && $1 == "tool" && $2 == "id=" id { sub(/^state=/, "", $3); state = $3 } END { print state }' "$HOLD_OUT"
}
expect_state() {
  local state
  state="$(tool_state "$1")"
  [ "$state" = "$2" ] || die "Agents shows $1 as [$state], not $2 ($3)"
}
last_check() { grep '^check ' "$HOLD_OUT" | grep -v '^check end' | tail -1 | cut -c7-; }
last_audit() { tail -n 1 "$AUDIT" | jq -e "$@" >/dev/null; }

# What the stand-in client will start, as one line, from its own configuration.
registered() { jq -r '.mcpServers.keypaste | [.command] + .args | join(" ")' "$CONFIG"; }

# Every file and folder under $1, a line each, a file with its bytes; a lock file its holder keeps open, which
# Windows lets nothing else read, reads as held.
tree_of() {
  local entry
  find "$1" | LC_ALL=C sort | while IFS= read -r entry; do
    if [ -f "$entry" ]; then printf '%s %s\n' "$entry" "$(bytes "$entry" 2>/dev/null || echo held)"; else printf '%s\n' "$entry"; fi
  done
}

# Runs keypaste doctor --json with no vault named in the environment, holding its findings in DOCTOR and its exit
# code in DOCTOR_CODE.
doctor() {
  DOCTOR_CODE=0
  DOCTOR="$(env -u KEYPASTE_VAULT PATH="$CLIENT_PATH" "$CLI" doctor --json "$@" | tr -d '\r')" || DOCTOR_CODE=$?
}

# Whether doctor said something of $1, and everything it said of it reads $2: ok, fix or note.
doctor_says() {
  jq -e --arg subject "$1" --arg status "$2" 'map(select(.subject == $subject)) | length > 0 and all(.status == $status)' \
    <<<"$DOCTOR" >/dev/null
}

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
# Claude Code sees the one untagged item once a person lets it see env/ci/**, confirmed at keypaste approve's
# terminal before the app holds the vault; Cursor, with no settings of its own, sees only the tagged ones,
# however wide its connection is registered.
agent_settings "$CLI" "$VAULT" "$MASTER" "$CLAUDE --see env/ci/**" || die "could not save $CLAUDE's settings in the vault"

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

# ----------------------------------------------- Agents finds both tools; a cancelled plan writes nothing
expect_state "$CLAUDE" connect "before Connect"
expect_state "$CURSOR_ID" connect "before Connect"

say '^preview end' connect "$CLAUDE"
PREVIEW="$(last_preview)"
[ "$(printf '%s' "$PREVIEW" | grep -c .)" = 2 ] || die "the plan is not the two commands Connect runs: [$PREVIEW]"
[ "$(printf '%s\n' "$PREVIEW" | sed -n 1p)" = 'claude mcp remove --scope user keypaste' ] \
  || die "the plan does not clear an earlier entry first"
ADD="$(printf '%s\n' "$PREVIEW" | sed -n 2p)"
case "$ADD" in
  *" --vault "*) die "the plan writes the chosen vault into the tool: [$ADD]" ;;
  "claude mcp add --scope user --transport stdio keypaste -- "*' --client-label claude-code --allow-run --expose **') ;;
  *) die "the plan does not add the bridge under the tool's id, offering run with every item in reach: [$ADD]" ;;
esac

say '^message ' cancel
[ "$(last_message)" = 'Cancelled. Nothing was changed.' ] || die "Cancel did not say it changed nothing"
[ -e "$CONFIG" ] && die "a cancelled plan wrote the tool's configuration"

# ------------------------------------------------- running it registers exactly what the plan showed
say '^preview end' connect "$CLAUDE"
[ "$(last_preview)" = "$PREVIEW" ] || die "the same tool planned something else"
say '^message ' confirm
case "$(last_message)" in Connected.*) ;; *) die "Run it did not connect: $(last_message)" ;; esac
[ "$(registered)" = "${ADD#*keypaste -- }" ] || die "the tool registered [$(registered)], not what the plan showed"
clean_config "$CONFIG"

# ----------------------------------- the check reaches the app's prompt through the registered bridge
say '^prompt client' check
grep '^prompt client' "$HOLD_OUT" | tail -1 | grep -qF "client=keypaste-check label=$CLAUDE entry=env/ci/DEPLOY_KEY field=password" \
  || die "the prompt does not show the check, the tool's id and the one item its settings let it see"
say '^check end' approve
case "$(last_check)" in
  "Connected: you approved env/ci/DEPLOY_KEY"*) ;;
  *) die "the check did not report the approval: $(last_check)" ;;
esac
last_audit --arg s "$FIRST" --arg l "$CLAUDE" \
  '.tool == "request_credential" and .decision == "granted" and .method == "prompt" and .session == $s and .client.label == $l and .client.name == "keypaste-check"' \
  || die "the audit file does not hold a prompted grant naming the app's session and the tool's id"
expect_state "$CLAUDE" connected "after Run it"

# -------------------- doctor finds everything working while the app holds the vault, and changes nothing
LOGGED="$(lines_in "$AUDIT")"
tree_of "$WORK/user" >"$WORK/home.before"
doctor
[ "$DOCTOR_CODE" = 0 ] || die "keypaste doctor exited $DOCTOR_CODE while the app holds the vault: $DOCTOR"
for subject in versions vault owner "$CLAUDE"; do
  doctor_says "$subject" ok || die "keypaste doctor does not find $subject working while the app holds the vault: $DOCTOR"
done
doctor_says "$CURSOR_ID" note || die "keypaste doctor does not note $CURSOR_ID as not connected: $DOCTOR"
[ "$(lines_in "$AUDIT")" = "$LOGGED" ] || die "keypaste doctor added a line to the audit file"
tree_of "$WORK/user" >"$WORK/home.after"
cmp -s "$WORK/home.before" "$WORK/home.after" \
  || die "keypaste doctor changed the gate's home: $(diff "$WORK/home.before" "$WORK/home.after" | cut -c1-160 || true)"

# ------------ keypaste alone on a terminal says where things stand while the app holds it, and changes nothing
case "$(uname -s)" in
  Linux | Darwin)
    require script
    VERSION="$("$CLI" --version | tr -d '\r')"
    printf '#!/bin/sh\n"$1"\necho "exit=$?"\n' >"$WORK/bare.sh"
    chmod +x "$WORK/bare.sh"
    tree_of "$WORK/user" >"$WORK/home.before"
    # A pseudo-terminal as stdout; NO_COLOR and TERM=dumb show that being a terminal, not colour, decides.
    if [ "$(uname -s)" = Linux ]; then
      STATUS="$(env -u KEYPASTE_VAULT NO_COLOR=1 TERM=dumb PATH="$CLIENT_PATH" script -qec "$WORK/bare.sh $CLI" /dev/null </dev/null | tr -d '\r')" \
        || die "script could not run keypaste on a pseudo-terminal"
    else
      STATUS="$(env -u KEYPASTE_VAULT NO_COLOR=1 TERM=dumb PATH="$CLIENT_PATH" script -q /dev/null "$WORK/bare.sh" "$CLI" </dev/null | tr -d '\r')" \
        || die "script could not run keypaste on a pseudo-terminal"
    fi
    printf '%s\n' "$STATUS" | grep -qx 'exit=0' || die "keypaste alone on a terminal did not exit 0: $STATUS"
    printf '%s\n' "$STATUS" | grep -qxF "keypaste $VERSION" || die "keypaste alone did not print its version: $STATUS"
    printf '%s\n' "$STATUS" | grep -q '^  vault     .*vault\.kdbx' || die "keypaste alone did not name the vault: $STATUS"
    printf '%s\n' "$STATUS" | grep -qF 'chosen in the app' || die "keypaste alone did not say the vault is chosen in the app: $STATUS"
    printf '%s\n' "$STATUS" | grep -qxF '  held by   the keypaste app, unlocked' \
      || die "keypaste alone did not find the app holding the vault unlocked: $STATUS"
    printf '%s\n' "$STATUS" | grep -q '^  AI tools  Claude Code connected' || die "keypaste alone did not find Claude Code connected: $STATUS"
    printf '%s\n' "$STATUS" | grep -qxE '(›|>) `keypaste --help` lists every command' \
      || die "keypaste alone gave another next step with nothing to fix: $STATUS"
    [ "$(lines_in "$AUDIT")" = "$LOGGED" ] || die "keypaste alone added a line to the audit file"
    tree_of "$WORK/user" >"$WORK/home.after"
    cmp -s "$WORK/home.before" "$WORK/home.after" \
      || die "keypaste alone changed the gate's home: $(diff "$WORK/home.before" "$WORK/home.after" | cut -c1-160 || true)"
    ;;
  *) echo "note: keypaste alone on a terminal is checked on Linux and macOS, where script gives it a pseudo-terminal" ;;
esac

# ---------------- with another vault chosen, Connect again pins this one, and that bridge still reaches it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$OTHER_VAULT" >/dev/null 2>&1 || die "could not create the other vault"
"$CLI" use "$OTHER_VAULT" >/dev/null 2>&1 || die "keypaste use did not choose the other vault"
say '^preview end' connect "$CLAUDE"
case "$(last_preview | sed -n 2p)" in
  *" --vault "*'vault.kdbx --client-label claude-code --allow-run --expose **') ;;
  *) die "Connect again did not pin the app's vault while another is chosen: [$(last_preview)]" ;;
esac
say '^message ' confirm
case "$(last_message)" in Connected.*) ;; *) die "connecting again failed: $(last_message)" ;; esac
[ "$(registered)" = "$(last_preview | sed -n 2p | sed 's/.*keypaste -- //')" ] || die "the reconnection is not what the plan showed"
clean_config "$CONFIG"

say '^prompt client' check
grep '^prompt client' "$HOLD_OUT" | tail -1 | grep -qF "label=$CLAUDE entry=env/ci/DEPLOY_KEY field=password" \
  || die "the pinned bridge did not reach the app's prompt"
say '^check end' deny
case "$(last_check)" in
  "Connected: the request for env/ci/DEPLOY_KEY reached you and was refused."*) ;;
  *) die "the check did not report the refusal: $(last_check)" ;;
esac
last_audit --arg s "$FIRST" --arg l "$CLAUDE" '.decision == "denied" and .method == "prompt" and .session == $s and .client.label == $l' \
  || die "the audit file does not hold the refusal"

# ------------------------------------------------------- Disconnect leaves no keypaste in the tool
say '^preview end' connect-remove "$CLAUDE"
[ "$(last_preview)" = 'claude mcp remove --scope user keypaste' ] || die "the Disconnect plan is not the tool's own removal"
say '^message ' confirm
case "$(last_message)" in Removed.*) ;; *) die "Disconnect did not remove: $(last_message)" ;; esac
LISTED="$(PATH="$CLIENT_PATH" "$FAKE" mcp list)"
case "$LISTED" in *keypaste*) die "the tool still lists keypaste after Disconnect: $LISTED" ;; esac
clean_config "$CONFIG"
expect_state "$CLAUDE" connect "after Disconnect"

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

say '^preview end' connect "$CURSOR_ID"
EDIT="$(last_preview)"
[ "$(printf '%s' "$EDIT" | grep -c .)" = 1 ] || die "the Cursor plan is not the one edit Connect makes: [$EDIT]"
case "$EDIT" in
  "edit "*"mcp.json: set mcpServers.keypaste {"*"}; keeps the original as "*"mcp.json.keypaste-backup") ;;
  *) die "the Cursor plan is not the edit of keypaste's entry naming its backup: [$EDIT]" ;;
esac
ENTRY="${EDIT#*set mcpServers.keypaste }"
ENTRY="${ENTRY%; keeps the original as *}"
printf '%s' "$ENTRY" | jq -e '.args | index("--client-label") as $i | .[$i:] == ["--client-label", "cursor", "--allow-run", "--expose", "**"]' >/dev/null \
  || die "the Cursor plan does not register it under its id, offering run with every item in reach: [$ENTRY]"

say '^message ' cancel
[ "$(last_message)" = 'Cancelled. Nothing was changed.' ] || die "Cancel did not say it changed nothing"
[ "$(bytes "$CURSOR")" = "$(bytes "$SEED")" ] || die "a cancelled plan changed Cursor's file"
[ -e "$CURSOR.keypaste-backup" ] && die "a cancelled plan kept a backup"

say '^preview end' connect "$CURSOR_ID"
[ "$(last_preview)" = "$EDIT" ] || die "the same tool planned another edit"
say '^message ' confirm
case "$(last_message)" in
  "Connected. Cursor starts keypaste mcp for this vault;"*"Restart Cursor to load it.") ;;
  *) die "Run it did not connect Cursor: $(last_message)" ;;
esac
[ "$(bytes "$CURSOR.keypaste-backup")" = "$(bytes "$SEED")" ] || die "the backup is not Cursor's file as it was before the first edit"
without_keypaste "$CURSOR" >"$STRIPPED"
[ "$(bytes "$STRIPPED")" = "$(bytes "$SEED")" ] || die "Run it changed Cursor's file outside keypaste's entry"
[ "$(grep -v '^ *//' "$CURSOR" | jq -cS '.mcpServers.keypaste')" = "$(printf '%s' "$ENTRY" | jq -cS .)" ] \
  || die "Cursor's file does not hold the entry the plan showed"
clean_config "$CURSOR"
expect_state "$CURSOR_ID" connected "after Run it"

# Through a bridge that reaches every item, an agent with no settings is listed the two projects' tagged
# items, not the untagged ones, and asks for a variable.
say '^check-entry n=2' check
grep -qx 'check-entry n=1 env/alpha/.env' "$HOLD_OUT" || die "the picker does not list the first tagged item"
grep -qx 'check-entry n=2 env/beta/.env' "$HOLD_OUT" || die "the picker does not list the second tagged item"
grep -q '^check-entry n=3' "$HOLD_OUT" && die "the picker lists an item an agent with no settings does not see"
say '^prompt client' pick 2
grep '^prompt client' "$HOLD_OUT" | tail -1 | grep -qF "client=keypaste-check label=$CURSOR_ID entry=env/beta/.env field=BETA_KEY" \
  || die "the prompt is not Cursor's registration asking for the picked item's variable"
say '^check end' approve
case "$(last_check)" in
  "Connected: you approved env/beta/.env"*) ;;
  *) die "the check through Cursor's registration did not report the approval: $(last_check)" ;;
esac
last_audit --arg s "$FIRST" --arg l "$CURSOR_ID" '.decision == "granted" and .method == "prompt" and .session == $s and .client.label == $l' \
  || die "the audit file does not hold the grant through Cursor's registration"

say '^preview end' connect-remove "$CURSOR_ID"
case "$(last_preview)" in
  "edit "*"mcp.json: remove mcpServers.keypaste") ;;
  *) die "the Cursor Disconnect plan is not the edit that removes keypaste's entry: [$(last_preview)]" ;;
esac
say '^message ' confirm
case "$(last_message)" in Removed.*) ;; *) die "Disconnect did not remove keypaste from Cursor: $(last_message)" ;; esac
[ "$(bytes "$CURSOR")" = "$(bytes "$SEED")" ] || die "Disconnect did not give back Cursor's file as it was"
[ "$(bytes "$CURSOR.keypaste-backup")" = "$(bytes "$SEED")" ] || die "Disconnect changed the backup"
expect_state "$CURSOR_ID" connect "after Disconnect"

grep -qF -- "$SECRET" "$HOLD_OUT" && die "a credential reached the app's output"
grep -qF -- "$OTHER_SECRET" "$HOLD_OUT" && die "a credential reached the app's output"

exec 7>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

# ------------------------------------------- once the app has shut down, doctor notes nothing holds the vault
doctor --vault "$VAULT"
[ "$DOCTOR_CODE" = 0 ] || die "keypaste doctor exited $DOCTOR_CODE once the app shut down: $DOCTOR"
doctor_says owner note || die "keypaste doctor does not note that nothing holds the vault once the app shut down: $DOCTOR"

echo "ok: the first unlock chose the vault and the CLI reached the app without --vault; Agents found both tools,"
echo "    a cancelled plan wrote nothing, Connect registered exactly the command shown under the tool's id with no"
echo "    vault in it and the row said Connected, the check raised the app's prompt through that bridge and the"
echo "    audit file holds its prompted grant under the app's session and the tool's id; doctor found the versions,"
echo "    the vault, the app and that registration working, noted Cursor as not connected and changed nothing, as"
echo "    keypaste alone on a terminal said where things stand, where script gives it one; with"
echo "    another vault chosen Connect again pinned this one and its bridge still reached the app, and Disconnect"
echo "    left the tool listing no keypaste; Cursor's file changed only in keypaste's entry, its first original was"
echo "    kept, its registration was listed only the tagged items an agent with no settings sees and reached the"
echo "    app's prompt, and Disconnect gave back its bytes, with no secret or session in any configuration; once the"
echo "    app shut down, doctor noted that nothing holds the vault and found nothing to fix"
