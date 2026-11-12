#!/usr/bin/env bash
# Proves this computer's agent rules hold across processes only as the vault's owner sealed them.
#
# agents.toml says what every agent gets without asking, and any program the person runs can write it, so the
# owner follows it only while the seal it wrote with the vault's key for this home holds. This gate drives the
# shipped processes: `keypaste approve` holding the vault, a bridge asking for a value, and `keypaste clients`
# turning an agent's run on, which only the owner can seal. Every absence of a prompt is paired with a presence:
# the agent's stdin holds the master password and two answers, both d, and is at EOF afterwards, so a prompt
# that did appear is a denial and a release nobody should have made fails the "nothing came back" checks too.
#
# NEGATIVE CONTROL: this script fails if an agent with no rules is released anything unasked, if a hand-edited
# Allow in agents.toml, sealed before the edit or not, releases anything unasked, if `keypaste clients` cannot
# turn run on through the owner or its change is left unsealed or unrecorded, if `keypaste clients` with nothing
# holding the vault turns run on or leaves the file looking sealed, if the old policy.toml still grants, or if
# the secret reaches the audit log. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-policy-master-pw'
readonly SECRET='SENTINEL-POLICY-DEPLOY-KEY-a91d3e'
readonly OTHER='SENTINEL-POLICY-OTHER-KEY-b02e4f'
readonly LABEL='ci-probe'
readonly ENTRY='env/ci/.env'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='AGENT_ERR OUT ERR'
require jq

CLI="$(keypaste_bin)"

WORK="$(mktemp -d)"
mkdir -p "$WORK/home"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
readonly VAULT="$WORK/vault.kdbx"
# The log in the owner's home, where the bridges here write too.
readonly AUDIT="$WORK/home/audit.jsonl"
readonly RULES="$WORK/home/agents.toml"
readonly RETIRED="$WORK/home/policy.toml"
readonly PIPE="keypaste-policy-$$-$(date +%s)"
readonly AGENT_ERR="$WORK/agent-stderr.txt"
AGENT_PID=""
cleanup() {
  # `keypaste approve` blocks on its pipe until it is stopped, and one that outlives the script keeps
  # the CI step's handles open until the job times out; Windows also keeps $WORK while it runs.
  if [ -n "$AGENT_PID" ]; then
    kill "$AGENT_PID" 2>/dev/null || true
    wait "$AGENT_PID" 2>/dev/null || true
  fi
  rm -rf "$WORK" 2>/dev/null || true
}
trap cleanup EXIT

# ---------------------------------------------------------------- a vault with something in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"

project_var "$CLI" "$VAULT" "$MASTER" ci DEPLOY_KEY "$SECRET" || die "could not store ci's DEPLOY_KEY"
project_var "$CLI" "$VAULT" "$MASTER" ci OTHER_KEY "$OTHER" || die "could not store ci's OTHER_KEY"

# The file keypaste read before agents' rules moved to agents.toml, as wide as a rule could be.
cat >"$RETIRED" <<EOF
[[allow]]
client          = "$LABEL"
entries         = ["**"]
fields          = ["OTHER_KEY", "DEPLOY_KEY"]
max_ttl_seconds = 3600
EOF

# ------------------------------------------------------------------------- starting the approver
# The master password, then d to phase A's request and d to phase C's. Everything after them reads EOF, which
# ConsoleSecretPrompt reports as no answer and the gate turns into a denial: the second guard described at the top.
printf '%s\nd\nd\n' "$MASTER" \
  | "$CLI" approve --vault "$VAULT" --approver "$PIPE" --approval-timeout 10 >/dev/null 2>"$AGENT_ERR" &
AGENT_PID=$!

for _ in $(seq 1 100); do
  grep -q 'listening on' "$AGENT_ERR" && break
  kill -0 "$AGENT_PID" 2>/dev/null || die "keypaste approve exited before it started listening"
  sleep 0.2
done
grep -q 'listening on' "$AGENT_ERR" || die "keypaste approve never started listening"

grep -q "policy.toml is no longer read" "$AGENT_ERR" || die "keypaste approve did not say the old policy.toml is no longer read"

# A request a person refuses cools its field down for every bridge for a minute (T-11), so phase C asks for a
# field phase A does not. TerminalApprovalChannel writes these lines to stderr before it reads an answer, so a
# prompt that was drawn is on disk whatever happened next; `|| true` because grep -c exits 1 on zero matches.
prompts_drawn() { grep -c 'an agent is asking for a credential' "$AGENT_ERR" 2>/dev/null || true; }
changes_drawn() { grep -c 'a change would give an agent more' "$AGENT_ERR" 2>/dev/null || true; }

# A bridge here is labelled ci-probe and exposed to env/**, unless the call says otherwise.
ask() {
  local id="$1" field="$2" out="$3" entry="${ENTRY}" label=(--client-label "$LABEL") expose=(--expose 'env/**')
  shift 3
  case " $* " in *" --entry "*) entry="$2"; shift 2 ;; esac
  case " $* " in *" --unlabelled "*) label=(); shift ;; esac
  case " $* " in *" --expose "*) expose=() ;; esac

  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    jq -cn --argjson id "$id" --arg entry "$entry" --arg field "$field" \
      '{jsonrpc:"2.0",id:$id,method:"tools/call",params:{name:"request_credential",arguments:{entry:$entry,field:$field,reason:"ci mode probe"}}}'
    await_replies 6 "$out" "$id" || true
  } | "$CLI" mcp --vault "$VAULT" --audit-log "$AUDIT" --approver "$PIPE" ${label[@]+"${label[@]}"} ${expose[@]+"${expose[@]}"} "$@" \
        >"$out" 2>"$WORK/mcp-stderr.txt" || die "keypaste mcp exited non-zero"
}

released() { jq -e --argjson id "$1" --arg v "$2" 'select(.id == $id) | .result.isError == false and .result.structuredContent.value == $v' <"$3" >/dev/null; }

# Whether a call's result says something, and whether it was an error. Slurped, as verify-mcp-stdio.sh
# explains, so the status means the same thing on every jq.
says() { jq -se --argjson id "$1" --arg text "$3" 'any(.[]; .id == $id and (.result.content[0].text | contains($text)))' <"$2" >/dev/null; }
refused() { jq -se --argjson id "$1" 'any(.[]; .id == $id and .result.isError == true)' <"$2" >/dev/null; }

clients() { "$CLI" clients "$@" --vault "$VAULT" --approver "$PIPE"; }

# ================================================ A: an agent with no rules is asked about a value
before="$(prompts_drawn)"
OUT="$WORK/defaults.json"
ask 2 DEPLOY_KEY "$OUT"

[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "an agent with no rules was not asked about a request"
grep -q "$SECRET" "$OUT" && die "the answer d still released the credential, or the old policy.toml granted it"

# ================================ B: `keypaste clients` turns run on through the owner, which seals it
ERR="$WORK/run-on.txt"
clients "$LABEL" --run on 2>"$ERR" || die "keypaste clients could not turn run on through the owner: $(cat "$ERR")"
grep -q 'sealed by the process holding the vault' "$ERR" || die "turning run on did not say the owner sealed it"
grep -q '^\[\[seal\]\]' "$RULES" || die "the owner wrote agents.toml without its seal"
jq -se --arg name "$LABEL" 'any(.[]; .tool == "access_change" and .method == "rules-changed" and .client.label == $name)' <"$AUDIT" >/dev/null \
  || die "the owner did not record the rules change"

# ===================================== C: a hand-edited Allow is ignored, though the old seal stays
sed 's/^variable = "ask"$/variable = "allow"/' "$RULES" >"$RULES.edited" && mv "$RULES.edited" "$RULES"
grep -q '^variable = "allow"$' "$RULES" || die "the gate could not edit agents.toml"
grep -q '^\[\[seal\]\]' "$RULES" || die "the edit dropped the seal, so this phase would prove less"

before="$(prompts_drawn)"
OUT="$WORK/edited.json"
ask 3 OTHER_KEY "$OUT"

[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "a hand-edited Allow was followed: the request did not reach a person"
grep -q "$OTHER" "$OUT" && die "a hand-edited Allow released a value unasked"
grep -q '^variable = "allow"$' "$RULES" || die "the owner rewrote the edited file instead of leaving it"

# ============================== D: with nothing holding the vault, nothing is sealed and run stays off
kill "$AGENT_PID" 2>/dev/null || true
wait "$AGENT_PID" 2>/dev/null || true
AGENT_PID=""

ERR="$WORK/no-owner-on.txt"
if clients "$LABEL" --run on 2>"$ERR"; then die "keypaste clients turned run on with nothing holding the vault"; fi
grep -q 'only the process holding it can seal' "$ERR" || die "turning run on with nothing holding the vault did not say why it was refused"

ERR="$WORK/no-owner-off.txt"
clients "$LABEL" --run off 2>"$ERR" || die "keypaste clients could not turn run off with nothing holding the vault"
grep -q 'written unsealed' "$ERR" || die "the change made with nothing holding the vault did not say it was written unsealed"
grep -q '^\[\[seal\]\]' "$RULES" && die "a change made with nothing holding the vault left agents.toml looking sealed"

# ============================================================== E: no value reached the audit log
for sentinel in "$SECRET" "$OTHER"; do
  grep -q "$sentinel" "$AUDIT" && die "a secret reached the audit log"
done

echo "verify-policy-e2e: agent rules hold only as the vault's owner sealed them"
