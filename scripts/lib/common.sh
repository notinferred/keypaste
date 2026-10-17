# shellcheck shell=bash
# Helpers the gates share, sourced from a path relative to the gate:
#   . "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
# Nothing here skips a check: a missing tool or binary fails the gate that asked for it.

# A gate that sets DIE_PREFIX prints it before the reason; otherwise the reason is a GitHub error
# annotation. DIE_FILES names variables holding files to print after it, read when the gate fails.
die() {
  local name file
  if [ -n "${DIE_PREFIX:-}" ]; then printf '\n%s%s\n' "$DIE_PREFIX" "$*" >&2; else echo "::error::$*" >&2; fi
  for name in ${DIE_FILES:-}; do
    file="${!name:-}"
    if [ -n "$file" ] && [ -f "$file" ]; then echo "--- $file ---" >&2; cat "$file" >&2; fi
  done
  exit 1
}

step() {
  printf '\n--- %s\n' "$*"
  if [ -n "${WATCHDOG_PHASE:-}" ]; then printf '%s\n' "$*" >"$WATCHDOG_PHASE" || true; fi
}

# Fails the gate past $1 seconds, printing the phase it reached and every process (F.63); $2... are its programs.
watchdog() {
  local seconds=$1 winpid='' before=''
  shift
  case "$seconds" in '' | 0* | *[!0-9]*) die "the watchdog needs a whole number of seconds, not '$seconds'" ;; esac
  WATCHDOG_PHASE=$(mktemp)
  printf 'before its first step\n' >"$WATCHDOG_PHASE"
  if [ -r "/proc/$$/winpid" ]; then winpid=$(cat "/proc/$$/winpid"); else before=" $(ps -A -o pid= | tr -s ' \n' '  ') "; fi
  # Detached, so ending the gate's tree does not end the watcher.
  (watch_gate "$$" "$(started "$$")" "$seconds" "$winpid" "$before" "$@" &)
}

watch_gate() {
  local gate=$1 start=$2 seconds=$3 winpid=$4 before=$5 deadline listing targets kind pid
  shift 5
  set +e
  deadline=$((SECONDS + seconds))
  while [ "$SECONDS" -lt "$deadline" ] && kill -0 "$gate" 2>/dev/null; do sleep 1; done
  # A gate that exited may have left its number to another process.
  if ! kill -0 "$gate" 2>/dev/null || [ "$(started "$gate")" != "$start" ]; then rm -f "$WATCHDOG_PHASE"; exit 0; fi
  kill -STOP "$gate" 2>/dev/null
  echo "::error::the gate ran past ${seconds}s, in: $(cat "$WATCHDOG_PHASE")"
  if [ -n "$winpid" ]; then
    targets=$(windows_targets "$winpid" "$@")
    # Git Bash's kill, since a Git Bash process ended by taskkill gives its parent exit status 0 (F.63).
    kill -9 "$gate" 2>/dev/null
    while read -r kind pid; do
      [ -n "$pid" ] || continue
      [ "$kind" = child ] || echo "ending process $pid and everything under it, which the gate started and lost track of"
      taskkill //F //T //PID "$pid" >/dev/null 2>&1
    done <<<"$targets"
  else
    listing=$(bounded 60 ps -A -o pid,ppid,args) || echo "the process list did not finish within a minute"
    printf '%s\n' "$listing"
    targets=$(strays "$before" "$listing" "$@")
    for pid in $targets; do echo "ending process $pid and everything under it, which the gate started and lost track of"; done
    # shellcheck disable=SC2086 # one process id per word
    end_tree "$gate" $targets
  fi
  rm -f "$WATCHDOG_PHASE"
}

# Each chain in listing $2 holding one of $3... that hangs from init and was not among processes $1 at arming.
strays() {
  local before=$1 listing=$2
  shift 2
  awk -v before="$before" -v names=" $* " '
    NR > 1 { parent[$1] = $2; name = $3; sub(/.*\//, "", name); if (index(names, " " name " ")) named[$1] = 1 }
    END {
      for (pid in named) {
        top = pid
        while (parent[top] != 1 && parent[top] != top && (parent[top] in parent)) top = parent[top]
        if (parent[top] == 1 && !index(before, " " top " ") && !seen[top]++) print top
      }
    }' <<<"$listing"
}

# Windows' targets: the children of the gate's process $1, and each chain the gate started that outlived its parent.
windows_targets() {
  local gate=$1
  shift
  # shellcheck disable=SC2016 # PowerShell expands its own variables, not bash.
  bounded 60 env WATCHDOG_GATE="$gate" WATCHDOG_NAMES="$*" powershell -NoProfile -NonInteractive -Command '
    $all = @(Get-CimInstance Win32_Process)
    [Console]::Error.WriteLine(($all | Sort-Object ParentProcessId | Format-Table ProcessId, ParentProcessId, Name, CommandLine -AutoSize | Out-String -Width 500))
    $by = @{}
    foreach ($p in $all) { $by[$p.ProcessId] = $p }
    $gate = $by[[uint32]$env:WATCHDOG_GATE]
    if (-not $gate) { exit }
    $names = @($env:WATCHDOG_NAMES -split " " | ForEach-Object { "$_.exe" })
    $tops = @{}
    foreach ($p in $all) {
      if ($p.ParentProcessId -eq $gate.ProcessId -and $p.CreationDate -ge $gate.CreationDate) { "child $($p.ProcessId)" }
      if ($names -notcontains $p.Name) { continue }
      $top = $p
      while (($parent = $by[$top.ParentProcessId]) -and $parent.ProcessId -ne $top.ProcessId -and $parent.CreationDate -le $top.CreationDate) { $top = $parent }
      if ($top.CreationDate -gt $gate.CreationDate) { $tops[$top.ProcessId] = 1 }
    }
    $tops.Keys | ForEach-Object { "stray $_" }' || ps -W >&2
}

# What $2... printed, ended and failed once it runs past $1 seconds, since a wedged process can stall even ps.
bounded() {
  local seconds=$1 out pid status=0
  shift
  out=$(mktemp)
  "$@" >"$out" &
  pid=$!
  for _ in $(seq 1 $((seconds * 5))); do kill -0 "$pid" 2>/dev/null || break; sleep 0.2; done
  if kill -0 "$pid" 2>/dev/null; then end_tree "$pid"; status=1; fi
  tr -d '\r' <"$out"
  rm -f "$out"
  return "$status"
}

# A process's start time, so a number another process took over is not mistaken for the one it named.
started() {
  local stat
  if [ -r "/proc/$1/stat" ]; then
    read -r stat <"/proc/$1/stat" || return 0
    # shellcheck disable=SC2086 # the fields after the command name, one per word; the start time is field 22
    set -- ${stat##*') '}
    printf '%s' "${20:-}"
  else
    ps -o lstart= -p "$1" 2>/dev/null
  fi
}

require() { command -v "$1" >/dev/null 2>&1 || die "$1 is required and was not found; this gate must never be skipped"; }

# The executable the variable named $1 gives, else the build output $2; Windows appends .exe.
resolve() {
  local path="${!1:-$2}"
  [ -x "$path" ] || path="$path.exe"
  [ -x "$path" ] || die "not found: ${!1:-$2} (build it first, or set $1)"
  printf '%s' "$path"
}
keypaste_bin() { resolve KEYPASTE_BIN artifacts/bin/Keypaste.Cli/release/keypaste; }
app_driver() { resolve KEYPASTE_APP_DRIVER artifacts/bin/Keypaste.AppDriver/release/Keypaste.AppDriver; }
vault_restorer() { resolve KEYPASTE_RESTORER artifacts/bin/Keypaste.VaultRestorer/release/Keypaste.VaultRestorer; }

# A project variable as `env set` writes it: a protected field of the environment's home entry,
# env/<project>/.env or .env.<environment>, created tagged.
#   project_var <keypaste> <vault> <master> <project>[/<environment>] <KEY> <value> [keypaste options...]
project_var() {
  local cli=$1 vault=$2 master=$3 project=${4%%/*} key=$5 value=$6 environment=dev
  case "$4" in */*) environment=${4#*/} ;; esac
  shift 6
  local said
  said=$(printf '%s\n%s\n' "$master" "$value" | "$cli" env set "$project" "$key" -p "$environment" --vault "$vault" "$@" 2>&1 >/dev/null) \
    || { printf '%s\n' "$said" >&2; return 1; }
}

# A path as a Windows program must be given it from Git Bash; unchanged elsewhere.
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

# Saves agents' settings in a vault as a person confirms them at `keypaste agent`'s terminal, before
# anything else holds the vault: a short-lived agent with a home of its own answers yes to each change
# and stops, so the gate's log and ceilings hold only what the gate itself does.
#   agent_settings <keypaste> <vault> <master> '<keypaste mcp policy words>'...
# Each change's words are split on spaces and never globbed, so `--see **` reaches keypaste as written.
agent_settings() {
  local cli=$1 vault=$2 master=$3 home pipe err pid change status=0 answers=''
  local -a words
  shift 3
  home=$(mktemp -d)
  pipe="keypaste-settings-$$-${RANDOM}"
  err="$home/agent-stderr.txt"
  for change in "$@"; do answers="${answers}y"$'\n'; done
  printf '%s\n%s' "$master" "$answers" \
    | KEYPASTE_HOME="$(native "$home")" "$cli" agent --vault "$vault" --approver "$pipe" >/dev/null 2>"$err" &
  pid=$!
  for _ in $(seq 1 100); do
    grep -q 'listening on' "$err" 2>/dev/null && break
    kill -0 "$pid" 2>/dev/null || break
    sleep 0.2
  done
  if grep -q 'listening on' "$err" 2>/dev/null; then
    for change in "$@"; do
      read -r -a words <<<"$change"
      KEYPASTE_HOME="$(native "$home")" "$cli" mcp policy "${words[@]}" --vault "$vault" --approver "$pipe" >/dev/null 2>>"$err" \
        || { status=1; break; }
    done
  else
    status=1
  fi
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  [ "$status" -eq 0 ] || cat "$err" >&2
  rm -rf "$home" 2>/dev/null || true
  return "$status"
}

# A file's bytes as one hex string, so a refusal can be shown to leave it unchanged.
bytes() { od -An -v -tx1 "$1" | tr -d ' \n'; }

# A log several phases write is read one phase at a time: lines_in counts the lines file $1 holds, 0 before
# it exists, and lines_after prints the lines it gained after it held $2.
lines_in() { if [ -f "$1" ]; then wc -l <"$1" | tr -d ' '; else echo 0; fi; }
lines_after() { tail -n +"$(($2 + 1))" "$1"; }

# Git Bash's kill cannot end a native Windows process.
kill_process() {
  if command -v taskkill >/dev/null 2>&1; then
    taskkill //F //PID "$1" >/dev/null 2>&1 || true
  else
    kill -9 "$1" 2>/dev/null || true
  fi
}

# The processes under $1, deepest first.
descendants() {
  local child
  for child in $(pgrep -P "$1" 2>/dev/null); do
    descendants "$child"
    echo "$child"
  done
}

# Ends each process given and everything under it, TERM then KILL a second later; Windows' taskkill walks a tree itself.
end_tree() {
  local tree='' root
  for root; do
    if [ -r "/proc/$root/winpid" ]; then
      taskkill //F //T //PID "$(cat "/proc/$root/winpid")" >/dev/null 2>&1 || true
    else
      tree="$tree $root $(descendants "$root")"
    fi
  done
  [ -n "$tree" ] || return 0
  # shellcheck disable=SC2086 # one process id per word
  kill $tree 2>/dev/null || true
  sleep 1
  # shellcheck disable=SC2086
  kill -9 $tree 2>/dev/null || true
}

# Waits until $2 holds $3 (default 1) lines matching $1, for WAIT_SECONDS (default 30).
wait_for() {
  local pattern="$1" file="$2" count="${3:-1}"
  for _ in $(seq 1 $((${WAIT_SECONDS:-30} * 5))); do
    [ "$(grep -c -- "$pattern" "$file" 2>/dev/null || true)" -ge "$count" ] && return 0
    sleep 0.2
  done
  die "timed out waiting for '$pattern' in $file"
}

# Waits up to $1 seconds until the JSON-RPC stream in $2 holds a response to every id after them, so a
# client can close its stdin; 1 if one never came, which the gate's own assertion then names.
await_replies() {
  local seconds="$1" file="$2" ids
  shift 2
  ids="[$(IFS=,; printf '%s' "$*")]"
  for _ in $(seq 1 $((seconds * 5))); do
    [ -s "$file" ] && jq -nRe --argjson ids "$ids" \
      '$ids - [inputs | fromjson? | objects | select(has("result") or has("error")) | .id] == []' \
      <"$file" >/dev/null 2>&1 && return 0
    sleep 0.2
  done
  return 1
}

# The held app's current session and process, from the latest line tests/Keypaste.AppDriver printed to HOLD_OUT.
session_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/^holding session ([0-9a-f]+).*/\1/'; }
process_of() { grep 'holding session' "$HOLD_OUT" | tail -1 | sed -E 's/.* as process ([0-9]+).*/\1/'; }
