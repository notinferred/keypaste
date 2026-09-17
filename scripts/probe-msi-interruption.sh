#!/usr/bin/env bash
# 4.7d1: measures which way of interrupting a per-user MSI upgrade makes Windows Installer roll the previous
# version back, so 4.7d's interrupted check can be built on a mechanism that was observed rather than assumed.
#
# Two ways are already known not to interrupt anything (D-0210): a property pointing at a volume that does not
# exist fails in CostFinalize, before the transaction, and killing the client `msiexec` leaves the service to
# finish the install. This probe measures three more against the same candidates 4.7d installs:
#
#   kill-service       the SYSTEM `msiexec /V` process is ended while InstallFiles runs
#   deny-folder        the install folder denies writes to everyone for the length of the upgrade
#   unreadable-source  the package the upgrade is running from is deleted, then truncated, at InstallFiles
#
# Each method starts from a freshly installed lower candidate, attempts the upgrade, and records what the
# verbose log and the installer say afterwards. A method is conclusive when the log names a rollback and the
# lower version is registered and runnable again. Nothing is classified pass or fail: a green run means the
# measurement completed, and the reading is its result (docs/diagnostics.md).
#
# This installs and uninstalls in the real user profile, so it refuses to start unless the caller says the
# profile is disposable: CI=true on a fresh runner, or KEYPASTE_UPGRADE_SANDBOX=1 (D-0206).
#
# Usage:
#   probe-msi-interruption.sh <candidates-dir> <out-dir>
#   probe-msi-interruption.sh --selftest
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
ROOT="$(cd "$(dirname "$SELF")/.." && pwd)"
readonly ROOT
readonly LOWER='0.0.1-upgrade'
readonly HIGHER='0.0.2-upgrade'
readonly METHODS=(kill-service deny-folder unreadable-source)
readonly CR=$'\r'

die() { echo "::error::$*" >&2; exit 1; }

powershell_out() { powershell -NoProfile -NonInteractive -Command "$1" | tr -d "$CR"; }

# An MSI verbose log is UTF-16 (D-0209).
log_says() { [ -f "$1" ] && tr -d '\0' < "$1" | grep -qiE "$2"; }

upgrade_code() {
  local code
  code="$(sed -n 's/.*UpgradeCode="\([^"]*\)".*/\1/p' "$ROOT/packaging/windows/Package.wxs" | head -n 1)"
  [ -n "$code" ] || die 'packaging/windows/Package.wxs declares no UpgradeCode'
  printf '{%s}' "${code^^}"
}

registrations() {
  powershell_out "\$i = New-Object -ComObject WindowsInstaller.Installer
    foreach (\$p in @(\$i.GetType().InvokeMember('RelatedProducts', 'GetProperty', \$null, \$i, @('$(upgrade_code)')))) {
      \"\$p|\$(\$i.ProductInfo(\$p, 'VersionString'))\"
    }"
}

installed_version() {
  local app
  app="$(cygpath -u "$LOCALAPPDATA")/Programs/keypaste/keypaste-app.exe"
  [ -x "$app" ] || { echo absent; return 0; }
  "$app" --version 2>/dev/null | tr -d "[:space:]$CR" || echo unreadable
}

candidate() { # candidate <version>
  local file="$CANDIDATES/keypaste-app-$1-win-x64-internal-unsigned.msi"
  [ -f "$file" ] || die "no candidate at $file"
  printf '%s' "$file"
}

# Everything this probe installed, gone, so each method starts from the same place.
reset_machine() {
  local product
  while IFS= read -r product; do
    [ -n "$product" ] || continue
    powershell_out "(Start-Process msiexec.exe -ArgumentList '/x', '${product%%|*}', '/qn' -Wait -PassThru).ExitCode" > /dev/null
  done < <(registrations)
  rm -rf "$(cygpath -u "$LOCALAPPDATA")/Programs/keypaste"
}

install_lower() {
  local code
  code="$(powershell_out "(Start-Process msiexec.exe -ArgumentList '/i', '\"$(cygpath -w "$(candidate "$LOWER")")\"', '/qn' -Wait -PassThru).ExitCode")"
  [ "$code" = 0 ] || die "the probe could not install $LOWER: msiexec exited $code"
  [ "$(installed_version)" = "$LOWER" ] || die "the probe installed $LOWER and the app reports $(installed_version)"
}

# Starts the upgrade, waits for InstallFiles, runs <at-installfiles>, and waits for the log to settle.
attempt_upgrade() { # attempt_upgrade <log> <at-installfiles-command...>
  local log="$1" pid alive previous size reached=no
  shift
  : > "$log"
  pid="$(powershell_out "(Start-Process msiexec.exe -ArgumentList '/i', '\"$(cygpath -w "$SOURCE")\"', '/qn', '/l*v', '\"$(cygpath -w "$log")\"' -PassThru).Id")"
  for _ in $(seq 1 600); do
    if log_says "$log" 'Action start.*InstallFiles'; then reached=yes; break; fi
    alive="$(powershell_out "if (Get-Process -Id $pid -ErrorAction SilentlyContinue) { 'yes' } else { 'no' }")"
    [ "$alive" = no ] && break
    sleep 0.1
  done
  if [ "$reached" = yes ]; then "$@" > "$OUT/$METHOD-action.log" 2>&1 || true; fi

  previous=''
  for _ in $(seq 1 180); do
    size="$(wc -c < "$log" | tr -d ' ')"
    [ "$size" = "$previous" ] && break
    previous="$size"
    sleep 1
  done
  powershell_out "Wait-Process -Id $pid -Timeout 60 -ErrorAction SilentlyContinue; 'waited'" > /dev/null
  echo "$reached"
}

# --- the three methods ----------------------------------------------------------------------------

# The service owns the transaction, so this is the process whose loss a power cut resembles.
kill_service() {
  powershell_out "@(Get-CimInstance Win32_Process -Filter \"Name = 'msiexec.exe'\" |
      Where-Object { \$_.CommandLine -match '/V' } |
      ForEach-Object { Stop-Process -Id \$_.ProcessId -Force -ErrorAction SilentlyContinue; \$_.ProcessId })"
}

deny_folder() {
  powershell_out "icacls '$(cygpath -w "$(cygpath -u "$LOCALAPPDATA")/Programs/keypaste")' /deny '*S-1-1-0:(W)'"
}

allow_folder() {
  powershell_out "icacls '$(cygpath -w "$(cygpath -u "$LOCALAPPDATA")/Programs/keypaste")' /remove:d '*S-1-1-0'" > /dev/null || true
}

unreadable_source() {
  rm -f "$SOURCE" 2>&1 || true
  : > "$SOURCE" 2>&1 || true
  ls -l "$SOURCE" 2>&1 || echo 'the source is gone'
}

# --- the measurement ------------------------------------------------------------------------------

reading() { # reading <method> <reached> <log>
  local method="$1" reached="$2" log="$3" rollback=absent inprogress
  log_says "$log" 'Action start.*Rollback|Rollback: ' && rollback=present
  inprogress="$(powershell_out "if (Get-ItemProperty 'HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Installer\\InProgress' -ErrorAction SilentlyContinue) { 'present' } else { 'absent' }")"
  jq -cn \
    --arg method "$method" \
    --arg reachedInstallFiles "$reached" \
    --arg rollback "$rollback" \
    --arg version "$(installed_version)" \
    --arg registrations "$(registrations | tr '\n' ' ')" \
    --arg inProgress "$inprogress" \
    --arg installed "$(log_says "$log" 'installed the product' && echo yes || echo no)" \
    --arg status "$(tr -d '\0' < "$log" | grep -oiE 'success or error status: [0-9]+' | tail -n 1)" \
    '{method: $method, reachedInstallFiles: $reachedInstallFiles, rollback: $rollback, version: $version,
      registrations: $registrations, inProgress: $inProgress, loggedInstalled: $installed, status: $status}'
}

# A method answers the question when the installer rolled back and the lower version is back.
conclusive() { # conclusive <reading>
  [ "$(printf '%s' "$1" | jq -r '.rollback')" = present ] \
    && [ "$(printf '%s' "$1" | jq -r '.version')" = "$LOWER" ]
}

run_all() {
  mkdir -p "$OUT"
  [ "${CI:-}" = true ] || [ "${KEYPASTE_UPGRADE_SANDBOX:-}" = 1 ] \
    || die 'this installs into the real user profile: set CI=true on a fresh runner, or KEYPASTE_UPGRADE_SANDBOX=1 for a throwaway account or virtual machine'
  case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) ;; *) die 'an MSI can only be installed on Windows' ;; esac
  : > "$OUT/methods.jsonl"

  local method reached read
  for method in "${METHODS[@]}"; do
    METHOD="$method"
    echo "== $method"
    reset_machine
    install_lower
    SOURCE="$OUT/$method-source.msi"
    cp "$(candidate "$HIGHER")" "$SOURCE"
    case "$method" in
      kill-service) reached="$(attempt_upgrade "$OUT/$method.log" kill_service)" ;;
      deny-folder)
        deny_folder > "$OUT/$method-deny.log" 2>&1 || true
        reached="$(attempt_upgrade "$OUT/$method.log" true)"
        allow_folder ;;
      unreadable-source) reached="$(attempt_upgrade "$OUT/$method.log" unreadable_source)" ;;
    esac
    read="$(reading "$method" "$reached" "$OUT/$method.log")"
    printf '%s\n' "$read" >> "$OUT/methods.jsonl"
    echo "$read"
    rm -f "$SOURCE"
  done
  reset_machine
  allow_folder

  jq -s '{conclusive: [.[] | select(.rollback == "present" and .version == "'"$LOWER"'") | .method],
          readings: .}' "$OUT/methods.jsonl" > "$OUT/probe.json"
  echo "conclusive methods: $(jq -r '.conclusive | if length == 0 then "none" else join(" ") end' "$OUT/probe.json")"
}

# --- self-test ------------------------------------------------------------------------------------

# The reader's own fixtures: a UTF-16 log with a rollback, one without, and the conclusiveness rule.
selftest() {
  local work cases=0 failures=0 rolled unrolled
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN

  printf 'Action start 20:01:29: InstallFiles.\nAction start 20:01:30: Rollback.\n' | iconv -f UTF-8 -t UTF-16LE > "$work/rolled.log"
  printf 'Action start 20:01:29: InstallFiles.\nWindows Installer installed the product.\n' | iconv -f UTF-8 -t UTF-16LE > "$work/plain.log"

  check() { # check <what> <expected> <actual>
    cases=$((cases + 1))
    if [ "$3" != "$2" ]; then
      echo "::error::selftest $1: got '$3', expected '$2'" >&2
      failures=$((failures + 1))
    fi
  }

  check 'a rollback in a UTF-16 log' present "$(log_says "$work/rolled.log" 'Action start.*Rollback|Rollback: ' && echo present || echo absent)"
  check 'no rollback in a UTF-16 log' absent "$(log_says "$work/plain.log" 'Action start.*Rollback|Rollback: ' && echo present || echo absent)"
  check 'InstallFiles in a UTF-16 log' present "$(log_says "$work/plain.log" 'Action start.*InstallFiles' && echo present || echo absent)"

  rolled="$(jq -cn --arg v "$LOWER" '{method: "kill-service", rollback: "present", version: $v}')"
  unrolled="$(jq -cn --arg v "$HIGHER" '{method: "kill-service", rollback: "absent", version: $v}')"
  check 'a rollback that restored the lower version is conclusive' yes "$(conclusive "$rolled" && echo yes || echo no)"
  check 'an install that finished is not' no "$(conclusive "$unrolled" && echo yes || echo no)"
  check 'a rollback leaving the higher version is not' no \
    "$(conclusive "$(printf '%s' "$rolled" | jq -c --arg v "$HIGHER" '.version = $v')" && echo yes || echo no)"

  [ "$failures" -eq 0 ] || die "probe-msi-interruption selftest: $failures of $cases cases failed"
  echo "probe-msi-interruption selftest: $cases cases"
}

case "${1:-}" in
  --selftest) selftest ;;
  -h|--help) echo 'usage: bash scripts/probe-msi-interruption.sh <candidates-dir> <out-dir> | --selftest' ;;
  '') echo 'usage: bash scripts/probe-msi-interruption.sh <candidates-dir> <out-dir> | --selftest' >&2; exit 2 ;;
  *)
    [ $# -eq 2 ] || { echo 'usage: bash scripts/probe-msi-interruption.sh <candidates-dir> <out-dir> | --selftest' >&2; exit 2; }
    CANDIDATES="$1"
    OUT="$2"
    METHOD=''
    SOURCE=''
    run_all
    ;;
esac
