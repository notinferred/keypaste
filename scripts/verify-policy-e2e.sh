#!/usr/bin/env bash
# Proves a standing rule releases a credential with NO prompt, and only inside its own pattern.
#
# The 2.2 gate proves a person's yes and no cross a process boundary. This is the opposite claim and
# it is harder: that a rule the human wrote in advance releases something without anybody being
# asked, and that the very same agent still draws a prompt for anything the rule does not cover.
#
# Proving a prompt did NOT happen is worth nothing on its own — an approver whose prompting is
# simply broken would pass such a check every time. So every absence here is paired with a presence
# on the same agent seconds later, and the whole run has a second, independent guard: the agent's
# stdin holds the master password and nothing else, so it is at EOF afterwards, and a prompt that
# did appear would have been answered "no" by default. A policy path that started prompting would
# therefore fail the "the secret came back" assertion as well as the counting one, by two mechanisms
# that break differently.
#
# NEGATIVE CONTROL: this script fails if a policy grant puts a prompt in front of the human, if a
# request outside every rule does not reach one, if a rule releases an entry outside the bridge's
# exposure, if a rule matches a bridge the operator never labelled, if a malformed policy file still
# grants, if a rule raises the TTL ceiling the operator set, or if the secret reaches the audit log.
# Removing any one of those leaves a script that passes while the policy path fails open. These
# checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-policy-master-pw'
readonly SECRET='SENTINEL-POLICY-PASSWORD-a91d3e'
readonly OTHER='SENTINEL-POLICY-USERNAME-b02e4f'
readonly OUTSIDE='SENTINEL-POLICY-OUTSIDE-c13f50'
readonly ENTRY='env/ci/DEPLOY_KEY'
readonly LABEL='ci-probe'
readonly API_ENTRY='api/OpenAI'
readonly API_PASSWORD='SENTINEL-POLICY-OPENAI-PASSWORD-d24a61'
readonly API_KEY='SENTINEL-POLICY-OPENAI-API-KEY-e35b72'
readonly RECOVERY='SENTINEL-POLICY-RECOVERY-CODES-f46c83'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='AGENT_ERR OUT ERR'
require jq

CLI="$(keypaste_bin)"

WORK="$(mktemp -d)"
readonly VAULT="$WORK/vault.kdbx"
readonly AUDIT="$WORK/audit.jsonl"
readonly POLICY="$WORK/policy.toml"
readonly BROKEN="$WORK/broken.toml"
readonly FIELDS="$WORK/fields.toml"
readonly RECOVERY_RULE="$WORK/recovery.toml"

AGENT_PID=""
AGENT_ERR=""
AGENT_PIDS=""

# Every agent this script started, not just the current one, and SIGKILL after SIGTERM.
#
# This is not tidiness. `keypaste agent` blocks on a pipe until it is stopped, and an agent that
# outlives the script keeps the CI step's handles open — so the step sits there until the job times
# out, twenty minutes after every assertion in here has already passed. That failure is silent and
# looks like a hang in the product rather than a leak in the harness, which is why the cleanup is
# thorough rather than minimal, and why the step carries its own timeout in ci.yml.
stop_agents() {
  local pid
  for pid in $AGENT_PIDS; do kill "$pid" 2>/dev/null || true; done
  sleep 1
  for pid in $AGENT_PIDS; do kill -9 "$pid" 2>/dev/null || true; done
  wait 2>/dev/null || true

  AGENT_PIDS=""
  AGENT_PID=""
}

cleanup() {
  stop_agents
  rm -rf "$WORK" 2>/dev/null || true
}
trap cleanup EXIT

# ---------------------------------------------------------------- a vault with something in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null \
  || die "could not create the vault"

printf '%s\n%s\n' "$MASTER" "$SECRET" | "$CLI" add "$ENTRY" --vault "$VAULT" >/dev/null \
  || die "could not store the test credential"

printf '%s\n%s\n' "$MASTER" "$OTHER" | "$CLI" add env/ci/OTHER_KEY --vault "$VAULT" >/dev/null \
  || die "could not store the second test credential"

printf '%s\n' "$MASTER" | "$CLI" add personal/bank --password "$OUTSIDE" --vault "$VAULT" >/dev/null 2>&1 \
  || printf '%s\n%s\n' "$MASTER" "$OUTSIDE" | "$CLI" add personal/bank --vault "$VAULT" >/dev/null \
  || die "could not store the out-of-scope credential"

printf '%s\n%s\n' "$MASTER" "$API_PASSWORD" | "$CLI" add "$API_ENTRY" --vault "$VAULT" >/dev/null \
  || die "could not store the entry with custom fields"
printf '%s\n%s\n%s\n' "$MASTER" "$API_KEY" "$RECOVERY" \
  | "$CLI" set "$API_ENTRY" --field OPENAI_API_KEY --field 'Recovery codes' --vault "$VAULT" >/dev/null \
  || die "could not set the entry's custom fields"

# ------------------------------------------------------------------------------ the policy file
# `entries = ["**"]` on purpose: the rule is as wide as a rule can be written, so phase C proves the
# bridge's own --expose is the ceiling rather than proving the rule happened to be narrow.
cat >"$POLICY" <<EOF
[[allow]]
client          = "$LABEL"
entries         = ["**"]
fields          = ["password"]
max_ttl_seconds = 3600
EOF

printf '[[allow]]\nclientt = "%s"\n' "$LABEL" >"$BROKEN"

# One env-named custom field, and a rule naming a field that never leaves (C.5a).
for rule in "$FIELDS:OPENAI_API_KEY" "$RECOVERY_RULE:Recovery codes"; do
  cat >"${rule%%:*}" <<EOF
[[allow]]
client          = "$LABEL"
entries         = ["api/**"]
fields          = ["${rule#*:}"]
max_ttl_seconds = 300
EOF
done

# ------------------------------------------------------------------------- starting an approver
# Only the master password on stdin. Everything after it reads EOF, which ConsoleSecretPrompt
# reports as no answer and the gate turns into a denial — the second, independent guard described
# at the top of this file.
start_agent() {
  local policy="$1" maxttl="${2:-300}"

  stop_agents

  PIPE="keypaste-policy-$$-$(date +%s)-${RANDOM}"
  AGENT_ERR="$WORK/agent-$(date +%s%N).txt"

  printf '%s\n' "$MASTER" \
    | "$CLI" agent --vault "$VAULT" --approver "$PIPE" --policy "$policy" \
        --approval-timeout 10 --max-ttl "$maxttl" >/dev/null 2>"$AGENT_ERR" &
  AGENT_PID=$!
  AGENT_PIDS="$AGENT_PIDS $AGENT_PID"

  for _ in $(seq 1 100); do
    grep -q 'listening on' "$AGENT_ERR" && break
    kill -0 "$AGENT_PID" 2>/dev/null || die "keypaste agent exited before it started listening"
    sleep 0.2
  done
  grep -q 'listening on' "$AGENT_ERR" || die "keypaste agent never started listening"
}

# TerminalApprovalChannel writes this line to stderr BEFORE it reads an answer, so a prompt that was
# drawn is on disk whatever happened next. `|| true` because grep -c exits 1 on zero matches, and
# under `set -e` that would abort the run instead of reporting a count of nothing.
prompts_drawn() { grep -c 'an agent is asking for a credential' "$AGENT_ERR" 2>/dev/null || true; }

# Without an --expose of its own, a bridge here is exposed to env/**, where $ENTRY lives.
ask() {
  local id="$1" entry="$2" field="$3" out="$4" ttl="$5" expose=(--expose 'env/**')
  shift 5
  case " $* " in *" --expose "*) expose=() ;; esac

  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$entry\",\"field\":\"$field\",\"reason\":\"ci policy probe\",\"ttl_seconds\":$ttl}}}"
    sleep 6
  } | "$CLI" mcp --vault "$VAULT" --audit-log "$AUDIT" --approver "$PIPE" ${expose[@]+"${expose[@]}"} "$@" \
        >"$out" 2>"$WORK/mcp-stderr.txt" || die "keypaste mcp exited non-zero"
}

# =============================================================== A: a rule that matches, no prompt
start_agent "$POLICY"
before="$(prompts_drawn)"

OUT="$WORK/policy-grant.json"
ask 2 "$ENTRY" password "$OUT" 300 --client-label "$LABEL"

jq -e 'select(.id == 2) | .result.isError == false' <"$OUT" >/dev/null \
  || die "a policy-covered request was reported as an error"

grep -q "$SECRET" "$OUT" || die "a policy-covered request did not return the credential"

[ "$(prompts_drawn)" -eq "$before" ] || die "a policy grant put a prompt in front of the human"

grep -q '"method":"policy"' "$AUDIT" || die "the policy grant was not recorded as coming from a policy"
grep -q '"method":"prompt"'  "$AUDIT" && die "a policy grant was recorded as a human approval"

grep -q 'without asking' "$AGENT_ERR" \
  || die "the policy grant was not announced in the approver's terminal"

# ============================================== B: the paired positive, same agent, seconds later
# Without this, phase A's zero could mean "prompting is broken" — which is how an absence assertion
# dies quietly. The rule covers `password` and not `username`.
before="$(prompts_drawn)"

OUT="$WORK/uncovered.json"
ask 3 "$ENTRY" username "$OUT" 300 --client-label "$LABEL"

[ "$(prompts_drawn)" -eq $((before + 1)) ] \
  || die "a request outside every policy rule did not reach a person"

grep -q "$SECRET" "$OUT" && die "a request outside every policy rule released the credential"
jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null \
  || die "a request outside every policy rule was not refused"

# ========================================================= C: a rule cannot widen past --expose
# The rule says `**`. The bridge says env/**. The bridge wins, and nobody is asked either.
before="$(prompts_drawn)"

OUT="$WORK/out-of-scope.json"
ask 4 personal/bank password "$OUT" 300 --client-label "$LABEL"

grep -q "$OUTSIDE" "$OUT" && die "a policy rule released an entry outside the bridge's exposure"
[ "$(prompts_drawn)" -eq "$before" ] \
  || die "an entry outside the exposure was put in front of a person"

# ================================================== D: a rule never matches an unlabelled bridge
# The same request as phase A, from a bridge the operator gave no --client-label. A rule keys on
# what the operator wrote, never on what the client calls itself (THREATS.md T-3).
before="$(prompts_drawn)"

OUT="$WORK/unlabelled.json"
ask 5 "$ENTRY" password "$OUT" 300

[ "$(prompts_drawn)" -eq $((before + 1)) ] \
  || die "a rule matched a bridge the operator never labelled"

grep -q "$SECRET" "$OUT" && die "a rule released a credential to a bridge the operator never labelled"

# ================================================================ E: a malformed file, fresh agent
start_agent "$BROKEN"

grep -q 'NOT in force' "$AGENT_ERR" || die "a malformed policy file was not reported as ignored"

before="$(prompts_drawn)"
OUT="$WORK/broken.json"
ask 6 "$ENTRY" password "$OUT" 300 --client-label "$LABEL"

grep -q "$SECRET" "$OUT" && die "a malformed policy file still granted a request without asking"
[ "$(prompts_drawn)" -eq $((before + 1)) ] \
  || die "a malformed policy file did not fall back to asking a person"

# ======================================================= F: a rule cannot raise the operator's TTL
# The rule says an hour. The agent was started with thirty seconds. Thirty wins.
start_agent "$POLICY" 30

OUT="$WORK/ttl.json"
ask 7 "$ENTRY" password "$OUT" 3600 --client-label "$LABEL"

jq -e 'select(.id == 7) | .result.structuredContent.expires_in_seconds == 30' <"$OUT" >/dev/null \
  || die "a policy rule raised the TTL ceiling the operator set with --max-ttl"

# ============================================ G: a rule naming one custom field releases it alone
start_agent "$FIELDS"
before="$(prompts_drawn)"

OUT="$WORK/field-rule.json"
ask 8 "$API_ENTRY" OPENAI_API_KEY "$OUT" 300 --client-label "$LABEL" --expose 'api/**'

jq -e --arg v "$API_KEY" 'select(.id == 8) | .result.isError == false and .result.structuredContent.value == $v' <"$OUT" >/dev/null \
  || die "a rule naming OPENAI_API_KEY did not release exactly its value"
[ "$(prompts_drawn)" -eq "$before" ] || die "a rule naming OPENAI_API_KEY put a prompt in front of the human"
tail -n 1 "$AUDIT" | jq -e '.method == "policy" and .args.field == "OPENAI_API_KEY"' >/dev/null \
  || die "the custom field's release was not audited as a policy release of that field"

OUT="$WORK/field-rule-password.json"
ask 9 "$API_ENTRY" password "$OUT" 300 --client-label "$LABEL" --expose 'api/**'

[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "the entry's password, outside the rule's fields, did not reach a person"
grep -q "$API_PASSWORD" "$OUT" && die "a rule naming OPENAI_API_KEY released the entry's password"

# A rule naming `Recovery codes` is not usable, so it releases nothing unprompted.
start_agent "$RECOVERY_RULE"
grep -q 'NOT in force' "$AGENT_ERR" || die "a rule naming 'Recovery codes' was not reported as ignored"
before="$(prompts_drawn)"

OUT="$WORK/recovery-rule.json"
ask 10 "$API_ENTRY" OPENAI_API_KEY "$OUT" 300 --client-label "$LABEL" --expose 'api/**'
[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "with the rule ignored, OPENAI_API_KEY did not reach a person"
grep -q "$API_KEY" "$OUT" && die "a file with a rule naming 'Recovery codes' released a field unprompted"

OUT="$WORK/recovery-ask.json"
ask 11 "$API_ENTRY" 'Recovery codes' "$OUT" 300 --client-label "$LABEL" --expose 'api/**'
[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "a request for 'Recovery codes' reached a person"
grep -q "$RECOVERY" "$OUT" && die "a request for 'Recovery codes' released it"

# ------------------------------------------------------------------------------ what was logged
[ -f "$AUDIT" ] || die "no audit log was written"
for value in "$API_PASSWORD" "$API_KEY" "$RECOVERY"; do
  grep -q "$value" "$AUDIT" && die "the audit log contains a custom-field entry's value"
done

grep -q '"decision":"granted"' "$AUDIT" || die "the policy release was not recorded as granted"
grep -q '"decision":"denied"'  "$AUDIT" || die "the refusals were not recorded as denied"
grep -q "allow#1"             "$AUDIT" || die "the audit line does not name which rule released it"

# The one thing the log must never contain, on the one path where nobody watched it happen.
grep -q "$SECRET" "$AUDIT" && die "the audit log contains the policy-released credential"

# Before the success line, not only in the trap: a reader who sees "ok" should be able to take it
# that nothing is still running.
stop_agents

echo "ok: a standing rule released one credential with no prompt, refused everything outside its pattern, and the same agent still asked about the rest"
