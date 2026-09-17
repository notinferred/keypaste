#!/usr/bin/env bash
# 4.7d: installs one internal desktop candidate, fills a real ~/.keypaste and vault with it, then upgrades,
# interrupts an upgrade, refuses a downgrade and uninstalls, checking after every transition that the user's
# data is byte-identical and still readable.
#
# The candidates are three unpublished builds of one commit at distinct numeric versions, because an MSI
# ProductVersion is numeric and two candidates of one version cannot be ordered (D-0139, D-0147). The CLI/MCP
# is the published release: neither package contains it, so no upgrade can change it (D-0206).
#
# This runs against the real user profile - the install folder, the Start menu entry, HKCU and ~/.keypaste -
# because that is what the checks are about. It therefore refuses to start unless the caller states that the
# profile is disposable: CI=true on a fresh runner, or KEYPASTE_UPGRADE_SANDBOX=1 for a throwaway account or
# virtual machine. On Windows, environment variables cannot move an MSI: Windows Installer resolves the
# install folder, the Start menu and HKCU through known folders, so redirecting LOCALAPPDATA sandboxes
# nothing (D-0206).
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
# Environment: KPXC_CLI  keepassxc-cli, if it is not on PATH
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
readonly VARIABLE='UPGRADE_TOKEN'
readonly VALUES=(first-value second-value third-value)
readonly HISTORY_TITLES=3
readonly DOWNGRADE_MESSAGE='A newer keypaste is already installed.'
readonly CR=$'\r'

usage() {
  echo 'usage: bash scripts/exercise-desktop-upgrade.sh <rid> <candidates-dir> <cli-version> <cli-dir> <output-dir> | --classify <output-dir> | --selftest'
}

die() { echo "::error::$*" >&2; exit 1; }

# --- classification -------------------------------------------------------------------------------

verdict() { jq -cn --arg check "$1" --arg result "$2" --arg why "$3" '{check: $check, result: $result, why: $why}'; }

fact_of() { [ -s "$1/facts.jsonl" ] && jq -rs --arg k "$2" '[.[] | select(.fact == $k) | .value] | last // empty' "$1/facts.jsonl" || true; }

step() { [ -s "$1/driver.jsonl" ] && jq -rs --arg a "$2" '[.[] | select(.action == $a)] | last | if . == null then "" else "\(.status)\t\(.detail)" end' "$1/driver.jsonl" || true; }

classify() {
  local dir="$1" check result why record status detail blocked='' platform
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

  if judge install-lower; then
    if [ "$(fact_of "$dir" lower_version)" = "$LOWER" ]; then result=pass; why="$detail"
    else result=contradiction; why="the installed app reports $(fact_of "$dir" lower_version), not $LOWER"
    fi
  fi
  emit install-lower

  if judge fixture; then
    if [ "$(fact_of "$dir" baseline)" = complete ]; then result=pass; why="$detail"
    else result=contradiction; why="the fixture left no complete baseline: $(fact_of "$dir" baseline)"
    fi
  fi
  emit fixture

  if judge upgrade; then
    if [ "$(fact_of "$dir" higher_version)" != "$HIGHER" ]; then result=contradiction; why="after the upgrade the app reports $(fact_of "$dir" higher_version), not $HIGHER"
    elif [ "$(fact_of "$dir" products_after_upgrade)" != 1 ]; then result=contradiction; why="$(fact_of "$dir" products_after_upgrade) keypaste installations are registered, not 1"
    else result=pass; why="$detail"
    fi
  fi
  emit upgrade

  if judge read-after-upgrade; then
    if [ "$(fact_of "$dir" data_after_upgrade)" != identical ]; then result=contradiction; why="the upgrade changed user data: $(fact_of "$dir" data_after_upgrade)"
    elif [ "$(fact_of "$dir" token_after_upgrade)" != "${VALUES[2]}" ]; then result=contradiction; why="the vault reads $(fact_of "$dir" token_after_upgrade) for $VARIABLE, not ${VALUES[2]}"
    elif [ "$(fact_of "$dir" log_verify_after_upgrade)" != pass ]; then result=contradiction; why="keypaste log verify says $(fact_of "$dir" log_verify_after_upgrade)"
    elif [ "$(fact_of "$dir" keepassxc_after_upgrade)" != "$HISTORY_TITLES" ]; then result=contradiction; why="KeePassXC reads $(fact_of "$dir" keepassxc_after_upgrade) $VARIABLE revisions, not $HISTORY_TITLES"
    else result=pass; why="the vault, its history, the settings, the policy and the audit came through the upgrade unchanged"
    fi
  fi
  emit data-after-upgrade

  if judge interrupt; then
    if [ "$(fact_of "$dir" interrupted_exit)" = 0 ]; then result=contradiction; why='the install that was meant to fail succeeded'
    elif [ "$(fact_of "$dir" version_after_interrupt)" != "$HIGHER" ]; then result=contradiction; why="after the interrupted install the app reports $(fact_of "$dir" version_after_interrupt), not $HIGHER"
    elif [ "$(fact_of "$dir" data_after_interrupt)" != identical ]; then result=contradiction; why="the interrupted install changed user data: $(fact_of "$dir" data_after_interrupt)"
    else result=pass; why="exit $(fact_of "$dir" interrupted_exit); $(fact_of "$dir" interrupted_where); $HIGHER still runs"
    fi
  fi
  emit interrupted

  if [ "$platform" = linux ]; then
    result=not-applicable; why='an AppImage has no installer, so nothing can refuse a downgrade'
  elif judge downgrade; then
    if [ "$(fact_of "$dir" downgrade_exit)" = 0 ]; then result=contradiction; why="the MSI installed $LOWER over $HIGHER"
    elif [ "$(fact_of "$dir" downgrade_message)" != present ]; then result=contradiction; why="the install log does not say '$DOWNGRADE_MESSAGE'"
    elif [ "$(fact_of "$dir" version_after_downgrade)" != "$HIGHER" ]; then result=contradiction; why="after the refused downgrade the app reports $(fact_of "$dir" version_after_downgrade)"
    else result=pass; why="exit $(fact_of "$dir" downgrade_exit) and the downgrade message; $HIGHER still runs"
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
    else result=pass; why='the app is gone and the vault, settings, policy and audit are untouched'
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
    | tr -d " \t$CR\n" | grep -o "<Key>Title</Key><Value>$VARIABLE</Value>" | grep -c . || true
}

# Everything the transitions must preserve, made by the published CLI (D-0146).
fixture() {
  local value
  KP="$(find "$CLI_DIR" -maxdepth 3 -type f \( -name keypaste -o -name keypaste.exe \) | head -n 1)"
  MCP="$(find "$CLI_DIR" -maxdepth 3 -type f \( -name keypaste-mcp -o -name keypaste-mcp.exe \) | head -n 1)"
  [ -n "$KP" ] && [ -n "$MCP" ] || { echo "no keypaste or keypaste-mcp under $CLI_DIR"; return 1; }
  chmod +x "$KP" "$MCP" 2>/dev/null || true
  "$KP" --version | tr -d "$CR" | grep -qF "$CLI_VERSION" || { echo "the CLI is not $CLI_VERSION: $("$KP" --version)"; return 1; }

  mkdir -p "$(dirname "$VAULT")" "$HOME_DIR"
  printf '%s\n%s\n' "$MASTER" "$MASTER" | "$KP" init "$VAULT_NATIVE" >/dev/null || { echo 'keypaste init failed'; return 1; }
  printf '%s\n' "$MASTER" | "$KP" add "$ENTRY" --username seeded --url https://example.test \
    --notes 'made by the CLI before the upgrade' --generate --vault "$VAULT_NATIVE" >/dev/null \
    || { echo 'keypaste add failed'; return 1; }
  # Three values, so the entry carries two history revisions the upgrade must not lose (D-0014).
  for value in "${VALUES[@]}"; do
    printf '%s\n%s\n' "$MASTER" "$value" | "$KP" env set "$PROJECT" "$VARIABLE" --vault "$VAULT_NATIVE" >/dev/null \
      || { echo "keypaste env set $value failed"; return 1; }
  done

  printf '[[vault]]\npath = "%s"\nopened_at = "%s"\n' "$VAULT_NATIVE" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$HOME_DIR/recent.toml"
  printf 'idle_timeout_seconds = 28800\n' > "$HOME_DIR/app.toml"
  printf '[[allow]]\nclient = "upgrade-check"\nentries = ["env/%s/**"]\nfields = ["password"]\nmax_ttl_seconds = 60\n' \
    "$PROJECT" > "$HOME_DIR/policy.toml"
  "$KP" policy ls >/dev/null || { echo 'keypaste policy ls refused the policy file'; return 1; }
  audit_record || { echo 'no audit record was written'; return 1; }

  snapshot "$OUT/hashes-baseline.txt"
  if grep -q '^absent' "$OUT/hashes-baseline.txt"; then fact baseline "$(grep '^absent' "$OUT/hashes-baseline.txt" | tr -d "$CR" | tr '\n' ' ')"
  else fact baseline complete
  fi
  fact keepassxc_at_fixture "$(keepassxc)"
  echo "vault $VAULT_NATIVE, $(wc -l < "$OUT/hashes-baseline.txt" | tr -d ' ') files, made by $("$KP" --version | tr -d "$CR")"
}

# One approved request, so ~/.keypaste holds an audit chain the transitions must not break.
audit_record() {
  local pipe="keypaste-upgrade-check-$$-$RANDOM" agent_pid
  printf '%s\ny\n' "$MASTER" | "$KP" agent --vault "$VAULT_NATIVE" --approver "$pipe" --approval-timeout 55 \
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
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"env/$PROJECT/$VARIABLE\",\"field\":\"password\",\"reason\":\"4.7d writes one audit record before the upgrade\",\"ttl_seconds\":60}}}"
    sleep 5
  } | "$MCP" --vault "$VAULT_NATIVE" --expose "env/**" --approver "$pipe" --client-label upgrade-check \
      > "$OUT/mcp-response.jsonl" 2> "$OUT/mcp-stderr.txt" || true
  kill "$agent_pid" 2>/dev/null || true
  wait "$agent_pid" 2>/dev/null || true
  fact audit_decision "$(jq -rs '[.[] | select(.decision != null)] | last | .decision // empty' "$HOME_DIR/audit.jsonl" 2>/dev/null | tr -d "$CR")"
  [ -s "$HOME_DIR/audit.jsonl" ]
}

# --- reading the data back ------------------------------------------------------------------------

read_after() { # read_after <when>
  compare "$1"
  fact "token_after_$1" "$(cli get "env/$PROJECT/$VARIABLE" --show || true)"
  if printf '%s\n' "$MASTER" | "$KP" log verify > "$OUT/log-verify-$1.txt" 2>&1; then
    fact "log_verify_after_$1" pass
  else
    fact "log_verify_after_$1" "exit $?"
  fi
  fact "keepassxc_after_$1" "$(keepassxc)"
  "$KP" policy ls > "$OUT/policy-$1.txt" 2>&1 || true
}

# --- Windows transitions --------------------------------------------------------------------------

msiexec_install() { # msiexec_install <msi> <log> [property...]
  local msi="$1" log="$2" extra='' property
  shift 2
  for property in "$@"; do extra="$extra, '$property'"; done
  powershell_out "(Start-Process msiexec.exe -ArgumentList '/i', '\"$(cygpath -w "$msi")\"', '/qn', '/l*v', '\"$(cygpath -w "$log")\"'$extra -Wait -PassThru).ExitCode"
}

windows_app() { printf '%s' "$(cygpath -u "$LOCALAPPDATA")/Programs/keypaste/keypaste-app.exe"; }

windows_shortcut() { printf '%s' "$(cygpath -u "$APPDATA")/Microsoft/Windows/Start Menu/Programs/keypaste.lnk"; }

# The per-user installation registers itself under HKCU, which is also where the uninstall must leave nothing.
windows_registrations() {
  powershell_out "@(Get-ItemProperty 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*' -ErrorAction SilentlyContinue | Where-Object { \$_.DisplayName -like 'keypaste*' } | ForEach-Object { \"\$(\$_.DisplayName)|\$(\$_.DisplayVersion)\" })"
}

windows_registration_count() { windows_registrations | grep -c . || true; }

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
    code="$(msiexec_install "$msi" "$OUT/install-lower.log")"
    [ "$code" = 0 ] || { echo "msiexec /i exited $code"; return 1; }
    APP="$(windows_app)"
    [ -x "$APP" ] || { echo "msiexec exited 0 and $APP does not exist"; return 1; }
    fact lower_version "$(app_version "$APP")"
    "$APP" --selftest > /dev/null || { echo 'the installed app failed --selftest'; return 1; }
    fact registrations_after_install "$(windows_registrations | tr '\n' ' ')"
    echo "msiexec /i exited 0; installed $(cygpath -m "$APP") reporting $(fact_read lower_version)"
  else
    app="$(candidate "$LOWER")" || return 1
    mkdir -p "$(dirname "$APPIMAGE")"
    cp "$app" "$APPIMAGE.part" && chmod +x "$APPIMAGE.part" && mv "$APPIMAGE.part" "$APPIMAGE"
    APP="$APPIMAGE"
    fact lower_version "$(app_version "$APP")"
    "$APP" --selftest > /dev/null || { echo 'the installed AppImage failed --selftest'; return 1; }
    echo "placed $APPIMAGE reporting $(fact_read lower_version)"
  fi
}

fact_read() { fact_of "$OUT" "$1"; }

upgrade() {
  local msi app code
  if [ "$PLATFORM" = windows ]; then
    msi="$(candidate "$HIGHER")" || return 1
    code="$(msiexec_install "$msi" "$OUT/upgrade.log")"
    [ "$code" = 0 ] || { echo "msiexec /i of $HIGHER exited $code"; return 1; }
    [ -x "$APP" ] || { echo "the upgrade left no app at $APP"; return 1; }
    fact higher_version "$(app_version "$APP")"
    fact products_after_upgrade "$(windows_registration_count)"
    fact registrations_after_upgrade "$(windows_registrations | tr '\n' ' ')"
    "$APP" --selftest > /dev/null || { echo 'the upgraded app failed --selftest'; return 1; }
    echo "MajorUpgrade exited 0; $(cygpath -m "$APP") now reports $(fact_read higher_version)"
  else
    # An AppImage has no installer: an upgrade is a complete file put in place of the old one.
    app="$(candidate "$HIGHER")" || return 1
    cp "$app" "$APPIMAGE.part" && chmod +x "$APPIMAGE.part" && mv "$APPIMAGE.part" "$APPIMAGE"
    fact higher_version "$(app_version "$APPIMAGE")"
    fact products_after_upgrade "$(find "$(dirname "$APPIMAGE")" -maxdepth 1 -name 'keypaste*.AppImage' | grep -c . || true)"
    "$APPIMAGE" --selftest > /dev/null || { echo 'the replaced AppImage failed --selftest'; return 1; }
    echo "replaced in place; $APPIMAGE now reports $(fact_read higher_version)"
  fi
}

# The third candidate is never installed: it is started against a directory it cannot write, so the
# transaction fails and Windows Installer rolls back to the version that was there (D-0206).
interrupt() {
  local msi code log="$OUT/interrupt.log" where
  if [ "$PLATFORM" = windows ]; then
    msi="$(candidate "$THIRD")" || return 1
    code="$(msiexec_install "$msi" "$log" 'INSTALLFOLDER=Z:\keypaste-no-such-volume')"
    fact interrupted_exit "$code"
    where='the log names no rollback'
    grep -qi 'action.*RemoveExistingProducts' "$log" 2>/dev/null && where='it stopped after RemoveExistingProducts'
    grep -qi 'action.*Rollback' "$log" 2>/dev/null && where="$where and rolled back"
    fact interrupted_where "$where"
    fact version_after_interrupt "$(app_version "$APP")"
    fact registrations_after_interrupt "$(windows_registrations | tr '\n' ' ')"
  else
    # A copy that stops halfway: the temporary file is never renamed, so the running image is untouched.
    msi="$(candidate "$THIRD")" || return 1
    head -c "$(( $(stat -c %s "$msi") / 2 ))" "$msi" > "$APPIMAGE.part"
    fact interrupted_exit 1
    fact interrupted_where 'the partial copy was never renamed over the installed image'
    fact version_after_interrupt "$(app_version "$APPIMAGE")"
    rm -f "$APPIMAGE.part"
  fi
  compare interrupt
}

downgrade() {
  local msi code log="$OUT/downgrade.log"
  msi="$(candidate "$LOWER")" || return 1
  code="$(msiexec_install "$msi" "$log")"
  fact downgrade_exit "$code"
  if grep -qF "$DOWNGRADE_MESSAGE" "$log" 2>/dev/null; then fact downgrade_message present; else fact downgrade_message absent; fi
  fact version_after_downgrade "$(app_version "$APP")"
}

uninstall() {
  local code
  if [ "$PLATFORM" = windows ]; then
    code="$(powershell_out "(Start-Process msiexec.exe -ArgumentList '/x', '\"$(cygpath -w "$(candidate "$HIGHER")")\"', '/qn', '/l*v', '\"$(cygpath -w "$OUT/uninstall.log")\"' -Wait -PassThru).ExitCode")"
    [ "$code" = 0 ] || { echo "msiexec /x exited $code"; return 1; }
    if [ -e "$(dirname "$APP")" ]; then fact installed_app_after_uninstall "$(cygpath -m "$(dirname "$APP")")"; else fact installed_app_after_uninstall absent; fi
    if [ -f "$(windows_shortcut)" ]; then fact shortcut_after_uninstall present; else fact shortcut_after_uninstall absent; fi
    if [ "$(powershell_out "if (Test-Path 'HKCU:\\Software\\keypaste\\Installer') { 'present' } else { 'absent' }")" = absent ] \
      && [ "$(windows_registration_count)" = 0 ]; then fact registration_after_uninstall absent
    else fact registration_after_uninstall "$(windows_registrations | tr '\n' ' ')HKCU key $(powershell_out "if (Test-Path 'HKCU:\\Software\\keypaste\\Installer') { 'present' } else { 'absent' }")"
    fi
    echo "msiexec /x exited 0"
  else
    rm -f "$APPIMAGE"
    if [ -e "$APPIMAGE" ]; then fact installed_app_after_uninstall "$APPIMAGE"; else fact installed_app_after_uninstall absent; fi
    fact shortcut_after_uninstall absent
    fact registration_after_uninstall absent
    echo "removed $APPIMAGE"
  fi
  read_after uninstall
}

# shellcheck disable=SC2016 # PowerShell expands its own variables, not bash.
environment() {
  local os
  case "$PLATFORM" in
    linux) os="$(sed -n 's/^PRETTY_NAME="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' /etc/os-release) $(uname -r)" ;;
    windows) os="$(powershell_out '(Get-CimInstance Win32_OperatingSystem | ForEach-Object { "$($_.Caption) $($_.Version)" })')" ;;
  esac
  jq -n \
    --arg rid "$RID" \
    --arg os "$os" \
    --arg image "${ImageOS:-local} ${ImageVersion:-}" \
    --arg sha "${GITHUB_SHA:-$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || echo unknown)}" \
    --arg run "${GITHUB_RUN_ID:-local}" \
    --arg versions "$LOWER $HIGHER $THIRD" \
    --arg cli "$CLI_VERSION" \
    --arg vault "$(native "$VAULT")" \
    --arg home "$(native "$HOME_DIR")" \
    --arg keepassxc "$({ "${KPXC_CLI:-keepassxc-cli}" --version 2>/dev/null || echo absent; } | tr -d "$CR")" \
    '{rid: $rid, os: $os, runnerImage: $image, sha: $sha, run: $run, candidates: $versions, cli: $cli,
      vault: $vault, home: $home, keepassxc: $keepassxc}'
}

run_all() {
  mkdir -p "$OUT"
  : > "$OUT/facts.jsonl"
  : > "$OUT/driver.jsonl"
  [ "${CI:-}" = true ] || [ "${KEYPASTE_UPGRADE_SANDBOX:-}" = 1 ] \
    || die 'this installs into the real user profile: set CI=true on a fresh runner, or KEYPASTE_UPGRADE_SANDBOX=1 for a throwaway account or virtual machine'
  fact platform "$PLATFORM"

  if act install-lower install_lower && act fixture fixture && act upgrade upgrade; then
    act read-after-upgrade read_after upgrade || true
    act interrupt interrupt || true
    if [ "$PLATFORM" = windows ]; then act downgrade downgrade || true; fi
    act uninstall uninstall || true
  fi
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

  # A complete, passing Windows run's records; each fixture below changes one thing.
  passing() {
    local dir="$work/$1"
    mkdir -p "$dir"
    printf '%s\n' \
      '{"action":"install-lower","status":"ok","detail":"msiexec /i exited 0"}' \
      '{"action":"fixture","status":"ok","detail":"vault made, 5 files"}' \
      '{"action":"upgrade","status":"ok","detail":"MajorUpgrade exited 0"}' \
      '{"action":"read-after-upgrade","status":"ok","detail":"read back"}' \
      '{"action":"interrupt","status":"ok","detail":"exit 1603"}' \
      '{"action":"downgrade","status":"ok","detail":"exit 1603"}' \
      '{"action":"uninstall","status":"ok","detail":"msiexec /x exited 0"}' > "$dir/driver.jsonl"
    printf '%s\n' \
      '{"fact":"platform","value":"windows"}' \
      "{\"fact\":\"lower_version\",\"value\":\"$LOWER\"}" \
      '{"fact":"baseline","value":"complete"}' \
      "{\"fact\":\"higher_version\",\"value\":\"$HIGHER\"}" \
      '{"fact":"products_after_upgrade","value":"1"}' \
      '{"fact":"data_after_upgrade","value":"identical"}' \
      "{\"fact\":\"token_after_upgrade\",\"value\":\"${VALUES[2]}\"}" \
      '{"fact":"log_verify_after_upgrade","value":"pass"}' \
      "{\"fact\":\"keepassxc_after_upgrade\",\"value\":\"$HISTORY_TITLES\"}" \
      '{"fact":"interrupted_exit","value":"1603"}' \
      '{"fact":"interrupted_where","value":"it stopped after RemoveExistingProducts and rolled back"}' \
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
  passing linux
  set_fact linux platform linux
  drop_action linux downgrade
  passing lower-not-installed
  refuse_action lower-not-installed install-lower 'msiexec /i exited 1603'
  passing lower-reports-another-version
  set_fact lower-reports-another-version lower_version 0.3.1
  passing fixture-incomplete
  set_fact fixture-incomplete baseline 'absent policy.toml'
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
  jq -c 'select(.fact != "finished")' "$work/unfinished/facts.jsonl" > "$work/t" && mv "$work/t" "$work/unfinished/facts.jsonl"

  local check
  for check in "${CHECKS[@]}"; do expect all-pass "$check" pass; done
  expect linux downgrade-refused not-applicable
  expect linux uninstall pass
  expect lower-not-installed install-lower unreached
  expect lower-not-installed fixture unreached
  expect lower-not-installed uninstall unreached
  expect lower-reports-another-version install-lower contradiction
  expect lower-reports-another-version upgrade unreached
  expect fixture-incomplete fixture contradiction
  expect upgrade-not-taken upgrade contradiction
  expect upgrade-not-taken data-after-upgrade unreached
  expect two-products upgrade contradiction
  expect vault-changed data-after-upgrade contradiction
  expect vault-changed interrupted unreached
  expect value-lost data-after-upgrade contradiction
  expect history-lost data-after-upgrade contradiction
  expect audit-broken data-after-upgrade contradiction
  expect interrupt-succeeded interrupted contradiction
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
      APPIMAGE="$HOME/Applications/keypaste.AppImage"
    fi
    VAULT_NATIVE="$(native "$VAULT")"
    run_all
    ;;
esac
