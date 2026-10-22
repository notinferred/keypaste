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
# uninstall, which an entry of the user's own, added first, shows. Open at login's Run value, written after the
# first install as the app's Settings writes it, must come through the upgrade unchanged and be gone after the
# uninstall. The upgraded app then starts once and shows its window before the data is read, so what its first
# start writes is compared too, as on Linux and macOS. On Linux the upgrade is a download left
# beside the old image: the lower app links keypaste from Settings, a start of the higher image while the lower
# app runs hands off to it (D-0397) and still re-points the link, the higher app then starts on its own, a later
# start of the lower one leaves the link, and the person then deletes the lower image. Removing the image
# leaves the link naming a missing file, which is recorded as a known limit, not checked. On macOS the lower
# app's Settings links keypaste at /usr/local/bin, made with sudo while Link is offered as a runner has nobody
# to answer the administrator dialog, and turns on Open at login. The upgrade quits the app, has Finder move the
# old keypaste.app to the Trash and copies the new one from its DMG to the same path, so the link and the
# LaunchAgent, which name that path, must come through unchanged with the five files; the upgraded app must
# then unlock the vault. Uninstalling moves the bundle to the Trash and leaves the link and the LaunchAgent.
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
# only a log naming the failed action counts: an install that never failed proves no recovery (D-0213). On
# macOS it is a download that stopped halfway, a half-length DMG that hdiutil must refuse to attach.
#
# Checks, in order: install-lower, fixture, upgrade, data-after-upgrade, interrupted, downgrade-refused,
# uninstall. A check passes only on its evidence, is unreached when its action could not be done or an
# earlier check failed, and is a contradiction when the action completed and the evidence disagrees.
# downgrade-refused is not-applicable on Linux and macOS, where an AppImage or a keypaste.app has no installer to
# refuse one.
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
readonly OPEN_AT_LOGIN='Open keypaste at login'
readonly RUN_KEY='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
readonly CR=$'\r'
readonly TAB=$'\t'

# shellcheck source=scripts/lib/desktop-drive.sh
. "$ROOT/scripts/lib/desktop-drive.sh"
# shellcheck source=scripts/lib/terminal.sh
. "$ROOT/scripts/lib/terminal.sh"
# shellcheck source=scripts/lib/macos-install.sh
. "$ROOT/scripts/lib/macos-install.sh"

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
    status="${record%%"$TAB"*}"; detail="${record#*"$TAB"}"
    if [ -z "$record" ]; then result=unreached; why="$action was never attempted"; return 1; fi
    if [ "$status" != ok ]; then result=unreached; why="$action: $detail"; return 1; fi
    return 0
  }

  emit() {
    verdict "$1" "$result" "$why"
    case "$result" in pass | not-applicable) ;; *) [ -n "$blocked" ] || blocked="$1" ;; esac
  }

  # What the upgraded app's unlock changed besides the two settings files an unlock writes.
  unlock_wrote_more() { local name; for name in $1; do case "$name" in app.toml | recent.toml) ;; *) printf '%s ' "$name" ;; esac; done; }

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
    elif [ "$platform" = windows ] && [ "$(fact_of "$dir" run_value_after_install)" = absent ]; then
      result=contradiction; why="Open at login's Run value was not there after it was written"
    else result=pass; why="$detail"
    fi
  fi
  emit install-lower

  # On Linux the person links keypaste from the lower app's Settings before the baseline is taken, since the
  # app's unlock writes its settings.
  # A Settings step that could not be done is named before the baseline it would have completed.
  if judge fixture && judge baseline; then
    made="$detail"
    if [ "$platform" = linux ] && ! { judge window && judge unlocked && judge settings && judge link-cli && judge linked \
      && terminal_runs fixture "$LOWER"; }; then :
    elif [ "$platform" = macos ] && ! { judge window && judge unlocked && judge settings && judge link-offered && judge link-made \
      && judge link-cli && judge linked && judge open-at-login && judge login-item && terminal_runs fixture "$LOWER"; }; then :
    elif [ "$(fact_of "$dir" baseline)" != complete ]; then result=contradiction; why="the fixture left no complete baseline: $(fact_of "$dir" baseline)"
    else
      result=pass; why="$made"
      if [ "$platform" = linux ]; then why="$why; Settings linked keypaste and a new terminal runs $LOWER through it, the login shell reading $(fact_of "$dir" shell_profile)"; fi
      if [ "$platform" = macos ]; then why="$why; the runner made the link with sudo while Settings offered Link, whose Link then found this copy linked, Open at login was turned on, and a new terminal runs $LOWER through the link"; fi
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
    elif [ "$platform" = windows ] && [ "$(fact_of "$dir" run_value_after_upgrade)" != "$(fact_of "$dir" run_value_after_install)" ]; then
      result=contradiction; why="the upgrade left Open at login's Run value '$(fact_of "$dir" run_value_after_upgrade)', not '$(fact_of "$dir" run_value_after_install)'"
    else result=pass; why="$detail"
    fi
  fi
  emit upgrade

  if judge read-after-upgrade; then
    if [ "$(fact_of "$dir" data_after_upgrade)" != identical ]; then result=contradiction; why="the upgrade changed user data: $(fact_of "$dir" data_after_upgrade)"
    elif [ "$(fact_of "$dir" token_after_upgrade)" != "${VALUES[2]}" ]; then result=contradiction; why="the vault reads $(fact_of "$dir" token_after_upgrade) for $VARIABLE, not ${VALUES[2]}"
    elif [ "$(fact_of "$dir" log_verify_after_upgrade)" != pass ]; then result=contradiction; why="keypaste log verify says $(fact_of "$dir" log_verify_after_upgrade)"
    elif [ "$(fact_of "$dir" keepassxc_after_upgrade)" != "$HISTORY_TITLES" ]; then result=contradiction; why="KeePassXC reads $(fact_of "$dir" keepassxc_after_upgrade) revisions of $PROJECT's $HOME_ENTRY, not $HISTORY_TITLES"
    elif [ "$platform" = macos ] && ! judge upgraded-unlocked; then :
    elif [ "$platform" = macos ] && [ -n "$(unlock_wrote_more "$(fact_of "$dir" unlock_changed)")" ]; then
      result=contradiction; why="the upgraded app's unlock changed $(unlock_wrote_more "$(fact_of "$dir" unlock_changed)"), not only its settings files"
    elif [ "$platform" = macos ]; then result=pass; why="the vault, its history, the settings, the ceilings, the audit, the link and the LaunchAgent came through the upgrade unchanged, and the upgraded app unlocked the vault"
    else result=pass; why="the vault, its history, the settings, the ceilings and the audit came through the upgrade unchanged"
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
  elif [ "$platform" = macos ]; then
    result=not-applicable; why='a keypaste.app has no installer, so nothing can refuse a downgrade'
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
    elif [ "$platform" = windows ] && [ "$(fact_of "$dir" run_value_after_uninstall)" != absent ]; then
      result=contradiction; why="the uninstall left Open at login's Run value: $(fact_of "$dir" run_value_after_uninstall)"
    elif [ "$platform" = windows ]; then result=pass; why='the app, its user Path entry, its terminal keypaste and its Run value are gone, and the vault, settings, ceilings and audit are untouched'
    elif [ "$platform" = macos ]; then
      result=pass; why="Finder moved the app to the Trash and the vault, settings, ceilings and audit are untouched; the link and the LaunchAgent are left, and a new terminal now says: $(fact_of "$dir" terminal_after_uninstall)"
    else result=pass; why="the app is gone and the vault, settings, ceilings and audit are untouched; the link Settings made is left, and a new terminal now says: $(fact_of "$dir" terminal_after_uninstall)"
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
    osx-arm64) echo macos ;;
    *) die "no installation candidate for $RID" ;;
  esac
}

native() { if [ "$PLATFORM" = windows ]; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

sha256_of() { if command -v sha256sum >/dev/null; then sha256sum < "$1"; else shasum -a 256 < "$1"; fi | awk '{print $1}'; }

powershell_out() { powershell -NoProfile -NonInteractive -Command "$1" | tr -d "$CR"; }

candidate() { # candidate <version>
  local ext
  case "$PLATFORM" in windows) ext=msi ;; linux) ext=AppImage ;; macos) ext=dmg ;; esac
  local name="keypaste-app-$1-$RID-internal-unsigned.$ext"
  [ -f "$CANDIDATES/$name" ] || { echo "no candidate at $CANDIDATES/$name" >&2; return 1; }
  printf '%s' "$CANDIDATES/$name"
}

# --- the data whose survival is the point ---------------------------------------------------------

# The five files a user would lose: the vault, the app's two settings files, this computer's ceilings and the audit;
# on macOS also Open at login's LaunchAgent.
data_files() {
  printf '%s\n' "$VAULT" "$HOME_DIR/app.toml" "$HOME_DIR/recent.toml" "$HOME_DIR/clients.toml" "$HOME_DIR/audit.jsonl"
  if [ "$PLATFORM" = macos ]; then printf '%s\n' "$LAUNCH_AGENT"; fi
}

snapshot() { # snapshot <file>
  local path
  : > "$1"
  while IFS= read -r path; do
    if [ -f "$path" ]; then printf '%s  %s\n' "$(sha256_of "$path")" "$(basename "$path")" >> "$1"
    else printf 'absent  %s\n' "$(basename "$path")" >> "$1"
    fi
  done < <(data_files)
  # The link names the bundle's fixed path, which an upgrade replaces in place (MacCliLink.KeepCurrent).
  if [ "$PLATFORM" = macos ]; then printf '%s  link\n' "$(readlink "$MAC_CLI_LINK" || echo absent)" >> "$1"; fi
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

  # A ceiling this computer keeps for the agent, made before any recent vault names one to hold it.
  "$KP" clients upgrade-check --run off >/dev/null 2>&1 || { echo 'keypaste clients did not write a ceiling'; return 1; }
  "$KP" clients --json 2>/dev/null | jq -e '. == [{"kind":"ceiling","label":"upgrade-check","run":"off"}]' >/dev/null \
    || { echo 'keypaste clients --json does not read the ceiling back'; return 1; }
  printf '[[vault]]\npath = "%s"\nopened_at = "%s"\n' "$VAULT_NATIVE" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$HOME_DIR/recent.toml"
  printf '[[settings]]\nidle_timeout_seconds = 28800\n' > "$HOME_DIR/app.toml"
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
  printf '%s\nh\n' "$MASTER" | "$KP" approve --vault "$VAULT_NATIVE" --approver "$pipe" --approval-timeout 55 \
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
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"env/$PROJECT/$HOME_ENTRY\",\"field\":\"$VARIABLE\",\"reason\":\"4.7d writes one audit record before the upgrade\",\"ttl_seconds\":60}}}"
    sleep 5
  } | "$KP" mcp --vault "$VAULT_NATIVE" --expose 'env/**' --approver "$pipe" --client-label upgrade-check \
      > "$OUT/mcp-response.jsonl" 2> "$OUT/mcp-stderr.txt" || true
  kill "$agent_pid" 2>/dev/null || true
  wait "$agent_pid" 2>/dev/null || true
  fact audit_decision "$(jq -rs '[.[] | select(.decision != null)] | last | .decision // empty' "$HOME_DIR/audit.jsonl" 2>/dev/null | tr -d "$CR")"
  [ -s "$HOME_DIR/audit.jsonl" ]
}

# --- reading the data back ------------------------------------------------------------------------

read_after() { # read_after <when>
  compare "$1"
  fact "token_after_$1" "$(cli get "env/$PROJECT/$HOME_ENTRY" --field "$VARIABLE" --reveal || true)"
  if printf '%s\n' "$MASTER" | "$KP" log verify > "$OUT/log-verify-$1.txt" 2>&1; then
    fact "log_verify_after_$1" pass
  else
    fact "log_verify_after_$1" "exit $?"
  fi
  fact "keepassxc_after_$1" "$(keepassxc)"
  "$KP" clients --json > "$OUT/ceilings-$1.json" 2>&1 || true
}

# --- a new terminal -------------------------------------------------------------------------------

# Where a new terminal should find keypaste: the MSI's install folder, or the link Settings makes.
terminal_expected() {
  case "$PLATFORM" in
    windows) printf '%s\\keypaste.exe' "$(windows_folder)" ;;
    linux) printf '%s' "$HOME/.local/bin/keypaste" ;;
    macos) printf '%s' "$MAC_CLI_LINK" ;;
  esac
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

# Open at login's Run value as the app writes it (WindowsRunKey), or absent.
run_value() { powershell_out "(Get-Item '$RUN_KEY').GetValue('keypaste', 'absent')"; }

# Open at login turned on as Settings does it: the Run value naming the installed app, with --background.
open_at_login() {
  powershell_out "New-ItemProperty -Path '$RUN_KEY' -Name keypaste -Value '\"$(cygpath -w "$APP")\" --background' -PropertyType String -Force | Out-Null" \
    || { echo 'the Run value could not be written'; return 1; }
  fact run_value_after_install "$(run_value)"
}

# --- the app a person starts ----------------------------------------------------------------------

# Every start stops the running app first, since a second start only asks the running one to show its
# window (D-0397). macOS starts the bundle through LaunchServices, Windows the installed program through PowerShell.
start_app() { # start_app <appimage|bundle|exe> <name>
  stop_app
  if [ "$PLATFORM" = macos ]; then
    APP_PID="$(mac_start "$1" "$OUT/app-$2.log")" || { echo "$APP_PID"; APP_PID=''; return 1; }
  elif [ "$PLATFORM" = windows ]; then
    # To a file, not a pipe: the app inherits PowerShell's handles, and an inherited pipe would hold this shell until the app exits.
    powershell -NoProfile -NonInteractive -Command "(Start-Process -FilePath '$(cygpath -w "$1")' -RedirectStandardOutput '$(cygpath -w "$OUT/app-$2.out")' -RedirectStandardError '$(cygpath -w "$OUT/app-$2.log")' -PassThru).Id" \
      > "$OUT/app-$2.pid" || { echo "PowerShell could not start $1"; return 1; }
    APP_PID="$(tr -d "$CR" < "$OUT/app-$2.pid")"
    case "$APP_PID" in '' | *[!0-9]*) echo "starting $1 gave no process ID: $APP_PID"; APP_PID=''; return 1 ;; esac
  else
    "$1" > "$OUT/app-$2.log" 2>&1 &
    APP_PID=$!
  fi
}

stop_app() {
  [ -n "${APP_PID:-}" ] || return 0
  if [ "$PLATFORM" = macos ]; then
    mac_quit
  elif [ "$PLATFORM" = windows ]; then
    taskkill //F //PID "$APP_PID" > /dev/null 2>&1 || true
  else
    pkill -f usr/bin/keypaste-app 2>/dev/null || true
    kill "$APP_PID" 2>/dev/null || true
    wait "$APP_PID" 2>/dev/null || true
  fi
  APP_PID=''
  sleep 2
}

# The lower app, unlocked, links keypaste from Settings as a person does once; a new terminal then runs it.
link_from_settings() {
  if [ "$PLATFORM" = linux ]; then act accessibility enable_accessibility || return 0; fi
  start_app "$APP" lower || true
  { act window drive window 120 \
    && act unlocked drive type "$MASTER" && act unlocked-enter drive key Return && act unlocked drive find 'Lock now' \
    && act settings shortcut 4 && link_cli; } || true
  stop_app
  probe fixture
}

# Linux's Link writes the link itself. macOS's asks an administrator, so a runner makes the link with sudo while
# Link is offered and Link then finds this copy linked; Open at login is turned on there too.
link_cli() {
  if [ "$PLATFORM" = linux ]; then
    act link-cli drive invoke "$LINK_BUTTON" && act linked drive find-prefix "$LINKED_PREFIX"
  else
    act link-offered drive find "$LINK_BUTTON" && act link-made mac_cli_link "$APP/Contents/MacOS/keypaste" \
      && act link-cli drive invoke "$LINK_BUTTON" && act linked drive find-prefix "$LINKED_PREFIX" \
      && act open-at-login drive toggle "$OPEN_AT_LOGIN" && act login-item login_item
  fi
}

login_item() {
  for _ in $(seq 1 40); do [ -f "$LAUNCH_AGENT" ] && break; sleep 0.25; done
  [ -f "$LAUNCH_AGENT" ] || { echo "Open at login wrote no $LAUNCH_AGENT"; return 1; }
  grep -qF "<string>$APP/Contents/MacOS/keypaste-app</string>" "$LAUNCH_AGENT" \
    || { echo "$LAUNCH_AGENT starts another program: $(tr -d '\n' < "$LAUNCH_AGENT")"; return 1; }
  echo "$LAUNCH_AGENT starts $APP/Contents/MacOS/keypaste-app"
}

# The upgraded app opens the vault. Its unlock writes the app's settings, so later transitions compare with what
# it left, and what it changed is recorded.
unlock_upgraded() {
  { act upgraded-typed drive type "$MASTER" && act upgraded-unlocked-enter drive key Return \
    && act upgraded-unlocked drive find 'Lock now'; } || true
  stop_app
  mv "$OUT/hashes-baseline.txt" "$OUT/hashes-baseline-before-unlock.txt"
  snapshot "$OUT/hashes-baseline.txt"
  fact unlock_changed "$(diff "$OUT/hashes-baseline-before-unlock.txt" "$OUT/hashes-baseline.txt" | sed -n 's/^> [^ ]*  //p' | tr '\n' ' ')"
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
  printf '{%s}' "$(printf '%s' "$code" | tr '[:lower:]' '[:upper:]')"
}

# An MSI verbose log is UTF-16 (D-0209); grep reads it to the end, as a grep -q stopping at the match kills tr and pipefail then reads no match (F.43).
msi_log_says() { # msi_log_says <log> <extended-regexp>
  [ -f "$1" ] && tr -d '\0' < "$1" | grep -iE "$2" > /dev/null
}

app_version() {
  local app="$1"
  [ "$PLATFORM" != macos ] || app="$app/Contents/MacOS/keypaste-app"
  [ -x "$app" ] || { echo absent; return 0; }
  "$app" --version 2>/dev/null | tr -d "[:space:]$CR" || echo unreadable
}

# --- the run --------------------------------------------------------------------------------------

install_lower() {
  local msi app code dmg
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
    open_at_login || return 1
    echo "msiexec /i exited 0; installed $(cygpath -m "$APP") reporting $(fact_read lower_version), $(fact_read products_after_install) registered; Open at login's Run value written"
  elif [ "$PLATFORM" = macos ]; then
    dmg="$(candidate "$LOWER")" || return 1
    APP=/Applications/keypaste.app
    [ ! -e "$APP" ] || { echo "$APP already exists on this runner"; return 1; }
    mac_install "$dmg" "$APP" || return 1
    fact lower_version "$(app_version "$APP")"
    "$APP/Contents/MacOS/keypaste-app" --selftest > /dev/null || { echo 'the installed app failed --selftest'; return 1; }
    fact products_after_install "$(mac_bundles)"
    echo "copied $LOWER's keypaste.app to $APP, which reports $(fact_read lower_version)"
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
  local msi app code higher dmg
  if [ "$PLATFORM" = windows ]; then
    msi="$(candidate "$HIGHER")" || return 1
    code="$(msiexec_install "$msi" "$OUT/upgrade.log")"
    [ "$code" = 0 ] || { echo "msiexec /i of $HIGHER exited $code"; return 1; }
    [ -x "$APP" ] || { echo "the upgrade left no app at $APP"; return 1; }
    fact higher_version "$(app_version "$APP")"
    fact products_after_upgrade "$(windows_registration_count)"
    fact registrations_after_upgrade "$(windows_registrations | tr '\n' ' ')"
    "$APP" --selftest > /dev/null || { echo 'the upgraded app failed --selftest'; return 1; }
    # Its first start, stopped before anything is read, so the Run value and the data are compared after it.
    start_app "$APP" higher || return 1
    drive window 120 || { stop_app; echo "the $HIGHER app showed no window"; return 1; }
    stop_app
    fact run_value_after_upgrade "$(run_value)"
    probe upgrade
    echo "MajorUpgrade exited 0; $(cygpath -m "$APP") now reports $(fact_read higher_version) and showed its window"
  elif [ "$PLATFORM" = macos ]; then
    # The upgraded app is left running and locked, so the data is compared with it started, before its unlock.
    dmg="$(candidate "$HIGHER")" || return 1
    stop_app
    mac_quit
    mac_trash "$APP" || { echo "Finder did not move the $LOWER app to the Trash"; return 1; }
    mac_install "$dmg" "$APP" || return 1
    fact higher_version "$(app_version "$APP")"
    "$APP/Contents/MacOS/keypaste-app" --selftest > /dev/null || { echo 'the upgraded app failed --selftest'; return 1; }
    fact products_after_upgrade "$(mac_bundles)"
    start_app "$APP" higher || return 1
    drive window 120 || { stop_app; echo "the $HIGHER app showed no window"; return 1; }
    probe upgrade
    echo "Finder moved the $LOWER app to the Trash and $HIGHER's took its place; $APP reports $(fact_read higher_version) and shows its window"
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
  local msi log="$OUT/interrupt.log" source code previous size image partial said volume
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
  elif [ "$PLATFORM" = macos ]; then
    # A download that stopped halfway: the image has lost its trailer, so hdiutil must refuse it and nothing is copied.
    image="$(candidate "$THIRD")" || return 1
    partial="$OUT/$(basename "$image" .dmg)-partial.dmg"
    head -c "$(( $(wc -c < "$image") / 2 ))" "$image" > "$partial"
    code=0
    volume="$(mac_mountpoint)"
    mkdir -p "$volume"
    said="$(mac_bounded 120 hdiutil attach "$partial" -readonly -nobrowse -noautoopen -mountpoint "$volume" 2>&1)" || code=$?
    fact interrupted_exit "$code"
    # 142 is the bound expiring, which refuses nothing; only a refusal of the image itself is the truncation's.
    if [ "$code" != 0 ] && [ "$code" != 142 ] && ! mount | grep -qF " on $volume (" \
      && printf '%s' "$said" | grep -qiE 'image not recognized|corrupt'; then
      fact interrupted_failed yes
      fact interrupted_where "hdiutil attach refused the half-length image: $(printf '%s' "$said" | tail -n 1)"
    elif [ "$code" != 0 ] && ! mount | grep -qF " on $volume ("; then
      fact interrupted_failed no
      fact interrupted_where "hdiutil refused the half-length image for another reason (exit $code): $(printf '%s' "$said" | tail -n 1)"
    else
      fact interrupted_failed no
      fact interrupted_where "hdiutil attached the half-length image (exit $code)"
      mac_detach "$volume" || true
    fi
    fact version_after_interrupt "$(app_version "$APP")"
    fact products_after_interrupt "$(mac_bundles)"
    rm -f "$partial"
  else
    # A download that stops halfway: its temporary file is never renamed, so nothing runs it.
    image="$(candidate "$THIRD")" || return 1
    partial="$APPS/$(basename "$image").part"
    head -c "$(( $(wc -c < "$image") / 2 ))" "$image" > "$partial"
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
    fact run_value_after_uninstall "$(run_value)"
    echo "msiexec /x exited 0"
  elif [ "$PLATFORM" = macos ]; then
    stop_app
    mac_trash "$APP" || { echo "Finder did not move $APP to the Trash"; return 1; }
    if [ -e "$APP" ]; then fact installed_app_after_uninstall "$APP"; else fact installed_app_after_uninstall absent; fi
    fact shortcut_after_uninstall absent
    fact registration_after_uninstall absent
    echo "Finder moved $APP to the Trash"
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
    macos) os="$(sw_vers -productName) $(sw_vers -productVersion) ($(sw_vers -buildVersion))" ;;
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
    if [ "$PLATFORM" != windows ]; then link_from_settings; fi
    if act baseline baseline && act upgrade upgrade; then
      act read-after-upgrade read_after upgrade || true
      if [ "$PLATFORM" = macos ]; then unlock_upgraded; fi
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
  local folder='C:\Users\runner\AppData\Local\Programs\keypaste' link=/home/runner/.local/bin/keypaste mac_link=/usr/local/bin/keypaste
  local path_before='%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;C:\work\upgrade-desktop\user-path-entry'
  local bridge='usage: keypaste mcp [--vault <path>]'
  local run_line="\"$folder\\keypaste-app.exe\" --background"

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
    set_fact "$1" run_value_after_install "$run_line"
    set_fact "$1" run_value_after_upgrade "$run_line"
    set_fact "$1" run_value_after_uninstall absent
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
  # The passing run on macOS, where the lower app links keypaste and turns on Open at login, and the upgraded app
  # unlocks the vault.
  passing_macos() {
    passing "$1"
    drop_action "$1" downgrade
    printf '%s\n' \
      '{"action":"window","status":"ok","detail":"window keypaste"}' \
      '{"action":"unlocked","status":"ok","detail":"found Lock now"}' \
      '{"action":"settings","status":"ok","detail":"sent cmd+4"}' \
      '{"action":"link-offered","status":"ok","detail":"found Link the keypaste command"}' \
      '{"action":"link-made","status":"ok","detail":"/usr/local/bin/keypaste -> /Applications/keypaste.app/Contents/MacOS/keypaste"}' \
      '{"action":"link-cli","status":"ok","detail":"invoked Link the keypaste command"}' \
      '{"action":"linked","status":"ok","detail":"A terminal runs this app\u0027s keypaste from /usr/local/bin/keypaste."}' \
      '{"action":"open-at-login","status":"ok","detail":"invoked Open keypaste at login"}' \
      '{"action":"login-item","status":"ok","detail":"com.keypaste.app.plist starts /Applications/keypaste.app/Contents/MacOS/keypaste-app"}' \
      '{"action":"upgraded-typed","status":"ok","detail":"typed 21 characters"}' \
      '{"action":"upgraded-unlocked-enter","status":"ok","detail":"sent Return"}' \
      '{"action":"upgraded-unlocked","status":"ok","detail":"found Lock now"}' >> "$work/$1/driver.jsonl"
    set_fact "$1" platform macos
    set_fact "$1" terminal_expected "$mac_link"
    set_fact "$1" terminal_after_fixture "$mac_link"$'\t'"$LOWER"$'\t'"$bridge"
    set_fact "$1" terminal_after_upgrade "$mac_link"$'\t'"$HIGHER"$'\t'"$bridge"
    set_fact "$1" terminal_after_interrupt "$mac_link"$'\t'"$HIGHER"$'\t'"$bridge"
    set_fact "$1" terminal_after_uninstall absent
    set_fact "$1" unlock_changed 'app.toml recent.toml '
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

  passing all-pass
  passing lower-not-installed
  refuse_action lower-not-installed install-lower 'msiexec /i exited 1603'
  passing lower-reports-another-version
  set_fact lower-reports-another-version lower_version 0.3.1
  passing install-unregistered
  set_fact install-unregistered products_after_install 0
  passing fixture-incomplete
  set_fact fixture-incomplete baseline 'absent clients.toml'
  passing no-baseline
  refuse_action no-baseline baseline 'no audit record was written'
  passing upgrade-not-taken
  set_fact upgrade-not-taken higher_version "$LOWER"
  passing two-products
  set_fact two-products products_after_upgrade 2
  passing upgraded-showed-no-window
  refuse_action upgraded-showed-no-window upgrade "the $HIGHER app showed no window"
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
  set_fact install-other-case terminal_after_install "$(lower "$folder")\\keypaste.exe"$'\t'"$LOWER"$'\t'"$bridge"
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

  # Open at login's Run value on Windows.
  passing open-at-login-unwritten
  set_fact open-at-login-unwritten run_value_after_install absent
  passing upgrade-took-open-at-login
  set_fact upgrade-took-open-at-login run_value_after_upgrade absent
  passing uninstall-left-open-at-login
  set_fact uninstall-left-open-at-login run_value_after_uninstall "$run_line"

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

  # macOS.
  passing_macos macos
  passing_macos macos-not-linked
  refuse_action macos-not-linked link-made '/usr/local/bin/keypaste already exists'
  set_fact macos-not-linked baseline 'absent  link'
  passing_macos macos-no-login-item
  refuse_action macos-no-login-item login-item 'Open at login wrote no com.keypaste.app.plist'
  set_fact macos-no-login-item baseline 'absent  com.keypaste.app.plist'
  passing_macos macos-unlock-wrote-the-vault
  set_fact macos-unlock-wrote-the-vault unlock_changed 'upgrade-check.kdbx app.toml '
  passing_macos macos-link-unread
  set_fact macos-link-unread terminal_after_fixture absent
  passing_macos macos-upgraded-locked
  refuse_action macos-upgraded-locked upgraded-unlocked "nothing named 'Lock now' appeared"
  passing_macos macos-agent-changed
  set_fact macos-agent-changed data_after_upgrade '< 1f2e  com.keypaste.app.plist > 9a8b  com.keypaste.app.plist'
  passing_macos macos-link-runs-the-lower
  set_fact macos-link-runs-the-lower terminal_after_upgrade "$mac_link"$'\t'"$LOWER"$'\t'"$bridge"
  passing_macos macos-interrupt-attached
  set_fact macos-interrupt-attached interrupted_failed no
  set_fact macos-interrupt-attached interrupted_where 'hdiutil attached the half-length image (exit 0)'
  passing_macos macos-uninstall-left-the-app
  set_fact macos-uninstall-left-the-app installed_app_after_uninstall /Applications/keypaste.app

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
  expect upgraded-showed-no-window upgrade unreached
  expect upgraded-showed-no-window data-after-upgrade unreached
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
  expect open-at-login-unwritten install-lower contradiction
  expect upgrade-took-open-at-login upgrade contradiction
  expect uninstall-left-open-at-login uninstall contradiction

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

  for check in "${CHECKS[@]}"; do
    if [ "$check" = downgrade-refused ]; then expect macos "$check" not-applicable; else expect macos "$check" pass; fi
  done
  expect macos-not-linked fixture unreached
  expect macos-no-login-item fixture unreached
  expect macos-no-login-item upgrade unreached
  expect macos-link-unread fixture contradiction
  expect macos-upgraded-locked upgrade pass
  expect macos-upgraded-locked data-after-upgrade unreached
  expect macos-agent-changed data-after-upgrade contradiction
  expect macos-unlock-wrote-the-vault data-after-upgrade contradiction
  expect macos-link-runs-the-lower upgrade contradiction
  expect macos-interrupt-attached interrupted unreached
  expect macos-uninstall-left-the-app uninstall contradiction

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
    mkdir -p "$5"
    # Absolute, because LaunchServices starts the macOS app from / and mount(8) reports resolved paths.
    OUT="$(cd "$5" && pwd -P)"
    PLATFORM="$(platform)"
    if [ "$PLATFORM" = windows ]; then
      HOME_DIR="$(cygpath -u "$USERPROFILE")/.keypaste"
      VAULT="$(cygpath -u "$USERPROFILE")/keypaste-upgrade/upgrade-check.kdbx"
    else
      HOME_DIR="$HOME/.keypaste"
      VAULT="$HOME/keypaste-upgrade/upgrade-check.kdbx"
      APPS="$HOME/Applications"
      LAUNCH_AGENT="$HOME/Library/LaunchAgents/com.keypaste.app.plist"
    fi
    VAULT_NATIVE="$(native "$VAULT")"
    run_all
    ;;
esac
