#!/usr/bin/env bash
# Proves docs/demo.md and README.md still describe the binaries as they actually are.
#
# Everything else in the suite checks behaviour. This checks a document against that behaviour, and
# nothing in-process can: what goes stale is a Markdown transcript, which no unit test reads, and
# the approval dialog it reproduces exists only on the stderr of a second process that a human is
# supposed to be looking at. docs/demo.md is the marketing (docs/PRODUCT.md law 5.1), and the first thing a
# stranger does with a demo page is type it in - so a page that has quietly drifted costs more than
# no page at all.
#
# README.md is held to the same standard for the parts it reproduces. The approval dialog is the
# first thing on it - it stands in for the demo GIF and it is the single block a stranger judges the
# project by - and an ungated copy of a transcript is a transcript that drifts. It carries the
# dialog and the log table's header row, so those are what it is checked against; docs/demo.md
# carries the whole demo and is checked against all of it.
#
# It stands in for Claude with the same scripted JSON-RPC client verify-approval-e2e.sh uses. That
# is a transport, not an agent: it decides nothing and chooses no wording, it sends exactly the
# calls the page says Claude sends, so that everything keypaste renders is fully determined. What
# is proved here is keypaste's half. Claude's half is proved by a person running the demo, and
# DECISIONS.md D-0034 records why it is deliberately not automated.
#
# NEGATIVE CONTROL: this script fails if scripts/demo/deploy.sh exits zero with no STRIPE_KEY set,
# if it exits non-zero with one, if any of the key beyond its masked prefix and suffix reaches its
# output, if the approval dialog the shipped agent draws differs by one character from the block in
# EITHER page, if the `Approve? [y/N]` line is missing from either, if either stops carrying the
# dialog at all, if an approved request does not return the credential, if a refused one does, if
# the credential reaches the audit log, if the rendered `keypaste log` table header differs from
# either page's, if either committed fixture is missing or not executable, or if an option the page
# tells you to type is absent from the shipped usage text. Removing any one of those leaves a script that passes while the page it defends has
# gone wrong. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-demo-master-pw'
readonly SECRET='sk_test_EXAMPLE_ONLY_not_a_real_key_0000'
readonly MASKED='sk_test_...0000'
readonly ENTRY='env/demo/STRIPE_KEY'
readonly REASON='deploy the billing service to staging'
readonly LABEL='claude-code'

readonly DOC='docs/demo.md'
readonly FIXTURE='scripts/demo/deploy.sh'

# Every page that reproduces the approval dialog and the log table, and is therefore held to what
# the binaries print. Adding a page here costs nothing; leaving one out is how a transcript rots.
# site/public/index.html is keypaste.com, and it is in this list for the same reason the README is:
# the dialog is the first thing on it, and a marketing page is exactly where a stale transcript
# survives longest, and docs/keepass-and-agents.md is here for the same reason again. The checks
# are grep and diff over a text file, so the markup is no obstacle.
#
# launch.md holds the block every launch post pastes, which is the copy most likely to be read by
# someone who has never run the binary. Note the dialog extraction below is a sed range, so a page
# in this list must carry exactly one copy of the block: a second, shortened one would be extracted
# too and the diff would fail. That is why launch.md keeps one copy and its posts reference it.
readonly TRANSCRIPT_PAGES='docs/demo.md README.md site/public/index.html docs/keepass-and-agents.md launch.md'

die() {
  echo "::error::$*" >&2
  for f in "${AGENT_ERR:-}" "${OUT:-}" "${ERR:-}" "${DIFF:-}"; do
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
readonly PIPE="keypaste-demo-$$-$(date +%s)"

AGENT_PID=""
cleanup() {
  if [ -n "$AGENT_PID" ]; then
    kill "$AGENT_PID" 2>/dev/null || true
    wait "$AGENT_PID" 2>/dev/null || true
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT

# Asserts a line appears in the page exactly as the binaries produced it. `grep -F -x` on a
# stripped line: the page indents nothing inside its output blocks, so an exact match is the
# right test, and a substring match would let a truncated transcript pass.
in_doc() {
  local line="$1" what="$2" file="${3:-$DOC}"
  grep -qFx -- "$line" "$file" || die "$what is not in $file as the binaries print it: |$line|"
}

# ------------------------------------------------------------ 0. the committed files are usable
# The -x check is not decoration: scripts/verify-log-chain.sh shipped without its mode bit and was
# skipped on two operating systems before anybody noticed.
for page in $TRANSCRIPT_PAGES; do
  [ -f "$page" ] || die "$page is missing"
done
[ -x "$FIXTURE" ] || die "$FIXTURE is missing or not executable (git update-index --chmod=+x)"

# --------------------------------------------------------------- A. the fixture, and its masking
OUT="$WORK/deploy-refuse.txt"
set +e
( unset STRIPE_KEY; "$FIXTURE" ) >"$OUT" 2>&1
refused=$?
set -e

[ "$refused" -eq 1 ] || die "the deploy fixture exited $refused with no STRIPE_KEY set; it must refuse"

while IFS= read -r line; do
  [ -n "$line" ] || continue
  in_doc "$line" "the fixture's refusal"
done <"$OUT"

OUT="$WORK/deploy-ok.txt"
set +e
STRIPE_KEY="$SECRET" "$FIXTURE" >"$OUT" 2>&1
deployed=$?
set -e

[ "$deployed" -eq 0 ] || die "the deploy fixture exited $deployed with STRIPE_KEY set; it must succeed"
grep -qF "$SECRET" "$OUT" && die "the deploy fixture printed the whole credential"
grep -qF "$MASKED" "$OUT" || die "the deploy fixture did not print the masked form the page shows"

while IFS= read -r line; do
  [ -n "$line" ] || continue
  in_doc "$line" "the fixture's output"
done <"$OUT"

# A short key is one where a prefix and a suffix would be most of it, so none of it may be shown.
OUT="$WORK/deploy-short.txt"
STRIPE_KEY='abc123' "$FIXTURE" >"$OUT" 2>&1
grep -q 'abc123' "$OUT" && die "the deploy fixture showed part of a key too short to mask"

# ------------------------------------------------------------------- a vault with the sentinel in
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null \
  || die "could not create the demo vault"

printf '%s\n%s\n' "$MASTER" "$SECRET" | "$CLI" env set demo STRIPE_KEY --vault "$VAULT" >/dev/null \
  || die "could not store the demo credential"

# --------------------------------------------------------------------------- start the approver
# One answer per request on stdin: y, then n. ConsoleSecretPrompt reads redirected input a byte at
# a time precisely so this works.
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
  : >"$out"
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"claude-code","version":"2.0.0"}}}'

    # Wait for the initialize *response* before saying anything else, which is what a real client
    # does and what the protocol requires. Writing all three messages at once races: the client's
    # name arrives with initialize and the server reads it off the handshake, so a tools/call that
    # overtakes it is answered for "an unnamed client" and the dialog no longer matches the page.
    # That is not hypothetical - it was observed failing on macOS in release run 30319209914 while
    # the same commit passed on the other three platforms.
    for _ in $(seq 1 100); do
      grep -q '"id":1' "$out" 2>/dev/null && break
      sleep 0.1
    done

    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"$REASON\",\"ttl_seconds\":900}}}"
    sleep 8
  } | "$MCP" --vault "$VAULT" --audit-log "$AUDIT" --approver "$PIPE" --client-label "$LABEL" \
        >"$out" 2>"$err" || die "keypaste-mcp exited non-zero"

  grep -q '"id":1' "$out" || die "keypaste-mcp never answered initialize"
}

# ----------------------------------------------------------- B. the dialog, character for character
OUT="$WORK/approve-stdout.txt"
ERR="$WORK/approve-stderr.txt"
ask 2 "$OUT" "$ERR"

# The block both sides must agree on, from "an agent is asking" to the disclaimer that closes it.
# Carriage returns are stripped: the agent writes native line endings and the document is stored
# with LF, and a diff over that difference would be noise rather than drift.
block() {
  sed -n '/^keypaste: an agent is asking for a credential\.$/,/^  That sentence was written by the agent/p' "$1" \
    | tr -d '\r'
}

block "$AGENT_ERR" >"$WORK/dialog-actual.txt"
[ -s "$WORK/dialog-actual.txt" ] || die "the approver drew no dialog"

DIFF="$WORK/dialog.diff"
for page in $TRANSCRIPT_PAGES; do
  block "$page" >"$WORK/dialog-doc.txt"
  [ -s "$WORK/dialog-doc.txt" ] || die "$page has no approval dialog block"
  if ! diff -u "$WORK/dialog-doc.txt" "$WORK/dialog-actual.txt" >"$DIFF" 2>&1; then
    die "the approval dialog in $page is not what the shipped agent draws"
  fi
done

# The rule is drawn around the dialog, and the page shows it. Its BYTES are deliberately not
# compared: .NET encodes stderr for the console's code page, so on Windows the U+2500 arrives as a
# single 0xC4 byte in CP850 rather than the three UTF-8 bytes this repository stores. It renders
# correctly on the console it was written for - only a byte comparison cannot survive the trip,
# which is also why the diffed block above stops short of it and is pure ASCII throughout. So the
# drawn rule is checked for shape, and the documented one for exact text.
# -a because that code page byte is not valid UTF-8, so grep would otherwise call this stream
# binary and silently drop the context line this check depends on.
rule="$(tr -d '\r' <"$AGENT_ERR" | grep -a -B1 -m1 'an agent is asking for a credential' | head -1)"
[ -n "$rule" ] || die "the approver drew no rule above the dialog"
[ "${#rule}" -ge 10 ] || die "the rule above the dialog is too short to be one: |$rule|"
case "$rule" in
  *[[:space:][:alnum:]]*) die "the line above the dialog is not a rule: |$rule|" ;;
esac

# Built as a literal and matched with -F -x rather than written as `^─{60}$`. A repetition count
# over a multi-byte character means "sixty of this character" only in a UTF-8 locale; under LC_ALL=C
# it means sixty of its last byte, and the runners do not all agree about which they are.
rule_literal="$(printf '─%.0s' $(seq 1 60))"
for page in $TRANSCRIPT_PAGES; do
  grep -qFx -- "$rule_literal" "$page" \
    || die "$page no longer shows the 60-character rule around the dialog"
done

# The one line this harness cannot observe, asserted against the page instead of faked.
# ConsoleSecretPrompt.ReadLine writes its prompt only when stdin is a terminal, and CI has none -
# so `Approve? [y/N] ` and `Master password: ` never reach stderr here. They are still the lines a
# human's screen ends on, which is why the page must carry them and why this check is not dropped.
for page in $TRANSCRIPT_PAGES; do
  grep -qF 'Approve? [y/N]' "$page" || die "$page does not show the question a person actually answers"
done
grep -qF 'Master password:' "$DOC" || die "$DOC does not show the master password prompt"

# --------------------------------------------------------------- C. what came back, and what was logged
jq -e 'select(.id == 2) | .result.isError == false' <"$OUT" >/dev/null \
  || die "an approved request was reported as an error"

grep -q "$SECRET" "$OUT" || die "an approved request did not return the credential"

jq -e --arg s "$SECRET" 'select(.id == 2) | .result.structuredContent.value == $s' <"$OUT" >/dev/null \
  || die "the structured result does not carry the released value"

# The page prints `for      300 seconds` against a request for 900. That is the --max-ttl clamp, and
# if it stopped applying the page would be describing a grant four times longer than the one issued.
jq -e 'select(.id == 2) | .result.structuredContent.expires_in_seconds == 300' <"$OUT" >/dev/null \
  || die "the released grant was not clamped to the approver's --max-ttl, which the page states as 300"

[ -f "$AUDIT" ] || die "no audit log was written"
grep -q '"decision":"granted"' "$AUDIT" || die "the approval was not recorded as granted"
grep -q '"method":"prompt"'    "$AUDIT" || die "the approval was not recorded as coming from a person"
grep -q "$SECRET" "$AUDIT" && die "the audit log contains the released credential"

grep -qF "$REASON" "$AGENT_ERR" || die "the agent's stated reason was not shown to the human"

# ------------------------------------------------------------------------------- D. the paired no
# A second bridge, so a second connection: the first one's grant belongs to a process that has gone,
# which is what makes this a fresh question rather than a cache hit. Without this the checks above
# would pass on an approver whose only behaviour is yes.
OUT="$WORK/deny-stdout.txt"
ERR="$WORK/deny-stderr.txt"
ask 3 "$OUT" "$ERR"

jq -e 'select(.id == 3) | .result.isError == true' <"$OUT" >/dev/null \
  || die "a refused request was not reported as an error"

grep -q "$SECRET" "$OUT" && die "a refused request returned the credential"
grep -q '"decision":"denied"' "$AUDIT" || die "the refusal was not recorded as denied"

in_doc 'keypaste: denied. Nothing was released.' "the refusal line"
grep -qF 'keypaste: DENIED. A person read this request and said no.' "$DOC" \
  || die "$DOC does not show what the agent is told when a person says no"

# ------------------------------------------------------------------------------ E. the payoff table
OUT="$WORK/log.txt"
"$CLI" log --audit-log "$AUDIT" >"$OUT" 2>&1 || die "keypaste log exited non-zero over its own log"

header="$(grep -m1 'time (UTC)' "$OUT" | tr -d '\r')"
[ -n "$header" ] || die "keypaste log printed no table header"
for page in $TRANSCRIPT_PAGES; do
  in_doc "$header" "the log table's header row" "$page"
done

# The gutter is what makes this worth asserting: AuditText pads a one-character column onto every
# row, so a transcript pasted without it is wrong in a way that is invisible to the eye.
row="$(grep -m1 "$ENTRY" "$OUT" | tr -d '\r')"
[ -n "$row" ] || die "keypaste log did not show the released entry"
case "$row" in
  '  '*) : ;;
  *) die "keypaste log's rows no longer start with the two-space gutter the page shows" ;;
esac

grep -q "$SECRET" "$OUT" && die "keypaste log printed the credential"

"$CLI" log verify --audit-log "$AUDIT" >"$WORK/verify.txt" 2>&1 \
  || die "keypaste log verify did not find its own log intact"

# ------------------------------------------------------- F. the options the page tells you to type
"$CLI" agent --help >"$WORK/agent-help.txt" 2>&1 || true
"$CLI" log --help   >"$WORK/log-help.txt"   2>&1 || true
"$MCP" --help       >"$WORK/mcp-help.txt"   2>&1 || true

for opt in --vault --approver --approval-timeout --max-ttl; do
  grep -q -- "$opt" "$WORK/agent-help.txt" || die "keypaste agent no longer offers $opt, which $DOC tells you to type"
done
for opt in --since --denied --client; do
  grep -q -- "$opt" "$WORK/log-help.txt" || die "keypaste log no longer offers $opt, which $DOC mentions"
done
for opt in --vault --client-label --expose; do
  grep -q -- "$opt" "$WORK/mcp-help.txt" || die "keypaste-mcp no longer offers $opt, which $DOC tells you to configure"
done

kill "$AGENT_PID" 2>/dev/null || true
wait "$AGENT_PID" 2>/dev/null || true
AGENT_PID=""

echo "ok: the demo's fixture behaves as docs/demo.md prints it, the approval dialog the shipped"
echo "ok: agent draws matches the page character for character, an approved request released the"
echo "ok: credential and a refused one released nothing. Claude's half of the demo is not checked"
echo "ok: here and cannot be: what a model chooses to do is not a thing a gate can hold."
