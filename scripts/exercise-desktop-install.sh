#!/usr/bin/env bash
# 4.7b: installs an attested desktop candidate on a fresh runner and exercises the installed app from outside
# its process, then classifies each check from what the app, the vault and the CLI/MCP recorded.
#
# The app carries no automation hook (D-0199). It is driven through the platform accessibility tree and real
# keystrokes: UI Automation on Windows (drive-desktop-windows.ps1), AT-SPI and xdotool on Linux
# (drive-desktop-linux.py). The vault is made by the CLI (D-0146). The app has no approval screen yet, so the
# approval is a person's y at the terminal agent, with the app's Agent Activity screen reporting that agent.
#
# Checks, in order: candidate, install, first-window, unlock, entry-list, edit, env-run, approval. A check
# passes only on its evidence, is unreached when its action could not be done or an earlier check failed,
# and is a contradiction when the action completed and the evidence disagrees. Anything but pass fails.
#
# Usage:
#   exercise-desktop-install.sh <rid> <candidate-file> <version> <cli-dir> <output-dir>
#   exercise-desktop-install.sh --classify <output-dir>
#   exercise-desktop-install.sh --selftest
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
ROOT="$(cd "$(dirname "$SELF")/.." && pwd)"
readonly ROOT
readonly CHECKS=(candidate install first-window unlock entry-list edit env-run approval)
readonly MASTER='InstallCheckMaster4b7'
readonly ENTRY_GROUP='web'
readonly ENTRY_TITLE='install-check'
readonly PROJECT='install'
readonly VARIABLE='APP_ADDED'
readonly AGENT_STATUS='A keypaste agent is running'
readonly CR=$'\r'

usage() {
  echo 'usage: bash scripts/exercise-desktop-install.sh <rid> <candidate-file> <version> <cli-dir> <output-dir> | --classify <output-dir> | --selftest'
}

die() { echo "::error::$*" >&2; exit 1; }

# --- classification -------------------------------------------------------------------------------

verdict() { jq -cn --arg check "$1" --arg result "$2" --arg why "$3" '{check: $check, result: $result, why: $why}'; }

fact_of() { [ -s "$1/facts.jsonl" ] && jq -rs --arg k "$2" '[.[] | select(.fact == $k) | .value] | last // empty' "$1/facts.jsonl" || true; }

step() { [ -s "$1/driver.jsonl" ] && jq -rs --arg a "$2" '[.[] | select(.action == $a)] | last | if . == null then "" else "\(.status)\t\(.detail)" end' "$1/driver.jsonl" || true; }

classify() {
  local dir="$1" check result why record status detail blocked='' after_unlock nonce readback colours ran got
  local -A results=()
  if [ "$(fact_of "$dir" finished)" != true ]; then
    for check in "${CHECKS[@]}"; do verdict "$check" harness-failure 'the run left no finished record'; done
    return
  fi

  judge() {
    local action="$1"
    if [ -n "$blocked" ]; then result=unreached; why="$blocked did not pass"; return 1; fi
    record="$(step "$dir" "$action")"
    status="${record%%$'\t'*}"; detail="${record#*$'\t'}"
    if [ -z "$record" ]; then result=unreached; why="$action was never attempted"; return 1; fi
    if [ "$status" != ok ]; then result=unreached; why="$action: $detail"; return 1; fi
    return 0
  }

  emit() {
    verdict "$1" "$result" "$why"
    results[$1]="$result"
    [ "$result" = pass ] || [ -n "$blocked" ] || blocked="$1"
  }

  if judge verify-candidate; then result=pass; why="$detail"
  elif [ -n "$record" ]; then result=contradiction; why="the candidate is not the attested build: $detail"
  fi
  emit candidate

  if judge install; then
    if [ -n "$(fact_of "$dir" installed_app)" ]; then result=pass; why="$detail; prompts: $(fact_of "$dir" install_prompts)"
    else result=contradiction; why='the installer succeeded and left no app to start'
    fi
  fi
  emit install

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
    else result=pass; why='the username saved in the app is the one keypaste-mcp released'
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
  [ "${results[install]}" = pass ] || blocked=install
  if judge agent-activity && judge request; then
    if [ "$(fact_of "$dir" mcp_error)" != false ]; then result=contradiction; why='keypaste-mcp reported the approved request as an error'
    elif [ "$(fact_of "$dir" audit_decision)" != granted ]; then result=contradiction; why="the audit records $(fact_of "$dir" audit_decision), not granted"
    elif [ "$(fact_of "$dir" audit_method)" != prompt ]; then result=contradiction; why="the audit records method $(fact_of "$dir" audit_method), not a person's prompt"
    else result=pass; why="a person approved at the terminal agent; the app showed: $(fact_of "$dir" agent_activity)"
    fi
  fi
  emit approval
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
    *) die "no installation candidate for $RID" ;;
  esac
}

native() { if [ "$(platform)" = windows ]; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

drive() {
  if [ "$(platform)" = windows ]; then
    local action="$1"
    shift
    powershell -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$ROOT/scripts/drive-desktop-windows.ps1")" \
      -Action "$action" -AppPid "$APP_PID" -First "${1:-}" -Second "${2:-}"
  else
    python3 "$ROOT/scripts/drive-desktop-linux.py" "$@"
  fi
}

sha256_of() { sha256sum < "$1" | awk '{print $1}'; }

# --- the vault, the CLI and the agent ------------------------------------------------------------

cli() { printf '%s\n' "$MASTER" | "$KP" "$@" --vault "$VAULT_NATIVE" | tr -d '\r'; }

fixture() {
  local vault_dir
  KP="$(find "$CLI_DIR" -maxdepth 3 -type f \( -name keypaste -o -name keypaste.exe \) | head -n 1)"
  MCP="$(find "$CLI_DIR" -maxdepth 3 -type f \( -name keypaste-mcp -o -name keypaste-mcp.exe \) | head -n 1)"
  [ -n "$KP" ] && [ -n "$MCP" ] || { echo "no keypaste or keypaste-mcp under $CLI_DIR"; return 1; }
  chmod +x "$KP" "$MCP" 2>/dev/null || true
  "$KP" --version | tr -d '\r' | grep -qF "$VERSION" || { echo "the CLI is not $VERSION: $("$KP" --version)"; return 1; }

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
  printf 'idle_timeout_seconds = 28800\n' > "$HOME_DIR/app.toml"
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
  else
    KEYPASTE_APPROVER="${2:-}" "$APP" > "$log" 2>&1 &
    APP_PID=$!
  fi
  echo "started pid $APP_PID"
}

stop_app() {
  if [ "$(platform)" = windows ]; then
    taskkill //F //PID "$APP_PID" > /dev/null 2>&1 || true
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

# --- the run ---------------------------------------------------------------------------------------

# Avalonia publishes its AT-SPI tree only once the session's accessibility bus says a reader is present.
enable_accessibility() {
  local property
  for property in IsEnabled ScreenReaderEnabled; do
    gdbus call --session --dest org.a11y.Bus --object-path /org/a11y/bus \
      --method org.freedesktop.DBus.Properties.Set org.a11y.Status "$property" '<true>' || return 1
  done
  gdbus call --session --dest org.a11y.Bus --object-path /org/a11y/bus --method org.a11y.Bus.GetAddress
}

first_session() {
  local before
  if [ "$(platform)" = linux ]; then act accessibility enable_accessibility || return 0; fi
  launch first > /dev/null
  act window drive window 120 || return 0
  act screenshot drive screenshot "$(native "$OUT/first-window.png")" || return 0
  unlock unlocked || return 0

  act entry-shortcut drive key ctrl+1 || return 0
  act entry-listed drive find "$ENTRY_TITLE" || return 0

  NONCE="edited-in-app-$RANDOM$RANDOM"
  fact nonce "$NONCE"
  before="$(sha256_of "$VAULT")"
  act select-entry drive select "$ENTRY_TITLE" \
    && act edit-entry drive invoke Edit \
    && act username drive set-after Username "$NONCE" \
    && act save-entry drive invoke Save \
    || return 0
  if wait_for_change "$before"; then fact vault_changed_by_edit true; else fact vault_changed_by_edit false; return 0; fi

  before="$(sha256_of "$VAULT")"
  act env-shortcut drive key ctrl+2 \
    && act open-project drive invoke Open \
    && act begin-variable drive invoke 'Add variable' \
    && act variable-name drive set-only "$VARIABLE" \
    && act add-variable drive invoke Add \
    || return 0
  if wait_for_change "$before"; then fact vault_changed_by_variable true; else fact vault_changed_by_variable false; fi
}

env_run() {
  local child="${BASH:-/bin/bash}"
  fact env_list "$(cli env ls "$PROJECT" 2>&1 || true)"
  fact get_output "$(cli get "env/$PROJECT/$VARIABLE" --show 2>/dev/null || true)"
  fact run_output "$(printf '%s\n' "$MASTER" | "$KP" run "$PROJECT" --vault "$VAULT_NATIVE" -- "$child" -c "printf %s \"\$$VARIABLE\"" 2>/dev/null | tr -d '\r' || true)"
}

approval() {
  local pipe="keypaste-install-check-$$-$RANDOM" agent_pid response audit="$OUT/audit.jsonl"
  printf '%s\ny\n' "$MASTER" | "$KP" agent --vault "$VAULT_NATIVE" --approver "$pipe" --approval-timeout 55 \
    > /dev/null 2> "$OUT/agent-stderr.txt" &
  agent_pid=$!
  for _ in $(seq 1 150); do
    grep -q 'listening on' "$OUT/agent-stderr.txt" 2>/dev/null && break
    kill -0 "$agent_pid" 2>/dev/null || break
    sleep 0.2
  done
  act agent-listening grep 'listening on' "$OUT/agent-stderr.txt" || { kill "$agent_pid" 2>/dev/null || true; return 0; }

  launch second "$pipe" > /dev/null
  if act second-window drive window 120 && unlock second-unlock && act activity-shortcut drive key ctrl+3 \
    && act check-again drive invoke 'Check again' && act agent-activity drive find-prefix "$AGENT_STATUS"; then
    fact agent_activity "$(step "$OUT" agent-activity | cut -f2)"
  fi

  response="$OUT/mcp-response.jsonl"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"install-check","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY_GROUP/$ENTRY_TITLE\",\"field\":\"username\",\"reason\":\"4.7b reads back the username the installed app saved\",\"ttl_seconds\":60}}}"
    sleep 10
  } | "$MCP" --vault "$VAULT_NATIVE" --expose "$ENTRY_GROUP/**" --audit-log "$(native "$audit")" --approver "$pipe" \
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
    '{rid: $rid, version: $version, os: $os, runnerImage: $image, sha: $sha, run: $run, candidate: $candidate, candidateSha256: $candidateSha256, display: $display, fusermount: $fusermount}'
}

run_all() {
  mkdir -p "$OUT"
  : > "$OUT/facts.jsonl"
  : > "$OUT/driver.jsonl"
  [ -f "$CANDIDATE" ] || die "no candidate at $CANDIDATE"
  if [ "$(platform)" = linux ] && [ -z "${DISPLAY:-}" ]; then die 'DISPLAY is unset; start Xvfb and a window manager first'; fi

  # The candidate is verified here, before anything else, so no refused package is ever installed.
  if act verify-candidate bash "$ROOT/scripts/verify-desktop-candidate.sh" "$CANDIDATE" "$VERSION" "$RID" \
    && act fixture fixture && act install install_candidate; then
    first_session || true
    stop_app
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
  if jq -e '[.checks[] | select(.result != "pass")] | length > 0' "$OUT/observation.json" >/dev/null; then
    jq -r '.checks[] | select(.result != "pass") | "::error::\(.check): \(.result): \(.why)"' "$OUT/observation.json" >&2
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
      '{"action":"agent-activity","status":"ok","detail":"A keypaste agent is running. Approvals appear in that terminal."}' \
      '{"action":"request","status":"ok","detail":"{}"}' > "$dir/driver.jsonl"
    printf '%s\n' \
      '{"fact":"installed_app","value":"C:/Users/runner/AppData/Local/Programs/keypaste/keypaste-app.exe"}' \
      '{"fact":"install_prompts","value":"none"}' \
      '{"fact":"nonce","value":"edited-in-app-1"}' \
      '{"fact":"vault_changed_by_edit","value":"true"}' \
      '{"fact":"vault_changed_by_variable","value":"true"}' \
      '{"fact":"get_output","value":"generated"}' \
      '{"fact":"run_output","value":"generated"}' \
      '{"fact":"agent_activity","value":"A keypaste agent is running."}' \
      '{"fact":"mcp_error","value":"false"}' \
      '{"fact":"username_readback","value":"edited-in-app-1"}' \
      '{"fact":"audit_decision","value":"granted"}' \
      '{"fact":"audit_method","value":"prompt"}' \
      '{"fact":"finished","value":"true"}' > "$dir/facts.jsonl"
  }
  refuse_action() { jq -c --arg a "$2" --arg d "$3" 'if .action == $a then .status = "refused" | .detail = $d else . end' "$work/$1/driver.jsonl" > "$work/t" && mv "$work/t" "$work/$1/driver.jsonl"; }
  drop_action() { jq -c --arg a "$2" 'select(.action != $a)' "$work/$1/driver.jsonl" > "$work/t" && mv "$work/t" "$work/$1/driver.jsonl"; }
  set_fact() { printf '{"fact":"%s","value":"%s"}\n' "$2" "$3" >> "$work/$1/facts.jsonl"; }

  expect() { # expect <fixture> <check> <result>
    local actual
    cases=$((cases + 1))
    actual="$(bash "$SELF" --classify "$work/$1" | jq -rs --arg c "$2" '[.[] | select(.check == $c) | .result] | last // "missing"')"
    if [ "$actual" != "$3" ]; then
      echo "::error::selftest $1: $2 classified $actual, expected $3" >&2
      failures=$((failures + 1))
    fi
  }

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
  passing policy-release
  set_fact policy-release audit_method policy
  passing denied
  set_fact denied audit_decision denied
  set_fact denied mcp_error true
  passing no-agent-screen
  refuse_action no-agent-screen agent-activity "nothing starting 'A keypaste agent is running' appeared"
  passing unfinished
  jq -c 'select(.fact != "finished")' "$work/unfinished/facts.jsonl" > "$work/t" && mv "$work/t" "$work/unfinished/facts.jsonl"

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
  expect policy-release approval contradiction
  expect denied approval contradiction
  expect no-agent-screen approval unreached
  expect unfinished candidate harness-failure
  expect unfinished approval harness-failure

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
    OUT="$5"
    run_all
    ;;
esac
