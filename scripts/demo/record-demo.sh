#!/usr/bin/env bash
#
# record-demo.sh
#
# Records the demo: a real Claude Code session, a real keypaste agent, and a real person pressing
# y, captured as an asciicast this repository commits as text.
#
# WHAT IT DOES NOT DO. It does not decide anything for you. It opens the panes, starts the
# approver, types the master password, waits for the approver to be listening, starts Claude and
# types the prompt - and then stops and hands you the keyboard. The approval keystroke is yours,
# because a demo whose y came from a script is a demo of a mock, and the difference is invisible in
# the output. That is the argument in DECISIONS.md D-0033, and --auto-approve exists only so you
# can rehearse without babysitting it.
#
# NEGATIVE CONTROL: the finished cast is rejected if the master password appears in it, if anything
# shaped like a real credential appears in it, if the demo sentinel is ABSENT (a take that released
# nothing proves nothing), if the approval dialog was never drawn, if the recording touched the
# author's real ~/.keypaste, or if the geometry is not the one this script asked for. A recording
# that fails any of those must not be committed, and these checks must never be softened.
#
# Usage:  scripts/demo/record-demo.sh [--auto-approve]
set -euo pipefail

cd "$(dirname "$0")/../.."
REPO="$PWD"
# shellcheck source=scripts/demo/demo-env.sh
. scripts/demo/demo-env.sh

AUTO_APPROVE=0
[ "${1:-}" = "--auto-approve" ] && AUTO_APPROVE=1

refuse_outside_wsl
refuse_inside_repo "$DEMO_ROOT"

# ------------------------------------------------------------------------------------- preflight
for tool in asciinema tmux; do
  command -v "$tool" >/dev/null 2>&1 || die "no $tool (run scripts/demo/install-recording-tools.sh)"
done
AGG="${AGG:-$HOME/.local/bin/agg}"
[ -x "$AGG" ] || die "no agg at $AGG (run scripts/demo/install-recording-tools.sh)"

# asciinema's flag surface is checked rather than assumed. This script depends on --cols/--rows
# sizing the pty it allocates; if a future version renames or drops them, the recording would come
# out at the terminal's size instead and the geometry assertion at the end would be the first thing
# to notice - too late, after a take.
rec_help="$(asciinema rec --help 2>&1 || true)"
for flag in --cols --rows --overwrite --idle-time-limit; do
  printf '%s' "$rec_help" | grep -q -- "$flag" \
    || die "this asciinema has no '$flag'. Pin ${ASCIINEMA_MAJOR}.x, or update this script deliberately."
done

# A Windows Claude Code spawns a Windows keypaste-mcp, which cannot reach the Linux approver in the
# top pane. Every request would be refused, and the cause would not be obvious from the screen.
claude_path="$(command -v claude || true)"
[ -n "$claude_path" ] || die "no claude on PATH (run scripts/demo/install-recording-tools.sh, then open a new shell)"
case "$claude_path" in
  /mnt/*) die "that is the Windows Claude Code at $claude_path. It cannot reach a Linux approver - see D-0033." ;;
esac

CLI="$(resolve_bin "$DEMO_BIN/keypaste")"
[ -x "$DEMO_BIN/keypaste-mcp" ] || die "no keypaste-mcp (run scripts/demo/build-demo-binaries.sh)"

# The binaries are framework-dependent, so their apphost needs DOTNET_ROOT. demo.bashrc exports it
# into both panes, and Claude Code inherits it when it spawns the bridge - but "You must install
# .NET to run this application" appearing inside a take is an expensive way to find that out, so
# run one under exactly the environment the panes will have.
DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" "$CLI" version >/dev/null 2>&1 \
  || die "the demo binaries will not start. They need the .NET runtime that DOTNET_ROOT points at; re-run build-demo-binaries.sh."
[ -f "$DEMO_VAULT" ] || die "no demo vault (run scripts/demo/make-demo-fixture.sh)"
[ -f "$DEMO_PROJECT/.mcp.json" ] || die "no .mcp.json (run scripts/demo/make-demo-fixture.sh)"
grep -q "$DEMO_BIN/keypaste-mcp" "$DEMO_PROJECT/.mcp.json" \
  || die "$DEMO_PROJECT/.mcp.json does not point at $DEMO_BIN/keypaste-mcp; re-run make-demo-fixture.sh"

# The recording must be incapable of writing to the author's real audit log.
[ "$DEMO_STATE" != "$HOME/.keypaste" ] || die "the demo state directory must not be your real ~/.keypaste"

pgrep -f 'keypaste agent' >/dev/null 2>&1 \
  && die "a keypaste agent is already running; stop it, or it will answer instead of the one in the recording"

# You cannot hand-drive a 112-column pty inside a 90-column window.
[ "$(tput cols)" -ge "$DEMO_COLS" ] \
  || die "this terminal is $(tput cols) columns; the recording needs $DEMO_COLS. Maximise it, or reduce the font size."
[ "$(tput lines)" -ge "$DEMO_ROWS" ] \
  || die "this terminal is $(tput lines) rows; the recording needs $DEMO_ROWS."

mkdir -p "$(dirname "$DEMO_CAST")"

# --------------------------------------------------------------------------------- the session
T() { tmux -L "$DEMO_TMUX_SOCKET" "$@"; }

cleanup() {
  [ -n "${DRIVER_PID:-}" ] && kill "$DRIVER_PID" 2>/dev/null
  T kill-session -t "$DEMO_TMUX_SESSION" 2>/dev/null
  T kill-server 2>/dev/null
  return 0
}
trap cleanup EXIT

T kill-server 2>/dev/null || true

T -f scripts/demo/demo.tmux.conf new-session -d -s "$DEMO_TMUX_SESSION" \
  -x "$DEMO_COLS" -y "$DEMO_ROWS" -c "$DEMO_ROOT" \
  -e KEYPASTE_DEMO_ROOT="$DEMO_ROOT" \
  "bash --rcfile $REPO/scripts/demo/demo.bashrc"

T split-window -v -t "$DEMO_TMUX_SESSION:0.0" -l "$DEMO_BOTTOM_ROWS" -c "$DEMO_PROJECT" \
  -e KEYPASTE_DEMO_ROOT="$DEMO_ROOT" \
  "bash --rcfile $REPO/scripts/demo/demo.bashrc"

T select-pane -t "$DEMO_TMUX_SESSION:0.0" -T 'you  —  keypaste agent'
T select-pane -t "$DEMO_TMUX_SESSION:0.1" -T 'claude code'

# ---------------------------------------------------------------------------------- the driver
# Everything up to the moment a decision is needed. Typed a character at a time, because instant
# text reads as a screenshot rather than as somebody working.
pane_text() { T capture-pane -p -t "$DEMO_TMUX_SESSION:0.$1" 2>/dev/null || true; }

wait_for() {
  local pane="$1" needle="$2" limit="${3:-30}" waited=0
  while [ "$waited" -lt "$((limit * 5))" ]; do
    pane_text "$pane" | grep -qF -- "$needle" && return 0
    sleep 0.2
    waited=$((waited + 1))
  done
  return 1
}

type_line() {
  local pane="$1" text="$2" i=0
  while [ "$i" -lt "${#text}" ]; do
    T send-keys -t "$DEMO_TMUX_SESSION:0.$pane" -l -- "${text:$i:1}"
    sleep 0.045
    i=$((i + 1))
  done
  T send-keys -t "$DEMO_TMUX_SESSION:0.$pane" Enter
}

drive() {
  # Wait for a client to attach before typing. A fixed sleep here is a race, and losing it means
  # the recording starts halfway through the first command.
  local waited=0
  while [ -z "$(T list-clients -t "$DEMO_TMUX_SESSION" 2>/dev/null)" ]; do
    sleep 0.2
    waited=$((waited + 1))
    [ "$waited" -gt 100 ] && return 1
  done
  sleep 1.2

  type_line 0 "keypaste agent --vault ${DEMO_VAULT/#$HOME/\~}"

  wait_for 0 'Master password:' 20 || return 1
  sleep 0.5
  # The master password is scriptable and echoes nothing: ConsoleSecretPrompt reads the pty a byte
  # at a time, so send-keys is indistinguishable from typing. The Argon2 pause that follows is
  # real, and it stays in.
  T send-keys -t "$DEMO_TMUX_SESSION:0.0" -l -- "$DEMO_MASTER"
  sleep 0.3
  T send-keys -t "$DEMO_TMUX_SESSION:0.0" Enter

  wait_for 0 'listening on' 25 || return 1
  sleep 1.8

  T select-pane -t "$DEMO_TMUX_SESSION:0.1"
  type_line 1 'claude'

  # Claude Code's ready hint. Checked loosely on purpose: the exact wording is not ours and has
  # changed before. If this times out the session is still usable - type the prompt yourself.
  wait_for 1 'shortcuts' 60 || say 'driver: could not spot Claude Code becoming ready; type the prompt yourself'
  sleep 1.0

  type_line 1 'Deploy the billing service to staging with ./deploy.sh. It needs a Stripe key - get it from my keypaste vault rather than asking me to paste one.'

  if [ "$AUTO_APPROVE" = "1" ]; then
    wait_for 0 'Approve?' 90 || return 1
    sleep 1.8
    T send-keys -t "$DEMO_TMUX_SESSION:0.0" -l -- 'y'
    sleep 0.3
    T send-keys -t "$DEMO_TMUX_SESSION:0.0" Enter
  fi
  return 0
}

say ""
say "  Recording. The driver starts the approver and types the prompt, then stops."
say ""
if [ "$AUTO_APPROVE" = "1" ]; then
  say "  --auto-approve is ON. This is a rehearsal: do not ship a take where a script"
  say "  pressed y. See DECISIONS.md D-0033."
else
  say "  When the question appears in the TOP pane, move to it (Ctrl+B o) and press y."
fi
say ""
say "  Then, in the bottom pane:   keypaste log"
say "  And in the top pane:        Ctrl+C     (the last line is worth keeping)"
say "  Finally:                    exit       in both panes, to end the recording."
say ""
say "  You have 45 seconds to answer once the question is up. Do not narrate over it."
say ""
read -r -p "  Press Enter when you are ready. " _

drive &
DRIVER_PID=$!

asciinema rec \
  --overwrite \
  --cols "$DEMO_COLS" \
  --rows "$DEMO_ROWS" \
  --idle-time-limit 2 \
  --title 'keypaste - an agent asks, you decide' \
  -c "tmux -L $DEMO_TMUX_SOCKET attach -t $DEMO_TMUX_SESSION" \
  "$DEMO_CAST"

kill "$DRIVER_PID" 2>/dev/null || true
DRIVER_PID=""

# ------------------------------------------------------------------ what the recording must be
[ -s "$DEMO_CAST" ] || die "nothing was recorded"

command -v jq >/dev/null 2>&1 || die "jq is required to check the recording"

head -1 "$DEMO_CAST" \
  | jq -e --argjson c "$DEMO_COLS" --argjson r "$DEMO_ROWS" \
      '.version == 2 and .width == $c and .height == $r' >/dev/null \
  || die "the cast is not asciicast v2 at ${DEMO_COLS}x${DEMO_ROWS}; something resized it"

grep -qF "$DEMO_MASTER" "$DEMO_CAST" \
  && die "the master password is in the recording. It must never echo - treat this as a bug, not a bad take."

grep -qE 'sk_live_|AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{36}|-----BEGIN' "$DEMO_CAST" \
  && die "the recording contains something shaped like a real credential. Refusing to hand it over."

grep -qF "$HOME/.keypaste" "$DEMO_CAST" \
  && die "the recording touched the real keypaste home rather than the demo's"

# The positive control. Everything above is satisfied by a recording of nothing happening.
grep -qF "$DEMO_SENTINEL" "$DEMO_CAST" \
  || die "the credential was never released in this take, so it proves nothing. Record it again."

grep -q '─────' "$DEMO_CAST" \
  || die "the approval dialog never appeared in this take"

say ""
say "==> $DEMO_CAST"
say "    $(wc -c <"$DEMO_CAST") bytes, built from $(cat "$DEMO_ROOT/BUILT_FROM" 2>/dev/null || echo 'unknown')"
say "    next: scripts/demo/render-demo-gif.sh"
