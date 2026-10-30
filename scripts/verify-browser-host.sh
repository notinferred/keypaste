#!/usr/bin/env bash
# A browser pairs with the vault's owner only while the person armed pairing (8.1d), across real processes:
# tests/Keypaste.FakeBrowser starts the shipped keypaste as each browser starts KeePassXC-Browser's native messaging host
# and speaks the extension's protocol to it, and tests/Keypaste.AppDriver holds the vault as launch composes the app, its
# pairing confirmation and notice drawn on a headless display and clicked through its hit-testing.
#
# Unarmed, a request to pair is refused with error 6 before anybody is asked, the app shows its notice naming the build,
# and the owner records it; the database hash the relay answers is keypaste's own. Armed with Settings' Pair a browser,
# the confirmation names the build the browser named, Pair saves the pairing, and the extension's check of it under the
# same identification key succeeds, the answers carrying the hash. The arming answers one confirmation: the next request
# is refused unasked again. A check of that pairing from another build, or with another key, is refused with error 8 and
# recorded; Deny pairs nothing; Remove ends the pairing, and its next check is refused. No line, and nothing the app
# prints, holds the identification key.
#
# NEGATIVE CONTROL: this fails if a request to pair is confirmed or saved while unarmed, if the notice is not shown, if a
# pairing is saved without Pair, if a check passes for another build, another key or a removed pairing, if a refusal is not
# recorded, or if the key reaches the log. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-browser-host-master-pw'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='HOLD_OUT BROWSER_OUT'
require jq

CLI="$(keypaste_bin)"
DRV="$(app_driver)"
FAKE="$(resolve KEYPASTE_FAKE_BROWSER artifacts/bin/Keypaste.FakeBrowser/release/Keypaste.FakeBrowser)"

WORK="$(mktemp -d)"
mkdir -p "$WORK/home"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
VAULT="$(native "$WORK/vault.kdbx")"
# The relay finds the vault as `keypaste mcp` with no arguments does; the browser passes the host its own environment.
KEYPASTE_VAULT="$VAULT"
export KEYPASTE_VAULT
readonly LOG="$WORK/home/audit.jsonl"
readonly HOLD_OUT="$WORK/hold.txt"
BROWSER_OUT="$WORK/browser.txt"

# The extension IDs a browser line names its build by.
readonly CHROME='oboonakemofpalcgghocfoadofidjkkk'
readonly EDGE='pdffhmdngciaglkoonimfcmckehcpafo'
readonly CHROME_BUILD='KeePassXC-Browser from the Chrome Web Store (oboonakemofpalcgghocfoadofidjkkk)'
readonly NOTICE='A browser asked to pair. To pair it, press Pair a browser.'

# The extension's identification key, which it presents again to prove the pairing, and another one.
ID_KEY="$(head -c 32 /dev/urandom | base64 | tr -d '\n')"
OTHER_KEY="$(head -c 32 /dev/urandom | base64 | tr -d '\n')"

HOLD_PID=""
# A fixed descriptor, as macOS's bash 3.2 needs (D-0398): 7 is the held app's input.
exec 7>/dev/null
cleanup() {
  exec 7>&- 2>/dev/null || true
  if [ -n "$HOLD_PID" ]; then kill_process "$HOLD_PID"; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

RUNS=0

# Names the file the next run of the browser answers into, here rather than in a run that may go to the background.
next_run() {
  RUNS=$((RUNS + 1))
  BROWSER_OUT="$WORK/browser-$RUNS.txt"
}

# Runs the fake browser as browser $1 with the rest as its actions, its answers one JSON line each in $BROWSER_OUT, the
# key exchange's first; the identification key is $ID_KEY unless KEY names another.
browser() {
  local as="$1"
  shift
  "$FAKE" "$CLI" "$as" --id-key "${KEY:-$ID_KEY}" --within 60 "$@" >"$BROWSER_OUT" 2>&1 || die "the browser's host exited with an error"
}

# The answer to the browser's action $1, counting from 1, from the last run.
answer() { sed -n "$(($1 + 1))p" "$BROWSER_OUT"; }

no_key_anywhere() {
  local key
  for key in "$ID_KEY" "$OTHER_KEY"; do
    grep -qF "$key" "$LOG" && die "an identification key reached the audit log"
    grep -qF "$key" "$HOLD_OUT" && die "an identification key reached what the app printed"
  done
  return 0
}

# ---------------------------------------------------------------- a vault, held as the app holds it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"

exec 7>&-
exec 7> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" exec "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"
SESSION="$(session_of)"

# ------------------------------------------------------------- unarmed: refused unasked, told and recorded
step "a request to pair while pairing is not on is refused unasked and noticed"
next_run
browser chrome get-databasehash associate
HASH="$(answer 1 | jq -er '.hash')" || die "the relay did not answer the database hash: $(answer 1)"
[[ "$HASH" =~ ^[0-9a-f]{64}$ ]] || die "the database hash is not a SHA-256 in lowercase hex: $HASH"
answer 2 | jq -e '.errorCode == "6"' >/dev/null || die "an unarmed request to pair was not refused with error 6: $(answer 2)"
wait_for "^pair-notice identity=$CHROME_BUILD text=$NOTICE" "$HOLD_OUT"
grep -q '^pair-prompt' "$HOLD_OUT" && die "an unarmed request to pair put a confirmation in front of the person"
tail -n 1 "$LOG" | jq -e --arg c "$CHROME" --arg s "$SESSION" \
  '.tool == "browser" and .args.field == "pair" and .decision == "denied" and .method == "not-armed" and .client.name == $c and .session == $s' >/dev/null \
  || die "the unarmed request was not recorded as refused while not armed: $(tail -n 1 "$LOG")"
echo pairings >&7
wait_for '^pairings none' "$HOLD_OUT"
echo "    refused with error 6, noticed and recorded; nothing paired"

# ------------------------------------------------------------- armed: confirmed naming the build, then proven
step "armed, a pairing is confirmed naming the build the browser named, saved and then proven"
echo pair >&7
wait_for '^pairing armed' "$HOLD_OUT"
next_run
browser chrome associate test-associate &
PAIRING_RUN=$!
wait_for '^pair-prompt identity=' "$HOLD_OUT"
PROMPT_LINE="$(grep '^pair-prompt identity=' "$HOLD_OUT" | tail -1 | tr -d '\r')"
case "$PROMPT_LINE" in
  "pair-prompt identity=$CHROME_BUILD name="?*) ;;
  *) die "the confirmation does not name the build the browser named: $PROMPT_LINE" ;;
esac
NAME="${PROMPT_LINE#*name=}"
echo allow >&7
wait "$PAIRING_RUN" || die "the pairing browser's host exited with an error"
wait_for '^pair-prompt withdrawn' "$HOLD_OUT"
answer 1 | jq -e --arg n "$NAME" --arg h "$HASH" '.success == "true" and .id == $n and .hash == $h' >/dev/null \
  || die "Pair did not pair the browser under its name with keypaste's hash: $(answer 1)"
answer 2 | jq -e --arg n "$NAME" --arg h "$HASH" '.success == "true" and .id == $n and .hash == $h' >/dev/null \
  || die "the pairing just made did not pass its check: $(answer 2)"
tail -n 1 "$LOG" | jq -e --arg c "$CHROME" --arg n "$NAME" \
  '.tool == "browser" and .args.field == "pair" and .decision == "granted" and .method == "prompt" and .client.name == $c and .client.label == $n' >/dev/null \
  || die "the pairing was not recorded as granted by the person: $(tail -n 1 "$LOG")"
echo pairings >&7
wait_for "^pairing identity=$CHROME_BUILD name=$NAME" "$HOLD_OUT"
no_key_anywhere
echo "    paired as $NAME and proven; the key is in no line"

# ------------------------------------------------------------- one confirmation per arming
step "the arming answered one confirmation, so the next request to pair is refused unasked"
next_run
browser chrome associate
answer 1 | jq -e '.errorCode == "6"' >/dev/null || die "a second request to pair was not refused: $(answer 1)"
[ "$(grep -c '^pair-prompt identity=' "$HOLD_OUT")" = 1 ] || die "a second request to pair was put to the person"
tail -n 1 "$LOG" | jq -e '.tool == "browser" and .method == "not-armed"' >/dev/null \
  || die "the second request was not recorded as refused while not armed: $(tail -n 1 "$LOG")"
echo "    refused with error 6"

# ------------------------------------------------------------- another build, another key
step "a check from another build, or with another key, is refused with error 8 and recorded"
next_run
browser edge "test-associate=$NAME"
answer 1 | jq -e '.errorCode == "8"' >/dev/null || die "another build's check of the pairing passed: $(answer 1)"
tail -n 1 "$LOG" | jq -e --arg c "$EDGE" --arg n "$NAME" \
  '.tool == "browser" and .args.field == "check" and .decision == "denied" and .method == "not-paired" and .client.name == $c and .client.label == $n' >/dev/null \
  || die "another build's check was not recorded as refused: $(tail -n 1 "$LOG")"
next_run
KEY="$OTHER_KEY"
browser chrome "test-associate=$NAME"
KEY=
answer 1 | jq -e '.errorCode == "8"' >/dev/null || die "a check with another key passed: $(answer 1)"
next_run
browser chrome "test-associate=$NAME"
answer 1 | jq -e --arg n "$NAME" '.success == "true" and .id == $n' >/dev/null || die "the pairing's own check no longer passes: $(answer 1)"
no_key_anywhere
echo "    refused for the Edge build and for another key; the Chrome build's own key still passes"

# ------------------------------------------------------------- Deny
step "Deny pairs nothing"
echo pair >&7
wait_for '^pairing armed' "$HOLD_OUT" 2
next_run
browser firefox associate &
DENIED_RUN=$!
wait_for '^pair-prompt identity=' "$HOLD_OUT" 2
grep '^pair-prompt identity=' "$HOLD_OUT" | tail -1 | grep -qF 'identity=KeePassXC-Browser from Firefox Add-ons (keepassxc-browser@keepassxc.org)' \
  || die "the confirmation does not name the Firefox build"
echo deny >&7
wait "$DENIED_RUN" || die "the denied browser's host exited with an error"
answer 1 | jq -e '.errorCode == "6"' >/dev/null || die "a denied pairing was not refused with error 6: $(answer 1)"
tail -n 1 "$LOG" | jq -e '.tool == "browser" and .args.field == "pair" and .decision == "denied" and .method == "prompt"' >/dev/null \
  || die "the denial was not recorded as the person's: $(tail -n 1 "$LOG")"
echo pairings >&7
wait_for '^pairing identity=' "$HOLD_OUT" 2
grep -q '^pairing identity=KeePassXC-Browser from Firefox' "$HOLD_OUT" && die "a denied pairing was saved"
echo "    refused and recorded; nothing paired"

# ------------------------------------------------------------- Remove
step "Remove ends the pairing, and its next check is refused"
echo "unpair $NAME" >&7
wait_for "^unpaired $NAME" "$HOLD_OUT"
tail -n 1 "$LOG" | jq -e --arg n "$NAME" '.tool == "browser" and .args.field == "remove" and .method == "removed" and .client.label == $n' >/dev/null \
  || die "the removal was not recorded: $(tail -n 1 "$LOG")"
next_run
browser chrome "test-associate=$NAME"
answer 1 | jq -e '.errorCode == "8"' >/dev/null || die "a removed pairing still passes its check: $(answer 1)"
no_key_anywhere
echo "    removed, recorded, and refused from its next check"

exec 7>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""
echo
echo "browser host gate passed"
