#!/usr/bin/env bash
# Every CLI verb that saves takes the vault's claim (N.10, V-N.10), across real processes: the shipped
# keypaste and keypaste mcp, tests/Keypaste.AppDriver holding the vault as launch composes the app, and
# `keypaste agent` answering in its terminal.
#
# While the app, then keypaste agent, holds the vault, add, rm, access, env set, env rm, env pull and
# import are each refused naming the holder's process and the next step. With stdin closed each says so
# rather than that no password was given, which is how a verb that reached its prompt ends; with the
# right answers piped the vault's bytes and backups are unchanged. The holder then still answers an
# agent's request instead of refusing it as vault-changed. Once `keypaste lock` ends the hold, each verb
# saves.
#
# NEGATIVE CONTROL: every refused attempt is given the passwords it would need, so a verb that saved
# behind the holder changes the vault's bytes and fails here, and the holder's refusal of the agent as
# vault-changed fails here too. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-held-saves-master-pw'
readonly SOURCE_PW='ci-held-saves-source-pw'
readonly V1='SENTINEL-HELD-SAVES-ONE-3f91'
readonly ENTRY='env/ci/DEPLOY_KEY'
readonly VERBS=(add rm access env-set env-rm env-pull import)

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='HOLD_OUT AGENT_ERR OUT ERR VERB_ERR'
require jq

CLI="$(keypaste_bin)"
DRV="$(app_driver)"

WORK="$(mktemp -d)"
mkdir -p "$WORK/home"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
VAULT="$(native "$WORK/vault.kdbx")"
SOURCE="$(native "$WORK/source.kdbx")"
DOTENV="$(native "$WORK/pull.env")"
AUDIT="$(native "$WORK/audit.jsonl")"
readonly HOLD_OUT="$WORK/hold.txt"
readonly AGENT_ERR="$WORK/agent-stderr.txt"
readonly OUT="$WORK/mcp-stdout.txt"
readonly ERR="$WORK/mcp-stderr.txt"

HOLD_PID=""
AGENT_PID=""
# Fixed descriptors, as macOS's bash 3.2 needs (D-0398): 7 is the held app's input, 8 the open bridge's and 9 keypaste agent's.
exec 7>/dev/null
exec 9>/dev/null
exec 8>/dev/null
cleanup() {
  exec 8>&- 2>/dev/null || true
  exec 7>&- 2>/dev/null || true
  exec 9>&- 2>/dev/null || true
  if [ -n "$HOLD_PID" ]; then kill_process "$HOLD_PID"; fi
  if [ -n "$AGENT_PID" ]; then kill_process "$AGENT_PID"; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

digest() { sha256sum "$WORK/vault.kdbx" | cut -d' ' -f1; }
backups() { find "$WORK/vault.kdbx.backups" -type f 2>/dev/null | wc -l | tr -d ' '; }

# The verb's arguments, one per line.
arguments() {
  case "$1" in
    add) printf '%s\n' add ci/NEW_ENTRY ;;
    rm) printf '%s\n' rm Work/github --yes ;;
    access) printf '%s\n' access --password ;;
    env-set) printf '%s\n' env set ci OTHER_KEY=held ;;
    env-rm) printf '%s\n' env rm ci OTHER_KEY --yes ;;
    env-pull) printf '%s\n' env pull ci "$DOTENV" --yes --keep ;;
    import) printf '%s\n' import "$SOURCE" ;;
  esac
}

# What the verb would be asked, one answer per line: import's source first, access's new password twice.
answers() {
  case "$1" in
    import) printf '%s\n' "$SOURCE_PW" "$MASTER" ;;
    access) printf '%s\n' "$MASTER" "$MASTER-next" "$MASTER-next" ;;
    add) printf '%s\n' "$MASTER" 'held-add-value' ;;
    *) printf '%s\n' "$MASTER" ;;
  esac
}

# Runs verb $1 with stdin from $2 (a file, or /dev/null) and sets VERB_EXIT and VERB_ERR.
attempt() {
  local verb="$1" input="$2" tag="$3" arg args=()
  while IFS= read -r arg; do args+=("$arg"); done < <(arguments "$verb")
  VERB_ERR="$WORK/$verb-$tag-stderr.txt"
  set +e
  "$CLI" "${args[@]}" --vault "$VAULT" <"$input" >/dev/null 2>"$VERB_ERR" 7>&- 9>&- 8>&-
  VERB_EXIT=$?
  set -e
}

# Each verb is refused, naming process $1 and the next step $2, and changes nothing.
refuse_all() {
  local pid="$1" step="$2" holder="$3" verb before kept
  before="$(digest)"
  kept="$(backups)"
  for verb in "${VERBS[@]}"; do
    attempt "$verb" /dev/null closed
    [ "$VERB_EXIT" -ne 0 ] || die "$verb under $holder: exited 0 with stdin closed"
    grep -q "(process $pid)" "$VERB_ERR" || die "$verb under $holder: the refusal does not name process $pid"
    grep -qF "$step" "$VERB_ERR" || die "$verb under $holder: the refusal does not say '$step'"
    grep -qE 'no master password given|no password given' "$VERB_ERR" && die "$verb under $holder: it reached a password prompt"

    answers "$verb" >"$WORK/answers.txt"
    attempt "$verb" "$WORK/answers.txt" answered
    [ "$VERB_EXIT" -ne 0 ] || die "$verb under $holder: exited 0 with its passwords given"
    grep -q "(process $pid)" "$VERB_ERR" || die "$verb under $holder: with its passwords given, the refusal does not name process $pid"
    [ "$(digest)" = "$before" ] || die "$verb under $holder: the vault's bytes changed"
    [ "$(backups)" = "$kept" ] || die "$verb under $holder: a backup was taken"
  done
}

# Opens a bridge labelled $1 and sets its reply files.
bridge() {
  exec 8>&-
  exec 8> >(exec 7>&- 9>&-; exec "$CLI" mcp --vault "$VAULT" --expose 'env/**' --audit-log "$AUDIT" --client-label "$1" >"$OUT" 2>"$ERR")
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-held-saves","version":"1.0.0"}}}' >&8
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}' >&8
}

# Sends request $1 on the open bridge.
request() {
  printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":$1,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY\",\"field\":\"password\",\"reason\":\"ci held-saves probe\",\"ttl_seconds\":60}}}" >&8
}

# The reply to $1 released the value, and its audit line is a grant, not a vault-changed refusal.
answered() {
  local id="$1" what="$2"
  for _ in $(seq 1 150); do
    jq -e --argjson id "$id" 'select(.id == $id)' <"$OUT" >/dev/null 2>&1 && break
    sleep 0.2
  done
  jq -e --argjson id "$id" --arg v "$V1" 'select(.id == $id) | .result.isError == false and (tostring | contains($v))' <"$OUT" >/dev/null \
    || die "$what: the holder did not release the value after the refused saves"
  tail -n 1 "$AUDIT" | jq -e '.tool == "request_credential" and .decision == "granted"' >/dev/null \
    || die "$what: the release was not audited as granted"
  grep -q 'vault-changed' "$AUDIT" && die "$what: a request was refused as vault-changed"
  return 0
}

# ---------------------------------------------------------------- a vault, a source and a .env
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n%s\n' "$MASTER" "$V1" | "$CLI" add "$ENTRY" --vault "$VAULT" >/dev/null || die "could not store the credential"
printf '%s\n%s\n' "$MASTER" 'gh-held' | "$CLI" add Work/github --vault "$VAULT" >/dev/null || die "could not store an entry to remove"
printf '%s\n%s\n' "$SOURCE_PW" "$SOURCE_PW" | "$CLI" init "$SOURCE" >/dev/null || die "could not create the import source"
printf '%s\n%s\n' "$SOURCE_PW" 'old-held' | "$CLI" add Imported/old --vault "$SOURCE" >/dev/null || die "could not fill the import source"
printf 'PULLED_KEY=held\n' >"$DOTENV"

# ------------------------------------------------------------------------------- the app holds it
exec 7>&-
exec 7> >(exec 9>&- 8>&-; KEYPASTE_DRIVER_PASSWORD="$MASTER" exec "$DRV" hold "$VAULT" --approving-prompt >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/.* as process ([0-9]+).*/\1/')"

refuse_all "$HOLD_PID" 'Make the change there, or run `keypaste lock` and try again.' "the app"
bridge ci-held-app
request 2
answered 2 "the app"

"$CLI" lock --vault "$VAULT" >"$WORK/lock.txt" 2>&1 7>&- 9>&- 8>&-   || die "keypaste lock did not lock the app: $(cat "$WORK/lock.txt")"
wait_for '^locked' "$HOLD_OUT"
exec 7>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

# ----------------------------------------------------------------------------- keypaste agent holds it
exec 9>&-
exec 9> >(exec 7>&- 8>&-; exec "$CLI" agent --vault "$VAULT" >/dev/null 2>"$AGENT_ERR")
AGENT_SHELL=$!
if [ -r "/proc/$AGENT_SHELL/winpid" ]; then AGENT_PID="$(cat "/proc/$AGENT_SHELL/winpid")"; else AGENT_PID="$AGENT_SHELL"; fi
printf '%s\n' "$MASTER" >&9
wait_for 'listening on' "$AGENT_ERR"

refuse_all "$AGENT_PID" 'Run `keypaste lock` and try again.' "keypaste agent"
bridge ci-held-agent
request 3
wait_for 'an agent is asking for a credential' "$AGENT_ERR"
printf 'o\n' >&9
answered 3 "keypaste agent"

"$CLI" lock --vault "$VAULT" >"$WORK/lock.txt" 2>&1 7>&- 9>&- 8>&-   || die "keypaste lock did not stop keypaste agent: $(cat "$WORK/lock.txt")"
wait_for 'the agent has stopped' "$AGENT_ERR"
exec 9>&-
AGENT_PID=""
exec 8>&-

# ------------------------------------------------------------------------ nothing holds it: each saves
# env rm removes the key env set writes, and access goes last, since it changes the master password the others are given.
for verb in add rm env-set env-rm env-pull import access; do
  before="$(digest)"
  answers "$verb" >"$WORK/answers.txt"
  attempt "$verb" "$WORK/answers.txt" free
  [ "$VERB_EXIT" -eq 0 ] || die "$verb with nothing holding the vault: exited $VERB_EXIT"
  [ "$(digest)" != "$before" ] || die "$verb with nothing holding the vault: nothing was saved"
done

echo "ok: add, rm, access, env set, env rm, env pull and import were each refused while the app and then"
echo "    keypaste agent held the vault, naming the holder's process and the next step, before any password"
echo "    and with the vault's bytes and backups unchanged; each holder then released to an agent, and once"
echo "    keypaste lock ended the hold each verb saved"
