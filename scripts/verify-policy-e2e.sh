#!/usr/bin/env bash
# Proves an agent's mode releases a credential with NO prompt only where the person made it Automatic.
#
# The 2.2 gate proves a person's yes and no cross a process boundary. This is the opposite claim and
# it is harder: that a setting the person confirmed in advance releases something without anybody
# being asked, and that the very same agent still draws a prompt for anything the setting does not
# cover. The setting is made the way a person makes it, with `keypaste mcp policy` confirmed in
# `keypaste agent`'s terminal, and a change the person refuses changes nothing. An agent can ask for a
# change to its own settings through its keypaste tool, and the same confirmation stands between it
# and anything more.
#
# Proving a prompt did NOT happen is worth nothing on its own — an approver whose prompting is
# simply broken would pass such a check every time. So every absence here is paired with a presence
# on the same agent seconds later, and the whole run has a second, independent guard: the agent's
# stdin holds the master password and the three answers below and nothing else, so it is at EOF
# afterwards, and a prompt that did appear would have been answered "no" by default. An Automatic
# path that started prompting would therefore fail the "the secret came back" assertion as well as
# the counting one, by two mechanisms that break differently.
#
# NEGATIVE CONTROL: this script fails if an agent with no settings is released anything unasked, if a
# refused change applies, if an agent's own request for more applies unconfirmed, is not shown as its
# own with its reason, or if a request for more from it, another agent or a terminal is put to the
# person again right after a refusal, if an agent cannot narrow
# itself at once or its narrowing reaches the vault, if a bridge with no label asks anybody, if a bridge set
# up for a home cannot lower that home's ceilings with nothing holding the vault or one that cannot tell
# which ceilings apply lowers any, if
# Automatic puts a prompt in front of the human, if a request outside its
# project or in a protected environment does not reach one, if Automatic releases an entry outside
# the bridge's exposure or to a bridge the operator never labelled, if `*` can be made Automatic, if
# this computer's ceiling or an unreadable clients.toml leaves Automatic in force, if a spent hourly
# limit does not fall back to asking, if Automatic raises the TTL ceiling the operator set, if the old
# policy.toml still grants, or if the secret reaches the audit log. Removing any one of those leaves a
# script that passes while the Automatic path fails open. These checks must never be skipped or
# soft-passed.
set -euo pipefail

readonly MASTER='ci-policy-master-pw'
readonly SECRET='SENTINEL-POLICY-DEPLOY-KEY-a91d3e'
readonly OTHER='SENTINEL-POLICY-OTHER-KEY-b02e4f'
readonly PROD='SENTINEL-POLICY-PROD-KEY-c13f50'
readonly WEB='SENTINEL-POLICY-WEB-KEY-d24a61'
readonly SPARE='SENTINEL-POLICY-SPARE-KEY-e35b72'
readonly CEILING='SENTINEL-POLICY-CEILING-KEY-f46c83'
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
readonly CLIENTS="$WORK/home/clients.toml"
readonly RETIRED="$WORK/home/policy.toml"
readonly PIPE="keypaste-policy-$$-$(date +%s)"
readonly AGENT_ERR="$WORK/agent-stderr.txt"

AGENT_PID=""
cleanup() {
  # `keypaste agent` blocks on its pipe until it is stopped, and one that outlives the script keeps
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
project_var "$CLI" "$VAULT" "$MASTER" ci SPARE_KEY "$SPARE" || die "could not store ci's SPARE_KEY"
project_var "$CLI" "$VAULT" "$MASTER" ci CEILING_KEY "$CEILING" || die "could not store ci's CEILING_KEY"
project_var "$CLI" "$VAULT" "$MASTER" ci/prod PROD_KEY "$PROD" || die "could not store ci's prod key"
project_var "$CLI" "$VAULT" "$MASTER" web WEB_KEY "$WEB" || die "could not store web's key"

# The file keypaste read before agents' settings moved into the vault, as wide as a rule could be.
cat >"$RETIRED" <<EOF
[[allow]]
client          = "$LABEL"
entries         = ["**"]
fields          = ["OTHER_KEY", "WEB_KEY", "PROD_KEY", "SPARE_KEY", "CEILING_KEY"]
max_ttl_seconds = 3600
EOF

# ------------------------------------------------------------------------- starting the approver
# The master password, then the four answers this run gives: d to phase A's request, y to phase B's
# change, then y and d to the agent's own two requests in phase K. Everything after them reads EOF,
# which ConsoleSecretPrompt reports as no answer and the gate turns into a denial — the second,
# independent guard described at the top.
printf '%s\nd\ny\ny\nd\n' "$MASTER" \
  | "$CLI" agent --vault "$VAULT" --approver "$PIPE" --approval-timeout 10 --max-ttl 30 >/dev/null 2>"$AGENT_ERR" &
AGENT_PID=$!

for _ in $(seq 1 100); do
  grep -q 'listening on' "$AGENT_ERR" && break
  kill -0 "$AGENT_PID" 2>/dev/null || die "keypaste agent exited before it started listening"
  sleep 0.2
done
grep -q 'listening on' "$AGENT_ERR" || die "keypaste agent never started listening"

grep -q "policy.toml is no longer read" "$AGENT_ERR" || die "keypaste agent did not say the old policy.toml is no longer read"

# A request a person refuses, or that reads the end of the input, cools its field down for every bridge
# for a minute (T-11), so each phase that ends in a refusal asks for a field no later phase asks for;
# a refused change cools every request for more from any agent or terminal the same way, so the one
# refused change comes after every change the person confirms.
#
# TerminalApprovalChannel writes these lines to stderr BEFORE it reads an answer, so a prompt that was
# drawn is on disk whatever happened next. `|| true` because grep -c exits 1 on zero matches, and
# under `set -e` that would abort the run instead of reporting a count of nothing.
prompts_drawn() { grep -c 'an agent is asking for a credential' "$AGENT_ERR" 2>/dev/null || true; }
changes_drawn() { grep -c 'a change would give an agent more' "$AGENT_ERR" 2>/dev/null || true; }

# A bridge here is labelled ci-probe and exposed to env/**, unless the call says otherwise.
ask() {
  local id="$1" field="$2" out="$3" ttl="$4" entry="${ENTRY}" label=(--client-label "$LABEL") expose=(--expose 'env/**')
  shift 4
  case " $* " in *" --entry "*) entry="$2"; shift 2 ;; esac
  case " $* " in *" --unlabelled "*) label=(); shift ;; esac
  case " $* " in *" --expose "*) expose=() ;; esac

  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    jq -cn --argjson id "$id" --arg entry "$entry" --arg field "$field" --argjson ttl "$ttl" \
      '{jsonrpc:"2.0",id:$id,method:"tools/call",params:{name:"request_credential",arguments:{entry:$entry,field:$field,reason:"ci mode probe",ttl_seconds:$ttl}}}'
    await_replies 6 "$out" "$id" || true
  } | "$CLI" mcp --vault "$VAULT" --audit-log "$AUDIT" --approver "$PIPE" ${label[@]+"${label[@]}"} ${expose[@]+"${expose[@]}"} "$@" \
        >"$out" 2>"$WORK/mcp-stderr.txt" || die "keypaste mcp exited non-zero"
}

released() { jq -e --argjson id "$1" --arg v "$2" 'select(.id == $id) | .result.isError == false and .result.structuredContent.value == $v' <"$3" >/dev/null; }

# The agent's own request for a change to its settings, through a bridge labelled ci-probe unless the
# call gives --label <name> or --unlabelled; --clients <path> pins its ceilings and --no-home starts it
# without KEYPASTE_HOME, as an AI tool may.
change() {
  local id="$1" out="$2" arguments="$3" label=(--client-label "$LABEL") extra=() run=("$CLI")
  shift 3
  while [ $# -gt 0 ]; do
    case "$1" in
      --unlabelled) label=() ;;
      --label) label=(--client-label "$2"); shift ;;
      --clients) extra=(--clients "$2"); shift ;;
      --no-home) run=(env -u KEYPASTE_HOME "$CLI") ;;
    esac
    shift
  done

  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    jq -cn --argjson id "$id" --argjson arguments "$arguments" \
      '{jsonrpc:"2.0",id:$id,method:"tools/call",params:{name:"request_access_change",arguments:$arguments}}'
    await_replies 6 "$out" "$id" || true
  } | "${run[@]}" mcp --vault "$VAULT" --audit-log "$AUDIT" --approver "$PIPE" ${label[@]+"${label[@]}"} ${extra[@]+"${extra[@]}"} \
        >"$out" 2>"$WORK/mcp-stderr.txt" || die "keypaste mcp exited non-zero"
}

# Whether a call's result says something, and whether it was an error. Slurped, as verify-mcp-stdio.sh
# explains, so the status means the same thing on every jq.
says() { jq -se --argjson id "$1" --arg text "$3" 'any(.[]; .id == $id and (.result.content[0].text | contains($text)))' <"$2" >/dev/null; }
refused() { jq -se --argjson id "$1" 'any(.[]; .id == $id and .result.isError == true)' <"$2" >/dev/null; }

policy() { "$CLI" mcp policy "$@" --vault "$VAULT" --approver "$PIPE"; }

# ================================================= A: an agent with no settings is asked about
before="$(prompts_drawn)"
OUT="$WORK/defaults.json"
ask 2 OTHER_KEY "$OUT" 300

[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "an agent with no settings was not asked about a request"
grep -q "$OTHER" "$OUT" && die "the answer d still released the credential, or the old policy.toml granted it"

# ======================================= B: a change that gives more waits for the person's answer
ERR="$WORK/confirmed-change.txt"
before="$(changes_drawn)"
policy "$LABEL" --project ci --mode automatic 2>"$ERR" || die "a change the person confirmed was not made"
[ "$(changes_drawn)" -eq $((before + 1)) ] || die "a change that gives more did not reach the person"
grep -q 'a person confirmed it' "$ERR" || die "the confirmed change did not say a person confirmed it"

# ======================================= K: the agent asks for a change to its own settings, itself
# The bridge sends its own label, never a name the agent gives. A change that gives more is put to the
# person as the agent's own request with its reason; once refused, every request for more is refused
# unasked, the agent's, another agent's and a terminal's alike; it can still narrow itself at once, on
# this computer only; and a bridge with no label asks nobody.
before="$(changes_drawn)"
OUT="$WORK/agent-run-on.json"
change 21 "$OUT" '{"run":"on","reason":"ci needs to run its deploy script","agent":"someone-else"}'

[ "$(changes_drawn)" -eq $((before + 1)) ] || die "the agent's own request for more did not reach the person"
grep -q 'asked by ci-probe through its keypaste tool' "$AGENT_ERR" || die "the confirmation did not say the agent asked through its own tool"
grep -q 'ci needs to run its deploy script' "$AGENT_ERR" || die "the confirmation did not show the agent's reason"
grep -q 'someone-else' "$AGENT_ERR" && die "the agent named another agent and the confirmation repeated it"
refused 21 "$OUT" && die "a change the person confirmed was refused to the agent"
says 21 "$OUT" 'A person read this request and confirmed it' || die "the agent was not told a person confirmed its change"

OUT="$WORK/agent-see-all.json"
change 22 "$OUT" '{"see":["**"],"reason":"ci wants to see every item"}'
[ "$(changes_drawn)" -eq $((before + 2)) ] || die "the agent's second request for more did not reach the person"
refused 22 "$OUT" || die "a change the person refused was reported to the agent as made"
says 22 "$OUT" 'A person read this request and said no' || die "the agent was not told a person refused its change"

OUT="$WORK/agent-reworded.json"
change 23 "$OUT" '{"project":"web","mode":"automatic","reason":"then only web"}'
[ "$(changes_drawn)" -eq $((before + 2)) ] || die "a reworded request for more, right after a refusal, was put to the person again"
says 23 "$OUT" 'was refused or went unanswered' || die "the reworded request was not refused as cooling down"

OUT="$WORK/agent-renamed.json"
change 26 "$OUT" '{"mode":"automatic","reason":"under another name"}' --label ci-other
[ "$(changes_drawn)" -eq $((before + 2)) ] || die "a request for more under another agent's name, right after a refusal, was put to the person"
says 26 "$OUT" 'was refused or went unanswered' || die "the request under another agent's name was not refused as cooling down"

ERR="$WORK/terminal-cooled.txt"
if policy "$LABEL" --mode automatic 2>"$ERR"; then die "a terminal's request for more, right after a refusal, was reported as made"; fi
[ "$(changes_drawn)" -eq $((before + 2)) ] || die "a terminal's request for more, right after a refusal, was put to the person"
grep -q 'try again in' "$ERR" || die "the terminal's request held back did not say when to try again"
grep -q 'which do not wait' "$ERR" || die "the terminal's request held back did not say the app's settings do not wait"

OUT="$WORK/agent-run-off.json"
change 24 "$OUT" '{"run":"off","reason":"ci is done running things"}'
[ "$(changes_drawn)" -eq $((before + 2)) ] || die "the agent narrowing itself was put to the person"
says 24 "$OUT" 'applied at once on this computer' || die "the agent's narrowing did not say it applied at once"

OUT="$WORK/agent-settings.json"
policy --json >"$OUT" || die "keypaste mcp policy --json failed through the agent"
jq -e --arg l "$LABEL" \
  '([.[] | select(.kind == "agent" and .name == $l)][0].record | .run == true and .mode != "automatic" and (.see // [] | index("**") | not) and ([.projects[] | select(.project == "ci" and .mode == "automatic")] | length == 1) and ([.projects[]? | select(.project == "web")] | length == 0))
   and ([.[] | select(.kind == "ceiling" and .label == $l and .run == "off")] | length == 1)' \
  <"$OUT" >/dev/null || die "the vault does not hold exactly the confirmed changes, ci Automatic and run on, with nothing refused or held back, or this computer does not hold the agent's narrowing"
rm -f "$CLIENTS"

OUT="$WORK/agent-unlabelled.json"
change 25 "$OUT" '{"run":"on","reason":"a bridge with no name"}' --unlabelled
[ "$(changes_drawn)" -eq $((before + 2)) ] || die "a bridge with no label put a change in front of the person"
says 25 "$OUT" '--client-label' || die "a bridge with no label did not say it needs one"
tail -n 1 "$AUDIT" | jq -e '.tool == "access_change" and .method == "invalid-request" and .client.label == null' >/dev/null \
  || die "the bridge with no label did not record its refusal"

# ===================================================== C: Automatic releases with no prompt
before="$(prompts_drawn)"
OUT="$WORK/automatic.json"
ask 3 DEPLOY_KEY "$OUT" 3600

released 3 "$SECRET" "$OUT" || die "an Automatic request did not return exactly its value"
grep -q "$OTHER" "$OUT" && die "an Automatic release carried another field's value"
[ "$(prompts_drawn)" -eq "$before" ] || die "an Automatic release put a prompt in front of the human"
jq -e 'select(.id == 3) | .result.structuredContent.expires_in_seconds == 30' <"$OUT" >/dev/null \
  || die "an Automatic release raised the TTL ceiling the operator set with --max-ttl"
tail -n 1 "$AUDIT" | jq -e --arg l "$LABEL" '.decision == "granted" and .method == "automatic" and .client.label == $l and .args.field == "DEPLOY_KEY"' >/dev/null \
  || die "the Automatic release was not recorded as automatic, for ci-probe and DEPLOY_KEY"
grep -q 'without asking (Automatic)' "$AGENT_ERR" || die "the Automatic release was not announced in the approver's terminal"

# ============================================== D: the paired positive, same agent, seconds later
# Without this, phase C's zero could mean "prompting is broken" — which is how an absence assertion
# dies quietly. ci is Automatic; web is not.
before="$(prompts_drawn)"
OUT="$WORK/other-project.json"
ask 4 WEB_KEY "$OUT" 300 --entry env/web/.env

[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "a request outside the Automatic project did not reach a person"
grep -q "$WEB" "$OUT" && die "a request outside the Automatic project released its credential"

# ================================================ E: a protected environment is asked about anyway
before="$(prompts_drawn)"
OUT="$WORK/protected.json"
ask 5 PROD_KEY "$OUT" 300 --entry env/ci/.env.prod

[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "a protected environment of an Automatic project did not reach a person"
grep -q "$PROD" "$OUT" && die "Automatic released an entry in a protected environment"

# ======================================================== F: Automatic cannot widen past --expose
before="$(prompts_drawn)"
OUT="$WORK/outside-exposure.json"
ask 6 DEPLOY_KEY "$OUT" 300 --expose 'env/web/**'

grep -q "$SECRET" "$OUT" && die "Automatic released an entry outside the bridge's exposure"
[ "$(prompts_drawn)" -eq "$before" ] || die "an entry outside the exposure was put in front of a person"

# ===================================== G: Automatic never answers a bridge the operator never labelled
# The same request as phase C, from a bridge with no --client-label. Settings key on what the operator
# wrote, never on what the client calls itself (THREATS.md T-3).
before="$(prompts_drawn)"
OUT="$WORK/unlabelled.json"
ask 7 SPARE_KEY "$OUT" 300 --unlabelled

[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "Automatic answered a bridge the operator never labelled"
grep -q "$SPARE" "$OUT" && die "a bridge the operator never labelled was released a credential"

# ================================================================= H: `*` is never made Automatic
before="$(changes_drawn)"
ERR="$WORK/any-automatic.txt"
if policy '*' --project ci --mode automatic 2>"$ERR"; then die "every agent was made Automatic"; fi
[ "$(changes_drawn)" -eq "$before" ] || die "making every agent Automatic was put to the person instead of refused"

# ============================================== I: this computer's ceiling narrows the vault's settings
printf '[[client]]\nlabel = "%s"\nmode = "ask"\n' "$LABEL" >"$CLIENTS"
before="$(prompts_drawn)"
OUT="$WORK/ceiling.json"
ask 8 CEILING_KEY "$OUT" 300

[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "with this computer's ceiling at Ask every time, an Automatic request did not reach a person"
grep -q "$CEILING" "$OUT" && die "this computer's ceiling left Automatic in force"
grep -q 'this agent is set to Ask every time' "$AGENT_ERR" || die "the prompt did not say the agent is set to Ask every time"

# The old form of the file is not this file: unreadable, it refuses rather than grants.
printf '[[client]]\nlabel = "%s"\npolicy = "ask"\n' "$LABEL" >"$CLIENTS"
before="$(prompts_drawn)"
OUT="$WORK/unreadable-ceiling.json"
ask 9 DEPLOY_KEY "$OUT" 300

jq -e 'select(.id == 9) | .result.isError == true' <"$OUT" >/dev/null || die "an unreadable clients.toml did not refuse the request"
grep -q "$SECRET" "$OUT" && die "an unreadable clients.toml left Automatic in force"
[ "$(prompts_drawn)" -eq "$before" ] || die "an unreadable clients.toml put a request in front of a person"

rm -f "$CLIENTS"
before="$(prompts_drawn)"
OUT="$WORK/ceiling-gone.json"
ask 10 DEPLOY_KEY "$OUT" 300

released 10 "$SECRET" "$OUT" || die "with the ceiling removed, the Automatic request was not released"
[ "$(prompts_drawn)" -eq "$before" ] || die "with the ceiling removed, an Automatic request reached a person"

# ======================================================= J: a spent hourly limit asks instead
# A lower limit gives the agent nothing more, so it applies at once with nobody asked.
before="$(changes_drawn)"
policy "$LABEL" --project ci --per-hour 1 2>"$WORK/limit.txt" || die "a lower hourly limit was not applied"
[ "$(changes_drawn)" -eq "$before" ] || die "a lower hourly limit was put to the person"
grep -q 'applied at once, since it gives the agent nothing more' "$WORK/limit.txt" || die "the lower limit did not say it applied at once"

before="$(prompts_drawn)"
OUT="$WORK/within-limit.json"
ask 11 DEPLOY_KEY "$OUT" 300
released 11 "$SECRET" "$OUT" || die "the hour's one Automatic release was not made"
[ "$(prompts_drawn)" -eq "$before" ] || die "the hour's one Automatic release reached a person"

OUT="$WORK/limit-spent.json"
ask 12 DEPLOY_KEY "$OUT" 300
[ "$(prompts_drawn)" -eq $((before + 1)) ] || die "with the hour's limit spent, the request did not fall back to a person"
grep -q "$SECRET" "$OUT" && die "a release went past the hourly limit unasked"

# ------------------------------------------------------------------------------ what was logged
[ -f "$AUDIT" ] || die "no audit log was written"

[ "$(grep -c '"method":"automatic"' "$AUDIT" || true)" -eq 3 ] || die "the log does not hold exactly the three Automatic releases"
jq -e -s --arg l "$LABEL" \
  '[.[] | select(.tool == "access_change" and .client.name == "keypaste mcp policy" and .client.label == $l)] | length == 3
   and .[0].decision == "granted" and .[1].method == "cooldown" and .[2].method == "narrowing"' \
  <"$AUDIT" >/dev/null || die "the log does not hold the confirmed change, the one held back and the narrowing, in that order"
jq -e -s --arg l "$LABEL" \
  '[.[] | select(.tool == "access_change" and .client.name == "ci-probe" and .client.label == $l)]
   | map([.decision, .method]) == [["granted", "prompt"], ["denied", "prompt"], ["denied", "cooldown"], ["granted", "narrowing"]]
   and .[0].args.reason_excerpt == "ci needs to run its deploy script"' \
  <"$AUDIT" >/dev/null || die "the log does not hold the agent's four requests, each with what became of it, under its own client"
jq -e -s '[.[] | select(.tool == "access_change" and .client.label == "*")] | length == 1 and .[0].decision == "denied"' \
  <"$AUDIT" >/dev/null || die "the refused change to every agent was not recorded as denied"

# The one thing the log must never contain, on the one path where nobody watched it happen.
for value in "$SECRET" "$OTHER" "$PROD" "$WEB" "$SPARE" "$CEILING"; do
  grep -q "$value" "$AUDIT" && die "the audit log contains a credential"
  grep -q "$value" "$AGENT_ERR" && die "the agent's terminal shows a credential"
done

# ============================== L: with nothing holding the vault, a bridge set up for a home lowers its ceilings
# keypaste setup registers a bridge for a home other than the default with that home's log and ceilings, since the
# AI tool may not pass KEYPASTE_HOME on. With the owner stopped, such a bridge lowers the ceilings the owner reads
# for the agent's own request for less; one given the log alone cannot tell which apply, lowers nothing and says
# to run setup again. Last, so its lines are not among those counted above.
"$CLI" lock --vault "$VAULT" --approver "$PIPE" >"$WORK/lock.txt" 2>&1 || die "keypaste lock did not stop keypaste agent: $(cat "$WORK/lock.txt")"
for _ in $(seq 1 50); do
  grep -q 'the agent has stopped' "$AGENT_ERR" && break
  sleep 0.2
done
grep -q 'the agent has stopped' "$AGENT_ERR" || die "keypaste agent did not stop on keypaste lock"
wait "$AGENT_PID" 2>/dev/null || true
AGENT_PID=""
rm -f "$CLIENTS"

OUT="$WORK/unpinned.json"
change 31 "$OUT" '{"run":"off","reason":"ci is done running things"}' --no-home
says 31 "$OUT" 'run `keypaste setup` again' || die "a bridge that cannot tell which ceilings apply did not say to run setup again"
[ -e "$CLIENTS" ] && die "a bridge given no --clients lowered ceilings it cannot tell the owner reads"

OUT="$WORK/pinned.json"
change 32 "$OUT" '{"run":"off","reason":"ci is done running things"}' --no-home --clients "$(native "$CLIENTS")"
says 32 "$OUT" 'applied at once on this computer' || die "a bridge pinned to its home's ceilings did not apply the agent's narrowing"

OUT="$WORK/pinned-settings.json"
policy --json >"$OUT" 2>/dev/null || die "keypaste mcp policy --json failed with nothing holding the vault"
jq -e --arg l "$LABEL" '[.[] | select(.kind == "ceiling" and .label == $l and .run == "off")] | length == 1' <"$OUT" >/dev/null \
  || die "the pinned bridge did not lower the ceilings the owner reads"

echo "ok: a mode the person confirmed released one project's credential with no prompt, and the same agent still asked about everything outside it"
