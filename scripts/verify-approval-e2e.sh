#!/usr/bin/env bash
# Proves the shipped approval flow works end to end, across two real processes.
#
# Everything else in the suite runs the approver and the bridge inside one test host. This is the
# only place a real `keypaste agent` unlocks a real vault in one process, a real `keypaste-mcp`
# asks it over a real named pipe from another, and a person's yes or no decides what comes back.
# "The credential crosses a process boundary" is the premise of the entire architecture, and a
# premise nothing exercises is a premise nobody is checking.
#
# NEGATIVE CONTROL: this script fails if an approved request does not return the secret, if a
# refused one does, if the secret ever reaches the audit log, or if a request is answered with no
# agent running. Removing any one of those checks leaves a script that passes while the flow is
# broken. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-approval-master-pw'
readonly SECRET='SENTINEL-E2E-PASSWORD-7c31f9'
readonly ENTRY='env/ci/DEPLOY_KEY'

die() {
  echo "::error::$*" >&2
  for f in "${AGENT_ERR:-}" "${OUT:-}" "${ERR:-}"; do
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

CLI="$(resolve "${KEYPASTE_BIN:-artifacts/bin/Keypaste.Cli/release/keypaste}")"
MCP="$(resolve "${KEYPASTE_MCP_BIN:-artifacts/bin/Keypaste.Mcp/release/keypaste-mcp}")"

WORK="$(mktemp -d)"
readonly VAULT="$WORK/vault.kdbx"
readonly AUDIT="$WORK/audit.jsonl"
readonly AGENT_ERR="$WORK/agent-stderr.txt"
readonly PIPE="keypaste-e2e-$$-$(date +%s)"

AGENT_PID=""
cleanup() {
  if [ -n "$AGENT_PID" ]; then kill "$AGENT_PID" 2>/dev/null || true; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

# ---------------------------------------------------------------- a vault with something in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null \
  || die "could not create the vault"

printf '%s\n' "$MASTER" | "$CLI" env set ci "DEPLOY_KEY=$SECRET" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"

# ------------------------------------------------------------------------------- no agent yet
# The ordinary state of a freshly spawned bridge, and it has to be a refusal that names the fix
# rather than a hang or a grant.
OUT="$WORK/no-agent-stdout.txt"
ERR="$WORK/no-agent-stderr.txt"

{
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci probe with no agent\",\"ttl_seconds\":60}}}"
  sleep 5
} | "$MCP" --vault "$VAULT" --audit-log "$AUDIT" --approver "$PIPE" --client-label ci-probe \
      >"$OUT" 2>"$ERR" || die "keypaste-mcp exited non-zero with no agent running"

jq -e 'select(.id == 2) | .result.isError == true' <"$OUT" >/dev/null \
  || die "with no agent running, the request was not refused"

grep -q 'keypaste agent' "$OUT" || die "the no-agent refusal does not name the command that fixes it"
grep -q "$SECRET" "$OUT" && die "a credential was returned with no agent running"

# --------------------------------------------------------------------------- start the approver
# The master password first, then one answer per request: y, then n. ConsoleSecretPrompt reads
# redirected input one byte at a time precisely so this works.
printf '%s\ny\nn\n' "$MASTER" \
  | "$CLI" agent --vault "$VAULT" --approver "$PIPE" --approval-timeout 30 >/dev/null 2>"$AGENT_ERR" &
AGENT_PID=$!

for _ in $(seq 1 100); do
  grep -q 'listening on' "$AGENT_ERR" && break
  kill -0 "$AGENT_PID" 2>/dev/null || die "keypaste agent exited before it started listening"
  sleep 0.2
done
grep -q 'listening on' "$AGENT_ERR" || die "keypaste agent never started listening"

ask() {
  local id="$1" out="$2" err="$3"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci approval probe\",\"ttl_seconds\":60}}}"
    sleep 8
  } | "$MCP" --vault "$VAULT" --audit-log "$AUDIT" --approver "$PIPE" --client-label ci-probe \
        >"$out" 2>"$err" || die "keypaste-mcp exited non-zero"
}

# -------------------------------------------------------------------------------- the yes path
OUT="$WORK/approve-stdout.txt"
ERR="$WORK/approve-stderr.txt"
ask 2 "$OUT" "$ERR"

jq -e 'select(.id == 2) | .result.isError == false' <"$OUT" >/dev/null \
  || die "an approved request was reported as an error"

grep -q "$SECRET" "$OUT" || die "an approved request did not return the credential"

jq -e --arg s "$SECRET" 'select(.id == 2) | .result.structuredContent.value == $s' <"$OUT" >/dev/null \
  || die "the structured result does not carry the released value"

# -------------------------------------------------------------------------------- the no path
# A second bridge, so a second connection: the first one's grant belongs to a process that has
# gone, which is exactly what makes this a fresh question rather than a cache hit.
OUT="$WORK/deny-stdout.txt"
ERR="$WORK/deny-stderr.txt"
ask 3 "$OUT" "$ERR"

jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null \
  || die "a refused request was not reported as an error"

grep -q "$SECRET" "$OUT" && die "a refused request returned the credential"

# ------------------------------------------------------------------------------- what was logged
[ -f "$AUDIT" ] || die "no audit log was written"

grep -q '"decision":"granted"' "$AUDIT" || die "the approval was not recorded as granted"
grep -q '"method":"prompt"'    "$AUDIT" || die "the approval was not recorded as coming from a person"
grep -q '"decision":"denied"'  "$AUDIT" || die "the refusal was not recorded as denied"
grep -q '"label":"ci-probe"'   "$AUDIT" || die "the operator-supplied client label was not recorded"

# The one thing the log must never contain, on the one path where a credential existed to leak.
grep -q "$SECRET" "$AUDIT" && die "the audit log contains the released credential"

# And the person really was shown who was asking and why, rather than being asked to approve a
# blank. This is the display half of THREATS.md T-2.
grep -q 'ci approval probe' "$AGENT_ERR" || die "the agent's stated reason was not shown to the human"
grep -q "$ENTRY"            "$AGENT_ERR" || die "the entry was not shown to the human"

echo "ok: a person approved one request and refused another, across two real processes, and only the approved one released anything"
