#!/usr/bin/env bash
# Adds the NativeAOT keypaste-mcp to a published desktop payload, so a client the app connects has a bridge
# to start from the installed package (2.6a). It is the same project release.yml publishes for the CLI/MCP
# archives, built from this commit into a directory of its own and copied in alone, so no symbol file or
# second runtime lands beside the app. The copy must then answer --help.
#
# Extra arguments are passed to `dotnet publish`, such as the version properties the app was built with.
#
# Usage: publish-desktop-bridge.sh <rid> <payload-dir> [<publish-arg>...]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT

die() { echo "::error::$*" >&2; exit 1; }

[ $# -ge 2 ] || die 'usage: publish-desktop-bridge.sh <rid> <payload-dir> [<publish-arg>...]'
rid="$1" payload="$2"
shift 2
[ -d "$payload" ] || die "no payload at $payload"

name=keypaste-mcp
case "$rid" in win-*) name=keypaste-mcp.exe ;; esac

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Restored for every RID, then published for one: narrowing the restore invalidates the lock file (D-0040).
dotnet restore "$ROOT/src/Keypaste.Mcp/Keypaste.Mcp.csproj" --locked-mode >&2
dotnet publish "$ROOT/src/Keypaste.Mcp/Keypaste.Mcp.csproj" -c Release -r "$rid" --no-restore -o "$work" "$@" >&2

[ -f "$work/$name" ] || die "publishing keypaste-mcp for $rid left no $name"
cp "$work/$name" "$payload/$name"
case "$("$payload/$name" --help 2>&1)" in "usage: keypaste-mcp"*) ;; *) die "the copied $name does not answer --help" ;; esac
echo "$payload/$name"
