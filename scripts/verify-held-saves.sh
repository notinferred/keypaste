#!/usr/bin/env bash
# A CLI verb that saves while the app or keypaste approve holds the vault (N.10, N.11), across real
# processes: the shipped keypaste and keypaste mcp, tests/Keypaste.AppDriver holding the vault as launch
# composes the app, and `keypaste approve` answering in its terminal.
#
# While each holds the vault, access and import are refused naming the holder's process and the next
# step, with the vault's bytes and backups unchanged. Every verb that edits items sends its edit to the
# holder instead (D-0464): piped only its values it is asked about there and saved; given no value it
# needs, it sends nothing; piped a master password before its values, as a script written for a verb
# that opened the vault itself does, it sends nothing either. Under keypaste approve each is answered `y`
# at its terminal, and the last is answered `d` and changes nothing. The holder then still answers an
# agent's request instead of refusing it as vault-changed. Once `keypaste lock` ends the hold, each verb
# saves itself.
#
# NEGATIVE CONTROL: every refused attempt is given the passwords it would need, so a verb that saved
# behind the holder changes the vault's bytes and fails here; an edit the holder did not ask about, or a
# denied one that changed the vault, fails here; and the holder's refusal of the agent as vault-changed
# fails here too. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-held-saves-master-pw'
readonly SOURCE_PW='ci-held-saves-source-pw'
readonly V1='SENTINEL-HELD-SAVES-ONE-3f91'
readonly ENTRY='env/ci/DEPLOY_KEY'
readonly REFUSED=(access import)
readonly EDITS=(add set set-field rotate field-rm env-set env-tag env-rm env-pull rm)

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
# The log in the owner's home, where the bridges here write too.
AUDIT="$(native "$WORK/home/audit.jsonl")"
readonly HOLD_OUT="$WORK/hold.txt"
readonly AGENT_ERR="$WORK/agent-stderr.txt"
readonly OUT="$WORK/mcp-stdout.txt"
readonly ERR="$WORK/mcp-stderr.txt"

HOLD_PID=""
AGENT_PID=""
# Fixed descriptors, as macOS's bash 3.2 needs (D-0398): 7 is the held app's input, 8 the open bridge's and 9 keypaste approve's.
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

# Verb $1's arguments in phase $2, one per line; each phase adds and pulls what the one before did not,
# and the last untags the item keypaste approve tagged.
arguments() {
  case "$1" in
    add) printf '%s\n' add "ci/NEW_$2" ;;
    set) printf '%s\n' set Work/github ;;
    set-field) printf '%s\n' set Work/github --field KEY ;;
    rotate) printf '%s\n' rotate Work/github ;;
    field-rm) printf '%s\n' field rm Work/github KEY ;;
    env-set) printf '%s\n' env set ci "OTHER_KEY=held-$2" ;;
    env-tag) if [ "$2" = free ]; then printf '%s\n' env untag ci Work/github --yes; else printf '%s\n' env tag ci Work/github --yes; fi ;;
    env-rm) printf '%s\n' env rm ci OTHER_KEY --yes ;;
    env-pull) printf '%s\n' env pull ci "$DOTENV" --yes --keep ;;
    rm) printf '%s\n' rm Work/github --yes ;;
    access) printf '%s\n' access --password ;;
    import) printf '%s\n' import "$SOURCE" ;;
  esac
}

# The values verb $1 reads in phase $2 after any master password, one per line; none for most.
values() {
  case "$1" in
    add) printf '%s\n' "held-add-$2" ;;
    set) printf '%s\n' "gh-$2" ;;
    set-field) printf '%s\n' "field-$2" ;;
  esac
}

# What verb $1 would be asked with nothing holding the vault: import's source first, access's new password twice.
answers() {
  case "$1" in
    import) printf '%s\n' "$SOURCE_PW" "$MASTER" ;;
    access) printf '%s\n' "$MASTER" "$MASTER-next" "$MASTER-next" ;;
    *) printf '%s\n' "$MASTER"; values "$1" "$2" ;;
  esac
}

# Runs verb $1 of phase $2 with stdin from $3 (a file, or /dev/null), naming the try $4; sets VERB_EXIT and VERB_ERR.
attempt() {
  local verb="$1" phase="$2" input="$3" tag="$4" arg args=()
  while IFS= read -r arg; do args+=("$arg"); done < <(arguments "$verb" "$phase")
  [ "$verb" != env-pull ] || printf 'PULLED_KEY=%s\n' "$phase" >"$DOTENV"
  VERB_ERR="$WORK/$verb-$phase-$tag-stderr.txt"
  set +e
  "$CLI" "${args[@]}" --vault "$VAULT" <"$input" >/dev/null 2>"$VERB_ERR" 7>&- 9>&- 8>&-
  VERB_EXIT=$?
  set -e
}

# Access and import are refused, naming process $1 and the next step $2, and change nothing.
refuse_all() {
  local pid="$1" step="$2" holder="$3" verb before kept
  before="$(digest)"
  kept="$(backups)"
  for verb in "${REFUSED[@]}"; do
    attempt "$verb" refused /dev/null closed
    [ "$VERB_EXIT" -ne 0 ] || die "$verb under $holder: exited 0 with stdin closed"
    grep -q "(process $pid)" "$VERB_ERR" || die "$verb under $holder: the refusal does not name process $pid"
    grep -qF "$step" "$VERB_ERR" || die "$verb under $holder: the refusal does not say '$step'"
    grep -qE 'no master password given|no password given' "$VERB_ERR" && die "$verb under $holder: it reached a password prompt"

    answers "$verb" refused >"$WORK/answers.txt"
    attempt "$verb" refused "$WORK/answers.txt" answered
    [ "$VERB_EXIT" -ne 0 ] || die "$verb under $holder: exited 0 with its passwords given"
    grep -q "(process $pid)" "$VERB_ERR" || die "$verb under $holder: with its passwords given, the refusal does not name process $pid"
    [ "$(digest)" = "$before" ] || die "$verb under $holder: the vault's bytes changed"
    [ "$(backups)" = "$kept" ] || die "$verb under $holder: a backup was taken"
  done
}

# Verb $1 under $2 sends nothing without a value it needs, or with a master password piped before its values.
sends_nothing() {
  local verb="$1" holder="$2" phase="$3" before
  before="$(digest)"

  if [ -n "$(values "$verb" "$phase")" ]; then
    attempt "$verb" "$phase" /dev/null closed
    [ "$VERB_EXIT" -ne 0 ] || die "$verb under $holder: exited 0 with no value given"
    [ "$(digest)" = "$before" ] || die "$verb under $holder: with no value given, the vault's bytes changed"
  fi

  old_style_refused "$verb" "$holder" "$phase"
}

# A script written for a verb that opened the vault itself pipes the master password first: nothing is sent.
old_style_refused() {
  local verb="$1" holder="$2" phase="$3" before
  before="$(digest)"
  answers "$verb" "$phase" >"$WORK/answers.txt"
  attempt "$verb" "$phase" "$WORK/answers.txt" old
  [ "$VERB_EXIT" -ne 0 ] || die "$verb under $holder: exited 0 with a master password piped first"
  grep -q 'stdin held more lines than this command reads' "$VERB_ERR" || die "$verb under $holder: a master password piped first was not refused"
  [ "$(digest)" = "$before" ] || die "$verb under $holder: with a master password piped first, the vault's bytes changed"
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
    || die "$what: the holder did not release the value after the terminal's edits"
  tail -n 1 "$AUDIT" | jq -e '.tool == "request_credential" and .decision == "granted"' >/dev/null \
    || die "$what: the release was not audited as granted"
  grep -q 'vault-changed' "$AUDIT" && die "$what: a request was refused as vault-changed"
  return 0
}

# ---------------------------------------------------------------- a vault, a source and a .env
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n%s\n' "$MASTER" "$V1" | "$CLI" add "$ENTRY" --vault "$VAULT" >/dev/null || die "could not store the credential"
printf '%s\n%s\n' "$SOURCE_PW" "$SOURCE_PW" | "$CLI" init "$SOURCE" >/dev/null || die "could not create the import source"
printf '%s\n%s\n' "$SOURCE_PW" 'old-held' | "$CLI" add Imported/old --vault "$SOURCE" >/dev/null || die "could not fill the import source"
# The entry is not tagged into a project, so each probe sees it only once a person lets it see env/**,
# confirmed at keypaste approve's terminal before anything holds the vault.
agent_settings "$CLI" "$VAULT" "$MASTER" 'ci-held-app --see env/**' 'ci-held-agent --see env/**' || die "could not save the two probes' settings in the vault"

# ------------------------------------------------------------------------------- the app holds it
exec 7>&-
exec 7> >(exec 9>&- 8>&-; KEYPASTE_DRIVER_PASSWORD="$MASTER" exec "$DRV" hold "$VAULT" --approving-prompt >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/.* as process ([0-9]+).*/\1/')"

refuse_all "$HOLD_PID" 'Make the change there, or run `keypaste lock` and try again.' "the app"

asked=0
for verb in "${EDITS[@]}"; do
  sends_nothing "$verb" "the app" app
  before="$(digest)"
  values "$verb" app >"$WORK/values.txt"
  attempt "$verb" app "$WORK/values.txt" sent
  [ "$VERB_EXIT" -eq 0 ] || die "$verb under the app: exited $VERB_EXIT: $(cat "$VERB_ERR")"
  grep -q 'holds this vault; it will ask you to allow this change' "$VERB_ERR" || die "$verb under the app: it did not say the app would ask"
  asked=$((asked + 1))
  wait_for '^edit-asking command=keypaste ' "$HOLD_OUT" "$asked"
  [ "$(digest)" != "$before" ] || die "$verb under the app: the app allowed it and nothing was saved"
done

bridge ci-held-app
request 2
answered 2 "the app"

"$CLI" lock --vault "$VAULT" >"$WORK/lock.txt" 2>&1 7>&- 9>&- 8>&-   || die "keypaste lock did not lock the app: $(cat "$WORK/lock.txt")"
wait_for '^locked' "$HOLD_OUT"
exec 7>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

# ----------------------------------------------------------------------------- keypaste approve holds it
exec 9>&-
exec 9> >(exec 7>&- 8>&-; exec "$CLI" approve --vault "$VAULT" >/dev/null 2>"$AGENT_ERR")
AGENT_SHELL=$!
if [ -r "/proc/$AGENT_SHELL/winpid" ]; then AGENT_PID="$(cat "/proc/$AGENT_SHELL/winpid")"; else AGENT_PID="$AGENT_SHELL"; fi
printf '%s\n' "$MASTER" >&9
wait_for 'listening on' "$AGENT_ERR"
# Read again once it listens: before the subshell's exec reaches the CLI, Windows' winpid is the shell's.
if [ -r "/proc/$AGENT_SHELL/winpid" ]; then AGENT_PID="$(cat "/proc/$AGENT_SHELL/winpid")"; else AGENT_PID="$AGENT_SHELL"; fi

refuse_all "$AGENT_PID" 'Run `keypaste lock` and try again.' "keypaste approve"
old_style_refused set "keypaste approve" agent

# Each edit waits in the background for the person at keypaste approve's terminal: every one is allowed
# but the last, which is denied, since a denial holds every later edit back for a minute.
asked=0
last="${EDITS[${#EDITS[@]}-1]}"
for verb in "${EDITS[@]}"; do
  before="$(digest)"
  values "$verb" agent >"$WORK/values.txt"
  [ "$verb" != env-pull ] || printf 'PULLED_KEY=agent\n' >"$DOTENV"
  args=()
  while IFS= read -r arg; do args+=("$arg"); done < <(arguments "$verb" agent)
  VERB_ERR="$WORK/$verb-agent-stderr.txt"
  "$CLI" "${args[@]}" --vault "$VAULT" <"$WORK/values.txt" >/dev/null 2>"$VERB_ERR" 7>&- 9>&- 8>&- &
  verb_pid=$!
  asked=$((asked + 1))
  wait_for 'keypaste: a terminal asks to change this vault' "$AGENT_ERR" "$asked"
  grep -q "  command  keypaste " "$AGENT_ERR" || die "$verb under keypaste approve: the prompt did not name the command"

  if [ "$verb" = "$last" ]; then
    printf 'd\n' >&9
    set +e; wait "$verb_pid"; VERB_EXIT=$?; set -e
    [ "$VERB_EXIT" -ne 0 ] || die "$verb under keypaste approve: exited 0 when denied"
    [ "$(digest)" = "$before" ] || die "$verb under keypaste approve: denied, and the vault's bytes changed"
  else
    printf 'y\n' >&9
    set +e; wait "$verb_pid"; VERB_EXIT=$?; set -e
    [ "$VERB_EXIT" -eq 0 ] || die "$verb under keypaste approve: exited $VERB_EXIT: $(cat "$VERB_ERR")"
    [ "$(digest)" != "$before" ] || die "$verb under keypaste approve: allowed, and nothing was saved"
  fi
done

bridge ci-held-agent
request 3
wait_for 'an agent is asking for a credential' "$AGENT_ERR"
printf 'o\n' >&9
answered 3 "keypaste approve"

"$CLI" lock --vault "$VAULT" >"$WORK/lock.txt" 2>&1 7>&- 9>&- 8>&-   || die "keypaste lock did not stop keypaste approve: $(cat "$WORK/lock.txt")"
wait_for 'keypaste approve has stopped' "$AGENT_ERR"
exec 9>&-
AGENT_PID=""
exec 8>&-

# ------------------------------------------------------------------------ nothing holds it: each saves
# keypaste approve's denied rm left Work/github in place; access goes last, since it changes the master password the others are given.
for verb in "${EDITS[@]}" import access; do
  before="$(digest)"
  answers "$verb" free >"$WORK/answers.txt"
  attempt "$verb" free "$WORK/answers.txt" free
  [ "$VERB_EXIT" -eq 0 ] || die "$verb with nothing holding the vault: exited $VERB_EXIT: $(cat "$VERB_ERR")"
  [ "$(digest)" != "$before" ] || die "$verb with nothing holding the vault: nothing was saved"
done

echo "ok: while the app and then keypaste approve held the vault, access and import were refused naming the"
echo "    holder's process and the next step, with the vault's bytes and backups unchanged; add, set, set"
echo "    --field, rotate, field rm, env set, env tag, env rm, env pull and rm were each asked about there and"
echo "    saved by the holder, none sent anything without its value or with a master password piped first,"
echo "    and the one keypaste approve denied changed nothing; each holder then released to an agent, and once"
echo "    keypaste lock ended the hold each verb saved"
