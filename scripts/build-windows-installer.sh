#!/usr/bin/env bash
# Wraps a staged win-x64 desktop payload in the internal per-user MSI release-targets.json declares (4.7a1).
#
# WixToolset.Sdk resolves outside packages.lock.json, so its restored package is held to the definition's
# SHA-512 before anything is built (D-0139, D-0206). The built MSI is then held to the definition by
# verify-windows-installer.sh. Signing stays with the caller.
#
# Build logs go to stderr; the path of the MSI is the only line on stdout.
#
# Usage: build-windows-installer.sh <staged-payload-dir> <version> <binary-version> <out-dir>
# Environment: KEYPASTE_RELEASE_DEFINITION  the definition to read (default: release-targets.json)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-$ROOT/release-targets.json}"
readonly RID='win-x64'

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d '\r'; }

[ $# -eq 4 ] || die "usage: build-windows-installer.sh <staged-payload-dir> <version> <binary-version> <out-dir>"
payload="$1" version="$2" binary_version="$3" out="$4"
[ -f "$payload/keypaste-app.exe" ] || die "no keypaste-app.exe in $payload"
case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) ;; *) die "an MSI can only be built on Windows" ;; esac

package='.components.app.targets[] | select(.rid == "win-x64") | .packages[] | select(.kind == "msi")'
[ "$(jqr "[$package] | length" "$DEFINITION" 2>/dev/null || true)" = "1" ] || die "$DEFINITION does not declare exactly one msi package for app/$RID"
read_package() { jqr "$package | .$1 // empty" "$DEFINITION"; }

project="$ROOT/$(read_package project)"
tool_version="$(read_package tool_version)"
tool_sha512="$(read_package tool_sha512)"
[ -n "$tool_sha512" ] || die "the definition does not pin WixToolset.Sdk $tool_version by SHA-512"
name="$(read_package pattern | sed -e "s/{version}/$version/g" -e "s/{rid}/$RID/g")"

dotnet restore "$(cygpath -w "$project")" --locked-mode >&2
packages="$(dotnet nuget locals global-packages --list | sed -E 's/^global-packages: //' | tr -d '\r')"
nupkg="$packages/wixtoolset.sdk/$tool_version/wixtoolset.sdk.$tool_version.nupkg"
observed="$(openssl dgst -sha512 -binary "$nupkg" | base64 -w0)"
echo "WixToolset.Sdk $tool_version sha512: $observed" >&2
[ "$observed" = "$tool_sha512" ] || die "WixToolset.Sdk $tool_version is not the pinned package"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
dotnet build "$(cygpath -w "$project")" -c Release --no-restore -o "$(cygpath -w "$work")" \
  -p:PayloadDir="$(cygpath -w "$(cd "$payload" && pwd)")\\" \
  -p:FullVersion="$version" -p:ProductVersion="${version%%-*}" >&2

mkdir -p "$out"
mv "$work/keypaste-app.msi" "$out/$name"
"$ROOT/scripts/verify-windows-installer.sh" "$out/$name" "$version" "$binary_version" "$payload" >&2
echo "$out/$name"
