#!/usr/bin/env bash
# Proves the shipped approval flow works end to end, across two real processes.
#
# Everything else in the suite runs the approver and the bridge inside one test host. This is the
# only place a real `keypaste agent` unlocks a real vault in one process, a real `keypaste mcp`
# asks it over a real named pipe from another, and a person's yes or no decides what comes back.
# "The credential crosses a process boundary" is the premise of the entire architecture, and a
# premise nothing exercises is a premise nobody is checking.
#
# NEGATIVE CONTROL: this script fails if an approved request does not return the secret, if a
# refused one does, if the secret ever reaches the audit log, if a request is answered with no
# agent running, or if an answer's audit line does not name the session the agent unlocked. Removing any one of those checks leaves a script that passes while the flow is
# broken. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-approval-master-pw'
readonly SECRET='SENTINEL-E2E-PASSWORD-7c31f9'
readonly ENTRY='env/ci/DEPLOY_KEY'

# One entry with an env-named custom field, which may leave by name, and one that never does (C.5a).
readonly API_ENTRY='api/OpenAI'
readonly API_PASSWORD='SENTINEL-E2E-OPENAI-PASSWORD-2b8d41'
readonly API_KEY='SENTINEL-E2E-OPENAI-API-KEY-9e0c57'
readonly RECOVERY='SENTINEL-E2E-RECOVERY-CODES-4f6a13'
readonly DENIAL='keypaste: DENIED. The "field" argument must be password, username, url, notes or a custom field named like an environment variable. This call was recorded in the audit log.'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='AGENT_ERR OUT ERR'
require jq

CLI="$(keypaste_bin)"

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

printf '%s\n%s\n' "$MASTER" "$SECRET" | "$CLI" add "$ENTRY" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"

printf '%s\n%s\n' "$MASTER" "$API_PASSWORD" | "$CLI" add "$API_ENTRY" --vault "$VAULT" >/dev/null \
  || die "could not store the entry with custom fields"
printf '%s\n%s\n%s\n' "$MASTER" "$API_KEY" "$RECOVERY" \
  | "$CLI" set "$API_ENTRY" --field OPENAI_API_KEY --field 'Recovery codes' --vault "$VAULT" >/dev/null \
  || die "could not set the entry's custom fields"

# ------------------------------------------------------------- a reference names one custom field
# `keypaste run --env-file` puts exactly that field in the child; a field that never leaves starts nothing.
CHILD=${BASH:-/bin/bash}
printf 'OPENAI_API_KEY=kp:///api/OpenAI#OPENAI_API_KEY\n' >"$WORK/field.env.keypaste"
got="$(printf '%s\n' "$MASTER" | "$CLI" run --env-file "$WORK/field.env.keypaste" --vault "$VAULT" \
  -- "$CHILD" -c 'printf %s "$OPENAI_API_KEY"' | tr -d '\r')" || die "run --env-file with a custom-field reference failed"
[ "$got" = "$API_KEY" ] || die "the child did not receive exactly the referenced custom field"

printf 'CODES=kp:///api/OpenAI#Recovery%%20codes\n' >"$WORK/codes.env.keypaste"
if printf '%s\n' "$MASTER" | "$CLI" run --env-file "$WORK/codes.env.keypaste" --vault "$VAULT" \
  -- "$CHILD" -c 'printf CHILD-RAN; printf %s "$CODES"' >"$WORK/codes-run.txt" 2>&1; then
  die "a reference naming 'Recovery codes' started its command"
fi
grep -q 'CHILD-RAN' "$WORK/codes-run.txt" && die "a reference naming 'Recovery codes' started its command"
grep -q "$RECOVERY" "$WORK/codes-run.txt" && die "a refused reference printed the field it named"

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
} | "$CLI" mcp --vault "$VAULT" --expose 'env/**' --audit-log "$AUDIT" --approver "$PIPE" --client-label ci-probe \
      >"$OUT" 2>"$ERR" || die "keypaste mcp exited non-zero with no agent running"

jq -e 'select(.id == 2) | .result.isError == true' <"$OUT" >/dev/null \
  || die "with no agent running, the request was not refused"

grep -q 'keypaste agent' "$OUT" || die "the no-agent refusal does not name the command that fixes it"
grep -q "$SECRET" "$OUT" && die "a credential was returned with no agent running"

# --------------------------------------------------------------------------- start the approver
# The master password first, then one answer per request: h, then d. The second request comes from
# a new bridge, so a new connection, and is asked again. ConsoleSecretPrompt reads redirected input
# one byte at a time precisely so this works.
printf '%s\nh\nd\no\nd\n' "$MASTER" \
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

ask() {
  local id="$1" out="$2" err="$3"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci approval probe\",\"ttl_seconds\":60}}}"
    sleep 8
  } | "$CLI" mcp --vault "$VAULT" --expose 'env/**' --audit-log "$AUDIT" --approver "$PIPE" --client-label ci-probe \
        >"$out" 2>"$err" || die "keypaste mcp exited non-zero"
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

# ----------------------------------------------------------------------- one custom field, by name
# Allowed once at the agent's third prompt, released alone; every other name is refused by the bridge
# before anyone is asked, in the words the plan fixes.
ask_field() {
  local id="$1" field="$2" out="$3" err="$4" wait="$5"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    jq -cn --argjson id "$id" --arg entry "$API_ENTRY" --arg field "$field" \
      '{jsonrpc:"2.0",id:$id,method:"tools/call",params:{name:"request_credential",arguments:{entry:$entry,field:$field,reason:"ci custom field probe",ttl_seconds:60}}}'
    sleep "$wait"
  } | "$CLI" mcp --vault "$VAULT" --audit-log "$AUDIT" --approver "$PIPE" --client-label ci-probe --expose 'api/**' \
        >"$out" 2>"$err" || die "keypaste mcp exited non-zero"
}

OUT="$WORK/field-stdout.txt"
ERR="$WORK/field-stderr.txt"
ask_field 4 OPENAI_API_KEY "$OUT" "$ERR" 8

jq -e --arg s "$API_KEY" 'select(.id == 4) | .result.isError == false and .result.structuredContent.value == $s and .result.structuredContent.field == "OPENAI_API_KEY"' \
  <"$OUT" >/dev/null || die "an approved OPENAI_API_KEY request did not return exactly its value"
for other in "$API_PASSWORD" "$RECOVERY" "$SECRET"; do
  grep -q "$other" "$OUT" && die "the custom-field release carried another field's value"
done

id=5
for field in 'Recovery codes' otp KP2A_URL_1 URL; do
  OUT="$WORK/refused-$id-stdout.txt"
  ERR="$WORK/refused-$id-stderr.txt"
  ask_field "$id" "$field" "$OUT" "$ERR" 3
  jq -e --arg t "$DENIAL" --argjson id "$id" 'select(.id == $id) | .result.isError == true and .result.content[0].text == $t' \
    <"$OUT" >/dev/null || die "a request for '$field' was not refused with the fixed denial"
  for value in "$API_PASSWORD" "$API_KEY" "$RECOVERY" "$SECRET"; do
    grep -q "$value" "$OUT" && die "a refused request for '$field' returned a value"
  done
  id=$((id + 1))
done

[ "$(grep -c '^  field    ' "$AGENT_ERR")" = 3 ] || die "the agent was not asked exactly three times: a refused field reached a person"
grep -q '^  field    OPENAI_API_KEY' "$AGENT_ERR" || die "the agent's prompt did not name the custom field"

# The run tool names the field beside its entry on the prompt, which the agent's fourth answer denies.
native() {
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) cygpath -w "$1" ;;
    *) printf '%s' "$1" ;;
  esac
}
mkdir -p "$WORK/project"
RUN_DIR="$(native "$(cd "$WORK/project" && pwd -P)")"
OUT="$WORK/run-stdout.txt"
ERR="$WORK/run-stderr.txt"
{
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  jq -cn --arg dir "$RUN_DIR" \
    '{jsonrpc:"2.0",id:9,method:"tools/call",params:{name:"run",arguments:{command:["sh","-c","printf %s \"$OPENAI_API_KEY\""],directory:$dir,env:{OPENAI_API_KEY:"kp:///api/OpenAI#OPENAI_API_KEY"},reason:"ci custom field run probe"}}}'
  sleep 8
} | "$CLI" mcp --vault "$VAULT" --audit-log "$AUDIT" --approver "$PIPE" --client-label ci-probe --expose 'api/**' --allow-run \
      >"$OUT" 2>"$ERR" || die "keypaste mcp exited non-zero"

jq -e 'select(.id == 9) | .result.isError == true' <"$OUT" >/dev/null || die "a denied run was not refused"
LC_ALL=C grep -Eq 'api/OpenAI .{1,3} OPENAI_API_KEY' "$AGENT_ERR" || die "the run's prompt did not name the entry and its custom field"
for value in "$API_PASSWORD" "$API_KEY" "$RECOVERY"; do
  grep -q "$value" "$OUT" && die "a denied run returned a value"
done

# ------------------------------------------------------------------------------- what was logged
[ -f "$AUDIT" ] || die "no audit log was written"

grep -q '"decision":"granted"' "$AUDIT" || die "the approval was not recorded as granted"
grep -q '"method":"prompt"'    "$AUDIT" || die "the approval was not recorded as coming from a person"
grep -q '"decision":"denied"'  "$AUDIT" || die "the refusal was not recorded as denied"
grep -q '"label":"ci-probe"'   "$AUDIT" || die "the operator-supplied client label was not recorded"

# The request with no agent reached no session; both later answers came from the agent's own gate,
# under the session it unlocked (4.3a).
jq -e -s --arg s "$SESSION" \
  'length == 9
   and .[0].decision == "denied" and .[0].method == "no-approver" and (.[0] | has("session") | not)
   and (.[1:4] | all(.method == "prompt" and .session == $s))
   and .[1].decision == "granted" and .[2].decision == "denied"
   and .[3].args.field == "OPENAI_API_KEY" and .[3].decision == "granted" and .[3].method == "prompt" and .[3].session == $s
   and (.[4:8] | all(.args.field == "invalid" and .method == "invalid-request" and .decision == "denied"))
   and .[8].tool == "run" and .[8].decision == "denied" and .[8].method == "prompt"' \
  <"$AUDIT" >/dev/null \
  || die "the audit lines do not show a sessionless denial, the prompts of keypaste agent's session $SESSION, the custom field, four invalid fields and a denied run"

# The one thing the log must never contain, on the one path where a credential existed to leak.
grep -q "$SECRET" "$AUDIT" && die "the audit log contains the released credential"
for value in "$API_PASSWORD" "$API_KEY" "$RECOVERY"; do
  grep -q "$value" "$AUDIT" && die "the audit log contains a custom-field entry's value"
  grep -q "$value" "$AGENT_ERR" && die "the agent's terminal shows a custom-field entry's value"
done

# And the person really was shown who was asking and why, rather than being asked to approve a
# blank. This is the display half of THREATS.md T-2.
grep -q 'ci approval probe' "$AGENT_ERR" || die "the agent's stated reason was not shown to the human"
grep -q "$ENTRY"            "$AGENT_ERR" || die "the entry was not shown to the human"

echo "ok: a person approved one request and refused another, across two real processes, and only the approved one released anything"
