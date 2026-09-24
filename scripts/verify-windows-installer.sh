#!/usr/bin/env bash
# Holds a built Windows installer to what release-targets.json declares about it (4.7a1, D-0139).
#
# What it refuses:
#   - a file name other than the declared msi pattern expanded for this version;
#   - a ProductName, summary Subject or Keywords that drop the full version or the internal and
#     unsigned labels, a ProductVersion other than the numeric prefix, or a publisher other than keypaste;
#   - a per-machine package, or a signature on a package the definition declares unsigned;
#   - an administrative extraction whose files differ from the staged payload, whose binary reports
#     another version than the payload's or fails --selftest, or whose binaries do not publish as keypaste;
#   - an extraction with no keypaste-mcp.exe answering --help beside the app, which is what a client
#     connected from the app is told to start (2.6a).
#
# The extraction uses `msiexec /a`, which lays the files out without installing, registering or
# running anything from the package; the extracted binaries are then run. Installing it is 4.7b.
#
# <version> names the package; <binary-version> is what the payload reports. They differ only on a
# dispatch, where app.yml names the package <binary-version>-dryrun.
#
# Usage: verify-windows-installer.sh <msi> <version> <binary-version> <staged-payload-dir>
# Environment: KEYPASTE_RELEASE_DEFINITION  the definition to read (default: release-targets.json)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-$ROOT/release-targets.json}"
readonly RID='win-x64'
readonly CR=$'\r'

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d "$CR"; }

[ $# -eq 4 ] || die "usage: verify-windows-installer.sh <msi> <version> <binary-version> <staged-payload-dir>"
msi="$1" version="$2" binary_version="$3" payload="$4"
[ -f "$msi" ] || die "no installer at $msi"
[ -d "$payload" ] || die "no staged payload at $payload"
case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) ;; *) die "an MSI can only be read and extracted on Windows" ;; esac

package=".components.app.targets[] | select(.rid == \"$RID\") | .packages // [] | .[] | select(.kind == \"msi\")"
pattern="$(jqr "[$package] | if length == 1 then .[0].pattern else empty end" "$DEFINITION")"
[ -n "$pattern" ] || die "$DEFINITION does not declare exactly one msi package for app/$RID"
signed="$(jqr "[$package][0].signed" "$DEFINITION")"

expected_name="$(printf '%s' "$pattern" | sed -e "s/{version}/$version/g" -e "s/{rid}/$RID/g")"
[ "$(basename "$msi")" = "$expected_name" ] \
  || die "installer is named $(basename "$msi"); the definition names it $expected_name"

numeric="${version%%-*}"
case "$numeric" in
  [0-9]*.[0-9]*.[0-9]*) ;;
  *) die "version $version has no numeric prefix an MSI ProductVersion can carry" ;;
esac

msi_win="$(cygpath -w "$msi")"

# One PowerShell process reads every property, so a missing one prints <absent> rather than failing.
readings="$(powershell -NoProfile -NonInteractive -Command "
  \$ErrorActionPreference = 'Stop'
  \$i = New-Object -ComObject WindowsInstaller.Installer
  \$db = \$i.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', \$null, \$i, @('$msi_win', 0))
  foreach (\$p in 'ProductName', 'ProductVersion', 'Manufacturer', 'ALLUSERS', 'MSIINSTALLPERUSER') {
    \$v = \$db.GetType().InvokeMember('OpenView', 'InvokeMethod', \$null, \$db, @(\"SELECT Value FROM Property WHERE Property='\$p'\"))
    [void]\$v.GetType().InvokeMember('Execute', 'InvokeMethod', \$null, \$v, \$null)
    \$r = \$v.GetType().InvokeMember('Fetch', 'InvokeMethod', \$null, \$v, \$null)
    \$value = if (\$r) { \$r.GetType().InvokeMember('StringData', 'GetProperty', \$null, \$r, 1) } else { '<absent>' }
    \"\$p=\$value\"
  }
  \$s = \$i.GetType().InvokeMember('SummaryInformation', 'GetProperty', \$null, \$i, @('$msi_win', 0))
  'Subject=' + \$s.GetType().InvokeMember('Property', 'GetProperty', \$null, \$s, 3)
  'Keywords=' + \$s.GetType().InvokeMember('Property', 'GetProperty', \$null, \$s, 5)
  'Signature=' + (Get-AuthenticodeSignature -LiteralPath '$msi_win').Status
" | tr -d "$CR")"

reading() {
  local line
  while IFS= read -r line; do
    case "$line" in "$1="*) printf '%s' "${line#*=}"; return 0 ;; esac
  done <<< "$readings"
  die "the installer database gave no $1 reading"
}

expect() { [ "$2" = "$3" ] || die "$(basename "$msi") reports $1 '$2', expected '$3'"; }

expect ProductName "$(reading ProductName)" "keypaste $version (internal, unsigned)"
expect ProductVersion "$(reading ProductVersion)" "$numeric"
expect Manufacturer "$(reading Manufacturer)" "keypaste"
expect Subject "$(reading Subject)" "keypaste $version internal unsigned candidate"
expect Keywords "$(reading Keywords)" "keypaste;$version;internal;unsigned"
expect ALLUSERS "$(reading ALLUSERS)" "<absent>"
expect MSIINSTALLPERUSER "$(reading MSIINSTALLPERUSER)" "<absent>"
if [ "$signed" = "false" ]; then
  expect Signature "$(reading Signature)" "NotSigned"
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
powershell -NoProfile -NonInteractive -Command "
  \$p = Start-Process -FilePath msiexec.exe -Wait -PassThru -ArgumentList @('/a', '\"$msi_win\"', '/qn', 'TARGETDIR=\"$(cygpath -w "$work")\"', '/l*v', '\"$(cygpath -w "$work")\\extract.log\"')
  exit \$p.ExitCode
" || die "msiexec /a could not extract $(basename "$msi"); see its log"

host="$(find "$work" -type f -name keypaste-app.exe)"
[ "$(printf '%s\n' "$host" | grep -c .)" = "1" ] || die "extraction holds no single keypaste-app.exe: [$host]"
# Mixed form, because verify-publisher-metadata.sh hands this path to PowerShell, which cannot resolve /tmp.
extracted="$(cygpath -m "$(dirname "$host")")"

listing() { (cd "$1" && find . -type f ! -name extract.log -exec sha256sum {} + | sort -k2); }
diff <(listing "$payload") <(listing "$extracted") > "$work/payload.diff" \
  || { cat "$work/payload.diff" >&2; die "the extracted files are not the staged payload"; }

"$host" --selftest || die "the extracted keypaste-app.exe failed --selftest"
expect "the extracted binary's version" "$("$host" --version | tr -d '[:space:]')" "$binary_version"
bridge="$(dirname "$host")/keypaste-mcp.exe"
[ -f "$bridge" ] || die "the extraction carries no keypaste-mcp.exe beside keypaste-app.exe for a connected client to start"
case "$("$bridge" --help 2>&1)" in "usage: keypaste-mcp"*) ;; *) die "the extracted keypaste-mcp.exe does not answer --help" ;; esac
"$ROOT/scripts/verify-publisher-metadata.sh" "$extracted"

echo "$(basename "$msi"): keypaste $version, per-user, internal and unsigned; $(listing "$payload" | wc -l | tr -d ' ') payload files extract byte-identical and pass --selftest."
