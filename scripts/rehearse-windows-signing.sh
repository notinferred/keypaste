#!/usr/bin/env bash
# The runner-only identity 3.6b rehearses with until 3.6a supplies a real one (D-0144, D-0204).
#
# Usage:
#   rehearse-windows-signing.sh identity                       prints trusted=<thumb> and untrusted=<thumb>
#   rehearse-windows-signing.sh refusals <trusted> <untrusted> <signed-file>...
#   rehearse-windows-signing.sh cleanup <trusted> <untrusted>
set -euo pipefail

readonly TIMESTAMP_URL='http://timestamp.acs.microsoft.com'
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly HERE

die() { echo "::error::$*" >&2; exit 1; }

case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) ;; *) die "rehearse-windows-signing.sh runs only on Windows" ;; esac
[ "${GITHUB_ACTIONS:-}" = "true" ] || die "the rehearsal trusts a generated root machine-wide, so it runs only on a disposable runner"
case "${GITHUB_REF:-}" in refs/tags/*) die "a rehearsal is refused on tag $GITHUB_REF" ;; esac

ps() { powershell -NoProfile -NonInteractive -Command "\$ErrorActionPreference = 'Stop'; $1" | tr -d '\r'; }

signtool() {
  MSYS_NO_PATHCONV=1 "$(find '/c/Program Files (x86)/Windows Kits/10/bin' -path '*/x64/signtool.exe' | sort -V | tail -n 1)" "$@"
}

identity() {
  local trusted untrusted
  trusted="$(ps "(New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=keypaste rehearsal, O=keypaste' -CertStoreLocation Cert:\\CurrentUser\\My -NotAfter (Get-Date).AddDays(1)).Thumbprint")"
  untrusted="$(ps "(New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=keypaste rehearsal outside the root, O=keypaste' -CertStoreLocation Cert:\\CurrentUser\\My -NotAfter (Get-Date).AddDays(1)).Thumbprint")"
  ps "\$c = Get-Item Cert:\\CurrentUser\\My\\$trusted; \$s = [System.Security.Cryptography.X509Certificates.X509Store]::new('Root', 'LocalMachine'); \$s.Open('ReadWrite'); \$s.Add(\$c); \$s.Close()" >/dev/null
  echo "trusted=$trusted"
  echo "untrusted=$untrusted"
}

# Commits one Property value through Windows Installer without re-signing, so the change is inside what the signature covers.
change_msi_property() {
  local msi="$1" script before reading
  script="${RUNNER_TEMP:?}/change-msi-property.ps1"
  cat > "$script" <<'PS'
param([string]$Path)
$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
function Call($target, $name, $arguments) { $target.GetType().InvokeMember($name, 'InvokeMethod', $null, $target, $arguments) }
$db = Call $installer 'OpenDatabase' @([string]$Path, [int]1)
$view = Call $db 'OpenView' @("UPDATE ``Property`` SET ``Value`` = 'keypaste rehearsal change' WHERE ``Property`` = 'Manufacturer'")
Call $view 'Execute' $null | Out-Null
Call $view 'Close' $null | Out-Null
Call $db 'Commit' $null | Out-Null
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($db)
$db = Call $installer 'OpenDatabase' @([string]$Path, [int]0)
$view = Call $db 'OpenView' @("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = 'Manufacturer'")
Call $view 'Execute' $null | Out-Null
$record = Call $view 'Fetch' $null
'Manufacturer=' + $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1))
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($db)
'Signature=' + (Get-AuthenticodeSignature -LiteralPath $Path).Status
PS
  before="$(sha256sum < "$msi")"
  reading="$(powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$(cygpath -w "$script")" -Path "$(cygpath -w "$msi")" | tr -d '\r')"
  echo "$reading"
  [ "$(sha256sum < "$msi")" != "$before" ] || die "committing a Property change left $(basename "$msi") byte-identical"
  grep -qx 'Manufacturer=keypaste rehearsal change' <<< "$reading" || die "the Property change did not reach $(basename "$msi")"
  grep -qx 'Signature=HashMismatch' <<< "$reading" || die "$(basename "$msi") lost its signature rather than failing its hash"
}

expect_refused() {
  local name="$1" trusted="$2" file="$3"
  if bash "$HERE/verify-windows-signature.sh" "$trusted" "$file" > "$file.verdict" 2>&1; then
    cat "$file.verdict"
    die "$name was accepted"
  fi
  echo "refused as expected: $name: $(grep -m1 '^refused:' "$file.verdict" || head -n 1 "$file.verdict")"
}

refusals() {
  local trusted="$1" untrusted="$2" work file base copy
  shift 2
  work="${RUNNER_TEMP:?}/signing-refusals"
  rm -rf "$work"; mkdir -p "$work"

  bash "$HERE/verify-windows-signature.sh" "$trusted" "$@"

  for file in "$@"; do
    base="$work/$(basename "$file")"
    mkdir -p "$base.changed" "$base.untimestamped" "$base.outside"

    copy="$base.changed/$(basename "$file")"
    cp "$file" "$copy"
    case "$file" in
      *.exe | *.dll) printf '\x5a' | dd of="$copy" bs=1 seek=$(($(stat -c %s "$copy") / 2)) conv=notrunc status=none ;;
      *) change_msi_property "$copy" ;;
    esac
    expect_refused "$(basename "$file") with a changed byte" "$trusted" "$copy"

    copy="$base.untimestamped/$(basename "$file")"
    cp "$file" "$copy"
    signtool sign /fd SHA256 /sha1 "$trusted" /s My "$(cygpath -w "$copy")" >/dev/null
    expect_refused "$(basename "$file") signed without a timestamp" "$trusted" "$copy"

    copy="$base.outside/$(basename "$file")"
    cp "$file" "$copy"
    signtool sign /fd SHA256 /tr "$TIMESTAMP_URL" /td SHA256 /sha1 "$untrusted" /s My "$(cygpath -w "$copy")" >/dev/null
    expect_refused "$(basename "$file") signed by a certificate outside the trusted root" "$trusted" "$copy"
  done

  mkdir -p "$work/authenticode"
  cp "$1" "$work/authenticode/"
  local definition="$work/authenticode.json" before
  jq '.components.app.signing.policy = "authenticode" | .components.cli.signing.policy = "authenticode"' \
    "$HERE/../release-targets.json" > "$definition"
  before="$(sha256sum < "$work/authenticode/$(basename "$1")")"
  if env -u KEYPASTE_SIGNING_ENDPOINT -u KEYPASTE_SIGNING_ACCOUNT -u KEYPASTE_SIGNING_PROFILE -u KEYPASTE_SIGNING_CLIENT_ID -u KEYPASTE_SIGNING_TENANT_ID \
    KEYPASTE_RELEASE_DEFINITION="$definition" bash "$HERE/sign-windows.sh" --component app "$work/authenticode" > "$work/authenticode.log" 2>&1; then
    cat "$work/authenticode.log"
    die "a policy of authenticode with no identity signed"
  fi
  [ "$(sha256sum < "$work/authenticode/$(basename "$1")")" = "$before" ] || die "a refused authenticode run changed the file"
  echo "refused as expected: authenticode with no identity: $(head -n 1 "$work/authenticode.log")"
}

cleanup() {
  ps "foreach (\$t in @('$1', '$2')) { Remove-Item -ErrorAction SilentlyContinue Cert:\\CurrentUser\\My\\\$t }; \$s = [System.Security.Cryptography.X509Certificates.X509Store]::new('Root', 'LocalMachine'); \$s.Open('ReadWrite'); foreach (\$c in @(\$s.Certificates.Find('FindByThumbprint', '$1', \$false))) { \$s.Remove(\$c) }; \$s.Close()"
  echo "removed the rehearsal certificates"
}

case "${1:-}" in
  identity) identity ;;
  refusals) [ $# -ge 4 ] || die "usage: rehearse-windows-signing.sh refusals <trusted> <untrusted> <signed-file>..."; shift; refusals "$@" ;;
  cleanup) [ $# -eq 3 ] || die "usage: rehearse-windows-signing.sh cleanup <trusted> <untrusted>"; cleanup "$2" "$3" ;;
  *) die "usage: rehearse-windows-signing.sh identity | refusals <trusted> <untrusted> <signed-file>... | cleanup <trusted> <untrusted>" ;;
esac
