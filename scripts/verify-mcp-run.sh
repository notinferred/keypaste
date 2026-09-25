#!/usr/bin/env bash
# Proves the shipped run tool end to end, across two real processes: a real `keypaste agent` holds a
# real vault and asks a person, and a real `keypaste-mcp --allow-run` starts the command with the
# approved value in its environment and returns its output scrubbed (D-0358, D-0359).
#
# NEGATIVE CONTROL: this script fails if the approved run does not return the command's exit code,
# if either stream is anything but the value's marker, if the sentinel appears anywhere in the
# transcript or the audit log, if the same run on the same connection is asked about again instead
# of served from the grant, if a denied run starts anything, or if the audit lines do not name the
# entry, the grant, the session and the vault. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-run-master-pw'
readonly CORE='SENTINEL-RUN-GATE-7c31f9'
# Every character the common dumps escape differently: a quote, an apostrophe, a backslash, a
# dollar, a line break, a non-ASCII letter and a percent-escape.
SECRET="${CORE}\"q'x\\y\$z"$'\n'"é%40end"
readonly SECRET

die() {
  echo "::error::$*" >&2
  for f in "${AGENT_ERR:-}" "${OUT:-}" "${ERR:-}" "${AUDIT:-}"; do
    if [ -n "$f" ] && [ -f "$f" ]; then echo "--- $f ---" >&2; cat "$f" >&2; fi
  done
  exit 1
}

command -v jq >/dev/null 2>&1 || die "jq is required and was not found; this gate must never be skipped"

resolve() {
  local candidate="$1"
  [ -x "$candidate" ] || candidate="${candidate}.exe"
  [ -x "$candidate" ] || die "not found: $1 (build first)"
  printf '%s' "$candidate"
}

native() {
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) cygpath -w "$1" ;;
    *) printf '%s' "$1" ;;
  esac
}

CLI="$(resolve "${KEYPASTE_BIN:-artifacts/bin/Keypaste.Cli/release/keypaste}")"
MCP="$(resolve "${KEYPASTE_MCP_BIN:-artifacts/bin/Keypaste.Mcp/release/keypaste-mcp}")"

WORK="$(mktemp -d)"
readonly VAULT="$WORK/vault.kdbx"
readonly AUDIT="$WORK/audit.jsonl"
readonly AGENT_ERR="$WORK/agent-stderr.txt"
readonly OUT="$WORK/stdout.txt"
readonly ERR="$WORK/stderr.txt"
readonly PIPE="keypaste-run-e2e-$$-$(date +%s)"
mkdir -p "$WORK/project"
PROJECT_DIR="$(cd "$WORK/project" && pwd -P)"
PROJECT_DIR="$(native "$PROJECT_DIR")"
readonly PROJECT_DIR
readonly MARKER="$WORK/project/denied-run-ran"

AGENT_PID=""
cleanup() {
  if [ -n "$AGENT_PID" ]; then kill "$AGENT_PID" 2>/dev/null || true; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

# ---------------------------------------------------------------- a vault with the sentinel in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null \
  || die "could not create the vault"
printf '%s\n' "$MASTER" | "$CLI" env set ci "DEPLOY_KEY=$SECRET" --vault "$VAULT" >/dev/null 2>&1 \
  || die "could not store the test credential"

# ------------------------------------------------------------------------------- start the owner
# The master password, then one answer per prompt: h for the first run, d for the third. The
# second is served from the grant the h gave, so nothing reads an answer for it.
printf '%s\nh\nd\n' "$MASTER" \
  | "$CLI" agent --vault "$VAULT" --approver "$PIPE" --approval-timeout 30 >/dev/null 2>"$AGENT_ERR" &
AGENT_PID=$!

for _ in $(seq 1 100); do
  grep -q 'listening on' "$AGENT_ERR" && break
  kill -0 "$AGENT_PID" 2>/dev/null || die "keypaste agent exited before it started listening"
  sleep 0.2
done
grep -q 'listening on' "$AGENT_ERR" || die "keypaste agent never started listening"
SESSION="$(grep 'listening on' "$AGENT_ERR" | head -1 | sed -E 's/.* for session ([^,]+),.*/\1/')"
[ -n "$SESSION" ] || die "keypaste agent named no session on its listening line"

call() {
  local id="$1" command="$2"
  jq -cn --argjson id "$id" --argjson command "$command" --arg dir "$PROJECT_DIR" \
    '{jsonrpc:"2.0",id:$id,method:"tools/call",params:{name:"run",arguments:{command:$command,directory:$dir,project:"ci",reason:"ci run probe"}}}'
}

wait_for() {
  local id="$1"
  for _ in $(seq 1 300); do
    jq -se --argjson id "$id" 'any(.[]; .id == $id)' <"$OUT" >/dev/null 2>&1 && return 0
    sleep 0.2
  done
  return 1
}

readonly PRINTS='["sh","-c","printf %s \"$DEPLOY_KEY\"; printf %s \"$DEPLOY_KEY\" >&2; exit 3"]'
DENIED="$(jq -cn --arg marker "$(native "$MARKER")" '["sh","-c",("printf ran > \"" + $marker + "\"")]')"
readonly DENIED

# ------------------------------------------------------------------------- one bridge, three runs
: >"$OUT"
{
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
  wait_for 1 || true
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  call 2 "$PRINTS"
  wait_for 2 || true
  call 3 "$PRINTS"
  wait_for 3 || true
  call 4 "$DENIED"
  wait_for 4 || true
  sleep 1
} | "$MCP" --vault "$VAULT" --audit-log "$AUDIT" --approver "$PIPE" --client-label ci-probe --allow-run \
      >"$OUT" 2>"$ERR" || die "keypaste-mcp exited non-zero"

for id in 2 3 4; do
  jq -se --argjson id "$id" 'any(.[]; .id == $id)' <"$OUT" >/dev/null || die "no answer to call $id"
done

# 1. The approved run: its exit code, and nothing but the marker on either stream.
jq -se 'any(.[]; .id == 2 and .result.isError == false
  and .result.structuredContent.exit_code == 3
  and .result.structuredContent.stdout == "[keypaste:DEPLOY_KEY]"
  and .result.structuredContent.stderr == "[keypaste:DEPLOY_KEY]")' <"$OUT" >/dev/null \
  || die "the approved run did not come back as exit 3 with both streams scrubbed"

# 2. The same run on the same connection is served from the grant the person gave, unasked.
jq -se 'any(.[]; .id == 3 and .result.isError == false and .result.structuredContent.exit_code == 3)' <"$OUT" >/dev/null \
  || die "the same run was not served from the grant"

# 3. The denied run starts nothing.
jq -se 'any(.[]; .id == 4 and .result.isError == true)' <"$OUT" >/dev/null || die "the denied run was not refused"
[ -e "$MARKER" ] && die "the denied run started its command"

# 4. No form of the value anywhere it could leak.
grep -q "$CORE" "$OUT" && die "the transcript carries the injected value"
grep -q "$CORE" "$ERR" && die "the bridge's stderr carries the injected value"
[ -f "$AUDIT" ] || die "no audit log was written"
grep -q "$CORE" "$AUDIT" && die "the audit log carries the injected value"

# 5. One line per run, written by the bridge, naming the entry, the grant, the session and the vault.
jq -e -s --arg s "$SESSION" '
  length == 3
  and all(.[]; .tool == "run" and .session == $s and (.vault | type == "string" and length == 16))
  and .[0].decision == "granted" and .[0].method == "prompt" and .[0].granted_seconds == 900
  and .[0].entries == ["env/ci/DEPLOY_KEY"] and (.[0].command | startswith("sh -c"))
  and .[1].decision == "granted" and .[1].method == "grant-cache" and .[1].entries == ["env/ci/DEPLOY_KEY"]
  and .[2].decision == "denied" and .[2].method == "prompt"' <"$AUDIT" >/dev/null \
  || die "the audit lines do not show a prompted grant, a grant-served run and a denial in session $SESSION"

# 6. The person was shown the exact command and the claim, and the owner narrated the grant's use.
grep -q 'tool       keypaste.run' "$AGENT_ERR" || die "the run dialog was not shown"
grep -q 'ci run probe' "$AGENT_ERR" || die "the agent's stated reason was not shown to the person"
grep -q 'from a timed grant' "$AGENT_ERR" || die "the grant-served run was not narrated"

echo "ok: a person approved a run and denied another across two real processes; the value reached the"
echo "    command and never the result, the transcript or the log, and the grant served the same run once more"
