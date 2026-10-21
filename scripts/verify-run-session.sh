#!/usr/bin/env bash
# `keypaste run --session` takes a project's set from the process holding the vault (E.1c, V-E.1c), across
# real processes: the shipped keypaste, tests/Keypaste.AppDriver holding the vault as launch composes the
# app with its prompt window drawn on a headless display and clicked, and `keypaste approve` answering in
# its terminal. The child is a real process that prints the set from its own environment.
#
# With the app holding the vault, the run raises the app's prompt naming the project, its variable names,
# the command and the directory; Approve starts the child with the set, and the runner prints no value and
# asks for no password. A set of a tagged entry's field and a field of the project's home entry names each
# source entry, and because that entry is also tagged into prod it offers Allow once only (C.1b). Deny, a lock while the prompt waits and the timeout each exit non-zero with no
# child, and a set E.1a refuses is refused before any prompt. With nothing holding the vault the run says
# so. With `keypaste approve` holding it, o releases once and n refuses.
#
# On macOS the app's own Run opens Terminal (E.1d): an assembled keypaste.app carries the real launcher and a
# keypaste that starts the built CLI, the held app presses Run in a terminal and Start, real Terminal runs the
# launcher, and the window's `keypaste run --session` presents the launch pass to the held app. The command
# reports the set from its own environment, the app shows no prompt, the request folder is left empty, and the
# owner writes one prompt line naming the item and no value; Open terminal is released the same way. The run
# gets KEYPASTE_HOME from the launcher and no other keypaste setting, and a later window Terminal opens for
# another .command holds none, since the app gives open its environment less every KEYPASTE_ variable. A lock
# and unlock between Start and the window's request leave it nothing to start. The bundle's keypaste waits
# while a file the gate holds exists, which is how the lock comes first, then gives that run no terminal to
# read, since a refusal there waits for a Return the gate cannot type, and it keeps what the run prints to
# the window. Terminal is ended with pkill, so the leg refuses to start while a person's Terminal is open.
#
# NEGATIVE CONTROL: every run is given the master password on its standard input, so a runner that fell
# back to opening the vault itself would start the child on a refusal and fail here. This also fails if a
# child starts without a press of Approve or an o, if a value reaches the runner's own output or the app's,
# if a prompt stays up after its run is answered, if Terminal's run asks a second question or starts its
# command after a lock, or if a later Terminal window holds a keypaste setting or the master password. These
# checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-run-session-master-pw'
readonly DEPLOY='SENTINEL-RUN-SESSION-DEPLOY-5e2b90'
readonly DATABASE='SENTINEL-RUN-SESSION-DB-a17c3d'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='HOLD_OUT RUN_OUT RUN_ERR AGENT_ERR TERMINAL_OUT WINDOWS EXITS'

CLI="$(keypaste_bin)"
DRV="$(app_driver)"
CLI="$(cd "$(dirname "$CLI")" && pwd)/$(basename "$CLI")"

# The child is this script's own interpreter by absolute path, as in verify-run-injection.sh.
readonly CHILD=${BASH:-/bin/bash}
[ -x "$CHILD" ] || die "cannot locate the bash that is running this script"

WORK="$(mktemp -d)"
mkdir -p "$WORK/home" "$WORK/project-e1c"
KEYPASTE_HOME="$(native "$WORK/home")"
export KEYPASTE_HOME
VAULT="$(native "$WORK/vault.kdbx")"
readonly HOLD_OUT="$WORK/hold.txt"
readonly AGENT_ERR="$WORK/agent-stderr.txt"

HOLD_PID=""
AGENT_PID=""
TERMINAL_OPENED=false
# Fixed descriptors, as macOS's bash 3.2 needs (D-0398): 7 is the held app's input and 9 keypaste approve's.
exec 7>/dev/null
exec 9>/dev/null
cleanup() {
  exec 7>&- 2>/dev/null || true
  exec 9>&- 2>/dev/null || true
  if [ -n "$HOLD_PID" ]; then kill_process "$HOLD_PID"; fi
  if [ -n "$AGENT_PID" ]; then kill_process "$AGENT_PID"; fi
  if [ "$TERMINAL_OPENED" = true ]; then pkill -x Terminal 2>/dev/null || true; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

# Starts `keypaste run --session <project>` in the project directory with the master password on its
# standard input, tagging the command with $2 so no two cases share a refusal's cooldown.
start_run() {
  local project="$1" tag="$2"
  RUN_OUT="$WORK/$tag-stdout.txt"
  RUN_ERR="$WORK/$tag-stderr.txt"
  (
    cd "$WORK/project-e1c"
    exec 7>&- 9>&-
    exec "$CLI" run --session --vault "$VAULT" "$project" -- "$CHILD" -c \
      'printf "deploy=%s database=%s" "${DEPLOY_KEY-unset}" "${DB_URL-unset}"' "$tag" \
      <<<"$MASTER" >"$RUN_OUT" 2>"$RUN_ERR"
  ) &
  RUN_PID=$!
}

# Waits for the run to end and sets RUN_EXIT.
finished() {
  set +e
  wait "$RUN_PID"
  RUN_EXIT=$?
  set -e
}

# Nothing the run printed of its own holds a value or a password prompt.
quiet() {
  local what="$1"
  grep -qE "$DEPLOY|$DATABASE" "$RUN_ERR" && die "$what: a value reached the runner's own output"
  grep -qi 'master password' "$RUN_ERR" && die "$what: the runner asked for a password"
  return 0
}

# The run exited non-zero, started no child and gave reason $2.
refused() {
  local what="$1" why="$2"
  finished
  [ "$RUN_EXIT" -ne 0 ] || die "$what: the run exited 0"
  [ ! -s "$RUN_OUT" ] || die "$what: a child started and printed '$(cat "$RUN_OUT")'"
  grep -q -- "$why" "$RUN_ERR" || die "$what: the runner did not say '$why'"
  quiet "$what"
}

# The run started the child, which printed the whole set from its own environment.
released() {
  local what="$1"
  finished
  [ "$RUN_EXIT" -eq 0 ] || die "$what: the run exited $RUN_EXIT"
  [ "$(tr -d '\r' <"$RUN_OUT")" = "deploy=$DEPLOY database=$DATABASE" ] || die "$what: the child did not receive the set"
  quiet "$what"
}

PROMPTS=0
next_prompt() {
  PROMPTS=$((PROMPTS + 1))
  wait_for '^env-prompt project=' "$HOLD_OUT" "$PROMPTS"
}
withdrawn() { wait_for '^prompt withdrawn' "$HOLD_OUT" "$PROMPTS"; }

# ---------------------------------------------------------------- a vault with two sets in it
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
project_var "$CLI" "$VAULT" "$MASTER" ci DEPLOY_KEY "$DEPLOY" || die "could not store the ci set"
project_var "$CLI" "$VAULT" "$MASTER" ci DB_URL "$DATABASE" || die "could not store the ci set"
project_var "$CLI" "$VAULT" "$MASTER" broken DB_URL 'postgres://ci:{REF:P@I:0}@db' || die "could not store a value keypaste does not fill in"
# The tagged project: DB_URL on its home entry beside DEPLOY_KEY on an entry tagged into dev and prod (C.1b).
project_var "$CLI" "$VAULT" "$MASTER" tagged DB_URL "$DATABASE" || die "could not store the tagged set's home entry"
printf '%s\n' "deploy-login" | KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" raw-add "$VAULT" services Deploy >/dev/null \
  || die "could not store the tagged entry"
printf '%s\n%s\n' "$MASTER" "$DEPLOY" | "$CLI" set services/Deploy --field DEPLOY_KEY --vault "$VAULT" >/dev/null 2>&1 \
  || die "could not set the tagged entry's field"
for environment in dev prod; do
  printf '%s\n' "$MASTER" | "$CLI" env tag tagged services/Deploy -p "$environment" --yes --vault "$VAULT" >/dev/null 2>&1 \
    || die "could not tag the entry into tagged/$environment"
done

# ------------------------------------------------------------------- nothing holds the vault
start_run ci no-owner
refused "no owner" "nothing holds"

# -------------------------------------------------------------- the app holds it: Approve releases
exec 7>&-
exec 7> >(exec 9>&-; KEYPASTE_DRIVER_PASSWORD="$MASTER" exec "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"

start_run ci case-approve
next_prompt
PROMPT_LINE="$(grep '^env-prompt project=' "$HOLD_OUT" | tail -1)"
case "$PROMPT_LINE" in
  "env-prompt project=ci command="*" case-approve directory="*"project-e1c keys=DB_URL,DEPLOY_KEY entries=env/ci/.env,env/ci/.env timed=True") ;;
  *) die "the prompt does not name the project, the command, the directory, the variable names and their entries: $PROMPT_LINE" ;;
esac
echo approve >&7
withdrawn
released "Approve in the app"
grep -qE "$DEPLOY|$DATABASE" "$HOLD_OUT" && die "a value reached the app's output"

# ------------- a tagged field and a home entry's: the prompt names each entry, and a prod-tagged member is once only
start_run tagged case-tagged
next_prompt
PROMPT_LINE="$(grep '^env-prompt project=' "$HOLD_OUT" | tail -1)"
case "$PROMPT_LINE" in
  "env-prompt project=tagged command="*" case-tagged directory="*"project-e1c keys=DB_URL,DEPLOY_KEY entries=env/tagged/.env,services/Deploy timed=False") ;;
  *) die "the tagged set's prompt does not name each source entry once only: $PROMPT_LINE" ;;
esac
echo once >&7
withdrawn
released "Allow once for the tagged set"

# ------------------------------------------------------------------------------ Deny refuses
start_run ci case-deny
next_prompt
echo deny >&7
withdrawn
refused "Deny in the app" "said no"

# ------------------------------------------------ a set E.1a refuses is refused before anyone is asked
start_run broken case-broken
refused "an unusable set" "placeholder"
[ "$(grep -c '^env-prompt project=' "$HOLD_OUT")" -eq "$PROMPTS" ] || die "an unusable set raised a prompt"

# ------------------------------------------------------------- a lock while the prompt waits refuses
start_run ci case-lock
next_prompt
echo lock >&7
wait_for '^locked' "$HOLD_OUT"
withdrawn
refused "a lock while asked" "locked"

# -------------------------------------------------------- nobody answering refuses when the window closes
echo unlock >&7
wait_for 'holding session' "$HOLD_OUT" 2
start_run ci case-timeout
next_prompt
refused "the timeout" "in time"
withdrawn

exec 7>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

# ------------------------------------- on macOS the app's Run opens Terminal, whose run takes the set (E.1d)
if [ "$(uname -s)" = Darwin ]; then
  USER_TEMP="$(getconf DARWIN_USER_TEMP_DIR)"
  readonly REQUESTS="${USER_TEMP%/}/keypaste-launch"
  readonly BUNDLE="$WORK/keypaste.app"
  readonly TERMINAL_OUT="$WORK/terminal-hold.txt"
  readonly WINDOWS="$WORK/terminal-windows.txt"
  readonly EXITS="$WORK/terminal-exits.txt"
  readonly HELD="$WORK/terminal-held"
  readonly AUDIT="$KEYPASTE_HOME/audit.jsonl"
  readonly PROJECT_DIR="$WORK/project-e1c"
  readonly REPORTED="$PROJECT_DIR/terminal.txt"
  readonly LATER="$WORK/later-window-environment.txt"
  readonly TERMINAL_COMMAND='printf "deploy=%s database=%s launch=%s driver=%s home=%s" "${DEPLOY_KEY-unset}" "${DB_URL-unset}" "${KEYPASTE_LAUNCH-unset}" "${KEYPASTE_DRIVER_PASSWORD-unset}" "${KEYPASTE_HOME-unset}" >terminal.tmp && mv terminal.tmp terminal.txt'

  pgrep -x Terminal >/dev/null && die "Terminal is running: this gate opens Terminal and ends it with pkill, so quit it first"
  [ -z "$(ls -A "$REQUESTS" 2>/dev/null)" ] || die "a run from the app is already waiting in $REQUESTS"
  case "$CLI$WORK${DOTNET_ROOT:-}" in *\'*) die "a path holds a single quote, which the bundle's keypaste cannot carry" ;; esac

  # A keypaste.app laid out as a release lays it out, beside the real launcher. Its keypaste starts the built
  # CLI once the gate removes $HELD, with no terminal input for a run it held, keeps what the run prints to
  # the window, and records how it exited.
  mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"
  cp "$(dirname "${BASH_SOURCE[0]}")/../packaging/macos/keypaste-launch.command" "$BUNDLE/Contents/Resources/"
  {
    printf '#!/bin/sh\n'
    if [ -n "${DOTNET_ROOT:-}" ]; then printf "DOTNET_ROOT='%s'\nexport DOTNET_ROOT\n" "$DOTNET_ROOT"; fi
    printf "if [ -e '%s' ]; then\n  while [ -e '%s' ]; do sleep 0.1; done\n  exec </dev/null\nfi\n" "$HELD" "$HELD"
    printf "'%s' \"\$@\" 2>>'%s'\nstatus=\$?\necho \"\$status\" >>'%s'\nexit \"\$status\"\n" "$CLI" "$WINDOWS" "$EXITS"
  } >"$BUNDLE/Contents/MacOS/keypaste"
  chmod 0755 "$BUNDLE/Contents/MacOS/keypaste" "$BUNDLE/Contents/Resources/keypaste-launch.command"
  : >"$WINDOWS"
  : >"$EXITS"
  : >"$REPORTED"

  TERMINAL_OPENED=true
  exec 7>&-
  exec 7> >(exec 9>&-; TMPDIR="$USER_TEMP" KEYPASTE_DRIVER_PASSWORD="$MASTER" exec "$DRV" hold "$VAULT" --terminal-bundle "$BUNDLE" >"$TERMINAL_OUT" 2>&1)
  wait_for 'holding session' "$TERMINAL_OUT"
  grep -q 'not served' "$TERMINAL_OUT" && die "the app unlocked and did not serve its vault"
  HOLD_PID="$(grep 'holding session' "$TERMINAL_OUT" | tail -1 | sed -E 's/.* as process ([0-9]+).*/\1/')"

  # Run in a terminal: the command Terminal runs has the set, and the app's card was the only question.
  AUDITED=$(($(wc -l <"$AUDIT")))
  printf 'terminal run ci %s %s\n' "$PROJECT_DIR" "$TERMINAL_COMMAND" >&7
  wait_for '^terminal [a-z]*:' "$TERMINAL_OUT" 1
  grep -q '^terminal launched: ' "$TERMINAL_OUT" || die "the app did not open Terminal for Run"
  CARD="$(grep '^terminal-card ' "$TERMINAL_OUT" | tail -1)"
  case "$CARD" in
    "terminal-card title=Run this in a terminal with 2 variables from ci? starts=$TERMINAL_COMMAND directory=$PROJECT_DIR keys=DB_URL, DEPLOY_KEY sources="*"env/ci/.env") ;;
    *) die "the card does not name the project, the command, the directory, the variable names and their item: $CARD" ;;
  esac
  wait_for '^deploy=' "$REPORTED"
  [ "$(cat "$REPORTED")" = "deploy=$DEPLOY database=$DATABASE launch=unset driver=unset home=$KEYPASTE_HOME" ] \
    || die "the command Terminal ran did not get exactly the set and the app's home: $(awk -v RS=' ' -F= '{ printf "%s=%s ", $1, ($2 == "unset" ? "unset" : "set") }' "$REPORTED")"
  grep -q 'keypaste run: starting with ci (dev) as you confirmed in the keypaste app' "$WINDOWS" \
    || die "the window's run did not say it starts as confirmed in the app"
  [ "$(($(wc -l <"$AUDIT") - AUDITED))" -eq 1 ] || die "the owner did not write exactly one line for the run Terminal started"
  tail -n 1 "$AUDIT" | jq -e '.client.name == "keypaste run --session" and .tool == "run" and .decision == "granted"
    and .method == "prompt" and .reason == "2 variable(s), confirmed in the app" and .entries == ["env/ci/.env"]' >/dev/null \
    || die "the owner's line for the run Terminal started is not a prompt release naming its item: $(tail -n 1 "$AUDIT")"
  [ -z "$(ls -A "$REQUESTS")" ] || die "a request was left in $REQUESTS after Run"

  # A later window gets Terminal's own environment, which is what the app gave open. It is opened from this gate's
  # environment, which holds KEYPASTE_HOME, so the check passes only through the Terminal the app started.
  pgrep -x Terminal >/dev/null || die "Terminal is not running after Run"
  {
    printf '#!/bin/sh\n'
    printf "/usr/bin/env >'%s.tmp' && mv '%s.tmp' '%s'\n" "$LATER" "$LATER" "$LATER"
  } >"$WORK/later-window.command"
  chmod 0755 "$WORK/later-window.command"
  /usr/bin/open -b com.apple.Terminal "$WORK/later-window.command" || die "open did not hand Terminal a later window"
  wait_for '.' "$LATER"
  grep -qi '^KEYPASTE_' "$LATER" \
    && die "a later Terminal window holds keypaste settings: $(grep -i '^KEYPASTE_' "$LATER" | cut -d= -f1 | tr '\n' ' ')"
  grep -qF "$MASTER" "$LATER" && die "a later Terminal window holds the master password"

  # Open terminal: the window's shell is released the set the same way.
  AUDITED=$(($(wc -l <"$AUDIT")))
  printf 'terminal open ci %s\n' "$PROJECT_DIR" >&7
  wait_for '^terminal [a-z]*:' "$TERMINAL_OUT" 2
  [ "$(grep -c '^terminal launched: ' "$TERMINAL_OUT")" -eq 2 ] || die "the app did not open Terminal for Open terminal"
  CARD="$(grep '^terminal-card ' "$TERMINAL_OUT" | tail -1)"
  case "$CARD" in
    "terminal-card title=Open a terminal with 2 variables from ci? starts=Terminal directory=$PROJECT_DIR keys=DB_URL, DEPLOY_KEY sources="*"env/ci/.env") ;;
    *) die "the Open terminal card does not name Terminal, the directory and the variable names: $CARD" ;;
  esac
  wait_for 'keypaste run: starting with ci (dev) as you confirmed in the keypaste app' "$WINDOWS" 2
  [ "$(($(wc -l <"$AUDIT") - AUDITED))" -eq 1 ] || die "the owner did not write exactly one line for Open terminal"
  tail -n 1 "$AUDIT" | jq -e '.client.name == "keypaste run --session" and .decision == "granted"
    and .method == "prompt" and .reason == "2 variable(s), confirmed in the app"' >/dev/null \
    || die "the owner's line for Open terminal is not a prompt release: $(tail -n 1 "$AUDIT")"
  [ -z "$(ls -A "$REQUESTS")" ] || die "a request was left in $REQUESTS after Open terminal"

  # A lock and unlock between Start and the window's request: the unlock that confirmed it held its pass.
  AUDITED=$(($(wc -l <"$AUDIT")))
  : >"$HELD"
  printf 'terminal run ci %s touch ran-after-a-lock\n' "$PROJECT_DIR" >&7
  wait_for '^terminal [a-z]*:' "$TERMINAL_OUT" 3
  [ "$(grep -c '^terminal launched: ' "$TERMINAL_OUT")" -eq 3 ] || die "the app did not open Terminal before the lock"
  echo lock >&7
  wait_for '^locked' "$TERMINAL_OUT"
  echo unlock >&7
  wait_for 'holding session' "$TERMINAL_OUT" 2
  rm -f "$HELD"
  wait_for '.' "$EXITS"
  [ "$(cat "$EXITS")" -ne 0 ] || die "the run Terminal started after a lock exited 0"
  grep -q 'expired or was already used' "$WINDOWS" || die "the window's run did not say the run it was confirmed for had ended"
  [ ! -e "$PROJECT_DIR/ran-after-a-lock" ] || die "the command ran after a lock"
  [ "$(($(wc -l <"$AUDIT") - AUDITED))" -eq 1 ] || die "the owner did not write exactly one line for the run after a lock"
  tail -n 1 "$AUDIT" | jq -e '.client.name == "keypaste run --session" and .decision == "denied" and .method == "invalid-request"' >/dev/null \
    || die "the owner's line for the run after a lock is not a refusal: $(tail -n 1 "$AUDIT")"
  [ -z "$(ls -A "$REQUESTS")" ] || die "a request was left in $REQUESTS after a lock"

  grep -qE '^(env-prompt|run-prompt|prompt) ' "$TERMINAL_OUT" && die "the app asked again after its own Run confirmation"
  grep -q 'answer in its prompt' "$WINDOWS" && die "a window's run asked the app without its launch pass"
  grep -qi 'master password' "$WINDOWS" && die "a window's run asked for a password"
  grep -qE "$DEPLOY|$DATABASE" "$TERMINAL_OUT" && die "a value reached the app's output"
  grep -qE "$DEPLOY|$DATABASE" "$WINDOWS" && die "a value reached what keypaste run printed in Terminal"
  grep -qE "$DEPLOY|$DATABASE" "$AUDIT" && die "a value reached the audit log"

  # Ending Terminal ends every window and what its run started.
  pkill -x Terminal || die "pkill found no Terminal to end"
  for _ in $(seq 1 50); do
    if ! pgrep -x Terminal >/dev/null && ! pgrep -f "run --session --vault $VAULT" >/dev/null; then break; fi
    sleep 0.2
  done
  pgrep -x Terminal >/dev/null && die "Terminal outlived pkill"
  pgrep -f "run --session --vault $VAULT" >/dev/null && die "a run Terminal started outlived Terminal"
  TERMINAL_OPENED=false

  exec 7>&-
  wait_for '^shut down' "$TERMINAL_OUT"
  HOLD_PID=""
fi

# ------------------------------------------------------------- keypaste approve holds it: o and n
exec 9>&-
exec 9> >(exec 7>&-; exec "$CLI" approve --vault "$VAULT" >/dev/null 2>"$AGENT_ERR")
AGENT_SHELL=$!
if [ -r "/proc/$AGENT_SHELL/winpid" ]; then AGENT_PID="$(cat "/proc/$AGENT_SHELL/winpid")"; else AGENT_PID="$AGENT_SHELL"; fi
printf '%s\n' "$MASTER" >&9
wait_for 'listening on' "$AGENT_ERR"

start_run ci agent-yes
wait_for "is asking for a project's variables" "$AGENT_ERR" 1
grep -q 'variables    DB_URL DEPLOY_KEY' "$AGENT_ERR" || die "keypaste approve did not show the variable names"
grep -q 'command      .* agent-yes' "$AGENT_ERR" || die "keypaste approve did not show the command"
printf 'o\n' >&9
released "o at keypaste approve"

start_run ci agent-no
wait_for "is asking for a project's variables" "$AGENT_ERR" 2
printf 'n\n' >&9
refused "n at keypaste approve" "said no"
grep -qE "$DEPLOY|$DATABASE" "$AGENT_ERR" && die "a value reached keypaste approve's terminal"

echo "ok: keypaste run --session took the set from the app only on Approve and from keypaste approve only on o,"
echo "    showing the project, names, command and directory and never a value or a password prompt; Deny, n,"
echo "    a lock, the timeout, an unusable set and no owner each started nothing"
if [ "$(uname -s)" = Darwin ]; then
  echo "    on macOS Run and Open terminal opened Terminal, whose run took the set from the app with no second"
  echo "    question and one prompt line naming no value, and a lock before the window asked started nothing"
fi
