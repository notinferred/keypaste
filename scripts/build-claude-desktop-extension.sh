#!/usr/bin/env bash
# Packs packaging/claude-desktop into keypaste's Claude Desktop extension with Anthropic's MCPB CLI at the version and
# integrity scripts/mcpb-cli locks, whose install scripts never run (G.6b). The CLI validates the manifest and packs;
# the bundle it unpacks must hold only the manifest, icon and launcher, never a copy of keypaste, and that launcher,
# started as Claude Desktop starts it, must run the installed keypaste with setup's arguments over the same stdio,
# pass its exit code back, skip a relative PATH entry, and say keypaste is not installed when it finds none. It runs
# on Linux, where the launcher looks only in ~/.local/bin and on PATH. Nothing is uploaded or published.
#
# Logs go to stderr; the path of the bundle is the only line on stdout.
#
# Usage: build-claude-desktop-extension.sh <version> <out-dir>
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
# shellcheck source=scripts/lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

[ $# -eq 2 ] || die 'usage: build-claude-desktop-extension.sh <version> <out-dir>'
version="$1" out="$2"
case "$version" in
  '' | [!0-9]* | *[!0-9A-Za-z.+-]*) die "$version is not a version" ;;
esac
[ "$(uname -s)" = Linux ] || die 'the launcher is checked on Linux, where it looks only in ~/.local/bin and on PATH'
for tool in node npm jq; do require "$tool"; done
node="$(command -v node)"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

step 'install the pinned MCPB CLI' >&2
cp "$ROOT/scripts/mcpb-cli/package.json" "$ROOT/scripts/mcpb-cli/package-lock.json" "$work/"
npm ci --ignore-scripts --no-audit --no-fund --prefix "$work" >&2
mcpb="$work/node_modules/.bin/mcpb"

step 'validate and pack' >&2
stage="$work/keypaste"
mkdir -p "$stage/server"
sed "s/{version}/$version/g" "$ROOT/packaging/claude-desktop/manifest.json" >"$stage/manifest.json"
cp "$ROOT/packaging/claude-desktop/server/index.js" "$stage/server/"
cp "$ROOT/src/Keypaste.App/Assets/keypaste-256.png" "$stage/icon.png"
"$mcpb" validate "$stage" >&2
mkdir -p "$out"
bundle="$(cd "$out" && pwd)/keypaste-$version.mcpb"
"$mcpb" pack "$stage" "$bundle" >&2

step 'the bundle carries no keypaste' >&2
unpacked="$work/unpacked"
"$mcpb" unpack "$bundle" "$unpacked" >&2
(cd "$unpacked" && find . -type f | LC_ALL=C sort) >"$work/files"
printf '%s\n' ./icon.png ./manifest.json ./server/index.js | diff - "$work/files" >&2 \
  || die 'the bundle must hold only its manifest, icon and launcher'
launcher="$unpacked/$(jq -r '.server.entry_point' "$unpacked/manifest.json")"

home="$work/home"
fake="$home/.local/bin/keypaste"
mkdir -p "$home/.local/bin" "$work/bin" "$work/relative" "$work/empty"
cat >"$fake" <<'FAKE'
#!/bin/sh
printf '%s\n' "$@" >"$HOME/arguments"
/bin/cat
exit 7
FAKE
chmod +x "$fake"
said="$work/stderr"
DIE_FILES=said
launch() { (cd "$work" && HOME="$home" PATH="$1" "$node" "$launcher" 2>"$said"); }

step 'the launcher starts the installed keypaste as setup registers it' >&2
code=0
reply="$(printf 'ping\n' | launch "$work/empty")" || code=$?
[ "$code" = 7 ] || die "the launcher exited $code, not the bridge's 7"
[ "$reply" = ping ] || die "the bridge's stdio did not pass through the launcher: '$reply'"
printf '%s\n' mcp --client-label claude-desktop --allow-run --expose '**' | diff - "$home/arguments" >&2 \
  || die 'the launcher did not start keypaste with the arguments setup registers for Claude Desktop'

step 'the launcher prefers the installed keypaste to one on PATH' >&2
printf '#!/bin/sh\nexit 8\n' >"$work/bin/keypaste"
chmod +x "$work/bin/keypaste"
code=0
launch "$work/bin" </dev/null >/dev/null || code=$?
[ "$code" = 7 ] || die "with keypaste installed and on PATH the launcher exited $code, not the installed bridge's 7"

step 'the launcher finds keypaste on PATH' >&2
mv "$fake" "$work/bin/keypaste"
code=0
launch "relative:$work/bin" </dev/null >/dev/null || code=$?
[ "$code" = 7 ] || die "with keypaste on PATH the launcher exited $code, not the bridge's 7"

step 'the launcher skips a relative PATH entry and says keypaste is not installed' >&2
mv "$work/bin/keypaste" "$work/relative/keypaste"
code=0
reply="$(launch relative </dev/null)" || code=$?
[ "$code" = 1 ] || die "with keypaste only in a relative PATH entry the launcher exited $code, not 1"
[ -z "$reply" ] || die "with no keypaste the launcher wrote to stdout: '$reply'"
grep -q '^keypaste is not installed: ' "$said" || die 'with no keypaste the launcher did not say keypaste is not installed'

printf '%s\n' "$bundle"
