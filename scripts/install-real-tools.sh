#!/usr/bin/env bash
# Puts the AI tools keypaste connects on a hosted runner, each at a pinned version, for ci.yml's real-tools job
# (G.2, D-0438).
#
# One owner for every version and digest, as install-keepassxc-windows.sh is for KeePassXC (D-0208). Every
# download goes through fetch-pinned-asset.sh against a SHA-256 kept below: the vendor's own where it publishes
# one (Claude Code's and Devin's release manifests, VS Code's update service), the Homebrew cask's or winget
# manifest's for Cursor, Cursor's Agent CLI and Claude Desktop, and for Cursor's Linux package, which neither
# carries, the digest of the file at its versioned URL when it was pinned. Codex and Gemini CLI come from npm
# through scripts/real-tools, whose lock pins every package by integrity and whose install scripts never run.
# No vendor's moving install script runs, and no tool's own setup step.
#
# Logs go to stderr; each directory to put on PATH is a line on stdout, in the runner's own form.
#
# Usage: install-real-tools.sh <dest-dir>
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT

die() { echo "::error::$*" >&2; exit 1; }

# <tool> <runner> <url> <sha256>, one download each; the version is in every URL.
readonly PINS='
claude-code    linux    https://downloads.claude.ai/claude-code-releases/2.1.296/linux-x64/claude                24972e3bc859fab2b46ed4c1e51f7d6130f06d3bd550811a114640de3370d0de
claude-code    macos    https://downloads.claude.ai/claude-code-releases/2.1.296/darwin-arm64/claude             c9b5341637becbd423ddffc5b254afb645682a3868cb708bbc6cc0e7bb419937
claude-code    windows  https://downloads.claude.ai/claude-code-releases/2.1.296/win32-x64/claude.exe            76de74f7694a5bdc03378a5f837c9e132827bf004c98c276bc4e9094ff06769a
devin          linux    https://static.devin.ai/cli/3000.11.3/devin-3000.11.3-x86_64-unknown-linux.tar.gz         83b3b113c01bf2a3e9e100db08d77e6b086806a7754f091e20831bdfe215157e
devin          macos    https://static.devin.ai/cli/3000.11.3/devin-3000.11.3-aarch64-apple-darwin.tar.gz         c08cc3f3507d103246b8c601a584fcc719fd77f6cc10f26cc90e9f4186640df2
devin          windows  https://static.devin.ai/cli/3000.11.3/devin-3000.11.3-x86_64-pc-windows.zip              dded69a40dc54012612eda47e0935f2fb394b97cd38cb4d7d84b1e1d71c5f7ea
vscode         linux    https://update.code.visualstudio.com/1.141.0/linux-deb-x64/stable                         d0ff4f7702957d897d2e06956f8d42a08250c5df8339de3bec8190e56f726277
vscode         macos    https://update.code.visualstudio.com/1.141.0/darwin-arm64/stable                          9b4ae386e3cf738d7ba990237ddf0a18092c924a1bcae465ed4697964745f42d
vscode         windows  https://update.code.visualstudio.com/1.141.0/win32-x64-user/stable                        e377a8e2e081a4b59dfd19514abdf4f35aa95aa697e217ad819a0fccff11c602
cursor         linux    https://downloads.cursor.com/production/37b865941ccb31e2fc90c685157534efca87f964/linux/x64/deb/amd64/deb/cursor_3.24.12_amd64.deb  880b5b69cd6cd79df4b81a778ea1f3429aaf775e771aab6c19c688de16a275f4
cursor         macos    https://downloads.cursor.com/production/37b865941ccb31e2fc90c685157534efca87f964/darwin/arm64/Cursor-darwin-arm64.zip           782929b6be6a9041c6d8491d34d6d59c2fa9a7a5935a87597d7d944a31eace5f
cursor         windows  https://downloads.cursor.com/production/90de2327392570a5f5f625c656c6749d228e6437/win32/x64/user-setup/CursorUserSetup-x64-3.19.7.exe  4fa4f1d58241d290f341d1b30052f502317ef81ccbd97012524e3bb33e0616d6
cursor-agent   linux    https://downloads.cursor.com/lab/2026.10.01-e373342/linux/x64/agent-cli-package.tar.gz    a79726c6e644520e993970be4c45775a6889802b67abe461a677a53219ae28e8
cursor-agent   macos    https://downloads.cursor.com/lab/2026.10.01-e373342/darwin/arm64/agent-cli-package.tar.gz 629e51de43a0b7fb3b86f5ebc7e579f7df7df941b39f29e82945cde750145afc
claude-desktop macos    https://downloads.claude.ai/releases/darwin/universal/2.31226.1/Claude-8576520051724193e9f74fc538f9ce8125e9ddd2.zip  3ce879784507147267e656c81c5eea96979f4db462c3569a4d9cb9e5c22d1953
claude-desktop windows  https://downloads.claude.ai/releases/win32/x64/2.19675.1/Claude-8613680e2e16d90700c039e084a7883f321ed4e3.msix   07b4935f7a75c5d980b9d63c4d059fdf0aefe68903fe96e65a5c45cd1a106a93
'

[ $# -eq 1 ] || die 'usage: install-real-tools.sh <dest-dir>'

case "$(uname -s):$(uname -m)" in
  Linux:x86_64) runner=linux ;;
  Darwin:arm64) runner=macos ;;
  MINGW*:x86_64 | MSYS*:x86_64) runner=windows ;;
  *) die "no pinned tools for $(uname -s) $(uname -m)" ;;
esac

# Prints a directory to put on PATH, in the form this runner's PATH takes.
on_path() { if [ "$runner" = windows ]; then cygpath -w "$1"; else printf '%s\n' "$1"; fi; }

dest="$1"
if [ "$runner" = windows ]; then dest="$(cygpath -u "$dest")"; fi
mkdir -p "$dest/bin"
dest="$(cd "$dest" && pwd)"

# fetch <tool> <file>: the tool's pinned download for this runner, kept only if its digest is the pin.
fetch() {
  local pin
  pin="$(awk -v tool="$1" -v runner="$runner" '$1 == tool && $2 == runner { print $3, $4 }' <<< "$PINS")"
  [ -n "$pin" ] || die "no pin for $1 on $runner"
  "$ROOT/scripts/fetch-pinned-asset.sh" "${pin% *}" "${pin#* }" "$2" >&2
}

# Inno Setup's switches start with a slash, which Git Bash would otherwise turn into a path.
inno() { MSYS2_ARG_CONV_EXCL='*' "$1" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART '/MERGETASKS=!runcode' >&2; }

claude="$dest/bin/claude"
[ "$runner" != windows ] || claude="$claude.exe"
fetch claude-code "$claude"
chmod +x "$claude"
on_path "$dest/bin"

mkdir -p "$dest/devin"
if [ "$runner" = windows ]; then
  fetch devin "$dest/devin.zip"
  /c/Windows/System32/tar.exe -xf "$(cygpath -w "$dest/devin.zip")" -C "$(cygpath -w "$dest/devin")" >&2
else
  fetch devin "$dest/devin.tar.gz"
  tar -xzf "$dest/devin.tar.gz" -C "$dest/devin"
fi
[ -x "$dest/devin/bin/devin" ] || [ -x "$dest/devin/bin/devin.exe" ] || die "the pinned Devin bundle holds no bin/devin"
on_path "$dest/devin/bin"

mkdir -p "$dest/npm"
cp "$ROOT/scripts/real-tools/package.json" "$ROOT/scripts/real-tools/package-lock.json" "$dest/npm/"
npm_prefix="$dest/npm"
[ "$runner" != windows ] || npm_prefix="$(cygpath -w "$npm_prefix")"
npm ci --ignore-scripts --no-audit --no-fund --prefix "$npm_prefix" >&2
on_path "$dest/npm/node_modules/.bin"

case "$runner" in
  linux)
    fetch vscode "$dest/code.deb"
    fetch cursor "$dest/cursor.deb"
    bash "$ROOT/scripts/apt-install.sh" --no-install-recommends "$dest/code.deb" "$dest/cursor.deb" >&2
    ;;
  macos)
    fetch vscode "$dest/vscode.zip"
    fetch cursor "$dest/cursor.zip"
    fetch claude-desktop "$dest/claude-desktop.zip"
    for zip in vscode cursor claude-desktop; do ditto -x -k "$dest/$zip.zip" /Applications; done
    for app in 'Visual Studio Code' Cursor Claude; do
      [ -d "/Applications/$app.app" ] || die "the pinned archive put no $app.app in /Applications"
    done
    on_path '/Applications/Visual Studio Code.app/Contents/Resources/app/bin'
    ;;
  windows)
    fetch vscode "$dest/VSCodeUserSetup.exe"
    inno "$dest/VSCodeUserSetup.exe"
    vscode="$(cygpath -u "$LOCALAPPDATA")/Programs/Microsoft VS Code"
    [ -f "$vscode/bin/code" ] || die "VS Code's user setup put no bin/code in $vscode"
    on_path "$vscode/bin"
    fetch cursor "$dest/CursorUserSetup.exe"
    inno "$dest/CursorUserSetup.exe"
    [ -f "$(cygpath -u "$LOCALAPPDATA")/Programs/cursor/Cursor.exe" ] || die "Cursor's user setup put no Cursor.exe in %LOCALAPPDATA%\\Programs\\cursor"
    fetch claude-desktop "$dest/Claude.msix"
    powershell.exe -NoProfile -NonInteractive -Command "\$ErrorActionPreference = 'Stop'; Add-AppxPackage -Path '$(cygpath -w "$dest/Claude.msix")'" >&2
    ;;
esac

# Cursor's Agent CLI, whose agent mcp list reads Cursor's file, ships for Linux and macOS only.
if [ "$runner" != windows ]; then
  fetch cursor-agent "$dest/cursor-agent.tar.gz"
  mkdir -p "$dest/cursor-agent"
  tar -xzf "$dest/cursor-agent.tar.gz" --strip-components=1 -C "$dest/cursor-agent"
  [ -x "$dest/cursor-agent/cursor-agent" ] || die "the pinned Agent CLI archive holds no cursor-agent"
  ln -sf "$dest/cursor-agent/cursor-agent" "$dest/bin/agent"
fi
