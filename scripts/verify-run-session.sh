#!/usr/bin/env bash
# `keypaste run --session` takes a project's set from the process holding the vault (E.1c, V-E.1c), across
# real processes: the shipped keypaste, tests/Keypaste.AppDriver holding the vault as launch composes the
# app with its prompt window drawn on a headless display and clicked, and `keypaste agent` answering in
# its terminal. The child is a real process that prints the set from its own environment.
#
# With the app holding the vault, the run raises the app's prompt naming the project, its variable names,
# the command and the directory; Approve starts the child with the set, and the runner prints no value and
# asks for no password. Deny, a lock while the prompt waits and the timeout each exit non-zero with no
# child, and a set E.1a refuses is refused before any prompt. With nothing holding the vault the run says
# so. With `keypaste agent` holding it, y releases and n refuses.
#
# NEGATIVE CONTROL: every run is given the master password on its standard input, so a runner that fell
# back to opening the vault itself would start the child on a refusal and fail here. This also fails if a
# child starts without a press of Approve or a y, if a value reaches the runner's own output or the app's,
# or if a prompt stays up after its run is answered. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-run-session-master-pw'
readonly DEPLOY='SENTINEL-RUN-SESSION-DEPLOY-5e2b90'
readonly DATABASE='SENTINEL-RUN-SESSION-DB-a17c3d'

die() {
  echo "::error::$*" >&2
  for f in "${HOLD_OUT:-}" "${RUN_OUT:-}" "${RUN_ERR:-}" "${AGENT_ERR:-}"; do
    if [ -n "$f" ] && [ -f "$f" ]; then echo "--- $f ---" >&2; cat "$f" >&2; fi
  done
  exit 1
}

resolve() {
  local candidate="$1"
  [ -x "$candidate" ] || candidate="${candidate}.exe"
  [ -x "$candidate" ] || die "not found: $1 (build first)"
  printf '%s' "$candidate"
}

native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

CLI="$(resolve "${KEYPASTE_BIN:-artifacts/bin/Keypaste.Cli/release/keypaste}")"
DRV="$(resolve "${KEYPASTE_APP_DRIVER:-artifacts/bin/Keypaste.AppDriver/release/Keypaste.AppDriver}")"
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
exec {HOLD_IN}>/dev/null
exec {AGENT_IN}>/dev/null
cleanup() {
  exec {HOLD_IN}>&- 2>/dev/null || true
  exec {AGENT_IN}>&- 2>/dev/null || true
  if [ -n "$HOLD_PID" ]; then kill_process "$HOLD_PID"; fi
  if [ -n "$AGENT_PID" ]; then kill_process "$AGENT_PID"; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

kill_process() {
  if command -v taskkill >/dev/null 2>&1; then
    taskkill //F //PID "$1" >/dev/null 2>&1 || true
  else
    kill -9 "$1" 2>/dev/null || true
  fi
}

wait_for() {
  local pattern="$1" file="$2" count="${3:-1}"
  for _ in $(seq 1 150); do
    [ "$(grep -c -- "$pattern" "$file" 2>/dev/null || true)" -ge "$count" ] && return 0
    sleep 0.2
  done
  die "timed out waiting for '$pattern' in $file"
}

process_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/.* as process ([0-9]+).*/\1/'; }

# Starts `keypaste run --session <project>` in the project directory with the master password on its
# standard input, tagging the command with $2 so no two cases share a refusal's cooldown.
start_run() {
  local project="$1" tag="$2"
  RUN_OUT="$WORK/$tag-stdout.txt"
  RUN_ERR="$WORK/$tag-stderr.txt"
  (
    cd "$WORK/project-e1c"
    exec "$CLI" run --session --vault "$VAULT" "$project" -- "$CHILD" -c \
      'printf "deploy=%s database=%s" "${DEPLOY_KEY-unset}" "${DB_URL-unset}"' "$tag" \
      <<<"$MASTER" >"$RUN_OUT" 2>"$RUN_ERR" {HOLD_IN}>&- {AGENT_IN}>&-
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
  grep -qi 'password' "$RUN_ERR" && die "$what: the runner asked for a password"
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
for pair in "DEPLOY_KEY=$DEPLOY" "DB_URL=$DATABASE"; do
  printf '%s\n' "$MASTER" | "$CLI" env set ci "$pair" --vault "$VAULT" >/dev/null || die "could not store the ci set"
done
printf '%s\n%s\n' "$MASTER" "$DEPLOY" | "$CLI" add env/broken/BAD-NAME --vault "$VAULT" >/dev/null \
  || die "could not store an entry whose name cannot be exported"

# ------------------------------------------------------------------- nothing holds the vault
start_run ci no-owner
refused "no owner" "nothing holds"

# -------------------------------------------------------------- the app holds it: Approve releases
exec {HOLD_IN}>&-
exec {HOLD_IN}> >(KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" hold "$VAULT" >"$HOLD_OUT" 2>&1 {AGENT_IN}>&-)
wait_for 'holding session' "$HOLD_OUT"
grep -q 'not served' "$HOLD_OUT" && die "the app unlocked and did not serve its vault"
HOLD_PID="$(process_of)"

start_run ci case-approve
next_prompt
PROMPT_LINE="$(grep '^env-prompt project=' "$HOLD_OUT" | tail -1)"
case "$PROMPT_LINE" in
  "env-prompt project=ci command="*" case-approve directory="*"project-e1c keys=DB_URL,DEPLOY_KEY") ;;
  *) die "the prompt does not name the project, the command, the directory and the variable names: $PROMPT_LINE" ;;
esac
echo approve >&"$HOLD_IN"
withdrawn
released "Approve in the app"
grep -qE "$DEPLOY|$DATABASE" "$HOLD_OUT" && die "a value reached the app's output"

# ------------------------------------------------------------------------------ Deny refuses
start_run ci case-deny
next_prompt
echo deny >&"$HOLD_IN"
withdrawn
refused "Deny in the app" "said no"

# ------------------------------------------------ a set E.1a refuses is refused before anyone is asked
start_run broken case-broken
refused "an unusable set" "BAD-NAME"
[ "$(grep -c '^env-prompt project=' "$HOLD_OUT")" -eq "$PROMPTS" ] || die "an unusable set raised a prompt"

# ------------------------------------------------------------- a lock while the prompt waits refuses
start_run ci case-lock
next_prompt
echo lock >&"$HOLD_IN"
wait_for '^locked' "$HOLD_OUT"
withdrawn
refused "a lock while asked" "locked"

# -------------------------------------------------------- nobody answering refuses when the window closes
echo unlock >&"$HOLD_IN"
wait_for 'holding session' "$HOLD_OUT" 2
start_run ci case-timeout
next_prompt
refused "the timeout" "in time"
withdrawn

exec {HOLD_IN}>&-
wait_for '^shut down' "$HOLD_OUT"
HOLD_PID=""

# ------------------------------------------------------------- keypaste agent holds it: y and n
exec {AGENT_IN}>&-
exec {AGENT_IN}> >(exec "$CLI" agent --vault "$VAULT" >/dev/null 2>"$AGENT_ERR" {HOLD_IN}>&-)
AGENT_SHELL=$!
if [ -r "/proc/$AGENT_SHELL/winpid" ]; then AGENT_PID="$(cat "/proc/$AGENT_SHELL/winpid")"; else AGENT_PID="$AGENT_SHELL"; fi
printf '%s\n' "$MASTER" >&"$AGENT_IN"
wait_for 'listening on' "$AGENT_ERR"

start_run ci agent-yes
wait_for "is asking for a project's variables" "$AGENT_ERR" 1
grep -q 'variables  DB_URL DEPLOY_KEY' "$AGENT_ERR" || die "keypaste agent did not show the variable names"
grep -q 'command    .* agent-yes' "$AGENT_ERR" || die "keypaste agent did not show the command"
printf 'y\n' >&"$AGENT_IN"
released "y at keypaste agent"

start_run ci agent-no
wait_for "is asking for a project's variables" "$AGENT_ERR" 2
printf 'n\n' >&"$AGENT_IN"
refused "n at keypaste agent" "said no"
grep -qE "$DEPLOY|$DATABASE" "$AGENT_ERR" && die "a value reached keypaste agent's terminal"

echo "ok: keypaste run --session took the set from the app only on Approve and from keypaste agent only on y,"
echo "    showing the project, names, command and directory and never a value or a password prompt; Deny, n,"
echo "    a lock, the timeout, an unusable set and no owner each started nothing"
