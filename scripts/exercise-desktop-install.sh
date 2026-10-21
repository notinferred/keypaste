#!/usr/bin/env bash
# 4.7b: installs an attested desktop candidate on a fresh runner and exercises the installed app from outside
# its process, then classifies each check from what the app, the vault and the CLI/MCP recorded.
#
# The app carries no automation hook (D-0199). It is driven through the platform accessibility tree and real
# keystrokes: UI Automation on Windows (drive-desktop-windows.ps1), AT-SPI and xdotool on Linux
# (drive-desktop-linux.py), the accessibility API and System Events on macOS (drive-desktop-macos.js). The vault
# is made by the CLI (D-0146). The approval is a person's answer at the terminal agent, which holds the vault, so
# the app's unlock is refused and its unlock screen names that agent. A new terminal must then run the keypaste
# the install carries (G.5): on Windows the MSI puts its folder on the user PATH, and on Linux and macOS the person
# links it from Settings, which the first session does.
#
# On macOS the DMG is quarantined as a browser download and its keypaste.app copied to /Applications, where
# Gatekeeper must refuse the unsigned copy (gatekeeper); docs/desktop.md's override then clears the quarantine
# and the session runs on that copy. Whether Terminal runs the copy's launcher, quarantined and cleared, is
# recorded and not judged (4.7e).
#
# Checks, in order: candidate, install, gatekeeper (macOS), first-window, unlock, entry-list, edit, env-run,
# approval, terminal. A check passes only on its evidence, is unreached when its action could not be done or an
# earlier check failed, and is a contradiction when the action completed and the evidence disagrees. Anything but
# pass fails, except a gatekeeper check on a runner whose assessments stay disabled, which records unassessed.
#
# A rehearsal installs a branch build instead (D-0433): app.yml names its packages <version>-dryrun and attests
# none, so the candidate is checked only against the .sha256 beside it and its check is rehearsal, never pass.
# That check alone may then be rehearsal; the run is never release evidence.
#
# Usage:
#   exercise-desktop-install.sh <rid> <candidate-file> <version> <cli-dir> <output-dir>
#   exercise-desktop-install.sh --classify <output-dir>
#   exercise-desktop-install.sh --selftest
# Environment: REHEARSAL  the app.yml run whose -dryrun package <candidate-file> is, for a rehearsal
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
ROOT="$(cd "$(dirname "$SELF")/.." && pwd)"
readonly ROOT
readonly CHECKS=(candidate install first-window unlock entry-list edit env-run approval terminal)
readonly MASTER='InstallCheckMaster4b7'
readonly ENTRY_GROUP='web'
readonly ENTRY_TITLE='install-check'
readonly PROJECT='install'
readonly VARIABLE='APP_ADDED'
readonly AGENT_STATUS='Keypaste approve (process '
readonly LINK_BUTTON='Link the keypaste command'
readonly LINKED_PREFIX="A terminal runs this app's keypaste from "
readonly CR=$'\r'
readonly TAB=$'\t'

# shellcheck source=scripts/lib/desktop-drive.sh
. "$ROOT/scripts/lib/desktop-drive.sh"
# shellcheck source=scripts/lib/terminal.sh
. "$ROOT/scripts/lib/terminal.sh"
# shellcheck source=scripts/lib/macos-install.sh
. "$ROOT/scripts/lib/macos-install.sh"

usage() {
  echo 'usage: bash scripts/exercise-desktop-install.sh <rid> <candidate-file> <version> <cli-dir> <output-dir> | --classify <output-dir> | --selftest'
}

die() { echo "::error::$*" >&2; exit 1; }

# --- classification -------------------------------------------------------------------------------

verdict() { jq -cn --arg check "$1" --arg result "$2" --arg why "$3" '{check: $check, result: $result, why: $why}'; }

fact_of() { [ -s "$1/facts.jsonl" ] && jq -rs --arg k "$2" '[.[] | select(.fact == $k) | .value] | last // empty' "$1/facts.jsonl" || true; }

step() { [ -s "$1/driver.jsonl" ] && jq -rs --arg a "$2" '[.[] | select(.action == $a)] | last | if . == null then "" else "\(.status)\t\(.detail)" end' "$1/driver.jsonl" || true; }

classify() {
  local dir="$1" check result why record status detail blocked='' after_unlock nonce readback colours ran got installed=''
  if [ "$(fact_of "$dir" finished)" != true ]; then
    for check in $(checks "$(fact_of "$dir" platform)"); do verdict "$check" harness-failure 'the run left no finished record'; done
    return
  fi

  judge() {
    local action="$1"
    if [ -n "$blocked" ]; then result=unreached; why="$blocked did not pass"; return 1; fi
    record="$(step "$dir" "$action")"
    status="${record%%"$TAB"*}"; detail="${record#*"$TAB"}"
    if [ -z "$record" ]; then result=unreached; why="$action was never attempted"; return 1; fi
    if [ "$status" != ok ]; then result=unreached; why="$action: $detail"; return 1; fi
    return 0
  }

  emit() {
    verdict "$1" "$result" "$why"
    [ "$1" != install ] || installed="$result"
    # The session runs on the copy the override cleared, whatever Gatekeeper showed.
    [ "$result" = pass ] || [ "$result" = rehearsal ] || [ "$1" = gatekeeper ] || [ -n "$blocked" ] || blocked="$1"
  }

  # Gatekeeper's refusal of the quarantined copy: a dialog, no window, and no process once it is declined.
  gatekeeper_verdict() {
    local assessments dialog windows after buttons
    judge gatekeeper || return 0
    assessments="$(fact_of "$dir" gatekeeper_assessments)"; dialog="$(fact_of "$dir" gatekeeper_dialog)"
    windows="$(fact_of "$dir" gatekeeper_windows)"; after="$(fact_of "$dir" gatekeeper_after)"
    buttons="$(fact_of "$dir" gatekeeper_buttons)"
    if [ "$assessments" = 'assessments disabled' ]; then
      result=unassessed; why="Gatekeeper reports assessments disabled even after spctl --global-enable, so its refusal is left to 3.5b"
    elif [ "$assessments" != 'assessments enabled' ]; then result=unreached; why="spctl --status reported '$assessments'"
    elif ! [[ $windows =~ ^[0-9]+$ ]]; then result=unreached; why="the window list could not be read: '$windows'"
    elif [ "$windows" != 0 ]; then result=contradiction; why="the quarantined copy opened $windows window(s) while assessments are enabled"
    elif [ -z "$dialog" ]; then result=unreached; why='opening the quarantined copy showed no Gatekeeper dialog'
    elif [[ $buttons != *'Move to Trash'* || $buttons == *Open* ]]; then result=contradiction; why="the dialog is not a refusal: $dialog [$buttons]"
    elif [ -n "$after" ]; then result=contradiction; why="a keypaste-app process outlived the declined dialog: $after"
    else result=pass; why="Gatekeeper refused the quarantined copy, showing: $dialog [$buttons]"
    fi
  }

  if [ "$(fact_of "$dir" rehearsal)" = true ]; then
    if judge verify-candidate; then result=rehearsal; why="a branch build intact against its .sha256; not attested, so this run is not release evidence: $detail"
    elif [ -n "$record" ]; then result=contradiction; why="the candidate is not the branch build app.yml hashed: $detail"
    fi
  elif judge verify-candidate; then result=pass; why="$detail"
  elif [ -n "$record" ]; then result=contradiction; why="the candidate is not the attested build: $detail"
  fi
  emit candidate

  if judge install; then
    if [ -n "$(fact_of "$dir" installed_app)" ]; then result=pass; why="$detail; prompts: $(fact_of "$dir" install_prompts)"
    else result=contradiction; why='the installer succeeded and left no app to start'
    fi
  fi
  emit install

  if [ "$(fact_of "$dir" platform)" = macos ]; then
    gatekeeper_verdict
    emit gatekeeper
  fi

  if judge window && judge screenshot; then
    colours="${detail#colours=}"; colours="${colours%% *}"
    case "$colours" in ''|*[!0-9]*) colours=0 ;; esac
    if [ "$colours" -gt 1 ]; then result=pass; why="rendered: $detail"
    else result=contradiction; why="the window is a single colour: $detail"
    fi
  fi
  emit first-window

  if judge unlocked; then result=pass; why='the shell appeared after the password and Enter'; fi
  emit unlock
  after_unlock="$blocked"

  if judge entry-listed; then result=pass; why="the CLI-made entry $ENTRY_TITLE is listed"; fi
  emit entry-list

  if judge save-entry; then
    nonce="$(fact_of "$dir" nonce)"; readback="$(fact_of "$dir" username_readback)"
    if [ "$(fact_of "$dir" vault_changed_by_edit)" != true ]; then result=contradiction; why='Save completed and the vault file did not change'
    elif [ -z "$readback" ]; then result=unreached; why='no CLI/MCP read of the saved username'
    elif [ "$readback" != "$nonce" ]; then result=contradiction; why="the app saved '$nonce' and the CLI/MCP read '$readback'"
    else result=pass; why='the username saved in the app is the one keypaste mcp released'
    fi
  fi
  emit edit

  # The env run needs only an unlocked app; the approval needs only an installed one.
  blocked="$after_unlock"
  if judge add-variable; then
    ran="$(fact_of "$dir" run_output)"; got="$(fact_of "$dir" get_output)"
    if [ "$(fact_of "$dir" vault_changed_by_variable)" != true ]; then result=contradiction; why='Add completed and the vault file did not change'
    elif [ -z "$ran" ] || [ -z "$got" ]; then result=contradiction; why="keypaste run or get printed nothing for $VARIABLE"
    elif [ "$ran" != "$got" ]; then result=contradiction; why='keypaste run injected a value other than the one keypaste get reads'
    else result=pass; why="keypaste run injected the $VARIABLE the app added"
    fi
  fi
  emit env-run

  blocked=''
  [ "$installed" = pass ] || blocked=install
  if judge agent-activity && judge request; then
    if [ "$(fact_of "$dir" mcp_error)" != false ]; then result=contradiction; why='keypaste mcp reported the approved request as an error'
    elif [ "$(fact_of "$dir" audit_decision)" != granted ]; then result=contradiction; why="the audit records $(fact_of "$dir" audit_decision), not granted"
    elif [ "$(fact_of "$dir" audit_method)" != prompt ]; then result=contradiction; why="the audit records method $(fact_of "$dir" audit_method), not a person's prompt"
    else result=pass; why="a person approved at the terminal agent; the app showed: $(fact_of "$dir" agent_activity)"
    fi
  fi
  emit approval

  # What a new shell ran, against what the install or the link should make it run.
  terminal_verdict() {
    local platform expected version entries
    platform="$(fact_of "$dir" platform)"; expected="$(fact_of "$dir" terminal_expected)"
    version="$(fact_of "$dir" binary_version)"; entries="$(fact_of "$dir" path_entries)"
    result=contradiction
    why="$(terminal_mismatch "$platform" "$(fact_of "$dir" terminal)" "$expected" "$version")"
    if [ -n "$why" ]; then return
    elif [ "$platform" = windows ] && [ "$entries" != 1 ]; then why="the user PATH names the install folder ${entries:-no} times, not once"
    elif [ "$platform" = windows ]; then
      result=pass; why="a new terminal runs keypaste $version from $expected, its bridge answers, and the install folder is on the user PATH once"
    elif [ "$platform" = macos ] && [ "$(fact_of "$dir" link_target)" != "$(fact_of "$dir" installed_app)/Contents/MacOS/keypaste" ]; then
      why="$expected names $(fact_of "$dir" link_target), not the installed app's keypaste"
    elif [ "$platform" = macos ]; then
      result=pass; why="a new terminal runs keypaste $version from $expected, which names the installed app's, and its bridge answers"
    else
      result=pass; why="a new terminal runs keypaste $version from $expected and its bridge answers; the login shell read $(fact_of "$dir" shell_profile)"
    fi
  }

  # The MSI puts keypaste on PATH as it installs; on Linux and macOS, Settings links it once the app is unlocked.
  case "$(fact_of "$dir" platform)" in
    windows)
      blocked=''
      [ "$installed" = pass ] || blocked=install
      if judge terminal-shell; then terminal_verdict; fi
      ;;
    linux)
      blocked="$after_unlock"
      if judge settings && judge link-cli && judge linked && judge terminal-shell; then terminal_verdict; fi
      ;;
    # A runner has nobody to answer the administrator dialog, so its link is made with sudo while Settings offers it.
    macos)
      blocked="$after_unlock"
      if judge settings && judge link-made && judge link-cli && judge linked && judge terminal-shell; then terminal_verdict; fi
      ;;
    *) result=harness-failure; why='the run recorded no platform' ;;
  esac
  emit terminal
}

# Each check that fails the run, as an annotation. Only a rehearsal's candidate may be rehearsal rather than pass,
# and only a gatekeeper check unassessed.
refusals() {
  jq -r --arg rehearsal "$(fact_of "$1" rehearsal)" \
    'select(.result != "pass" and ($rehearsal != "true" or .check != "candidate" or .result != "rehearsal")
        and (.check != "gatekeeper" or .result != "unassessed"))
      | "::error::\(.check): \(.result): \(.why)"' "$1/verdicts.jsonl"
}

# --- recording ------------------------------------------------------------------------------------

fact() { jq -cn --arg fact "$1" --arg value "$2" '{fact: $fact, value: $value}' >> "$OUT/facts.jsonl"; }

record() { jq -cn --arg action "$1" --arg status "$2" --arg detail "$3" '{action: $action, status: $status, detail: $detail}' >> "$OUT/driver.jsonl"; }

# act <name> <command...>: runs it, records ok or refused with its output, and returns its status.
act() {
  local name="$1" status=ok
  shift
  # Not a subshell: what the command sets is kept for the steps after it.
  "$@" > "$OUT/act.log" 2>&1 || status=refused
  record "$name" "$status" "$(tail -n 20 "$OUT/act.log" | tr -d "$CR")"
  [ "$status" = ok ]
}

platform() {
  case "$RID" in
    win-x64) echo windows ;;
    linux-x64) echo linux ;;
    osx-arm64) echo macos ;;
    *) die "no installation candidate for $RID" ;;
  esac
}

checks() { if [ "$1" = macos ]; then echo "${CHECKS[*]:0:2} gatekeeper ${CHECKS[*]:2}"; else echo "${CHECKS[*]}"; fi; }

native() { if [ "$(platform)" = windows ]; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

sha256_of() { if command -v sha256sum >/dev/null; then sha256sum < "$1"; else shasum -a 256 < "$1"; fi | awk '{print $1}'; }

# A rehearsal's candidate: app.yml's package for this RID under the branch build's -dryrun version, intact
# against the .sha256 that job wrote beside it. Nothing attests a branch build (D-0433).
verify_branch_build() {
  local extension name recorded
  case "$(platform)" in windows) extension=msi ;; linux) extension=AppImage ;; macos) extension=dmg ;; esac
  name="keypaste-app-$VERSION-$RID-internal-unsigned.$extension"
  [ "$(basename "$CANDIDATE")" = "$name" ] || { echo "$(basename "$CANDIDATE") is not the branch build's package name $name"; return 1; }
  [ -f "$CANDIDATE.sha256" ] || { echo "no $name.sha256 beside the candidate"; return 1; }
  recorded="$(awk 'NR == 1 {print $1}' "$CANDIDATE.sha256" | tr -d "$CR")"
  [ "$(sha256_of "$CANDIDATE")" = "$recorded" ] || { echo "$name does not match its .sha256"; return 1; }
  echo "ok: $name from app run $REHEARSAL matches its .sha256; a branch build, not attested"
}

# --- the vault, the CLI and the agent ------------------------------------------------------------

cli() { printf '%s\n' "$MASTER" | "$KP" "$@" --vault "$VAULT_NATIVE" | tr -d '\r'; }

fixture() {
  local vault_dir
  KP="$(find "$CLI_DIR" -maxdepth 3 -type f \( -name keypaste -o -name keypaste.exe \) | head -n 1)"
  [ -n "$KP" ] || { echo "no keypaste under $CLI_DIR"; return 1; }
  chmod +x "$KP" 2>/dev/null || true
  "$KP" --version | tr -d '\r' | grep -qF "$BINARY_VERSION" || { echo "the CLI is not $BINARY_VERSION: $("$KP" --version)"; return 1; }

  HOME_DIR="$OUT/home"
  vault_dir="$OUT/vault"
  mkdir -p "$HOME_DIR" "$vault_dir"
  VAULT="$vault_dir/install-check.kdbx"
  VAULT_NATIVE="$(native "$VAULT")"
  export KEYPASTE_HOME
  KEYPASTE_HOME="$(native "$HOME_DIR")"

  # act runs this where errexit does not apply, so each step reports its own failure.
  printf '%s\n%s\n' "$MASTER" "$MASTER" | "$KP" init "$VAULT_NATIVE" >/dev/null || { echo 'keypaste init failed'; return 1; }
  printf '%s\n' "$MASTER" | "$KP" add "$ENTRY_GROUP/$ENTRY_TITLE" --username seeded --url https://example.test --notes 'made by the CLI' \
    --generate --vault "$VAULT_NATIVE" >/dev/null || { echo 'keypaste add failed'; return 1; }
  printf '%s\n%s\n' "$MASTER" seeded-value | "$KP" env set "$PROJECT" SEEDED --vault "$VAULT_NATIVE" >/dev/null \
    || { echo 'keypaste env set failed'; return 1; }

  printf '[[vault]]\npath = "%s"\nopened_at = "%s"\n' "$VAULT_NATIVE" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$HOME_DIR/recent.toml"
  printf '[[settings]]\nidle_timeout_seconds = 28800\n' > "$HOME_DIR/app.toml"
  echo "vault $VAULT_NATIVE with $ENTRY_GROUP/$ENTRY_TITLE and env/$PROJECT, made by $("$KP" --version | tr -d '\r')"
}

wait_for_change() {
  local before="$1" i
  for ((i = 0; i < 120; i++)); do
    [ "$(sha256_of "$VAULT")" != "$before" ] && { sleep 1; return 0; }
    sleep 0.25
  done
  return 1
}

# --- install, launch and stop --------------------------------------------------------------------

install_candidate() {
  if [ "$(platform)" = windows ]; then
    local code exe
    code="$(powershell -NoProfile -Command "(Start-Process msiexec.exe -ArgumentList '/i', '\"$(cygpath -w "$CANDIDATE")\"', '/qn', '/l*v', '\"$(cygpath -w "$OUT/install.log")\"' -Wait -PassThru).ExitCode" | tr -d '\r')"
    [ "$code" = 0 ] || { echo "msiexec /i exited $code"; return 1; }
    exe="$(cygpath -u "$LOCALAPPDATA")/Programs/keypaste/keypaste-app.exe"
    [ -x "$exe" ] || { echo "msiexec exited 0 and $exe does not exist"; return 1; }
    APP="$exe"
    fact installed_app "$(cygpath -m "$exe")"
    local shortcut zone
    shortcut="$(cygpath -u "$APPDATA")/Microsoft/Windows/Start Menu/Programs/keypaste.lnk"
    if [ -f "$shortcut" ]; then fact start_menu_shortcut present; else fact start_menu_shortcut absent; fi
    zone="$(powershell -NoProfile -Command "if (Get-Item -LiteralPath '$(cygpath -w "$CANDIDATE")' -Stream Zone.Identifier -ErrorAction SilentlyContinue) { 'present' } else { 'absent' }" | tr -d '\r')"
    fact install_prompts "msiexec /i /qn shows no installer UI; per-user scope requested no elevation and no UAC prompt; Zone.Identifier $zone on the MSI, and msiexec does not run SmartScreen, so a browser download's double-click prompt was not observed"
    echo "msiexec /i exited 0; installed $(cygpath -m "$exe")"
  elif [ "$(platform)" = macos ]; then
    local download quarantine
    download="$OUT/download/$(basename "$CANDIDATE")"
    APP=/Applications/keypaste.app
    [ ! -e "$APP" ] || { echo "$APP already exists on this runner"; return 1; }
    mkdir -p "$(dirname "$download")"
    cp "$CANDIDATE" "$download"
    quarantine="$(mac_quarantine "$download")" || { echo "$quarantine"; return 1; }
    fact quarantine_dmg "$quarantine"
    mac_install "$download" "$APP" || return 1
    fact installed_app "$APP"
    fact quarantined_files "$(mac_quarantined "$APP")"
    fact install_prompts "a DMG has no installer: attached read-only and keypaste.app copied to /Applications with ditto, which keeps the quarantine a Finder drag keeps, with no prompt; a person's Finder drag was not performed"
    echo "copied keypaste.app from the attached image to $APP"
  else
    mkdir -p "$OUT/app"
    APP="$OUT/app/$(basename "$CANDIDATE")"
    cp "$CANDIDATE" "$APP"
    chmod +x "$APP"
    fact installed_app "$APP"
    fact install_prompts "an AppImage has no installer: made executable and run directly, with no prompt"
    echo "copied and made executable: $APP"
  fi
}

launch() {
  local log="$OUT/app-$1.log"
  if [ "$(platform)" = windows ]; then
    # To a file, not a pipe: the app inherits PowerShell's handles, and an inherited pipe would hold this shell until the app exits.
    powershell -NoProfile -Command "\$env:KEYPASTE_HOME = '$KEYPASTE_HOME'; \$env:KEYPASTE_APPROVER = '${2:-}'; (Start-Process -FilePath '$(cygpath -w "$APP")' -RedirectStandardOutput '$(cygpath -w "$log.out")' -RedirectStandardError '$(cygpath -w "$log")' -PassThru).Id" \
      > "$OUT/app-$1.pid"
    APP_PID="$(tr -d "$CR" < "$OUT/app-$1.pid")"
  elif [ "$(platform)" = macos ]; then
    APP_PID="$(export KEYPASTE_APPROVER="${2:-}"; mac_start "$APP" "$log")" || { echo "$APP_PID"; return 1; }
  else
    KEYPASTE_APPROVER="${2:-}" "$APP" > "$log" 2>&1 &
    APP_PID=$!
  fi
  echo "started pid $APP_PID"
}

stop_app() {
  if [ "$(platform)" = windows ]; then
    taskkill //F //PID "$APP_PID" > /dev/null 2>&1 || true
  elif [ "$(platform)" = macos ]; then
    mac_quit
  else
    pkill -f "usr/bin/keypaste-app" 2>/dev/null || true
    kill "$APP_PID" 2>/dev/null || true
    wait "$APP_PID" 2>/dev/null || true
  fi
  sleep 2
}

unlock() {
  act "$1" drive type "$MASTER" && act "$1-enter" drive key Return && act "$1" drive find 'Lock now'
}

# What a new terminal runs as keypaste, read as it would read its PATH, and what it should run.
terminal_shell() {
  local said folder
  if [ "$(platform)" = windows ]; then
    folder="$(cygpath -w "$(dirname "$APP")")"
    fact terminal_expected "$folder\\keypaste.exe"
    fact path_entries "$(user_path_entries "$folder")"
  elif [ "$(platform)" = macos ]; then
    fact terminal_expected "$MAC_CLI_LINK"
    fact link_target "$(readlink "$MAC_CLI_LINK")"
  else
    fact terminal_expected "$HOME/.local/bin/keypaste"
    fact shell_profile "$(stock_login_profile)"
  fi
  said="$(terminal_keypaste "$(platform)")" || { echo "no new shell answered: $said"; return 1; }
  fact terminal "$said"
  printf '%s\n' "$said"
}

# --- the run ---------------------------------------------------------------------------------------

first_session() {
  local before
  if [ "$(platform)" = linux ]; then act accessibility enable_accessibility || return 0; fi
  launch first > /dev/null
  act window drive window 120 || return 0
  act screenshot drive screenshot "$(native "$OUT/first-window.png")" || return 0
  unlock unlocked || return 0

  case "$(platform)" in
    linux) { act settings shortcut 4 && act link-cli drive invoke "$LINK_BUTTON" && act linked drive find-prefix "$LINKED_PREFIX"; } || true ;;
    # Settings reads the link as absent first, so its Link then finds this copy already linked and asks for nothing.
    macos)
      { act settings shortcut 4 && act link-offered drive find "$LINK_BUTTON" && act link-made mac_cli_link "$APP/Contents/MacOS/keypaste" \
        && act link-cli drive invoke "$LINK_BUTTON" && act linked drive find-prefix "$LINKED_PREFIX"; } || true
      ;;
  esac

  act entry-shortcut shortcut 1 || return 0
  act entry-listed drive find "$ENTRY_TITLE" || return 0

  NONCE="edited-in-app-$RANDOM$RANDOM"
  fact nonce "$NONCE"
  before="$(sha256_of "$VAULT")"
  act select-entry drive select "$ENTRY_TITLE" \
    && act entry-menu drive toggle 'More actions' \
    && act edit-entry drive invoke 'Edit fields' \
    && act username drive set-after Username "$NONCE" \
    && act save-entry drive invoke Save \
    || return 0
  if wait_for_change "$before"; then fact vault_changed_by_edit true; else fact vault_changed_by_edit false; return 0; fi

  before="$(sha256_of "$VAULT")"
  act open-project drive select "$PROJECT" \
    && act begin-variable drive invoke 'Add a key to dev' \
    && act variable-name drive set-named 'New key' "$VARIABLE" \
    && act add-variable drive invoke Add \
    || return 0
  if wait_for_change "$before"; then fact vault_changed_by_variable true; else fact vault_changed_by_variable false; fi
}

env_run() {
  local child="${BASH:-/bin/bash}"
  fact env_list "$(cli env ls "$PROJECT" 2>&1 || true)"
  # The app adds a new key as a field of the project's home entry (D-0413).
  fact get_output "$(cli get "env/$PROJECT/.env" --field "$VARIABLE" --reveal 2>/dev/null || true)"
  fact run_output "$(printf '%s\n' "$MASTER" | "$KP" run "$PROJECT" --vault "$VAULT_NATIVE" -- "$child" -c "printf %s \"\$$VARIABLE\"" 2>/dev/null | tr -d '\r' || true)"
}

approval() {
  local pipe="keypaste-install-check-$$-$RANDOM" agent_pid response audit="$HOME_DIR/audit.jsonl"
  # y lets install-check see the group the app saved into, which is in no project; h then allows the request for the hour.
  printf '%s\ny\nh\n' "$MASTER" | "$KP" approve --vault "$VAULT_NATIVE" --approver "$pipe" --approval-timeout 55 \
    > /dev/null 2> "$OUT/agent-stderr.txt" &
  agent_pid=$!
  for _ in $(seq 1 150); do
    grep -q 'listening on' "$OUT/agent-stderr.txt" 2>/dev/null && break
    kill -0 "$agent_pid" 2>/dev/null || break
    sleep 0.2
  done
  act agent-listening grep 'listening on' "$OUT/agent-stderr.txt" || { kill "$agent_pid" 2>/dev/null || true; return 0; }
  act agent-settings "$KP" clients install-check --see "$ENTRY_GROUP/**" --vault "$VAULT_NATIVE" --approver "$pipe" || true

  launch second "$pipe" > /dev/null
  if act second-window drive window 120 && act second-unlock drive type "$MASTER" && act second-unlock-enter drive key Return \
    && act agent-activity drive find-prefix "$AGENT_STATUS"; then
    fact agent_activity "$(step "$OUT" agent-activity | cut -f2)"
  fi

  response="$OUT/mcp-response.jsonl"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"install-check","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY_GROUP/$ENTRY_TITLE\",\"field\":\"username\",\"reason\":\"4.7b reads back the username the installed app saved\",\"ttl_seconds\":60}}}"
    sleep 10
  } | "$KP" mcp --vault "$VAULT_NATIVE" --expose "$ENTRY_GROUP/**" --approver "$pipe" \
      --client-label install-check > "$response" 2> "$OUT/mcp-stderr.txt" || true
  act request jq -e 'select(.id == 2) | .result' "$response" || true

  fact mcp_error "$(jq -r 'select(.id == 2) | .result.isError' "$response" 2>/dev/null | tr -d '\r')"
  fact username_readback "$(jq -r 'select(.id == 2) | .result.structuredContent.value // empty' "$response" 2>/dev/null | tr -d '\r')"
  fact audit_decision "$(jq -rs '[.[] | select(.decision != null)] | last | .decision // empty' "$audit" 2>/dev/null | tr -d '\r')"
  fact audit_method "$(jq -rs '[.[] | select(.decision != null)] | last | .method // empty' "$audit" 2>/dev/null | tr -d '\r')"

  stop_app
  kill "$agent_pid" 2>/dev/null || true
  wait "$agent_pid" 2>/dev/null || true
}

# shellcheck disable=SC2016 # PowerShell expands its own variables, not bash.
environment() {
  local os
  case "$(platform)" in
    linux) os="$(sed -n 's/^PRETTY_NAME="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' /etc/os-release) $(uname -r)" ;;
    windows) os="$(powershell -NoProfile -Command '(Get-CimInstance Win32_OperatingSystem | ForEach-Object { "$($_.Caption) $($_.Version)" })' | tr -d '\r')" ;;
    macos) os="$(sw_vers -productName) $(sw_vers -productVersion) ($(sw_vers -buildVersion)); Terminal $(defaults read /System/Applications/Utilities/Terminal.app/Contents/Info CFBundleShortVersionString)" ;;
  esac
  jq -n \
    --arg rid "$RID" \
    --arg version "$VERSION" \
    --arg os "$os" \
    --arg image "${ImageOS:-local} ${ImageVersion:-}" \
    --arg sha "${GITHUB_SHA:-$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || echo unknown)}" \
    --arg run "${GITHUB_RUN_ID:-local}" \
    --arg candidate "$(basename "$CANDIDATE")" \
    --arg candidateSha256 "$(sha256_of "$CANDIDATE")" \
    --arg display "${DISPLAY:-}" \
    --arg fusermount "$(command -v fusermount3 || command -v fusermount || true)" \
    --arg appRun "${REHEARSAL:-}" \
    '{rid: $rid, version: $version, os: $os, runnerImage: $image, sha: $sha, run: $run, candidate: $candidate, candidateSha256: $candidateSha256, display: $display, fusermount: $fusermount, rehearsal: ($appRun != ""), appRun: (if $appRun == "" then null else $appRun end)}'
}

run_all() {
  local refused cleared=true
  mkdir -p "$OUT"
  : > "$OUT/facts.jsonl"
  : > "$OUT/driver.jsonl"
  [ -f "$CANDIDATE" ] || die "no candidate at $CANDIDATE"
  if [ "$(platform)" = linux ] && [ -z "${DISPLAY:-}" ]; then die 'DISPLAY is unset; start Xvfb and a window manager first'; fi
  local verify=(bash "$ROOT/scripts/verify-desktop-candidate.sh" "$CANDIDATE" "$VERSION" "$RID")
  BINARY_VERSION="$VERSION"
  if [ -n "${REHEARSAL:-}" ]; then
    case "$REHEARSAL" in *[!0-9]*) die "REHEARSAL names an app.yml run, and '$REHEARSAL' is not a run ID" ;; esac
    case "$VERSION" in *-dryrun) ;; *) die "a rehearsal installs a branch build, whose version ends -dryrun, not $VERSION" ;; esac
    BINARY_VERSION="${VERSION%-dryrun}"
    verify=(verify_branch_build)
    fact rehearsal true
    fact app_run "$REHEARSAL"
  fi
  fact platform "$(platform)"
  fact binary_version "$BINARY_VERSION"

  # The candidate is verified here, before anything else, so no refused package is ever installed.
  if act verify-candidate "${verify[@]}" && act fixture fixture && act install install_candidate; then
    if [ "$(platform)" = windows ]; then act terminal-shell terminal_shell || true; fi
    if [ "$(platform)" = macos ]; then
      act gatekeeper mac_gatekeeper "$APP" || true
      fact launcher_quarantined "$(mac_launcher "$APP")"
      act override xattr -dr com.apple.quarantine "$APP" || cleared=false
      fact quarantined_files_after_override "$(mac_quarantined "$APP")"
      # A copy still quarantined would only show Gatekeeper's dialog again, so the session is not attempted.
      [ "$(mac_quarantined "$APP")" = 0 ] || cleared=false
      fact launcher_cleared "$(mac_launcher "$APP")"
    fi
    if [ "$cleared" = true ]; then first_session || true; fi
    stop_app
    if [ "$(platform)" != windows ]; then act terminal-shell terminal_shell || true; fi
    if [ "$(step "$OUT" add-variable | cut -f1)" = ok ]; then env_run; fi
    approval || true
  fi
  fact finished true

  classify "$OUT" > "$OUT/verdicts.jsonl"
  cat "$OUT/verdicts.jsonl"
  jq -n --argjson environment "$(environment)" --slurpfile checks "$OUT/verdicts.jsonl" \
    --slurpfile facts "$OUT/facts.jsonl" \
    '{environment: $environment, checks: $checks, facts: ($facts | map({(.fact): .value}) | add)}' > "$OUT/observation.json"

  echo "observation: $(jq -r '[.checks[] | "\(.check)=\(.result)"] | join(" ")' "$OUT/observation.json")"
  refused="$(refusals "$OUT")"
  if [ -n "$refused" ]; then
    printf '%s\n' "$refused" >&2
    return 1
  fi
}

# --- self-test ------------------------------------------------------------------------------------

selftest() {
  local work cases=0 failures=0
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN

  # A complete, passing run's records; each fixture below changes one thing.
  passing() {
    local dir="$work/$1"
    mkdir -p "$dir"
    printf '%s\n' \
      '{"action":"verify-candidate","status":"ok","detail":"ok: built by app.yml"}' \
      '{"action":"fixture","status":"ok","detail":"vault made"}' \
      '{"action":"install","status":"ok","detail":"msiexec /i exited 0"}' \
      '{"action":"window","status":"ok","detail":"window keypaste"}' \
      '{"action":"screenshot","status":"ok","detail":"colours=412 size=1000x680"}' \
      '{"action":"unlocked","status":"ok","detail":"found Lock now"}' \
      '{"action":"entry-listed","status":"ok","detail":"found install-check"}' \
      '{"action":"save-entry","status":"ok","detail":"invoked Save"}' \
      '{"action":"add-variable","status":"ok","detail":"invoked Add"}' \
      '{"action":"agent-activity","status":"ok","detail":"Keypaste approve (process 4242) holds this vault, and agents reach it there."}' \
      '{"action":"request","status":"ok","detail":"{}"}' \
      '{"action":"terminal-shell","status":"ok","detail":"C:\\Users\\runner\\AppData\\Local\\Programs\\keypaste\\keypaste.exe"}' > "$dir/driver.jsonl"
    printf '%s\n' \
      '{"fact":"platform","value":"windows"}' \
      '{"fact":"binary_version","value":"0.5.0-rc.1"}' \
      '{"fact":"installed_app","value":"C:/Users/runner/AppData/Local/Programs/keypaste/keypaste-app.exe"}' \
      '{"fact":"install_prompts","value":"none"}' \
      '{"fact":"nonce","value":"edited-in-app-1"}' \
      '{"fact":"vault_changed_by_edit","value":"true"}' \
      '{"fact":"vault_changed_by_variable","value":"true"}' \
      '{"fact":"get_output","value":"generated"}' \
      '{"fact":"run_output","value":"generated"}' \
      '{"fact":"agent_activity","value":"Keypaste approve (process 4242) holds this vault, and agents reach it there."}' \
      '{"fact":"mcp_error","value":"false"}' \
      '{"fact":"username_readback","value":"edited-in-app-1"}' \
      '{"fact":"audit_decision","value":"granted"}' \
      '{"fact":"audit_method","value":"prompt"}' \
      '{"fact":"terminal_expected","value":"C:\\Users\\runner\\AppData\\Local\\Programs\\keypaste\\keypaste.exe"}' \
      '{"fact":"path_entries","value":"1"}' \
      '{"fact":"terminal","value":"C:\\Users\\runner\\AppData\\Local\\Programs\\keypaste\\keypaste.exe\t0.5.0-rc.1\tusage: keypaste mcp [--vault <path>]"}' \
      '{"fact":"finished","value":"true"}' > "$dir/facts.jsonl"
  }
  # The passing run on Linux, where the first session links keypaste from Settings.
  passing_linux() {
    passing "$1"
    printf '%s\n' \
      '{"action":"settings","status":"ok","detail":"sent ctrl+4"}' \
      '{"action":"link-cli","status":"ok","detail":"invoked Link the keypaste command"}' \
      '{"action":"linked","status":"ok","detail":"A terminal runs this app\u0027s keypaste from /home/runner/.local/bin/keypaste."}' >> "$work/$1/driver.jsonl"
    set_fact "$1" platform linux
    set_fact "$1" installed_app /home/runner/work/keypaste/install-desktop/app/keypaste-app-0.5.0-rc.1-linux-x64-internal-unsigned.AppImage
    set_fact "$1" terminal_expected /home/runner/.local/bin/keypaste
    set_fact "$1" terminal $'/home/runner/.local/bin/keypaste\t0.5.0-rc.1\tusage: keypaste mcp [--vault <path>]'
    set_fact "$1" shell_profile /home/runner/.profile
  }
  # The passing run on macOS: Gatekeeper refuses the quarantined copy, and Settings links keypaste once sudo made the link.
  passing_macos() {
    passing_linux "$1"
    printf '%s\n' \
      '{"action":"gatekeeper","status":"ok","detail":"dialog: [...]"}' \
      '{"action":"link-made","status":"ok","detail":"/usr/local/bin/keypaste -> /Applications/keypaste.app/Contents/MacOS/keypaste"}' >> "$work/$1/driver.jsonl"
    set_fact "$1" platform macos
    set_fact "$1" installed_app /Applications/keypaste.app
    set_fact "$1" gatekeeper_assessments 'assessments enabled'
    set_fact "$1" gatekeeper_dialog 'keypaste is damaged and can not be opened. You should move it to the Trash.'
    set_fact "$1" gatekeeper_buttons 'Cancel, Move to Trash'
    set_fact "$1" gatekeeper_windows 0
    set_fact "$1" gatekeeper_after ''
    set_fact "$1" terminal_expected /usr/local/bin/keypaste
    set_fact "$1" link_target /Applications/keypaste.app/Contents/MacOS/keypaste
    set_fact "$1" terminal $'/usr/local/bin/keypaste\t0.5.0-rc.1\tusage: keypaste mcp [--vault <path>]'
  }
  refuse_action() { jq -c --arg a "$2" --arg d "$3" 'if .action == $a then .status = "refused" | .detail = $d else . end' "$work/$1/driver.jsonl" > "$work/t" && mv "$work/t" "$work/$1/driver.jsonl"; }
  drop_action() { jq -c --arg a "$2" 'select(.action != $a)' "$work/$1/driver.jsonl" > "$work/t" && mv "$work/t" "$work/$1/driver.jsonl"; }
  set_fact() { jq -cn --arg f "$2" --arg v "$3" '{fact: $f, value: $v}' >> "$work/$1/facts.jsonl"; }
  drop_fact() { jq -c --arg f "$2" 'select(.fact != $f)' "$work/$1/facts.jsonl" > "$work/t" && mv "$work/t" "$work/$1/facts.jsonl"; }

  expect() { # expect <fixture> <check> <result>
    local actual
    cases=$((cases + 1))
    actual="$("$BASH" "$SELF" --classify "$work/$1" | jq -rs --arg c "$2" '[.[] | select(.check == $c) | .result] | last // "missing"')"
    if [ "$actual" != "$3" ]; then
      echo "::error::selftest $1: $2 classified $actual, expected $3" >&2
      failures=$((failures + 1))
    fi
  }

  expect_run() { # expect_run <fixture> <accepted|refused>: whether the run's exit status accepts its verdicts
    local actual=accepted
    cases=$((cases + 1))
    [ -z "$(refusals "$work/$1")" ] || actual=refused
    if [ "$actual" != "$2" ]; then
      echo "::error::selftest $1: the run is $actual, expected $2" >&2
      failures=$((failures + 1))
    fi
  }
  verdicts() { "$BASH" "$SELF" --classify "$work/$1" > "$work/$1/verdicts.jsonl"; }

  passing all-pass
  passing candidate-refused
  refuse_action candidate-refused verify-candidate 'does not match its .sha256'
  drop_action candidate-refused install
  passing no-window
  refuse_action no-window window 'no window for process 4242 within 120 s'
  passing fuse-missing
  refuse_action fuse-missing window 'no accessible window named keypaste within 120 s'
  set_fact fuse-missing install_prompts 'Error: No suitable fusermount binary found on the PATH'
  passing flat-window
  refuse_action flat-window screenshot 'x'
  jq -c 'if .action == "screenshot" then .status = "ok" | .detail = "colours=1 size=1000x680" else . end' "$work/flat-window/driver.jsonl" > "$work/t" && mv "$work/t" "$work/flat-window/driver.jsonl"
  passing not-unlocked
  refuse_action not-unlocked unlocked "nothing named 'Lock now' appeared"
  passing edit-not-read-back
  set_fact edit-not-read-back username_readback seeded
  passing edit-no-readback
  set_fact edit-no-readback username_readback ''
  passing edit-unsaved
  set_fact edit-unsaved vault_changed_by_edit false
  passing env-mismatch
  set_fact env-mismatch run_output other
  passing automatic-release
  set_fact automatic-release audit_method automatic
  passing denied
  set_fact denied audit_decision denied
  set_fact denied mcp_error true
  passing no-agent-screen
  refuse_action no-agent-screen agent-activity "nothing starting 'Keypaste approve (process ' appeared"
  passing unfinished
  jq -c 'select(.fact != "finished")' "$work/unfinished/facts.jsonl" > "$work/t" && mv "$work/t" "$work/unfinished/facts.jsonl"
  passing terminal-other-version
  set_fact terminal-other-version terminal $'C:\\Users\\runner\\AppData\\Local\\Programs\\keypaste\\keypaste.exe\t0.4.0\tusage: keypaste mcp'
  passing terminal-elsewhere
  set_fact terminal-elsewhere terminal $'C:\\tools\\keypaste.exe\t0.5.0-rc.1\tusage: keypaste mcp'
  passing terminal-other-case
  set_fact terminal-other-case terminal $'c:\\users\\runner\\appdata\\local\\programs\\keypaste\\keypaste.exe\t0.5.0-rc.1\tusage: keypaste mcp'
  passing terminal-two-entries
  set_fact terminal-two-entries path_entries 2
  passing terminal-no-bridge
  set_fact terminal-no-bridge terminal $'C:\\Users\\runner\\AppData\\Local\\Programs\\keypaste\\keypaste.exe\t0.5.0-rc.1\tunknown command: mcp'
  passing terminal-absent
  set_fact terminal-absent terminal absent
  passing_linux linux-pass
  passing_linux linux-not-linked
  refuse_action linux-not-linked link-cli "no button 'Link the keypaste command'"
  passing_linux linux-not-unlocked
  refuse_action linux-not-unlocked unlocked "nothing named 'Lock now' appeared"
  passing_linux linux-absent
  set_fact linux-absent terminal absent
  passing_linux linux-fails
  set_fact linux-fails terminal $'fails\t/home/runner/.local/bin/keypaste exited 127: not found'
  passing_macos macos-pass
  passing_macos macos-opened
  set_fact macos-opened gatekeeper_windows 1
  passing_macos macos-no-dialog
  set_fact macos-no-dialog gatekeeper_dialog ''
  passing_macos macos-outlived
  set_fact macos-outlived gatekeeper_after '4242 T /private/var/folders/x/T/AppTranslocation/y/d/keypaste.app/Contents/MacOS/keypaste-app'
  passing_macos macos-unassessed
  set_fact macos-unassessed gatekeeper_assessments 'assessments disabled'
  passing_macos macos-gatekeeper-refused
  refuse_action macos-gatekeeper-refused gatekeeper 'no dialog to read'
  passing_macos macos-other-target
  set_fact macos-other-target link_target /Users/runner/Downloads/keypaste.app/Contents/MacOS/keypaste
  passing_macos macos-not-linked
  refuse_action macos-not-linked link-made '/usr/local/bin/keypaste already exists'
  passing_macos macos-unfinished
  drop_fact macos-unfinished finished
  passing_macos macos-not-refusal
  set_fact macos-not-refusal gatekeeper_buttons 'Cancel, Open'
  passing_macos macos-windows-unread
  set_fact macos-windows-unread gatekeeper_windows unread
  passing_macos macos-spctl-odd
  set_fact macos-spctl-odd gatekeeper_assessments 'spctl: unable to read the policy'
  passing rehearsal
  set_fact rehearsal rehearsal true
  set_fact rehearsal app_run 4242
  passing rehearsal-not-intact
  set_fact rehearsal-not-intact rehearsal true
  refuse_action rehearsal-not-intact verify-candidate 'does not match its .sha256'
  drop_action rehearsal-not-intact install
  passing rehearsal-unmarked
  set_fact rehearsal-unmarked rehearsal true

  local check
  for check in "${CHECKS[@]}"; do expect all-pass "$check" pass; done
  expect candidate-refused candidate contradiction
  expect candidate-refused install unreached
  expect candidate-refused approval unreached
  expect no-window install pass
  expect no-window first-window unreached
  expect no-window unlock unreached
  expect no-window edit unreached
  expect no-window env-run unreached
  expect fuse-missing first-window unreached
  expect flat-window first-window contradiction
  expect flat-window unlock unreached
  expect not-unlocked unlock unreached
  expect not-unlocked entry-list unreached
  expect not-unlocked env-run unreached
  expect edit-not-read-back edit contradiction
  expect edit-not-read-back approval pass
  expect edit-no-readback edit unreached
  expect edit-unsaved edit contradiction
  expect env-mismatch env-run contradiction
  expect env-mismatch edit pass
  expect automatic-release approval contradiction
  expect denied approval contradiction
  expect no-agent-screen approval unreached
  expect unfinished candidate harness-failure
  expect unfinished approval harness-failure
  expect unfinished terminal harness-failure
  expect candidate-refused terminal unreached
  expect not-unlocked terminal pass
  expect terminal-other-version terminal contradiction
  expect terminal-elsewhere terminal contradiction
  expect terminal-other-case terminal pass
  expect terminal-two-entries terminal contradiction
  expect terminal-no-bridge terminal contradiction
  expect terminal-absent terminal contradiction
  for check in "${CHECKS[@]}"; do expect linux-pass "$check" pass; done
  expect linux-not-linked terminal unreached
  expect linux-not-linked entry-list pass
  expect linux-not-unlocked terminal unreached
  expect linux-absent terminal contradiction
  expect linux-fails terminal contradiction
  for check in $(checks macos); do expect macos-pass "$check" pass; done
  expect macos-opened gatekeeper contradiction
  expect macos-opened first-window pass
  expect macos-no-dialog gatekeeper unreached
  expect macos-no-dialog unlock pass
  expect macos-outlived gatekeeper contradiction
  expect macos-unassessed gatekeeper unassessed
  expect macos-unassessed terminal pass
  expect macos-gatekeeper-refused gatekeeper unreached
  expect macos-other-target terminal contradiction
  expect macos-not-linked terminal unreached
  expect macos-not-linked entry-list pass
  expect macos-unfinished gatekeeper harness-failure
  expect macos-not-refusal gatekeeper contradiction
  expect macos-windows-unread gatekeeper unreached
  expect macos-spctl-odd gatekeeper unreached
  expect rehearsal candidate rehearsal
  for check in "${CHECKS[@]:1}"; do expect rehearsal "$check" pass; done
  expect rehearsal-not-intact candidate contradiction
  expect rehearsal-not-intact install unreached

  verdicts all-pass
  expect_run all-pass accepted
  verdicts rehearsal
  expect_run rehearsal accepted
  verdicts rehearsal-not-intact
  expect_run rehearsal-not-intact refused
  verdicts terminal-two-entries
  expect_run terminal-two-entries refused
  verdicts macos-pass
  expect_run macos-pass accepted
  verdicts macos-unassessed
  expect_run macos-unassessed accepted
  verdicts macos-opened
  expect_run macos-opened refused
  # A rehearsal verdict the run did not mark as one is refused.
  verdicts rehearsal-unmarked
  drop_fact rehearsal-unmarked rehearsal
  expect_run rehearsal-unmarked refused

  if [ "$failures" -gt 0 ]; then
    die "exercise-desktop-install selftest: $failures of $cases cases failed"
  fi
  echo "exercise-desktop-install selftest: $cases cases"
}

case "${1:-}" in
  --selftest) selftest ;;
  --classify) [ $# -eq 2 ] || { usage >&2; exit 2; }; classify "$2" ;;
  -h|--help) usage ;;
  '') usage >&2; exit 2 ;;
  *)
    [ $# -eq 5 ] || { usage >&2; exit 2; }
    RID="$1"
    CANDIDATE="$2"
    VERSION="$3"
    CLI_DIR="$4"
    mkdir -p "$5"
    # Absolute, because LaunchServices starts the macOS app from / with the paths the fixture wrote.
    OUT="$(cd "$5" && pwd -P)"
    run_all
    ;;
esac
