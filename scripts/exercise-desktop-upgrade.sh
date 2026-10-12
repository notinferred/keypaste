#!/usr/bin/env bash
# 4.7d: installs one internal desktop candidate, fills a real ~/.keypaste and vault with it, then upgrades,
# interrupts an upgrade, refuses a downgrade and uninstalls, checking after every transition that the user's
# data is byte-identical and still readable.
#
# The candidates are three unpublished builds of one commit at distinct numeric versions, because an MSI
# ProductVersion is numeric and two candidates of one version cannot be ordered (D-0139, D-0147). The CLI/MCP
# that makes the data is the published release, kept apart from the packages, so no upgrade can change it
# (D-0206).
#
# After each transition a new terminal must run the keypaste the installed app carries (G.5). On Windows the
# MSI appends its folder to the user Path, keeps one entry through an upgrade and removes only that entry on
# uninstall, which an entry of the user's own, added first, shows. On Linux the upgrade is a download left
# beside the old image: the lower app links keypaste from Settings, a start of the higher image while the lower
# app runs hands off to it (D-0397) and still re-points the link, the higher app then starts on its own, a later
# start of the lower one leaves the link, and the person then deletes the lower image. Removing the image
# leaves the link naming a missing file, which is recorded as a known limit, not checked.
#
# A rehearsal makes the data with a CLI/MCP built from this commit instead (D-0433). The candidates are never
# attested either way, so no check changes; the run is never release evidence.
#
# This runs against the real user profile - the install folder, the Start menu entry, HKCU, ~/.keypaste and the
# login files a new terminal reads - because that is what the checks are about. It therefore refuses to start
# unless the caller states that the profile is disposable: CI=true on a fresh runner, or
# KEYPASTE_UPGRADE_SANDBOX=1 for a throwaway account or virtual machine. On Windows, environment variables
# cannot move an MSI: Windows Installer resolves the install folder, the Start menu and HKCU through known
# folders, so redirecting LOCALAPPDATA sandboxes nothing (D-0206).
#
# The interrupted install fails inside the transaction, from a copy whose cabinet cannot be extracted, and
# only a log naming the failed action counts: an install that never failed proves no recovery (D-0213).
#
# Checks, in order: install-lower, fixture, upgrade, data-after-upgrade, interrupted, downgrade-refused,
# uninstall. A check passes only on its evidence, is unreached when its action could not be done or an
# earlier check failed, and is a contradiction when the action completed and the evidence disagrees.
# downgrade-refused is not-applicable on Linux, where an AppImage has no installer to refuse one.
#
# Usage:
#   exercise-desktop-upgrade.sh <rid> <candidates-dir> <cli-version> <cli-dir> <output-dir>
#   exercise-desktop-upgrade.sh --classify <output-dir>
#   exercise-desktop-upgrade.sh --selftest
# Environment: KPXC_CLI    keepassxc-cli, if it is not on PATH
#              REHEARSAL  1 when <cli-dir> holds a CLI/MCP built from this commit, not a published release
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
ROOT="$(cd "$(dirname "$SELF")/.." && pwd)"
readonly ROOT
readonly CHECKS=(install-lower fixture upgrade data-after-upgrade interrupted downgrade-refused uninstall)
readonly LOWER='0.0.1-upgrade'
readonly HIGHER='0.0.2-upgrade'
readonly THIRD='0.0.3-upgrade'
readonly MASTER='UpgradeCheckMaster47d'
readonly ENTRY='web/upgrade-check'
readonly PROJECT='upgrade'
readonly HOME_ENTRY='.env'
readonly VARIABLE='UPGRADE_TOKEN'
readonly VALUES=(first-value second-value third-value)
readonly HISTORY_TITLES=3
readonly DOWNGRADE_MESSAGE='A newer keypaste is already installed.'
readonly INSTALL_FAILED='Action ended.*InstallFinalize. Return value 3|Action ended.*InstallFiles. Return value 3'
readonly LINK_BUTTON='Link the keypaste command'
readonly LINKED_PREFIX="A terminal runs this app's keypaste from "
readonly CR=$'\r'

# shellcheck source=scripts/lib/desktop-drive.sh
. "$ROOT/scripts/lib/desktop-drive.sh"
# shellcheck source=scripts/lib/terminal.sh
. "$ROOT/scripts/lib/terminal.sh"

usage() {
  echo 'usage: bash scripts/exercise-desktop-upgrade.sh <rid> <candidates-dir> <cli-version> <cli-dir> <output-dir> | --classify <output-dir> | --selftest'
}

die() { echo "::error::$*" >&2; exit 1; }

# --- classification -------------------------------------------------------------------------------

verdict() { jq -cn --arg check "$1" --arg result "$2" --arg why "$3" '{check: $check, result: $result, why: $why}'; }

fact_of() { [ -s "$1/facts.jsonl" ] && jq -rs --arg k "$2" '[.[] | select(.fact == $k) | .value] | last // empty' "$1/facts.jsonl" || true; }

step() { [ -s "$1/driver.jsonl" ] && jq -rs --arg a "$2" '[.[] | select(.action == $a)] | last | if . == null then "" else "\(.status)\t\(.detail)" end' "$1/driver.jsonl" || true; }

classify() {
  local dir="$1" check result why record status detail blocked='' platform made
  if [ "$(fact_of "$dir" finished)" != true ]; then
    for check in "${CHECKS[@]}"; do verdict "$check" harness-failure 'the run left no finished record'; done
    return
  fi
  platform="$(fact_of "$dir" platform)"

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
    case "$result" in pass | not-applicable) ;; *) [ -n "$blocked" ] || blocked="$1" ;; esac
  }

  # Whether the new terminal started after <when> ran keypaste <version> from where the MSI or the link puts
  # it, with one user Path entry on Windows; when not, the result and why.
  terminal_runs() { # terminal_runs <when> <version>
    local said mismatch entries
    said="$(fact_of "$dir" "terminal_after_$1")"
    if [ -z "$said" ]; then result=unreached; why="no new terminal was started (terminal_after_$1)"; return 1; fi
    mismatch="$(terminal_mismatch "$platform" "$said" "$(fact_of "$dir" terminal_expected)" "$2")"
    if [ -n "$mismatch" ]; then result=contradiction; why="$mismatch (terminal_after_$1)"; return 1; fi
    [ "$platform" = windows ] || return 0
    entries="$(fact_of "$dir" "path_entries_after_$1")"
    if [ "$entries" != 1 ]; then result=contradiction; why="the user Path names the install folder ${entries:-no} times, not once (path_entries_after_$1)"; return 1; fi
  }

  # The registration count is read here as well as after the upgrade, so a query that cannot see an
  # installation says so at the first install rather than first at the upgrade (run 35263524309).
  if judge install-lower; then
    if [ "$(fact_of "$dir" lower_version)" != "$LOWER" ]; then result=contradiction; why="the installed app reports $(fact_of "$dir" lower_version), not $LOWER"
    elif [ "$(fact_of "$dir" products_after_install)" != 1 ]; then result=contradiction; why="$(fact_of "$dir" products_after_install) keypaste installations are registered after installing $LOWER, not 1"
    elif [ "$platform" = windows ] && ! terminal_runs install "$LOWER"; then :
    else result=pass; why="$detail"
    fi
  fi
  emit install-lower

  # On Linux the person links keypaste from the lower app's Settings before the baseline is taken, since the
  # app's unlock writes its settings.
  if judge fixture && judge baseline; then
    made="$detail"
    if [ "$(fact_of "$dir" baseline)" != complete ]; then result=contradiction; why="the fixture left no complete baseline: $(fact_of "$dir" baseline)"
    elif [ "$platform" = linux ] && ! { judge window && judge unlocked && judge settings && judge link-cli && judge linked \
      && terminal_runs fixture "$LOWER"; }; then :
    else
      result=pass; why="$made"
      if [ "$platform" = linux ]; then why="$why; Settings linked keypaste and a new terminal runs $LOWER through it, the login shell reading $(fact_of "$dir" shell_profile)"; fi
      if [ "$(fact_of "$dir" rehearsal)" = true ]; then why="$why; the CLI/MCP that made the data is a branch build of this commit, not a published release"; fi
    fi
  fi
  emit fixture

  if judge upgrade; then
    if [ "$(fact_of "$dir" higher_version)" != "$HIGHER" ]; then result=contradiction; why="after the upgrade the app reports $(fact_of "$dir" higher_version), not $HIGHER"
    elif [ "$(fact_of "$dir" products_after_upgrade)" != 1 ]; then result=contradiction; why="$(fact_of "$dir" products_after_upgrade) keypaste installations are registered, not 1"
    elif [ "$platform" = linux ] && ! terminal_runs handed_off "$HIGHER"; then :
    elif ! terminal_runs upgrade "$HIGHER"; then :
    elif [ "$platform" = linux ] && ! terminal_runs older_start "$HIGHER"; then :
    else result=pass; why="$detail"
    fi
  fi
  emit upgrade

  if judge read-after-upgrade; then
    if [ "$(fact_of "$dir" data_after_upgrade)" != identical ]; then result=contradiction; why="the upgrade changed user data: $(fact_of "$dir" data_after_upgrade)"
    elif [ "$(fact_of "$dir" token_after_upgrade)" != "${VALUES[2]}" ]; then result=contradiction; why="the vault reads $(fact_of "$dir" token_after_upgrade) for $VARIABLE, not ${VALUES[2]}"
    elif [ "$(fact_of "$dir" log_verify_after_upgrade)" != pass ]; then result=contradiction; why="keypaste log verify says $(fact_of "$dir" log_verify_after_upgrade)"
    elif [ "$(fact_of "$dir" keepassxc_after_upgrade)" != "$HISTORY_TITLES" ]; then result=contradiction; why="KeePassXC reads $(fact_of "$dir" keepassxc_after_upgrade) revisions of $PROJECT's $HOME_ENTRY, not $HISTORY_TITLES"
    else result=pass; why="the vault, its history, the settings, the policy and the audit came through the upgrade unchanged"
    fi
  fi
  emit data-after-upgrade

  if judge interrupt; then
    # An install that never failed inside its transaction was not interrupted, and a version that
    # still runs then says nothing about recovery (D-0213).
    if [ "$(fact_of "$dir" interrupted_failed)" != yes ]; then
      result=unreached; why="the install did not fail inside the transaction: $(fact_of "$dir" interrupted_where)"
    elif [ "$(fact_of "$dir" interrupted_exit)" = 0 ]; then result=contradiction; why='the install that was meant to fail succeeded'
    elif [ "$(fact_of "$dir" version_after_interrupt)" != "$HIGHER" ]; then result=contradiction; why="after the interrupted install the app reports $(fact_of "$dir" version_after_interrupt), not $HIGHER"
    elif [ "$(fact_of "$dir" data_after_interrupt)" != identical ]; then result=contradiction; why="the interrupted install changed user data: $(fact_of "$dir" data_after_interrupt)"
    elif [ "$(fact_of "$dir" products_after_interrupt)" != 1 ]; then result=contradiction; why="$(fact_of "$dir" products_after_interrupt) keypaste installations are registered after the failure, not 1"
    elif ! terminal_runs interrupt "$HIGHER"; then :
    else result=pass; why="exit $(fact_of "$dir" interrupted_exit); $(fact_of "$dir" interrupted_where); $HIGHER is still registered and runs, in a new terminal too"
    fi
  fi
  emit interrupted

  if [ "$platform" = linux ]; then
    result=not-applicable; why='an AppImage has no installer, so nothing can refuse a downgrade'
  elif judge downgrade; then
    if [ "$(fact_of "$dir" downgrade_exit)" = 0 ]; then result=contradiction; why="the MSI installed $LOWER over $HIGHER"
    elif [ "$(fact_of "$dir" downgrade_message)" != present ]; then result=contradiction; why="the install log does not say '$DOWNGRADE_MESSAGE'"
    elif [ "$(fact_of "$dir" version_after_downgrade)" != "$HIGHER" ]; then result=contradiction; why="after the refused downgrade the app reports $(fact_of "$dir" version_after_downgrade)"
    elif ! terminal_runs downgrade "$HIGHER"; then :
    else result=pass; why="exit $(fact_of "$dir" downgrade_exit) and the downgrade message; $HIGHER still runs, in a new terminal too"
    fi
  fi
  emit downgrade-refused

  if judge uninstall; then
    if [ "$(fact_of "$dir" installed_app_after_uninstall)" != absent ]; then result=contradiction; why='the install folder survived the uninstall'
    elif [ "$(fact_of "$dir" shortcut_after_uninstall)" != absent ]; then result=contradiction; why='the Start menu shortcut survived the uninstall'
    elif [ "$(fact_of "$dir" registration_after_uninstall)" != absent ]; then result=contradiction; why="the installation is still registered: $(fact_of "$dir" registration_after_uninstall)"
    elif [ "$(fact_of "$dir" data_after_uninstall)" != identical ]; then result=contradiction; why="the uninstall changed or removed user data: $(fact_of "$dir" data_after_uninstall)"
    elif [ "$(fact_of "$dir" token_after_uninstall)" != "${VALUES[2]}" ]; then result=contradiction; why="after the uninstall the vault reads $(fact_of "$dir" token_after_uninstall) for $VARIABLE"
    elif [ "$(fact_of "$dir" log_verify_after_uninstall)" != pass ]; then result=contradiction; why="after the uninstall keypaste log verify says $(fact_of "$dir" log_verify_after_uninstall)"
    elif [ "$platform" = windows ] && [ "$(fact_of "$dir" terminal_after_uninstall)" != absent ]; then
      result=contradiction; why="a new terminal still finds keypaste after the uninstall: $(fact_of "$dir" terminal_after_uninstall)"
    elif [ "$platform" = windows ] && [ -z "$(fact_of "$dir" user_path_before)" ]; then result=unreached; why='the user Path was not recorded before the install'
    elif [ "$platform" = windows ] && [ "$(fact_of "$dir" user_path_after_uninstall)" != "$(fact_of "$dir" user_path_before)" ]; then
      result=contradiction; why="the uninstall left the user Path '$(fact_of "$dir" user_path_after_uninstall)', not '$(fact_of "$dir" user_path_before)' as before the install"
    elif [ "$platform" = windows ]; then result=pass; why='the app, its user Path entry and its terminal keypaste are gone, and the vault, settings, policy and audit are untouched'
    else result=pass; why="the app is gone and the vault, settings, policy and audit are untouched; the link Settings made is left, and a new terminal now says: $(fact_of "$dir" terminal_after_uninstall)"
    fi
  fi
  emit uninstall
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

native() { if [ "$PLATFORM" = windows ]; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

sha256_of() { sha256sum < "$1" | awk '{print $1}'; }

powershell_out() { powershell -NoProfile -NonInteractive -Command "$1" | tr -d "$CR"; }

candidate() { # candidate <version>
  local ext=msi
  [ "$PLATFORM" = linux ] && ext=AppImage
  local name="keypaste-app-$1-$RID-internal-unsigned.$ext"
  [ -f "$CANDIDATES/$name" ] || { echo "no candidate at $CANDIDATES/$name" >&2; return 1; }
  printf '%s' "$CANDIDATES/$name"
}

# --- the data whose survival is the point ---------------------------------------------------------

# The five files a user would lose: the vault, the app's two settings files, the policy and the audit.
data_files() { printf '%s\n' "$VAULT" "$HOME_DIR/app.toml" "$HOME_DIR/recent.toml" "$HOME_DIR/policy.toml" "$HOME_DIR/audit.jsonl"; }

snapshot() { # snapshot <file>
  local path
  : > "$1"
  while IFS= read -r path; do
    if [ -f "$path" ]; then printf '%s  %s\n' "$(sha256_of "$path")" "$(basename "$path")" >> "$1"
    else printf 'absent  %s\n' "$(basename "$path")" >> "$1"
    fi
  done < <(data_files)
}

# Records this transition's before and after, and says identical or what differs.
compare() { # compare <when>
  local when="$1" differ
  local after="$OUT/hashes-$when.txt"
  snapshot "$after"
  jq -n --arg when "$when" --rawfile before "$OUT/hashes-baseline.txt" --rawfile after "$after" \
    '{transition: $when, before: $before, after: $after}' > "$OUT/hashes-$when.json"
  differ="$(diff "$OUT/hashes-baseline.txt" "$after" | tr -d "$CR" | tr '\n' ' ')"
  rm -f "$after"
  if [ -z "$differ" ]; then fact "data_after_$when" identical; else fact "data_after_$when" "$differ"; fi
}

cli() { printf '%s\n' "$MASTER" | "$KP" "$@" --vault "$VAULT_NATIVE" 2>/dev/null | tr -d "$CR"; }

keepassxc() {
  local kpxc="${KPXC_CLI:-$(command -v keepassxc-cli || true)}"
  [ -n "$kpxc" ] || { echo 'no keepassxc-cli' >&2; return 1; }
  # 2.7 renamed export to db-export and kept the old name; whichever answers, the reading is the same.
  { printf '%s\n' "$MASTER" | "$kpxc" db-export --format xml "$VAULT_NATIVE" 2>/dev/null \
    || printf '%s\n' "$MASTER" | "$kpxc" export --format xml "$VAULT_NATIVE" 2>/dev/null; } \
    | tr -d " \t$CR\n" | grep -oF "<Key>Title</Key><Value>$HOME_ENTRY</Value>" | grep -c . || true
}

# Everything the transitions must preserve, made by the CLI/MCP (D-0146).
fixture() {
  local value
  KP="$(find "$CLI_DIR" -maxdepth 3 -type f \( -name keypaste -o -name keypaste.exe \) | head -n 1)"
  [ -n "$KP" ] || { echo "no keypaste under $CLI_DIR"; return 1; }
  chmod +x "$KP" 2>/dev/null || true
  "$KP" --version | tr -d "$CR" | grep -qF "$CLI_VERSION" || { echo "the CLI is not $CLI_VERSION: $("$KP" --version)"; return 1; }

  mkdir -p "$(dirname "$VAULT")" "$HOME_DIR"
  printf '%s\n%s\n' "$MASTER" "$MASTER" | "$KP" init "$VAULT_NATIVE" >/dev/null || { echo 'keypaste init failed'; return 1; }
  printf '%s\n' "$MASTER" | "$KP" add "$ENTRY" --username seeded --url https://example.test \
    --notes 'made by the CLI before the upgrade' --generate --vault "$VAULT_NATIVE" >/dev/null \
    || { echo 'keypaste add failed'; return 1; }
  # Three values, so the project's home entry carries two history revisions the upgrade must not lose.
  for value in "${VALUES[@]}"; do
    printf '%s\n%s\n' "$MASTER" "$value" | "$KP" env set "$PROJECT" "$VARIABLE" --vault "$VAULT_NATIVE" >/dev/null \
      || { echo "keypaste env set $value failed"; return 1; }
  done

  printf '[[vault]]\npath = "%s"\nopened_at = "%s"\n' "$VAULT_NATIVE" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$HOME_DIR/recent.toml"
  printf '[[settings]]\nidle_timeout_seconds = 28800\n' > "$HOME_DIR/app.toml"
  printf '[[allow]]\nclient = "upgrade-check"\nentries = ["env/%s/**"]\nfields = ["password"]\nmax_ttl_seconds = 60\n' \
    "$PROJECT" > "$HOME_DIR/policy.toml"
  "$KP" policy ls >/dev/null || { echo 'keypaste policy ls refused the policy file'; return 1; }
  echo "vault $VAULT_NATIVE with $ENTRY and env/$PROJECT, made by $("$KP" --version | tr -d "$CR")"
}

# One approved request, so ~/.keypaste holds an audit chain the transitions must not break, then the hashes
# every transition is compared with.
baseline() {
  audit_record || { echo 'no audit record was written'; return 1; }
  snapshot "$OUT/hashes-baseline.txt"
  if grep -q '^absent' "$OUT/hashes-baseline.txt"; then fact baseline "$(grep '^absent' "$OUT/hashes-baseline.txt" | tr -d "$CR" | tr '\n' ' ')"
  else fact baseline complete
  fi
  fact keepassxc_at_fixture "$(keepassxc)"
  echo "vault $VAULT_NATIVE, $(wc -l < "$OUT/hashes-baseline.txt" | tr -d ' ') files, made by $("$KP" --version | tr -d "$CR")"
}

audit_record() {
  local pipe="keypaste-upgrade-check-$$-$RANDOM" agent_pid
  printf '%s\nh\n' "$MASTER" | "$KP" agent --vault "$VAULT_NATIVE" --approver "$pipe" --approval-timeout 55 \
    > /dev/null 2> "$OUT/agent-stderr.txt" &
  agent_pid=$!
  for _ in $(seq 1 150); do
    grep -q 'listening on' "$OUT/agent-stderr.txt" 2>/dev/null && break
    kill -0 "$agent_pid" 2>/dev/null || break
    sleep 0.2
  done
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"upgrade-check","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"username\",\"reason\":\"4.7d writes one audit record before the upgrade\",\"ttl_seconds\":60}}}"
    sleep 5
  } | "$KP" mcp --vault "$VAULT_NATIVE" --expose "${ENTRY%%/*}/**" --approver "$pipe" --client-label upgrade-check \
      > "$OUT/mcp-response.jsonl" 2> "$OUT/mcp-stderr.txt" || true
  kill "$agent_pid" 2>/dev/null || true
  wait "$agent_pid" 2>/dev/null || true
  fact audit_decision "$(jq -rs '[.[] | select(.decision != null)] | last | .decision // empty' "$HOME_DIR/audit.jsonl" 2>/dev/null | tr -d "$CR")"
  [ -s "$HOME_DIR/audit.jsonl" ]
}

# --- reading the data back ------------------------------------------------------------------------

read_after() { # read_after <when>
  compare "$1"
  fact "token_after_$1" "$(cli get "env/$PROJECT/$HOME_ENTRY" --field "$VARIABLE" --show || true)"
  if printf '%s\n' "$MASTER" | "$KP" log verify > "$OUT/log-verify-$1.txt" 2>&1; then
    fact "log_verify_after_$1" pass
  else
    fact "log_verify_after_$1" "exit $?"
  fi
  fact "keepassxc_after_$1" "$(keepassxc)"
  "$KP" policy ls > "$OUT/policy-$1.txt" 2>&1 || true
}

# --- a new terminal -------------------------------------------------------------------------------

# Where a new terminal should find keypaste: the MSI's install folder, or the link Settings makes.
terminal_expected() {
  if [ "$PLATFORM" = windows ]; then printf '%s\\keypaste.exe' "$(windows_folder)"; else printf '%s' "$HOME/.local/bin/keypaste"; fi
}

# What a new terminal runs after <when>, and on Windows how many user Path entries name the install folder.
probe() { # probe <when>
  fact "terminal_after_$1" "$(terminal_keypaste "$PLATFORM")"
  if [ "$PLATFORM" = windows ]; then fact "path_entries_after_$1" "$(user_path_entries "$(windows_folder)")"; fi
}

# A new terminal's answer once it reports <version>, or the last one after 60 s.
probe_until() { # probe_until <when> <version>
  local said
  for _ in $(seq 1 30); do
    said="$(terminal_keypaste "$PLATFORM")"
    [ "$(cut -f2 <<< "$said")" = "$2" ] && break
    sleep 2
  done
  fact "terminal_after_$1" "$said"
}

# HKCU's Path as stored, before Windows expands it.
user_path() { powershell_out "(Get-Item 'HKCU:\\Environment').GetValue('Path', '', 'DoNotExpandEnvironmentNames')"; }

# An entry of the user's own, added before the first install: every install must keep it, and the uninstall
# must leave the user Path as it was.
mark_user_path() {
  local entry
  entry="$(cygpath -aw "$OUT")\\user-path-entry"
  powershell_out "\$path = (Get-Item 'HKCU:\\Environment').GetValue('Path', '', 'DoNotExpandEnvironmentNames')
    \$path = if (\$path) { \"\$path;$entry\" } else { '$entry' }
    New-ItemProperty -Path 'HKCU:\\Environment' -Name Path -Value \$path -PropertyType ExpandString -Force | Out-Null" \
    || { echo 'the user Path could not be written'; return 1; }
  fact user_path_before "$(user_path)"
  echo "added $entry to the user Path"
}

# --- Linux: the app a person starts ---------------------------------------------------------------

# Every start stops the running app first, since a second start only asks the running one to show its
# window (D-0397).
start_app() { # start_app <appimage> <name>
  stop_app
  "$1" > "$OUT/app-$2.log" 2>&1 &
  APP_PID=$!
}

stop_app() {
  [ -n "${APP_PID:-}" ] || return 0
  pkill -f usr/bin/keypaste-app 2>/dev/null || true
  kill "$APP_PID" 2>/dev/null || true
  wait "$APP_PID" 2>/dev/null || true
  APP_PID=''
  sleep 2
}

# The lower app, unlocked, links keypaste from Settings as a person does once; a new terminal then runs it.
link_from_settings() {
  act accessibility enable_accessibility || return 0
  start_app "$APP" lower
  { act window drive window 120 \
    && act unlocked drive type "$MASTER" && act unlocked-enter drive key Return && act unlocked drive find 'Lock now' \
    && act settings drive key ctrl+4 && act link-cli drive invoke "$LINK_BUTTON" && act linked drive find-prefix "$LINKED_PREFIX"; } \
    || true
  stop_app
  probe fixture
}

# A complete download, renamed into place as a browser does.
place() { # place <file> <path>
  mkdir -p "$(dirname "$2")"
  cp "$1" "$2.part" && chmod +x "$2.part" && mv "$2.part" "$2"
}

linux_images() { find "$APPS" -maxdepth 1 -name 'keypaste*.AppImage' | grep -c . || true; }

# --- Windows transitions --------------------------------------------------------------------------

msiexec_install() { # msiexec_install <msi> <log> [property...]
  local msi="$1" log="$2" extra='' property
  shift 2
  for property in "$@"; do extra="$extra, '$property'"; done
  powershell_out "(Start-Process msiexec.exe -ArgumentList '/i', '\"$(cygpath -w "$msi")\"', '/qn', '/l*v', '\"$(cygpath -w "$log")\"'$extra -Wait -PassThru).ExitCode"
}

windows_app() { printf '%s' "$(cygpath -u "$LOCALAPPDATA")/Programs/keypaste/keypaste-app.exe"; }

windows_folder() { cygpath -w "$(dirname "$(windows_app)")"; }

windows_shortcut() { printf '%s' "$(cygpath -u "$APPDATA")/Microsoft/Windows/Start Menu/Programs/keypaste.lnk"; }

# Windows Installer is asked, not a registry path: this install registers under
# HKLM\...\Installer\UserData\<SID>\Products and nothing lands in HKCU's Uninstall list, so
# enumerating that hive reported no installation at all (run 35263524309, D-0209).
windows_registrations() {
  powershell_out "\$i = New-Object -ComObject WindowsInstaller.Installer
    foreach (\$p in @(\$i.GetType().InvokeMember('RelatedProducts', 'GetProperty', \$null, \$i, @('$(upgrade_code)')))) {
      \"\$p|\$(\$i.ProductInfo(\$p, 'VersionString'))|\$(\$i.ProductInfo(\$p, 'ProductName'))\"
    }"
}

windows_registration_count() { windows_registrations | grep -c . || true; }

# The package's own code, so the harness and the installer cannot disagree about which product this is.
upgrade_code() {
  local code
  code="$(sed -n 's/.*UpgradeCode="\([^"]*\)".*/\1/p' "$ROOT/packaging/windows/Package.wxs" | head -n 1)"
  [ -n "$code" ] || die 'packaging/windows/Package.wxs declares no UpgradeCode'
  printf '{%s}' "${code^^}"
}

# An MSI verbose log is UTF-16 (D-0209); grep reads it to the end, as a grep -q stopping at the match kills tr and pipefail then reads no match (F.43).
msi_log_says() { # msi_log_says <log> <extended-regexp>
  [ -f "$1" ] && tr -d '\0' < "$1" | grep -iE "$2" > /dev/null
}

app_version() {
  local app="$1"
  [ -x "$app" ] || { echo absent; return 0; }
  "$app" --version 2>/dev/null | tr -d "[:space:]$CR" || echo unreadable
}

# --- the run --------------------------------------------------------------------------------------

install_lower() {
  local msi app code
  if [ "$PLATFORM" = windows ]; then
    msi="$(candidate "$LOWER")" || return 1
    mark_user_path || return 1
    code="$(msiexec_install "$msi" "$OUT/install-lower.log")"
    [ "$code" = 0 ] || { echo "msiexec /i exited $code"; return 1; }
    APP="$(windows_app)"
    [ -x "$APP" ] || { echo "msiexec exited 0 and $APP does not exist"; return 1; }
    fact lower_version "$(app_version "$APP")"
    "$APP" --selftest > /dev/null || { echo 'the installed app failed --selftest'; return 1; }
    fact products_after_install "$(windows_registration_count)"
    fact registrations_after_install "$(windows_registrations | tr '\n' ' ')"
    probe install
    echo "msiexec /i exited 0; installed $(cygpath -m "$APP") reporting $(fact_read lower_version), $(fact_read products_after_install) registered"
  else
    app="$(candidate "$LOWER")" || return 1
    APP="$APPS/$(basename "$app")"
    place "$app" "$APP"
    fact lower_version "$(app_version "$APP")"
    "$APP" --selftest > /dev/null || { echo 'the installed AppImage failed --selftest'; return 1; }
    fact products_after_install "$(linux_images)"
    echo "placed $APP reporting $(fact_read lower_version)"
  fi
}

fact_read() { fact_of "$OUT" "$1"; }

upgrade() {
  local msi app code higher
  if [ "$PLATFORM" = windows ]; then
    msi="$(candidate "$HIGHER")" || return 1
    code="$(msiexec_install "$msi" "$OUT/upgrade.log")"
    [ "$code" = 0 ] || { echo "msiexec /i of $HIGHER exited $code"; return 1; }
    [ -x "$APP" ] || { echo "the upgrade left no app at $APP"; return 1; }
    fact higher_version "$(app_version "$APP")"
    fact products_after_upgrade "$(windows_registration_count)"
    fact registrations_after_upgrade "$(windows_registrations | tr '\n' ' ')"
    "$APP" --selftest > /dev/null || { echo 'the upgraded app failed --selftest'; return 1; }
    probe upgrade
    echo "MajorUpgrade exited 0; $(cygpath -m "$APP") now reports $(fact_read higher_version)"
  else
    # An AppImage has no installer: the new image arrives beside the old one while the old app runs, as after a
    # start at login. The person starts the new image, which hands off to the running app and must still
    # re-point the link, quits the old app, starts the new one and then deletes the old image.
    app="$(candidate "$HIGHER")" || return 1
    higher="$APPS/$(basename "$app")"
    place "$app" "$higher"
    fact higher_version "$(app_version "$higher")"
    "$higher" --selftest > /dev/null || { echo 'the new AppImage failed --selftest'; return 1; }
    start_app "$APP" lower-running
    drive window 120 || { stop_app; echo "the $LOWER app showed no window"; return 1; }
    code=0
    timeout 60 "$higher" > "$OUT/app-higher-handed-off.log" 2>&1 || code=$?
    [ "$code" = 0 ] || { stop_app; echo "the $HIGHER start beside the running $LOWER app exited $code"; return 1; }
    probe_until handed_off "$HIGHER"
    start_app "$higher" higher
    drive window 120 || { stop_app; echo "the $HIGHER app showed no window"; return 1; }
    probe upgrade
    start_app "$APP" lower-again
    drive window 120 || { stop_app; echo "the $LOWER app showed no window when started again"; return 1; }
    stop_app
    probe older_start
    rm -f "$APP"
    APP="$higher"
    fact products_after_upgrade "$(linux_images)"
    echo "started $HIGHER while $LOWER ran, then on its own, started $LOWER again and deleted it; $APP reports $(fact_read higher_version)"
  fi
}

# The third candidate is installed from a copy whose cabinet cannot be extracted, so the transaction
# fails inside the install itself. Five other ways interrupt nothing: a property on a missing volume
# fails in CostFinalize, killing the client leaves the service to finish, an icacls deny does not stop
# a SYSTEM write, deleting the source fails only after InstallFinalize, and the log's own process
# prefix is no process to end (D-0210, D-0212, D-0213).
interrupt() {
  local msi log="$OUT/interrupt.log" source code previous size image partial
  if [ "$PLATFORM" = windows ]; then
    msi="$(candidate "$THIRD")" || return 1
    source="$OUT/interrupt-source.msi"
    cp "$msi" "$source"
    bash "$ROOT/scripts/break-msi-cabinet.sh" "$source" > "$OUT/interrupt-cabinet.log" 2>&1       || { echo "the cabinet could not be replaced: $(tail -n 3 "$OUT/interrupt-cabinet.log")"; return 1; }

    code="$(msiexec_install "$source" "$log")"
    fact interrupted_exit "$code"
    rm -f "$source"

    # The installer keeps writing after msiexec returns, so let the log settle before reading it.
    previous=''
    for _ in $(seq 1 120); do
      size="$(wc -c < "$log" | tr -d ' ')"
      [ "$size" = "$previous" ] && break
      previous="$size"
      sleep 1
    done

    if msi_log_says "$log" "$INSTALL_FAILED"; then
      fact interrupted_failed yes
      fact interrupted_where 'the transaction failed while the install was running'
    else
      fact interrupted_failed no
      fact interrupted_where 'the log names no failed action'
    fi
    fact version_after_interrupt "$(app_version "$APP")"
    fact products_after_interrupt "$(windows_registration_count)"
    fact registrations_after_interrupt "$(windows_registrations | tr '
' ' ')"
  else
    # A download that stops halfway: its temporary file is never renamed, so nothing runs it.
    image="$(candidate "$THIRD")" || return 1
    partial="$APPS/$(basename "$image").part"
    head -c "$(( $(stat -c %s "$image") / 2 ))" "$image" > "$partial"
    fact interrupted_exit 1
    fact interrupted_failed yes
    fact interrupted_where 'the partial download was never renamed beside the installed image'
    fact version_after_interrupt "$(app_version "$APP")"
    fact products_after_interrupt "$(linux_images)"
    rm -f "$partial"
  fi
  probe interrupt
  compare interrupt
}

downgrade() {
  local msi code log="$OUT/downgrade.log"
  msi="$(candidate "$LOWER")" || return 1
  code="$(msiexec_install "$msi" "$log")"
  fact downgrade_exit "$code"
  if msi_log_says "$log" "$DOWNGRADE_MESSAGE"; then fact downgrade_message present; else fact downgrade_message absent; fi
  fact version_after_downgrade "$(app_version "$APP")"
  probe downgrade
}

uninstall() {
  local code product
  if [ "$PLATFORM" = windows ]; then
    # By product code, not by candidate path: a path names a package, and msiexec refuses it with
    # 1605 when the installed product is another one (run 35263524309).
    product="$(windows_registrations | head -n 1 | cut -d'|' -f1)"
    [ -n "$product" ] || { echo 'no registered keypaste product to uninstall'; return 1; }
    fact uninstalled_product "$product"
    code="$(powershell_out "(Start-Process msiexec.exe -ArgumentList '/x', '$product', '/qn', '/l*v', '\"$(cygpath -w "$OUT/uninstall.log")\"' -Wait -PassThru).ExitCode")"
    [ "$code" = 0 ] || { echo "msiexec /x exited $code"; return 1; }
    if [ -e "$(dirname "$APP")" ]; then fact installed_app_after_uninstall "$(cygpath -m "$(dirname "$APP")")"; else fact installed_app_after_uninstall absent; fi
    if [ -f "$(windows_shortcut)" ]; then fact shortcut_after_uninstall present; else fact shortcut_after_uninstall absent; fi
    if [ "$(powershell_out "if (Test-Path 'HKCU:\\Software\\keypaste\\Installer') { 'present' } else { 'absent' }")" = absent ] \
      && [ "$(windows_registration_count)" = 0 ]; then fact registration_after_uninstall absent
    else fact registration_after_uninstall "$(windows_registrations | tr '\n' ' ')HKCU key $(powershell_out "if (Test-Path 'HKCU:\\Software\\keypaste\\Installer') { 'present' } else { 'absent' }")"
    fi
    fact user_path_after_uninstall "$(user_path)"
    echo "msiexec /x exited 0"
  else
    rm -f "$APP"
    if [ -e "$APP" ]; then fact installed_app_after_uninstall "$APP"; else fact installed_app_after_uninstall absent; fi
    fact shortcut_after_uninstall absent
    fact registration_after_uninstall absent
    echo "removed $APP"
  fi
  probe uninstall
  read_after uninstall
}

# shellcheck disable=SC2016 # PowerShell expands its own variables, not bash.
environment() {
  local os sha
  case "$PLATFORM" in
    linux) os="$(sed -n 's/^PRETTY_NAME="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' /etc/os-release) $(uname -r)" ;;
    windows) os="$(powershell_out '(Get-CimInstance Win32_OperatingSystem | ForEach-Object { "$($_.Caption) $($_.Version)" })')" ;;
  esac
  sha="${GITHUB_SHA:-$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || echo unknown)}"
  jq -n \
    --arg rid "$RID" \
    --arg os "$os" \
    --arg image "${ImageOS:-local} ${ImageVersion:-}" \
    --arg sha "$sha" \
    --arg run "${GITHUB_RUN_ID:-local}" \
    --arg versions "$LOWER $HIGHER $THIRD" \
    --arg cli "$CLI_VERSION" \
    --arg rehearsal "${REHEARSAL:-}" \
    --arg vault "$(native "$VAULT")" \
    --arg home "$(native "$HOME_DIR")" \
    --arg keepassxc "$({ "${KPXC_CLI:-keepassxc-cli}" --version 2>/dev/null || echo absent; } | tr -d "$CR")" \
    '{rid: $rid, os: $os, runnerImage: $image, sha: $sha, run: $run, candidates: $versions, cli: $cli,
      rehearsal: ($rehearsal == "1"), cliOrigin: (if $rehearsal == "1" then "built from \($sha)" else "published release \($cli)" end),
      vault: $vault, home: $home, keepassxc: $keepassxc}'
}

run_all() {
  mkdir -p "$OUT"
  : > "$OUT/facts.jsonl"
  : > "$OUT/driver.jsonl"
  [ "${CI:-}" = true ] || [ "${KEYPASTE_UPGRADE_SANDBOX:-}" = 1 ] \
    || die 'this installs into the real user profile: set CI=true on a fresh runner, or KEYPASTE_UPGRADE_SANDBOX=1 for a throwaway account or virtual machine'
  case "${REHEARSAL:-}" in '') ;; 1) fact rehearsal true ;; *) die "REHEARSAL is 1 for a rehearsal, not '$REHEARSAL'" ;; esac
  fact platform "$PLATFORM"
  fact terminal_expected "$(terminal_expected)"
  if [ "$PLATFORM" = linux ]; then
    [ -n "${DISPLAY:-}" ] || die 'DISPLAY is unset; start Xvfb and a window manager first'
    fact shell_profile "$(stock_login_profile)"
  fi

  if act install-lower install_lower && act fixture fixture; then
    if [ "$PLATFORM" = linux ]; then link_from_settings; fi
    if act baseline baseline && act upgrade upgrade; then
      act read-after-upgrade read_after upgrade || true
      act interrupt interrupt || true
      if [ "$PLATFORM" = windows ]; then act downgrade downgrade || true; fi
      act uninstall uninstall || true
    fi
  fi
  stop_app
  fact finished true

  classify "$OUT" > "$OUT/verdicts.jsonl"
  cat "$OUT/verdicts.jsonl"
  jq -n --argjson environment "$(environment)" --slurpfile checks "$OUT/verdicts.jsonl" \
    --slurpfile facts "$OUT/facts.jsonl" \
    '{environment: $environment, checks: $checks, facts: ($facts | map({(.fact): .value}) | add)}' > "$OUT/observation.json"

  echo "observation: $(jq -r '[.checks[] | "\(.check)=\(.result)"] | join(" ")' "$OUT/observation.json")"
  if jq -e '[.checks[] | select(.result != "pass" and .result != "not-applicable")] | length > 0' "$OUT/observation.json" >/dev/null; then
    jq -r '.checks[] | select(.result != "pass" and .result != "not-applicable") | "::error::\(.check): \(.result): \(.why)"' "$OUT/observation.json" >&2
    return 1
  fi
}

# --- self-test ------------------------------------------------------------------------------------

selftest() {
  local work cases=0 failures=0
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN
  local folder='C:\Users\runner\AppData\Local\Programs\keypaste' link=/home/runner/.local/bin/keypaste
  local path_before='%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;C:\work\upgrade-desktop\user-path-entry'
  local bridge='usage: keypaste mcp [--vault <path>]'

  # A complete, passing Windows run's records; each fixture below changes one thing.
  passing() {
    local dir="$work/$1" when version
    mkdir -p "$dir"
    printf '%s\n' \
      '{"action":"install-lower","status":"ok","detail":"msiexec /i exited 0"}' \
      '{"action":"fixture","status":"ok","detail":"vault made"}' \
      '{"action":"baseline","status":"ok","detail":"vault made, 5 files"}' \
      '{"action":"upgrade","status":"ok","detail":"MajorUpgrade exited 0"}' \
      '{"action":"read-after-upgrade","status":"ok","detail":"read back"}' \
      '{"action":"interrupt","status":"ok","detail":"exit 1603"}' \
      '{"action":"downgrade","status":"ok","detail":"exit 1603"}' \
      '{"action":"uninstall","status":"ok","detail":"msiexec /x exited 0"}' > "$dir/driver.jsonl"
    printf '%s\n' \
      '{"fact":"platform","value":"windows"}' \
      "{\"fact\":\"lower_version\",\"value\":\"$LOWER\"}" \
      '{"fact":"products_after_install","value":"1"}' \
      '{"fact":"baseline","value":"complete"}' \
      "{\"fact\":\"higher_version\",\"value\":\"$HIGHER\"}" \
      '{"fact":"products_after_upgrade","value":"1"}' \
      '{"fact":"data_after_upgrade","value":"identical"}' \
      "{\"fact\":\"token_after_upgrade\",\"value\":\"${VALUES[2]}\"}" \
      '{"fact":"log_verify_after_upgrade","value":"pass"}' \
      "{\"fact\":\"keepassxc_after_upgrade\",\"value\":\"$HISTORY_TITLES\"}" \
      '{"fact":"interrupted_exit","value":"1603"}' \
      '{"fact":"interrupted_failed","value":"yes"}' \
      '{"fact":"products_after_interrupt","value":"1"}' \
      '{"fact":"interrupted_where","value":"the transaction failed while the install was running"}' \
      "{\"fact\":\"version_after_interrupt\",\"value\":\"$HIGHER\"}" \
      '{"fact":"data_after_interrupt","value":"identical"}' \
      '{"fact":"downgrade_exit","value":"1603"}' \
      '{"fact":"downgrade_message","value":"present"}' \
      "{\"fact\":\"version_after_downgrade\",\"value\":\"$HIGHER\"}" \
      '{"fact":"installed_app_after_uninstall","value":"absent"}' \
      '{"fact":"shortcut_after_uninstall","value":"absent"}' \
      '{"fact":"registration_after_uninstall","value":"absent"}' \
      '{"fact":"data_after_uninstall","value":"identical"}' \
      "{\"fact\":\"token_after_uninstall\",\"value\":\"${VALUES[2]}\"}" \
      '{"fact":"log_verify_after_uninstall","value":"pass"}' \
      '{"fact":"finished","value":"true"}' > "$dir/facts.jsonl"
    set_fact "$1" terminal_expected "$folder\\keypaste.exe"
    for when in install upgrade interrupt downgrade; do
      version="$HIGHER"
      [ "$when" = install ] && version="$LOWER"
      set_fact "$1" "terminal_after_$when" "$folder\\keypaste.exe"$'\t'"$version"$'\t'"$bridge"
      set_fact "$1" "path_entries_after_$when" 1
    done
    set_fact "$1" terminal_after_uninstall absent
    set_fact "$1" user_path_before "$path_before"
    set_fact "$1" user_path_after_uninstall "$path_before"
  }
  # The passing run on Linux, where the lower app links keypaste from Settings and a start of the higher, handed
  # to the running lower app, re-points it.
  passing_linux() {
    passing "$1"
    drop_action "$1" downgrade
    printf '%s\n' \
      '{"action":"window","status":"ok","detail":"window keypaste"}' \
      '{"action":"unlocked","status":"ok","detail":"found Lock now"}' \
      '{"action":"settings","status":"ok","detail":"sent ctrl+4"}' \
      '{"action":"link-cli","status":"ok","detail":"invoked Link the keypaste command"}' \
      '{"action":"linked","status":"ok","detail":"A terminal runs this app\u0027s keypaste from /home/runner/.local/bin/keypaste."}' >> "$work/$1/driver.jsonl"
    set_fact "$1" platform linux
    set_fact "$1" terminal_expected "$link"
    set_fact "$1" shell_profile '/home/runner/.profile; set aside /home/runner/.bash_profile, which read: export NVM_DIR=...'
    set_fact "$1" terminal_after_fixture "$link"$'\t'"$LOWER"$'\t'"$bridge"
    set_fact "$1" terminal_after_handed_off "$link"$'\t'"$HIGHER"$'\t'"$bridge"
    set_fact "$1" terminal_after_upgrade "$link"$'\t'"$HIGHER"$'\t'"$bridge"
    set_fact "$1" terminal_after_older_start "$link"$'\t'"$HIGHER"$'\t'"$bridge"
    set_fact "$1" terminal_after_interrupt "$link"$'\t'"$HIGHER"$'\t'"$bridge"
    set_fact "$1" terminal_after_uninstall $'fails\t/home/runner/.local/bin/keypaste exited 127: not found'
  }
  refuse_action() { jq -c --arg a "$2" --arg d "$3" 'if .action == $a then .status = "refused" | .detail = $d else . end' "$work/$1/driver.jsonl" > "$work/t" && mv "$work/t" "$work/$1/driver.jsonl"; }
  drop_action() { jq -c --arg a "$2" 'select(.action != $a)' "$work/$1/driver.jsonl" > "$work/t" && mv "$work/t" "$work/$1/driver.jsonl"; }
  set_fact() { jq -cn --arg f "$2" --arg v "$3" '{fact: $f, value: $v}' >> "$work/$1/facts.jsonl"; }
  drop_fact() { jq -c --arg f "$2" 'select(.fact != $f)' "$work/$1/facts.jsonl" > "$work/t" && mv "$work/t" "$work/$1/facts.jsonl"; }

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
  passing lower-not-installed
  refuse_action lower-not-installed install-lower 'msiexec /i exited 1603'
  passing lower-reports-another-version
  set_fact lower-reports-another-version lower_version 0.3.1
  passing install-unregistered
  set_fact install-unregistered products_after_install 0
  passing fixture-incomplete
  set_fact fixture-incomplete baseline 'absent policy.toml'
  passing no-baseline
  refuse_action no-baseline baseline 'no audit record was written'
  passing upgrade-not-taken
  set_fact upgrade-not-taken higher_version "$LOWER"
  passing two-products
  set_fact two-products products_after_upgrade 2
  passing vault-changed
  set_fact vault-changed data_after_upgrade 'upgrade-check.kdbx'
  passing value-lost
  set_fact value-lost token_after_upgrade first-value
  passing history-lost
  set_fact history-lost keepassxc_after_upgrade 1
  passing audit-broken
  set_fact audit-broken log_verify_after_upgrade 'exit 5'
  passing interrupt-succeeded
  set_fact interrupt-succeeded interrupted_exit 0
  passing interrupt-before-the-transaction
  set_fact interrupt-before-the-transaction interrupted_failed no
  set_fact interrupt-before-the-transaction interrupted_where 'the log names no failed action'
  passing interrupt-deregistered
  set_fact interrupt-deregistered products_after_interrupt 0
  passing interrupt-broke-the-app
  set_fact interrupt-broke-the-app version_after_interrupt absent
  passing interrupt-touched-the-data
  set_fact interrupt-touched-the-data data_after_interrupt 'audit.jsonl'
  passing downgrade-installed
  set_fact downgrade-installed downgrade_exit 0
  passing downgrade-silent
  set_fact downgrade-silent downgrade_message absent
  passing uninstall-left-the-app
  set_fact uninstall-left-the-app installed_app_after_uninstall 'C:/Users/runner/AppData/Local/Programs/keypaste'
  passing uninstall-left-the-shortcut
  set_fact uninstall-left-the-shortcut shortcut_after_uninstall present
  passing uninstall-left-the-registration
  set_fact uninstall-left-the-registration registration_after_uninstall 'keypaste|0.0.2'
  passing uninstall-took-the-vault
  set_fact uninstall-took-the-vault data_after_uninstall 'absent upgrade-check.kdbx'
  passing uninstall-took-the-audit
  set_fact uninstall-took-the-audit log_verify_after_uninstall 'exit 5'
  passing no-read-back
  drop_action no-read-back read-after-upgrade
  passing unfinished
  drop_fact unfinished finished
  passing rehearsal
  set_fact rehearsal rehearsal true

  # A new terminal on Windows.
  passing install-off-path
  set_fact install-off-path terminal_after_install absent
  passing install-other-keypaste
  set_fact install-other-keypaste terminal_after_install $'C:\\tools\\keypaste.exe\t'"$LOWER"$'\t'"$bridge"
  passing install-other-case
  set_fact install-other-case terminal_after_install "${folder,,}\\keypaste.exe"$'\t'"$LOWER"$'\t'"$bridge"
  passing install-not-probed
  drop_fact install-not-probed terminal_after_install
  passing upgrade-runs-the-lower
  set_fact upgrade-runs-the-lower terminal_after_upgrade "$folder\\keypaste.exe"$'\t'"$LOWER"$'\t'"$bridge"
  passing upgrade-two-entries
  set_fact upgrade-two-entries path_entries_after_upgrade 2
  passing upgrade-no-bridge
  set_fact upgrade-no-bridge terminal_after_upgrade "$folder\\keypaste.exe"$'\t'"$HIGHER"$'\tunknown command: mcp'
  passing interrupt-took-the-entry
  set_fact interrupt-took-the-entry path_entries_after_interrupt 0
  set_fact interrupt-took-the-entry terminal_after_interrupt absent
  passing downgrade-runs-the-lower
  set_fact downgrade-runs-the-lower terminal_after_downgrade "$folder\\keypaste.exe"$'\t'"$LOWER"$'\t'"$bridge"
  passing uninstall-left-keypaste
  set_fact uninstall-left-keypaste terminal_after_uninstall "$folder\\keypaste.exe"$'\t'"$HIGHER"$'\t'"$bridge"
  passing uninstall-changed-the-path
  set_fact uninstall-changed-the-path user_path_after_uninstall '%USERPROFILE%\AppData\Local\Microsoft\WindowsApps'
  passing uninstall-path-unrecorded
  drop_fact uninstall-path-unrecorded user_path_before
  drop_fact uninstall-path-unrecorded user_path_after_uninstall

  # A new terminal on Linux.
  passing_linux linux
  passing_linux linux-not-unlocked
  refuse_action linux-not-unlocked unlocked "nothing named 'Lock now' appeared"
  passing_linux linux-not-linked
  refuse_action linux-not-linked link-cli "no button 'Link the keypaste command'"
  passing_linux linux-link-unread
  set_fact linux-link-unread terminal_after_fixture absent
  passing_linux linux-not-repointed
  set_fact linux-not-repointed terminal_after_upgrade "$link"$'\t'"$LOWER"$'\t'"$bridge"
  passing_linux linux-handoff-not-repointed
  set_fact linux-handoff-not-repointed terminal_after_handed_off "$link"$'\t'"$LOWER"$'\t'"$bridge"
  passing_linux linux-handoff-not-probed
  drop_fact linux-handoff-not-probed terminal_after_handed_off
  passing_linux linux-older-took-the-link
  set_fact linux-older-took-the-link terminal_after_older_start "$link"$'\t'"$LOWER"$'\t'"$bridge"
  passing_linux linux-interrupt-broke-the-link
  set_fact linux-interrupt-broke-the-link terminal_after_interrupt $'fails\t/home/runner/.local/bin/keypaste exited 127: not found'
  passing_linux linux-uninstall-link-gone
  set_fact linux-uninstall-link-gone terminal_after_uninstall absent

  local check
  for check in "${CHECKS[@]}"; do expect all-pass "$check" pass; done
  expect lower-not-installed install-lower unreached
  expect lower-not-installed fixture unreached
  expect lower-not-installed uninstall unreached
  expect lower-reports-another-version install-lower contradiction
  expect lower-reports-another-version upgrade unreached
  expect install-unregistered install-lower contradiction
  expect install-unregistered upgrade unreached
  expect fixture-incomplete fixture contradiction
  expect no-baseline fixture unreached
  expect no-baseline upgrade unreached
  expect upgrade-not-taken upgrade contradiction
  expect upgrade-not-taken data-after-upgrade unreached
  expect two-products upgrade contradiction
  expect vault-changed data-after-upgrade contradiction
  expect vault-changed interrupted unreached
  expect value-lost data-after-upgrade contradiction
  expect history-lost data-after-upgrade contradiction
  expect audit-broken data-after-upgrade contradiction
  expect interrupt-succeeded interrupted contradiction
  expect interrupt-before-the-transaction interrupted unreached
  expect interrupt-before-the-transaction uninstall unreached
  expect interrupt-deregistered interrupted contradiction
  expect interrupt-broke-the-app interrupted contradiction
  expect interrupt-touched-the-data interrupted contradiction
  expect downgrade-installed downgrade-refused contradiction
  expect downgrade-silent downgrade-refused contradiction
  expect uninstall-left-the-app uninstall contradiction
  expect uninstall-left-the-shortcut uninstall contradiction
  expect uninstall-left-the-registration uninstall contradiction
  expect uninstall-took-the-vault uninstall contradiction
  expect uninstall-took-the-audit uninstall contradiction
  expect no-read-back data-after-upgrade unreached
  expect no-read-back interrupted unreached
  expect unfinished install-lower harness-failure
  expect unfinished uninstall harness-failure
  for check in "${CHECKS[@]}"; do expect rehearsal "$check" pass; done

  expect install-off-path install-lower contradiction
  expect install-off-path fixture unreached
  expect install-other-keypaste install-lower contradiction
  expect install-other-case install-lower pass
  expect install-not-probed install-lower unreached
  expect upgrade-runs-the-lower upgrade contradiction
  expect upgrade-two-entries upgrade contradiction
  expect upgrade-no-bridge upgrade contradiction
  expect interrupt-took-the-entry interrupted contradiction
  expect downgrade-runs-the-lower downgrade-refused contradiction
  expect uninstall-left-keypaste uninstall contradiction
  expect uninstall-changed-the-path uninstall contradiction
  expect uninstall-path-unrecorded uninstall unreached

  for check in "${CHECKS[@]}"; do
    if [ "$check" = downgrade-refused ]; then expect linux "$check" not-applicable; else expect linux "$check" pass; fi
  done
  expect linux-not-unlocked fixture unreached
  expect linux-not-unlocked upgrade unreached
  expect linux-not-linked fixture unreached
  expect linux-link-unread fixture contradiction
  expect linux-not-repointed upgrade contradiction
  expect linux-handoff-not-repointed upgrade contradiction
  expect linux-handoff-not-probed upgrade unreached
  expect linux-older-took-the-link upgrade contradiction
  expect linux-interrupt-broke-the-link interrupted contradiction
  expect linux-uninstall-link-gone uninstall pass

  utf16() { local s="$1" i; for ((i = 0; i < ${#s}; i++)); do printf '%s\0' "${s:i:1}"; done; }
  # Two megabytes after the line read for, so a reader that stops at the match leaves its producer writing (F.43).
  utf16 $'MSI (s) (D0:2C) [15:21:46:444]: Note: 1: 2205 2:  3: Error\n' > "$work/filler"
  for _ in $(seq 1 14); do cat "$work/filler" "$work/filler" > "$work/t" && mv "$work/t" "$work/filler"; done
  expect_log() { # expect_log <name> <line> <extended-regexp> <yes|no>
    local actual=no
    cases=$((cases + 1))
    { utf16 "=== Verbose logging started ==="$'\n'"$2"$'\n'; cat "$work/filler"; } > "$work/$1.log"
    if msi_log_says "$work/$1.log" "$3"; then actual=yes; fi
    if [ "$actual" != "$4" ]; then
      echo "::error::selftest $1: the log read $actual, expected $4" >&2
      failures=$((failures + 1))
    fi
  }
  expect_log install-failed 'Action ended 15:21:46: InstallFinalize. Return value 3.' "$INSTALL_FAILED" yes
  expect_log files-failed 'Action ended 15:21:45: InstallFiles. Return value 3.' "$INSTALL_FAILED" yes
  expect_log install-succeeded 'Action ended 15:21:46: InstallFinalize. Return value 1.' "$INSTALL_FAILED" no
  expect_log downgrade-refused "MSI (s) (D0:2C) [15:21:46:444]: Product: keypaste -- $DOWNGRADE_MESSAGE" "$DOWNGRADE_MESSAGE" yes
  expect_log downgrade-silent 'MSI (s) (D0:2C) [15:21:46:444]: Product: keypaste -- Installation failed.' "$DOWNGRADE_MESSAGE" no
  cases=$((cases + 1))
  if msi_log_says "$work/no-such.log" "$INSTALL_FAILED"; then
    echo '::error::selftest no-log: a missing log read as naming the failure' >&2
    failures=$((failures + 1))
  fi

  if [ "$failures" -gt 0 ]; then
    die "exercise-desktop-upgrade selftest: $failures of $cases cases failed"
  fi
  echo "exercise-desktop-upgrade selftest: $cases cases"
}

case "${1:-}" in
  --selftest) selftest ;;
  --classify) [ $# -eq 2 ] || { usage >&2; exit 2; }; classify "$2" ;;
  -h|--help) usage ;;
  '') usage >&2; exit 2 ;;
  *)
    [ $# -eq 5 ] || { usage >&2; exit 2; }
    RID="$1"
    CANDIDATES="$2"
    CLI_VERSION="$3"
    CLI_DIR="$4"
    OUT="$5"
    PLATFORM="$(platform)"
    if [ "$PLATFORM" = windows ]; then
      HOME_DIR="$(cygpath -u "$USERPROFILE")/.keypaste"
      VAULT="$(cygpath -u "$USERPROFILE")/keypaste-upgrade/upgrade-check.kdbx"
    else
      HOME_DIR="$HOME/.keypaste"
      VAULT="$HOME/keypaste-upgrade/upgrade-check.kdbx"
      APPS="$HOME/Applications"
    fi
    VAULT_NATIVE="$(native "$VAULT")"
    run_all
    ;;
esac
