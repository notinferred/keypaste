# shellcheck shell=bash
# Helpers the gates share, sourced from a path relative to the gate:
#   . "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
# Nothing here skips a check: a missing tool or binary fails the gate that asked for it.

# A gate that sets DIE_PREFIX prints it before the reason; otherwise the reason is a GitHub error
# annotation. DIE_FILES names variables holding files to print after it, read when the gate fails.
die() {
  local name file
  if [ -n "${DIE_PREFIX:-}" ]; then printf '\n%s%s\n' "$DIE_PREFIX" "$*" >&2; else echo "::error::$*" >&2; fi
  for name in ${DIE_FILES:-}; do
    file="${!name:-}"
    if [ -n "$file" ] && [ -f "$file" ]; then echo "--- $file ---" >&2; cat "$file" >&2; fi
  done
  exit 1
}

step() { printf '\n--- %s\n' "$*"; }

require() { command -v "$1" >/dev/null 2>&1 || die "$1 is required and was not found; this gate must never be skipped"; }

# The executable the variable named $1 gives, else the build output $2; Windows appends .exe.
resolve() {
  local path="${!1:-$2}"
  [ -x "$path" ] || path="$path.exe"
  [ -x "$path" ] || die "not found: ${!1:-$2} (build it first, or set $1)"
  printf '%s' "$path"
}
keypaste_bin() { resolve KEYPASTE_BIN artifacts/bin/Keypaste.Cli/release/keypaste; }
keypaste_mcp() { resolve KEYPASTE_MCP_BIN artifacts/bin/Keypaste.Mcp/release/keypaste-mcp; }
app_driver() { resolve KEYPASTE_APP_DRIVER artifacts/bin/Keypaste.AppDriver/release/Keypaste.AppDriver; }
vault_restorer() { resolve KEYPASTE_RESTORER artifacts/bin/Keypaste.VaultRestorer/release/Keypaste.VaultRestorer; }

# Writes a variable in the env/<project> layout of earlier releases, one entry per key, which
# `env set` no longer creates (D-0413), for a gate that needs one as its fixture. `add` asks no
# username, URL or notes without a terminal, so the master password and the value are its input.
#   legacy_var <keypaste> <vault> <master> <project>[/<profile>] <KEY> <value> [keypaste options...]
legacy_var() {
  local cli=$1 vault=$2 master=$3 project=$4 key=$5 value=$6
  shift 6
  printf '%s\n%s\n' "$master" "$value" | "$cli" add "env/$project/$key" --vault "$vault" "$@" >/dev/null
}

# A path as a Windows program must be given it from Git Bash; unchanged elsewhere.
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

# A file's bytes as one hex string, so a refusal can be shown to leave it unchanged.
bytes() { od -An -v -tx1 "$1" | tr -d ' \n'; }

# Git Bash's kill cannot end a native Windows process.
kill_process() {
  if command -v taskkill >/dev/null 2>&1; then
    taskkill //F //PID "$1" >/dev/null 2>&1 || true
  else
    kill -9 "$1" 2>/dev/null || true
  fi
}

# Waits until $2 holds $3 (default 1) lines matching $1, for WAIT_SECONDS (default 30).
wait_for() {
  local pattern="$1" file="$2" count="${3:-1}"
  for _ in $(seq 1 $((${WAIT_SECONDS:-30} * 5))); do
    [ "$(grep -c -- "$pattern" "$file" 2>/dev/null || true)" -ge "$count" ] && return 0
    sleep 0.2
  done
  die "timed out waiting for '$pattern' in $file"
}

# The held app's current session and process, from the latest line tests/Keypaste.AppDriver printed to HOLD_OUT.
session_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/^holding session ([0-9a-f]+).*/\1/'; }
process_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/.* as process ([0-9]+).*/\1/'; }
