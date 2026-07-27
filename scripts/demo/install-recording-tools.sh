#!/usr/bin/env bash
#
# install-recording-tools.sh
#
# Installs everything scripts/demo/record-demo.sh needs, so that re-recording the demo in six
# months is one command rather than an archaeology exercise across five README files.
#
# It asks for sudo once, and prints exactly what it will install before it does. Three of the five
# tools have no packaged form worth using, so each arrives differently and each says why below.
#
# Usage:  scripts/demo/install-recording-tools.sh
set -euo pipefail

cd "$(dirname "$0")/../.."
# shellcheck source=scripts/demo/demo-env.sh
. scripts/demo/demo-env.sh

refuse_outside_wsl

say "This will install, into this Linux environment:"
say ""
say "  apt      asciinema (${ASCIINEMA_MAJOR}.x), tmux, gifsicle, fonts-jetbrains-mono   [needs sudo]"
say "  curl     agg ${AGG_VERSION}, pinned by SHA-256, into ~/.local/bin"
say "  curl     the .NET SDK pinned by global.json, into ~/.dotnet"
say "  npm      @anthropic-ai/claude-code, into nvm's node"
say ""
say "Nothing here touches the repository or your Windows install."
say ""

# ------------------------------------------------------------------------------------------ apt
# asciinema is packaged and that is the whole argument for taking 2.x: one apt line against another
# checksummed binary download, for a tool whose only job is to write a file format we pin anyway.
say "==> apt"
sudo apt-get update -qq
sudo apt-get install -y -qq --no-install-recommends \
  asciinema tmux gifsicle fonts-jetbrains-mono

have_major=$(asciinema --version 2>&1 | grep -oE '[0-9]+' | head -1 || echo 0)
[ "$have_major" = "$ASCIINEMA_MAJOR" ] \
  || die "expected asciinema ${ASCIINEMA_MAJOR}.x, got $(asciinema --version 2>&1). record-demo.sh asserts the cast is v2."

# ------------------------------------------------------------------------------------------- agg
# Pinned by content hash. A mismatch is fatal rather than a warning: this binary renders the
# marketing for a security product, and the repository already pins its entire NuGet closure by
# hash rather than by version alone.
say "==> agg ${AGG_VERSION}"
mkdir -p "$HOME/.local/bin"
if [ -x "$HOME/.local/bin/agg" ] && "$HOME/.local/bin/agg" --version 2>/dev/null | grep -q "${AGG_VERSION#v}"; then
  say "    already present"
else
  tmp="$(mktemp)"
  curl -fsSL --max-time 300 -o "$tmp" \
    "https://github.com/asciinema/agg/releases/download/${AGG_VERSION}/${AGG_ASSET}" \
    || die "could not download agg"

  observed="$(sha256sum "$tmp" | cut -d' ' -f1)"
  if [ "$observed" != "$AGG_SHA256" ]; then
    rm -f "$tmp"
    die "SHA-256 mismatch for ${AGG_ASSET}: expected ${AGG_SHA256}, got ${observed}"
  fi

  chmod +x "$tmp"
  mv "$tmp" "$HOME/.local/bin/agg"
  say "    installed, hash verified"
fi

# ------------------------------------------------------------------------------------ .NET SDK
# The version is read out of global.json rather than restated here, so this script cannot drift
# from the pin the build actually enforces.
say "==> .NET SDK"
sdk="$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]+"' global.json | head -1 | grep -oE '[0-9][^"]*')"
[ -n "$sdk" ] || die "could not read the SDK version out of global.json"

if [ -x "$HOME/.dotnet/dotnet" ] && "$HOME/.dotnet/dotnet" --list-sdks 2>/dev/null | grep -q "^${sdk%.*}"; then
  say "    ${sdk} line already present"
else
  curl -fsSL --max-time 300 -o /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh \
    || die "could not download dotnet-install.sh"
  bash /tmp/dotnet-install.sh --version "$sdk" --install-dir "$HOME/.dotnet" \
    || die "the .NET SDK install failed"
  rm -f /tmp/dotnet-install.sh
fi

# -------------------------------------------------------------------------------- Claude Code
# A WSL-native install with its own credentials. The Windows one cannot be used: see
# refuse_outside_wsl in demo-env.sh, and D-0033.
say "==> Claude Code"
# shellcheck disable=SC1090
[ -s "$HOME/.nvm/nvm.sh" ] && . "$HOME/.nvm/nvm.sh" && nvm use --lts >/dev/null 2>&1 || true

if command -v npm >/dev/null 2>&1; then
  npm install -g @anthropic-ai/claude-code || die "npm could not install Claude Code"
else
  die "no npm on PATH. Install node (nvm is the easy route), then run this again."
fi

# ------------------------------------------------------------------------------------- manifest
# Printed so it can be pasted beside the recording. A re-record on different versions is a
# re-record, not a patch, and the only way to know which you did is to have written it down.
say ""
say "==> versions, for the record"
printf '    asciinema  %s\n' "$(asciinema --version 2>&1 | head -1)"
printf '    agg        %s\n' "$("$HOME/.local/bin/agg" --version 2>&1 | head -1)"
printf '    tmux       %s\n' "$(tmux -V)"
printf '    dotnet     %s\n' "$("$HOME/.dotnet/dotnet" --version 2>&1 | head -1)"
printf '    claude     %s\n' "$(claude --version 2>&1 | head -1 || echo 'not on PATH yet - open a new shell')"
say ""
say "One manual step is left, and it must happen before the first take:"
say ""
say "  1. scripts/demo/build-demo-binaries.sh"
say "  2. scripts/demo/make-demo-fixture.sh"
say "  3. cd ${DEMO_PROJECT} && claude"
say "     - /login"
say "     - accept the folder-trust prompt"
say "     - approve the keypaste MCP server it finds in .mcp.json"
say "     - then quit"
say ""
say "Skipping step 3 means the first thing your demo shows is a consent dialog"
say "for the very tool it is demonstrating."
