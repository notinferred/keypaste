#!/usr/bin/env bash
# keypaste setup and doctor against the real AI tools (G.2, G.3, D-0438), as install-real-tools.sh puts them on a
# runner at their pinned versions: Claude Code, Codex, Gemini CLI, Devin, VS Code and Cursor on every runner, and
# Claude Desktop on macOS and Windows. It writes those tools' own configuration in the runner account's home, so it
# refuses to run anywhere but GitHub Actions. keypaste runs with a KEYPASTE_HOME of its own and a vault chosen
# with keypaste use, so setup pins --vault, --audit-log, --clients and --approver (D-0389), and the bridge is the
# built keypaste.
#
# Seeded first: VS Code's, Cursor's and Claude Desktop's files each hold another server and another top-level key,
# VS Code's a comment too, in directories created as each app's first run would, since none has run. On Windows the
# installed Claude package's family name must be the one keypaste's catalog looks under. Each command tool has run
# once, so its own first-run files are written before anything is compared.
#
# Without --yes and without a terminal, setup changes nothing. Before anything is connected, setup --remove calls
# every tool not connected and changes no byte of any tool's configuration. setup --yes configures every tool, and
# each tool with a reader of its own confirms it: gemini mcp list shows keypaste connected, which starts the bridge,
# and the pinned audit log then exists while the default home's does not; claude mcp get, codex mcp get --json and
# devin mcp get find the entry; and Cursor's agent mcp list lists it on Linux and macOS. VS Code's code --add-mcp
# writes the same entry from the same fields into a throwaway profile, but VS Code never reads the real file back.
# Every tool's entry starts the bridge with the pinned vault, audit log, ceilings and approver, the tool's id as its label, run
# offered and every item in reach, which each agent's settings narrow, and each seeded file keeps everything else.
# Before any tool has started the bridge, keypaste doctor finds every tool's registration working and nothing to
# fix, and changes no tool's configuration, the pinned audit log or the default home's (D-0467).
# VS Code, Claude Desktop and, on Windows, Cursor are checked at the file level only. setup --remove --dry-run then
# finds every registration, so each of keypaste's readers reads the file its tool wrote on this runner, and on
# Windows starts Codex and Gemini CLI through node.exe, never cmd.exe. The backups equal the seeds. A second connect
# changes no entry and no backup. Removing leaves no tool listing keypaste and gives each seeded file back byte for
# byte, and a second removal calls every tool not connected. No configuration or backup holds the master password
# or names a keyfile.
#
# NEGATIVE CONTROL: this fails if setup changes a file without --yes, if a removal with nothing to remove runs a
# tool or changes a byte, if any tool is not configured, if keypaste doctor finds something to fix or a configured
# tool's registration anything but working, or changes a tool's configuration, the pinned audit log or the default
# home's, if a tool's reader does not find keypaste, if Gemini CLI's bridge does not reach the pinned audit log, if
# an entry names another program, vault, audit log, ceilings, approver or label or leaves out run or every item's
# reach, if a seeded file loses another entry, if VS Code would write the entry differently, if keypaste's reader
# for a tool misses the entry that tool wrote, if Windows starts a tool through cmd.exe, if a backup differs from
# its seed or changes on a second connect, if removal leaves keypaste anywhere or a seeded file changed, if a second
# removal is not a no-op, or if a secret reaches a configuration. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-real-tools-master-pw'

# shellcheck source=scripts/lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='OUT ERR'
require jq

[ "${GITHUB_ACTIONS:-}" = true ] \
  || die "verify-real-tools.sh writes the real AI tools' configuration in this account's home; it runs only on a GitHub runner"

case "$(uname -s)" in
  Linux*) platform=linux ;;
  Darwin*) platform=macos ;;
  MINGW* | MSYS* | CYGWIN*) platform=windows ;;
  *) die "no real-tools check for $(uname -s)" ;;
esac

for tool in claude codex gemini devin code; do require "$tool"; done
[ "$platform" = windows ] || require agent

TIMEOUT=timeout
if command -v gtimeout >/dev/null 2>&1; then TIMEOUT=gtimeout; fi

# The Ubuntu image sets XDG_CONFIG_HOME to its default; the tools run as a stock account, which sets none and reads
# the same directories.
unset XDG_CONFIG_HOME
unset KEYPASTE_VAULT KEYPASTE_APPROVER

CLI="$(keypaste_bin)"
CLI="$(cd "$(dirname "$CLI")" && pwd)/$(basename "$CLI")"
CATALOG_PACKAGE="$(sed -n 's/.*ClaudeDesktopPackage = "\([^"]*\)".*/\1/p' src/Keypaste.Core/Clients/McpConfigLocation.cs)"

WORK="$(mktemp -d)"
trap 'cd /; rm -rf "$WORK" || true' EXIT
mkdir -p "$WORK/project" "$WORK/home"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
VAULT="$(native "$WORK/vault.kdbx")"
OUT="$WORK/out.txt"
ERR="$WORK/err.txt"

# A path keypaste printed, as this shell opens it.
unix() { if command -v cygpath >/dev/null 2>&1; then cygpath -u "$1"; else printf '%s' "$1"; fi; }

if [ "$platform" = windows ]; then
  H="$(cygpath -u "$USERPROFILE")"
  APPDATA_DIR="$(cygpath -u "$APPDATA")"
else
  H="$HOME"
fi
CLAUDE_FILE="$H/.claude.json"
CODEX_FILE="$H/.codex/config.toml"
GEMINI_FILE="$H/.gemini/settings.json"
CURSOR_FILE="$H/.cursor/mcp.json"
case "$platform" in
  linux)
    DEVIN_FILE="$H/.config/devin/mcp_config.json"
    VSCODE_FILE="$H/.config/Code/User/mcp.json"
    ;;
  macos)
    DEVIN_FILE="$H/.config/devin/mcp_config.json"
    VSCODE_FILE="$H/Library/Application Support/Code/User/mcp.json"
    DESKTOP_FILE="$H/Library/Application Support/Claude/claude_desktop_config.json"
    ;;
  windows)
    DEVIN_FILE="$APPDATA_DIR/devin/mcp_config.json"
    VSCODE_FILE="$APPDATA_DIR/Code/User/mcp.json"
    DESKTOP_FILE="$APPDATA_DIR/Claude/claude_desktop_config.json"
    ;;
esac
DEFAULT_AUDIT="$H/.keypaste/audit.jsonl"

TOOLS='claude-code codex gemini-cli devin vscode cursor'
FILE_TOOLS='vscode cursor'
CONFIGS=("$CLAUDE_FILE" "$CODEX_FILE" "$GEMINI_FILE" "$DEVIN_FILE" "$VSCODE_FILE" "$CURSOR_FILE")
if [ "$platform" != linux ]; then
  TOOLS="$TOOLS claude-desktop"
  FILE_TOOLS="$FILE_TOOLS claude-desktop"
  CONFIGS+=("$DESKTOP_FILE")
fi

file_of() {
  case "$1" in
    claude-code) echo "$CLAUDE_FILE" ;;
    gemini-cli) echo "$GEMINI_FILE" ;;
    devin) echo "$DEVIN_FILE" ;;
    vscode) echo "$VSCODE_FILE" ;;
    cursor) echo "$CURSOR_FILE" ;;
    claude-desktop) echo "$DESKTOP_FILE" ;;
  esac
}
servers_of() { if [ "$1" = vscode ]; then echo servers; else echo mcpServers; fi; }

# VS Code's file is JSON with comments, which jq does not read; the seed's comment is a whole line.
json() { sed '/^[[:space:]]*\/\//d' "$1"; }

# A file's bytes, or absent.
contents() { if [ -e "$1" ]; then bytes "$1"; else echo absent; fi; }

# Every tool's own configuration, a line per file: its bytes, or absent.
snapshot() {
  local file
  for file in "${CONFIGS[@]}"; do printf '%s %s\n' "$file" "$(contents "$file")"; done
}

# Runs keypaste setup with no terminal, holding its output in OUT and ERR and its exit code in CODE.
run_setup() {
  CODE=0
  "$CLI" setup "$@" < /dev/null > "$OUT" 2> "$ERR" || CODE=$?
  tr -d '\r' < "$OUT" > "$OUT.lf" && mv "$OUT.lf" "$OUT"
  tr -d '\r' < "$ERR" > "$ERR.lf" && mv "$ERR.lf" "$ERR"
}

# Each tool setup reports on a line of its own, in the form the pattern gives.
every_tool() {
  local id
  for id in $TOOLS; do
    grep -Eq "^  $id +$1" "$OUT" || die "setup did not report $id as $2"
  done
}

header() { sed -n -E "s/^$1 +//p" "$OUT" | head -n 1; }

# The arguments every tool's entry starts the bridge with, labelled with the tool's id.
expected_args() {
  jq -cn --arg vault "$PIN_VAULT" --arg audit "$PIN_AUDIT" --arg clients "$PIN_CLIENTS" --arg approver "$PIN_APPROVER" --arg label "$1" \
    '["mcp", "--vault", $vault, "--audit-log", $audit, "--clients", $clients, "--approver", $approver, "--client-label", $label, "--allow-run", "--expose", "**"]'
}

# The first object in a tool's JSON that names a command and its arguments.
launch_of() { jq -c 'first(.. | objects | select(has("command") and has("args"))) | {command, args}'; }

entry_starts_the_bridge() {
  local id="$1" file key
  file="$(file_of "$id")"
  key="$(servers_of "$id")"
  json "$file" | jq -e --arg key "$key" --arg command "$BRIDGE" --argjson args "$(expected_args "$id")" \
    '.[$key].keypaste | .command == $command and .args == $args' >/dev/null \
    || die "$file does not start the bridge with the pinned settings for $id: $(json "$file" | jq -c --arg key "$key" '.[$key].keypaste')"
}

# Each tool's keypaste entry, as Codex's reader or the tool's file gives it.
entries() {
  local id
  codex mcp get keypaste --json 2> "$ERR" | launch_of || die "codex mcp get keypaste --json failed"
  for id in claude-code gemini-cli devin $FILE_TOOLS; do
    json "$(file_of "$id")" | jq -cS --arg key "$(servers_of "$id")" '.[$key].keypaste' || die "could not read $(file_of "$id")"
  done
}

no_secret() {
  local file
  for file in "$@"; do
    [ -e "$file" ] || continue
    grep -qF -- "$MASTER" "$file" && die "$file holds the master password"
    grep -qi 'keyfile' "$file" && die "$file names a keyfile"
  done
  return 0
}

# On a GitHub runner Gemini CLI passes a server only a handful of variables, and so not the .NET runtime's location
# that this gate's framework-dependent build needs on macOS; without GITHUB_SHA it filters as on a person's machine,
# where every variable whose name holds KEY, KEYPASTE_HOME among them, is still dropped.
gemini_list() { env -u GITHUB_SHA GEMINI_CLI_TRUST_WORKSPACE=true gemini mcp list; }

same_file() {
  if command -v cygpath >/dev/null 2>&1; then
    [ "$(cygpath -m -l "$1" | tr '[:upper:]' '[:lower:]')" = "$(cygpath -m -l "$2" | tr '[:upper:]' '[:lower:]')" ]
  else
    [ "$1" -ef "$2" ]
  fi
}

# ------------------------------------------------------------------------------ a vault of its own, chosen
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
"$CLI" use "$VAULT" >/dev/null || die "keypaste use did not choose the vault"
CHOSEN="$("$CLI" use 2>&1 | tr -d '\r\357\273\277')" || die "keypaste use printed no chosen vault: [$CHOSEN]"
same_file "$CHOSEN" "$VAULT" || die "keypaste use chose [$CHOSEN], not [$VAULT]"
cd "$WORK/project"

# ------------------------------------------------------------------------------------------------- seeds
step 'seed the file tools with a server and a key of their own'
mkdir -p "$(dirname "$VSCODE_FILE")" "$(dirname "$CURSOR_FILE")"
cat > "$VSCODE_FILE" <<'JSON'
{
  // A person's own server, which connecting and removing keypaste must leave alone.
  "servers": {
    "other": {
      "type": "stdio",
      "command": "other-server",
      "args": ["--port", "4000"]
    }
  },
  "inputs": []
}
JSON
cat > "$CURSOR_FILE" <<'JSON'
{
  "mcpServers": {
    "other": {
      "command": "other-server",
      "args": ["--port", "4000"]
    }
  },
  "seededBy": "verify-real-tools.sh"
}
JSON
if [ "$platform" != linux ]; then
  mkdir -p "$(dirname "$DESKTOP_FILE")"
  cp "$CURSOR_FILE" "$DESKTOP_FILE"
fi
for id in $FILE_TOOLS; do cp "$(file_of "$id")" "$WORK/seed.$id"; done

if [ "$platform" = windows ]; then
  [ -n "$CATALOG_PACKAGE" ] || die "could not read the catalog's Claude Desktop package family"
  FAMILY="$(powershell.exe -NoProfile -NonInteractive -Command '(Get-AppxPackage -Name Claude).PackageFamilyName' | tr -d '\r')"
  [ "$FAMILY" = "$CATALOG_PACKAGE" ] || die "the installed Claude package's family is [$FAMILY], not the catalog's $CATALOG_PACKAGE"
  echo "Claude Desktop's package family is $FAMILY, as the catalog has it"
fi

for tool in claude codex gemini devin; do
  version="$("$tool" --version 2>&1)" || die "$tool --version failed: $version"
  printf '%s: %s\n' "$tool" "$(printf '%s\n' "$version" | head -n 1)"
done
DEFAULT_AUDIT_EXISTED=false
[ ! -e "$DEFAULT_AUDIT" ] || DEFAULT_AUDIT_EXISTED=true

# ---------------------------------------------------------------- without --yes or a terminal, nothing changes
step 'setup without --yes and without a terminal changes nothing'
BEFORE="$(snapshot)"
run_setup
[ "$CODE" = 1 ] || die "setup without --yes exited $CODE, not 1"
grep -qF -- '--yes is required' "$ERR" || die "setup without --yes did not say --yes is required"
[ "$(snapshot)" = "$BEFORE" ] || die "setup without --yes changed a tool's configuration"

# -------------------------------------------------------------- nothing connected: removal is a no-op per tool
step 'before anything is connected, removing calls every tool not connected and changes nothing'
run_setup --remove --yes
[ "$CODE" = 0 ] || die "a removal with nothing connected exited $CODE"
every_tool 'not connected; nothing to remove$' 'not connected'
[ "$(snapshot)" = "$BEFORE" ] || die "a removal with nothing to remove changed a tool's configuration"

# ------------------------------------------------------------------------------------------------ connect
step 'setup --yes configures every tool'
run_setup --yes
[ "$CODE" = 0 ] || die "setup --yes exited $CODE"
every_tool 'configured(;|$)' configured
BRIDGE="$(header 'keypaste mcp')"
PIN_VAULT="$(header vault)"
PIN_AUDIT="$(header 'audit log')"
PIN_CLIENTS="$(header ceilings)"
PIN_APPROVER="$(header approver)"
[ -n "$BRIDGE" ] && [ -n "$PIN_AUDIT" ] && [ -n "$PIN_CLIENTS" ] && [ -n "$PIN_APPROVER" ] \
  || die "setup under a KEYPASTE_HOME of its own did not pin an audit log, ceilings and an approver"
[ "$PIN_VAULT" = "$CHOSEN" ] || die "setup pinned the vault [$PIN_VAULT], not the chosen [$CHOSEN]"
for id in $FILE_TOOLS; do
  grep -Eq "^  $id +configured; the original is kept as .*\\.keypaste-backup" "$OUT" || die "setup did not name $id's backup"
done
no_secret "${CONFIGS[@]}"

# -------------------------------------------------------------- keypaste doctor, before any bridge has run
step 'keypaste doctor finds every registration working and nothing to fix, and changes nothing'
CONFIGURED="$(snapshot)"
PINNED_LOG="$(contents "$(unix "$PIN_AUDIT")")"
CODE=0
PATH="$(dirname "$CLI"):$PATH" "$CLI" doctor --json < /dev/null 2> "$ERR" | tr -d '\r' > "$OUT" || CODE=$?
[ "$CODE" = 0 ] || die "keypaste doctor exited $CODE after setup --yes"
for id in $TOOLS; do
  jq -e --arg id "$id" 'map(select(.subject == $id)) | length > 0 and all(.status == "ok")' "$OUT" >/dev/null \
    || die "keypaste doctor does not find $id's registration working"
done
[ "$(snapshot)" = "$CONFIGURED" ] || die "keypaste doctor changed a tool's configuration"
[ "$(contents "$(unix "$PIN_AUDIT")")" = "$PINNED_LOG" ] || die "keypaste doctor changed the pinned audit log $PIN_AUDIT"
[ "$DEFAULT_AUDIT_EXISTED" = true ] || [ ! -e "$DEFAULT_AUDIT" ] || die "keypaste doctor wrote the default home's audit log $DEFAULT_AUDIT"

# ------------------------------------------------------------------------------- each tool's own reader
step "Gemini CLI starts the bridge it registered, which takes its home's settings from its arguments"
rm -f "$(unix "$PIN_AUDIT")"
gemini_list > "$OUT" 2>&1 || die "gemini mcp list failed"
grep -Eq 'keypaste: .* - Connected' "$OUT" || die "gemini mcp list does not show keypaste connected"
[ -e "$(unix "$PIN_AUDIT")" ] || die "Gemini CLI's bridge did not open the pinned audit log $PIN_AUDIT"
[ "$DEFAULT_AUDIT_EXISTED" = true ] || [ ! -e "$DEFAULT_AUDIT" ] || die "a bridge wrote the default home's audit log $DEFAULT_AUDIT"

step 'Claude Code, Codex and Devin find the entry'
claude mcp get keypaste > "$OUT" 2>&1 || die "claude mcp get keypaste failed"
grep -qF -- "$BRIDGE" "$OUT" || die "claude mcp get keypaste does not name the bridge $BRIDGE"
codex mcp get keypaste --json > "$OUT" 2> "$ERR" || die "codex mcp get keypaste failed"
[ "$(launch_of < "$OUT")" = "$(jq -cn --arg command "$BRIDGE" --argjson args "$(expected_args codex)" '{command: $command, args: $args}')" ] \
  || die "Codex starts [$(launch_of < "$OUT")], not the bridge with the pinned settings"
devin mcp get keypaste > "$OUT" 2>&1 || die "devin mcp get keypaste failed"
grep -qF keypaste "$OUT" || die "devin mcp get keypaste does not name keypaste"

if [ "$platform" != windows ]; then
  step "Cursor's Agent CLI lists keypaste"
  agent mcp list > "$OUT" 2>&1 || die "agent mcp list failed"
  grep -qF keypaste "$OUT" || die "agent mcp list does not list keypaste"
fi

step "each tool's file starts the bridge with the pinned settings and keeps everything else"
for id in claude-code gemini-cli devin $FILE_TOOLS; do entry_starts_the_bridge "$id"; done
for id in $FILE_TOOLS; do
  key="$(servers_of "$id")"
  [ "$(json "$(file_of "$id")" | jq -S --arg key "$key" 'del(.[$key].keypaste)')" = "$(json "$WORK/seed.$id" | jq -S .)" ] \
    || die "$(file_of "$id") lost or changed something besides keypaste's entry"
done
json "$VSCODE_FILE" | jq -e '.servers.keypaste.type == "stdio"' >/dev/null || die "VS Code's entry is not typed stdio"
# Claude Desktop, and Cursor on Windows, where its Agent CLI does not install, are checked at the file level only:
# nothing here starts either app to load it.

step 'VS Code writes the same entry from the same fields'
mkdir -p "$WORK/vscode"
FIELDS="$(json "$VSCODE_FILE" | jq -c '.servers.keypaste + {name: "keypaste"}')"
MSYS2_ARG_CONV_EXCL='*' "$TIMEOUT" 120 code --user-data-dir "$(native "$WORK/vscode")" --add-mcp "$FIELDS" > "$OUT" 2>&1 \
  || die "code --add-mcp failed"
[ "$(json "$WORK/vscode/User/mcp.json" | jq -S '.servers.keypaste')" = "$(json "$VSCODE_FILE" | jq -S '.servers.keypaste')" ] \
  || die "VS Code writes $(json "$WORK/vscode/User/mcp.json" | jq -c '.servers.keypaste'), not keypaste's $(json "$VSCODE_FILE" | jq -c '.servers.keypaste')"

# ---------------------------------------------------------------------------------- keypaste's own readers
step "keypaste's reader for every tool finds the entry that tool wrote"
run_setup --remove --dry-run
[ "$CODE" = 0 ] || die "setup --remove --dry-run exited $CODE"
! grep -q 'not connected' "$OUT" || die "keypaste reads a tool as not connected that holds keypaste: $(grep 'not connected' "$OUT")"
for id in claude-code codex gemini-cli devin; do
  grep -Eq "^  $id +[a-z]+ mcp remove" "$OUT" || die "setup --remove --dry-run shows no remove command for $id"
done
for id in $FILE_TOOLS; do
  grep -Eq "^  $id +edit .*: remove $(servers_of "$id")\\.keypaste" "$OUT" || die "setup --remove --dry-run shows no edit of $id's file"
done
if [ "$platform" = windows ]; then
  if grep -i 'starts: .*cmd\.exe' "$OUT"; then die "setup would start a tool through cmd.exe"; fi
  for id in codex gemini-cli; do
    grep -Eiq "^  $id +starts: .*node\\.exe" "$OUT" || die "setup does not start $id's npm launcher as node.exe: $(grep -E "^  $id +starts:" "$OUT")"
  done
fi

step 'the backups are the seeds'
for id in $FILE_TOOLS; do
  cmp -s "$(file_of "$id").keypaste-backup" "$WORK/seed.$id" || die "$(file_of "$id").keypaste-backup is not the seed"
done

# ----------------------------------------------------------------------------------------------- idempotent
step 'a second connect changes no entry and no backup'
entries > "$WORK/entries.first"
run_setup --yes
[ "$CODE" = 0 ] || die "a second setup --yes exited $CODE"
every_tool 'configured(;|$)' configured
entries > "$WORK/entries.second"
cmp -s "$WORK/entries.first" "$WORK/entries.second" \
  || die "a second connect changed an entry: $(diff "$WORK/entries.first" "$WORK/entries.second" || true)"
for id in $FILE_TOOLS; do
  cmp -s "$(file_of "$id").keypaste-backup" "$WORK/seed.$id" || die "a second connect changed $id's backup"
done

# -------------------------------------------------------------------------------------------------- remove
step 'removing leaves no tool listing keypaste and gives each seeded file back'
run_setup --remove --yes
[ "$CODE" = 0 ] || die "setup --remove --yes exited $CODE"
every_tool 'removed(;|$)' removed
! claude mcp get keypaste > "$OUT" 2>&1 || die "Claude Code still holds keypaste"
! codex mcp get keypaste --json > "$OUT" 2>&1 || die "Codex still holds keypaste"
! devin mcp get keypaste > "$OUT" 2>&1 || die "Devin still holds keypaste"
gemini_list > "$OUT" 2>&1 || die "gemini mcp list failed"
! grep -qF keypaste "$OUT" || die "Gemini CLI still lists keypaste"
if [ "$platform" != windows ]; then
  agent mcp list > "$OUT" 2>&1 || die "agent mcp list failed"
  ! grep -qF keypaste "$OUT" || die "Cursor's Agent CLI still lists keypaste"
fi
for id in claude-code gemini-cli devin; do
  [ ! -e "$(file_of "$id")" ] || json "$(file_of "$id")" | jq -e --arg key "$(servers_of "$id")" '.[$key] // {} | has("keypaste") | not' >/dev/null \
    || die "$(file_of "$id") still holds keypaste"
done
for id in $FILE_TOOLS; do
  cmp -s "$(file_of "$id")" "$WORK/seed.$id" || die "removing keypaste did not give $(file_of "$id") back as seeded"
  cmp -s "$(file_of "$id").keypaste-backup" "$WORK/seed.$id" || die "removing keypaste changed $id's backup"
done

step 'a second removal calls every tool not connected'
run_setup --remove --yes
[ "$CODE" = 0 ] || die "a second removal exited $CODE"
every_tool 'not connected; nothing to remove$' 'not connected'

no_secret "${CONFIGS[@]}"
for id in $FILE_TOOLS; do no_secret "$(file_of "$id").keypaste-backup"; done

echo
echo "ok: keypaste setup connected, read back and removed $(echo "$TOOLS" | wc -w | tr -d ' ') real AI tools on $platform, and keypaste doctor found each one working"
