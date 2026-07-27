#!/usr/bin/env bash
#
# demo-env.sh
#
# Sourced by every other script in this directory. One place for the constants, because the
# recording, the fixture and the renderer all have to agree about paths and geometry, and three
# scripts deriving them independently is how a re-record in six months produces a different GIF.
#
# Not executable on its own.

# ------------------------------------------------------------------------------------ the basics
DEMO_ROOT="${KEYPASTE_DEMO_ROOT:-$HOME/kp}"
DEMO_BIN="$DEMO_ROOT/bin"
DEMO_SRC="$DEMO_ROOT/src"
DEMO_VAULT="$DEMO_ROOT/vault.kdbx"
DEMO_PROJECT="$DEMO_ROOT/billing"
DEMO_STATE="$DEMO_ROOT/state"

# Throwaway, and it appears nowhere on screen because the prompt does not echo.
DEMO_MASTER='demo-master-password'

# ------------------------------------------------------------------------- the value on the screen
# A released credential is returned to the agent twice - once as text and once as structured data,
# so that a client reading either half works (src/Keypaste.Mcp/Tools/ToolResults.cs). It is
# therefore rendered in Claude's transcript, written to its session file, and captured in a .cast
# this repository commits to git forever. Any truthful recording of a successful request shows the
# value. So the value must be worth nothing, and make-demo-fixture.sh refuses to build a vault
# whose sentinel does not match the pattern below. Never point this at a real vault.
DEMO_SENTINEL='sk_test_EXAMPLE_ONLY_not_a_real_key_0000'
DEMO_SENTINEL_PATTERN='^sk_test_(EXAMPLE|FAKE)_'

# ----------------------------------------------------------------------------------- the geometry
# 112 columns is a measurement, not a preference. The widest line either pane must not wrap is the
# approver's startup banner at 109 characters; after that come `keypaste log verify`'s hash line at
# 84 and the dialog's "That sentence was written by the agent" disclaimer at 79.
#
# That disclaimer is why the panes are STACKED rather than side by side. Two 60-column panes would
# wrap the one sentence in the dialog that tells a viewer the agent's reason is only a claim, which
# is the sentence the whole design exists to put on screen.
DEMO_COLS=112
DEMO_ROWS=40
DEMO_TOP_ROWS=18     # the approver: banner, then the dialog
DEMO_BOTTOM_ROWS=20  # Claude Code
# 18 + 20 + one border row per pane = 40. Verified with `tmux list-panes`.

DEMO_TMUX_SOCKET=kpdemo
DEMO_TMUX_SESSION=keypaste-demo

# --------------------------------------------------------------------------------- the tool pins
# agg is downloaded rather than packaged, so it is pinned by content hash - the same treatment
# Directory.Build.props gives the NuGet closure, and the same reasoning as the KeePassXC pin in
# ci.yml: a constant here changes only through a reviewed diff.
AGG_VERSION=v1.9.0
AGG_ASSET=agg-x86_64-unknown-linux-musl
AGG_SHA256=ddcbf6ca044c8ac3a434dcb9ee89fb9e3be87209982b7c2adb55f782e8f0f390

# asciinema 2.x, from apt, because it writes asciicast v2 and record-demo.sh asserts that version
# on the committed file. agg 1.9 reads v1, v2 and v3, so this is a choice about having one fixed
# format to check rather than a compatibility requirement.
ASCIINEMA_MAJOR=2

# ------------------------------------------------------------------------------------ the outputs
DEMO_CAST=docs/demo/keypaste-demo.cast
DEMO_GIF=docs/demo/keypaste-demo.gif

# -------------------------------------------------------------------------------------- helpers
die() { printf '\n%s: %s\n' "${0##*/}" "$*" >&2; exit 1; }

say() { printf '%s\n' "$*"; }

# The approver channel is a .NET named pipe: \\.\pipe\<name> on Windows, a socket under the
# temporary directory on Unix. A Windows Claude Code would spawn a Windows keypaste-mcp, which
# cannot reach a Linux keypaste agent - every request would come back "no keypaste agent is
# running". The whole recording therefore lives on one side of that line.
refuse_outside_wsl() {
  [ "$(uname -s)" = "Linux" ] || die "this runs inside WSL (or any Linux), not from Windows - see DECISIONS.md D-0033"
  grep -qi microsoft /proc/sys/kernel/osrelease 2>/dev/null \
    || say "note: this does not look like WSL. That is fine on a real Linux machine."
}

# Nothing the demo generates may land in the working tree: the vault is a secrets file, the state
# directory is an audit log, and .gitignore's *.kdbx would hide the first while committing the rest.
refuse_inside_repo() {
  local target="$1" repo
  repo="$(git rev-parse --show-toplevel 2>/dev/null || true)"
  [ -n "$repo" ] || return 0
  case "$(readlink -f "$target")/" in
    "$(readlink -f "$repo")"/*) die "$target is inside the repository; set KEYPASTE_DEMO_ROOT somewhere else" ;;
  esac
}

resolve_bin() {
  local candidate="$1"
  [ -x "$candidate" ] || candidate="${candidate}.exe"
  [ -x "$candidate" ] || die "not found: $1 (run scripts/demo/build-demo-binaries.sh)"
  printf '%s' "$candidate"
}
