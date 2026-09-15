#!/usr/bin/env bash
# F.2b2: minimizes the observer from outside its process and classifies each docs/desktop.md check.
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"

usage() {
  echo 'usage: bash scripts/observe-minimize-lock.sh <observer-executable> <output-dir> [check...] | --classify <check> <run-dir> | --selftest'
}

die() { echo "::error::$*" >&2; exit 1; }

platform() {
  case "$(uname -s)" in
    Linux) echo linux ;;
    Darwin) echo macos ;;
    MINGW*|MSYS*|CYGWIN*) echo windows ;;
    *) die "no minimize driver for $(uname -s)" ;;
  esac
}

# --- classification -------------------------------------------------------------------------------

verdict() { jq -cn --arg check "$1" --arg result "$2" --arg why "$3" '{check: $check, result: $result, why: $why}'; }

count() { jq -s "[.[] | select($2)] | length" "$1"; }

first_ms() { jq -s "[.[] | select($2) | .ms] | first // empty" "$1"; }

driver_why() {
  if [ -s "$1/driver.jsonl" ]; then
    jq -rs '[.[] | "\(.action) \(.status): \(.detail)"] | join("; ")' "$1/driver.jsonl"
  else
    echo 'no driver record'
  fi
}

classify() {
  local check="$1" dir="$2" events="$2/events.jsonl" locks minimized reasons
  local held='select(.event == "locked" and .reason != "Shutdown" and .reason != "Replaced")'

  if [ ! -s "$events" ] || [ "$(count "$events" '.event == "exit"')" -eq 0 ]; then
    verdict "$check" harness-failure 'the observer left no exit event'; return
  fi
  if [ "$(count "$events" '.event == "refused"')" -gt 0 ]; then
    verdict "$check" harness-failure "$(jq -rs '[.[] | select(.event == "refused") | .why] | first' "$events")"; return
  fi

  locks="$(count "$events" "$held")"
  reasons="$(jq -rs "[.[] | $held | .reason] | join(\",\")" "$events")"
  minimized="$(count "$events" '.event == "windowState" and .value == "Minimized"')"

  if [ "$check" = cmd-h ]; then
    if [ "$(jq -s '[.[] | select(.action == "hide" and .status == "ok")] | length' "$dir/driver.jsonl" 2>/dev/null || echo 0)" -eq 0 ]; then
      verdict "$check" unreached "$(driver_why "$dir")"
    elif [ "$locks" -gt 0 ]; then
      verdict "$check" contradiction "hiding the app locked it ($reasons)"
    else
      verdict "$check" pass 'hidden and shown again, still unlocked'
    fi
    return
  fi

  if [ "$minimized" -eq 0 ]; then
    verdict "$check" unreached "no Minimized window state arrived; $(driver_why "$dir")"; return
  fi

  case "$check" in
    enabled|restart)
      if [ "$locks" -eq 0 ]; then
        verdict "$check" contradiction 'minimized with the setting on and did not lock'
      elif [ "$reasons" != Minimized ]; then
        verdict "$check" contradiction "locked by $reasons, not Minimized"
      elif [ "$check" = enabled ] && [ "$(count "$events" '.event == "clipboard" and .marker == "present"')" -gt 0 ]; then
        verdict "$check" contradiction 'the copied marker survived the lock'
      elif [ "$check" = restart ] && ! cmp -s "$dir/toml-before" "$dir/toml-after"; then
        verdict "$check" contradiction 'app.toml changed during the relaunched run'
      else
        verdict "$check" pass 'minimize locked with reason Minimized'
      fi
      ;;
    control)
      if [ "$locks" -eq 0 ]; then
        verdict "$check" pass 'with the watch removed, a real minimize arrived and nothing locked'
      else
        verdict "$check" contradiction "locked by $reasons with the watch removed, so a lock proves no wiring"
      fi
      ;;
    disabled)
      local minimize_at at_minimize at_lock idle moved late
      minimize_at="$(first_ms "$events" '.event == "windowState" and .value == "Minimized"')"
      at_minimize="$(jq -s '[.[] | select(.event == "deadline" and .at == "minimize") | .deadlineMs] | first // empty' "$events")"
      at_lock="$(jq -s '[.[] | select(.event == "deadline" and .at == "lock") | .deadlineMs] | first // empty' "$events")"
      idle="$(first_ms "$events" '.event == "locked" and .reason == "Idle"')"
      moved="$(jq -rs --argjson after "$minimize_at" '[.[] | select(.event == "touch" and .ms >= $after) | "\(.source) at \(.ms) ms"] | join(", ")' "$events")"
      if [ "$(count "$events" '.event == "locked" and .reason == "Minimized"')" -gt 0 ]; then
        verdict "$check" contradiction 'minimize locked with the setting off'
      elif [ -n "$moved" ]; then
        verdict "$check" contradiction "the idle deadline moved after minimize with nobody at the app: $moved"
      elif [ -z "$idle" ] || [ -z "$at_minimize" ] || [ -z "$at_lock" ]; then
        verdict "$check" contradiction 'the idle countdown never locked'
      elif [ $((at_lock - at_minimize)) -lt -50 ] || [ $((at_lock - at_minimize)) -gt 50 ]; then
        verdict "$check" contradiction "the deadline was ${at_minimize} ms at minimize and ${at_lock} ms at lock"
      else
        late=$((idle - at_lock))
        # The session's one-shot timer fired 3–14 ms after its deadline on Windows 10 and under Xvfb; 250 ms allows a loaded runner.
        if [ "$late" -lt -50 ] || [ "$late" -gt 250 ]; then
          verdict "$check" contradiction "idle lock ${late} ms from its recorded deadline"
        else
          verdict "$check" pass "still unlocked after minimize; idle lock ${late} ms after its recorded deadline"
        fi
      fi
      ;;
    *) die "no check named $check" ;;
  esac
}

# --- drivers ----------------------------------------------------------------------------------------

record() { jq -cn --arg action "$2" --arg status "$3" --arg detail "$4" '{action: $action, status: $status, detail: $detail}' >> "$1/driver.jsonl"; }

act() {
  local dir="$1" action="$2" title="$3" pid="$4" output status=ok
  output="$(drive "$action" "$title" "$pid" 2>&1)" || status=refused
  record "$dir" "$action" "$status" "$output"
}

drive() {
  local action="$1" title="$2" pid="$3" process wid
  case "$(platform)" in
    linux)
      wid="$(timeout 10 xdotool search --sync --name "$title" | head -n 1)"
      case "$action" in
        pointer) xdotool mousemove --window "$wid" 400 300 ;;
        minimize) timeout 10 xdotool windowminimize --sync "$wid" ;;
        restore) timeout 10 xdotool windowactivate --sync "$wid" ;;
        *) echo "no $action on Linux"; return 1 ;;
      esac
      ;;
    macos)
      process="$(basename "$OBSERVER")"
      case "$action" in
        minimize)
          osascript -e "tell application \"System Events\" to tell process \"$process\" to click (first button of window 1 whose subrole is \"AXMinimizeButton\")" \
            || osascript -e "tell application \"System Events\" to tell process \"$process\" to set value of attribute \"AXMinimized\" of window 1 to true"
          ;;
        restore) osascript -e "tell application \"System Events\" to tell process \"$process\" to set value of attribute \"AXMinimized\" of window 1 to false" ;;
        hide) osascript -e "tell application \"System Events\" to set visible of process \"$process\" to false" ;;
        show) osascript -e "tell application \"System Events\" to set visible of process \"$process\" to true" ;;
        *) echo "no $action on macOS"; return 1 ;;
      esac
      ;;
    windows)
      local command
      # shellcheck disable=SC2016 # PowerShell expands these variables, not bash.
      case "$action" in
        pointer) command='$r = New-Object U.W+R; [void][U.W]::GetWindowRect($h, [ref]$r); [void][U.W]::SetCursorPos([int](($r.L + $r.Ri) / 2), [int](($r.T + $r.B) / 2))' ;;
        minimize) command='[void][U.W]::ShowWindow($h, 6)' ;;
        restore) command='[void][U.W]::ShowWindow($h, 9)' ;;
        *) echo "no $action on Windows"; return 1 ;;
      esac
      powershell -NoProfile -Command "Add-Type -Name W -Namespace U -MemberDefinition '[DllImport(\"user32.dll\")] public static extern bool ShowWindow(IntPtr h, int c); [DllImport(\"user32.dll\")] public static extern bool SetCursorPos(int x, int y); [DllImport(\"user32.dll\")] public static extern bool GetWindowRect(IntPtr h, ref R r); public struct R { public int L; public int T; public int Ri; public int B; }'; \$h = (Get-Process -Id $pid).MainWindowHandle; if (\$h -eq 0) { 'no window handle'; exit 1 }; $command"
      ;;
  esac
}

# --- runs ---------------------------------------------------------------------------------------------

wait_for() {
  local file="$1" filter="$2" seconds="$3" i
  for ((i = 0; i < seconds * 4; i++)); do
    if [ -s "$file" ] && [ "$(jq -s "[.[] | select($filter)] | length" "$file" 2>/dev/null || echo 0)" -gt 0 ]; then return 0; fi
    sleep 0.25
  done
  return 1
}

launch() {
  local dir="$1" scenario="$2" events="$3" home="$1/home"
  mkdir -p "$home"
  rm -f "$home/quit"
  KEYPASTE_HOME="$home" "$OBSERVER" --scenario "$scenario" --quit-file "$home/quit" --deadline-seconds 150 \
    > "$events" 2>> "$dir/stderr.log" &
  OBSERVER_PID=$!
}

finish() {
  local dir="$1" events="$2" i
  touch "$dir/home/quit"
  for ((i = 0; i < 120; i++)); do
    kill -0 "$OBSERVER_PID" 2>/dev/null || break
    sleep 0.25
  done
  if kill -0 "$OBSERVER_PID" 2>/dev/null; then
    kill "$OBSERVER_PID" 2>/dev/null || true
    echo 'observer killed after 30 s' >> "$dir/stderr.log"
  fi
  wait "$OBSERVER_PID" 2>/dev/null || true
  [ -s "$events" ] || : > "$events"
}

ready_or_fail() {
  wait_for "$2" '.event == "ready" or .event == "refused" or .event == "exit"' 90 || echo 'no ready event within 90 s' >> "$1/stderr.log"
  jq -rs '[.[] | select(.event == "ready")] | first | "\(.title // "")\t\(.pid // "")"' "$2"
}

observe() {
  local check="$1" scenario="$2" dir="$OUT/$1" events title pid ids
  mkdir -p "$dir"
  events="$dir/events.jsonl"
  launch "$dir" "$scenario" "$events"
  ids="$(ready_or_fail "$dir" "$events")"
  title="${ids%%$'\t'*}"
  pid="${ids##*$'\t'}"

  if [ -n "$title" ]; then
    case "$check" in
      cmd-h)
        act "$dir" hide "$title" "$pid"; sleep 3
        act "$dir" show "$title" "$pid"; sleep 2
        ;;
      *)
        if [ "$check" = disabled ]; then
          # The window restores under a resting pointer, as it does when a person restores it; past the 5 s pointer throttle.
          act "$dir" pointer "$title" "$pid"; sleep 6
        fi
        act "$dir" minimize "$title" "$pid"; sleep 3
        act "$dir" restore "$title" "$pid"; sleep 2
        ;;
    esac
    if [ "$check" = disabled ]; then
      wait_for "$events" '.event == "locked"' 80 || true
      sleep 2
    fi
  fi

  finish "$dir" "$events"
}

restart() {
  local dir="$OUT/restart"
  mkdir -p "$dir"
  launch "$dir" persist-write "$dir/write.jsonl"
  wait_for "$dir/write.jsonl" '.event == "exit"' 90 || true
  finish "$dir" "$dir/write.jsonl"
  if [ "$(jq -s '[.[] | select(.event == "settings" and .lockWhenMinimized == true)] | length' "$dir/write.jsonl" 2>/dev/null || echo 0)" -eq 0 ]; then
    : > "$dir/events.jsonl"
    echo 'persist-write did not tick the setting' >> "$dir/stderr.log"
    return
  fi
  cp "$dir/home/app.toml" "$dir/toml-before"
  observe restart persist-read
  cp "$dir/home/app.toml" "$dir/toml-after"
}

environment() {
  local os
  case "$(platform)" in
    linux) os="$(sed -n 's/^PRETTY_NAME="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' /etc/os-release) $(uname -r)" ;;
    macos) os="$(sw_vers -productName) $(sw_vers -productVersion) $(sw_vers -buildVersion)" ;;
    windows) os="$(uname -s) $(uname -r)" ;;
  esac
  jq -n \
    --arg os "$os" \
    --arg platform "$(platform)" \
    --arg image "${ImageOS:-local} ${ImageVersion:-}" \
    --arg sha "${GITHUB_SHA:-$(git rev-parse HEAD 2>/dev/null || echo unknown)}" \
    --arg run "${GITHUB_RUN_ID:-local}" \
    --arg session "${XDG_SESSION_TYPE:-}" \
    --arg display "${DISPLAY:-}" \
    --arg wm "$(command -v openbox >/dev/null && openbox --version | head -n 1 || true)" \
    --arg xdotool "$(command -v xdotool >/dev/null && xdotool version | head -n 1 || true)" \
    '{os: $os, platform: $platform, runnerImage: $image, sha: $sha, run: $run, session: $session, display: $display, windowManager: $wm, xdotool: $xdotool}'
}

run_all() {
  local checks=("$@") check bad=0
  [ -x "$OBSERVER" ] || die "observer is not executable: $OBSERVER"
  mkdir -p "$OUT"
  if [ "$(platform)" = linux ] && [ -z "${DISPLAY:-}" ]; then die 'DISPLAY is unset; start Xvfb and a window manager first'; fi

  if [ "${#checks[@]}" -eq 0 ]; then
    checks=(enabled control disabled restart)
    if [ "$(platform)" = macos ]; then checks+=(cmd-h); fi
  fi

  for check in "${checks[@]}"; do
    case "$check" in
      enabled|control|disabled) observe "$check" "$check" ;;
      restart) restart ;;
      cmd-h) observe cmd-h hide ;;
      *) die "no check named $check" ;;
    esac
  done

  : > "$OUT/verdicts.jsonl"
  for check in "${checks[@]}"; do
    classify "$check" "$OUT/$check" | tee -a "$OUT/verdicts.jsonl"
  done

  jq -n --argjson environment "$(environment)" --slurpfile checks "$OUT/verdicts.jsonl" \
    '{environment: $environment, checks: $checks}' > "$OUT/observation.json"

  if jq -e '[.checks[] | select(.result == "contradiction" or .result == "harness-failure")] | length > 0' "$OUT/observation.json" >/dev/null; then
    jq -r '.checks[] | select(.result == "contradiction" or .result == "harness-failure") | "::error::\(.check): \(.result): \(.why)"' "$OUT/observation.json" >&2
    bad=1
  fi
  echo "observation: $(jq -r '[.checks[] | "\(.check)=\(.result)"] | join(" ")' "$OUT/observation.json")"
  return "$bad"
}

# --- self-test ----------------------------------------------------------------------------------------

selftest() {
  local work cases=0 failures=0
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN

  fixture() {
    local name="$1"
    mkdir -p "$work/$name"
    cat > "$work/$name/events.jsonl"
  }

  expect() {
    local check="$1" name="$2" result="$3" actual
    cases=$((cases + 1))
    actual="$(bash "$SELF" --classify "$check" "$work/$name" | jq -r .result)"
    if [ "$actual" != "$result" ]; then
      echo "::error::selftest $name: $check classified $actual, expected $result" >&2
      failures=$((failures + 1))
    fi
  }

  local unlocked='{"event":"unlocked","ms":1000}' exit='{"event":"exit","code":0}'
  local minimized='{"event":"windowState","value":"Minimized","ms":2000}'

  printf '%s\n' "$unlocked" '{"event":"locked","reason":"Minimized","ms":2001}' "$minimized" \
    '{"event":"clipboard","marker":"absent","ms":3000}' '{"event":"locked","reason":"Shutdown","ms":9000}' "$exit" | fixture locks
  printf '%s\n' "$unlocked" "$minimized" '{"event":"clipboard","marker":"absent","ms":3000}' "$exit" | fixture no-lock
  printf '%s\n' "$unlocked" '{"event":"locked","reason":"Minimized","ms":2001}' "$minimized" \
    '{"event":"clipboard","marker":"present","ms":3000}' "$exit" | fixture marker-survives
  printf '%s\n' "$unlocked" '{"event":"locked","reason":"Idle","ms":2001}' "$minimized" "$exit" | fixture wrong-reason
  printf '%s\n' "$unlocked" "$exit" | fixture never-minimized
  printf '%s\n' '{"action":"minimize","status":"refused","detail":"no window"}' > "$work/never-minimized/driver.jsonl"
  printf '%s\n' "$unlocked" "$minimized" '{"event":"windowState","value":"Normal","ms":5000}' | fixture no-exit
  printf '%s\n' '{"event":"refused","why":"the unlock screen did not take the vault"}' "$exit" | fixture refused
  local setup='{"event":"touch","source":"PointerMoved","deadlineMs":61500,"ms":1500}'
  local at_minimize='{"event":"deadline","at":"minimize","deadlineMs":61500,"ms":2001}'
  idle_run() {
    printf '%s\n' "$unlocked" "$setup" "$minimized" "$at_minimize" "$@" "$exit"
  }
  idle_run '{"event":"locked","reason":"Idle","ms":61508}' '{"event":"deadline","at":"lock","deadlineMs":61500,"ms":61509}' | fixture idle-on-time
  idle_run '{"event":"locked","reason":"Idle","ms":62300}' '{"event":"deadline","at":"lock","deadlineMs":61500,"ms":62301}' | fixture idle-late
  idle_run '{"event":"locked","reason":"Idle","ms":30000}' '{"event":"deadline","at":"lock","deadlineMs":61500,"ms":30001}' | fixture idle-early
  idle_run | fixture idle-missing
  # F.13 as observed on Windows 10 Pro 19045: a move at the parked cursor 10 ms after SW_RESTORE moved the deadline.
  printf '%s\n' '{"event":"unlocked","ms":1141}' '{"event":"windowState","value":"Minimized","ms":8416}' \
    '{"event":"deadline","at":"minimize","deadlineMs":63389,"ms":8417}' '{"event":"windowState","value":"Normal","ms":11917}' \
    '{"event":"deadline","at":"restore","deadlineMs":63389,"ms":11917}' \
    '{"event":"touch","source":"PointerMoved","x":500,"y":329,"deadlineMs":71926,"ms":11927}' \
    '{"event":"locked","reason":"Idle","ms":71930}' '{"event":"deadline","at":"lock","deadlineMs":71927,"ms":71932}' \
    "$exit" | fixture idle-restore-touched
  idle_run '{"event":"locked","reason":"Idle","ms":66504}' '{"event":"deadline","at":"lock","deadlineMs":66500,"ms":66505}' | fixture idle-deadline-drifted

  cp -r "$work/locks" "$work/restart-same"
  printf 'lock_when_minimized = 1\n' > "$work/restart-same/toml-before"
  cp "$work/restart-same/toml-before" "$work/restart-same/toml-after"
  cp -r "$work/locks" "$work/restart-changed"
  printf 'lock_when_minimized = 1\n' > "$work/restart-changed/toml-before"
  printf 'lock_when_minimized = 0\n' > "$work/restart-changed/toml-after"

  printf '%s\n' "$unlocked" "$exit" | fixture hidden
  printf '%s\n' '{"action":"hide","status":"ok","detail":""}' '{"action":"show","status":"ok","detail":""}' > "$work/hidden/driver.jsonl"
  cp -r "$work/hidden" "$work/hidden-locks"
  printf '%s\n' "$unlocked" '{"event":"locked","reason":"Minimized","ms":2001}' "$exit" > "$work/hidden-locks/events.jsonl"
  printf '%s\n' "$unlocked" "$exit" | fixture hide-refused
  printf '%s\n' '{"action":"hide","status":"refused","detail":"not allowed assistive access"}' > "$work/hide-refused/driver.jsonl"

  expect enabled locks pass
  expect enabled no-lock contradiction
  expect enabled marker-survives contradiction
  expect enabled wrong-reason contradiction
  expect enabled never-minimized unreached
  expect enabled no-exit harness-failure
  expect enabled refused harness-failure
  expect control no-lock pass
  expect control locks contradiction
  expect control never-minimized unreached
  expect disabled idle-on-time pass
  expect disabled idle-late contradiction
  expect disabled idle-early contradiction
  expect disabled idle-missing contradiction
  expect disabled idle-restore-touched contradiction
  expect disabled idle-deadline-drifted contradiction
  expect disabled locks contradiction
  expect disabled never-minimized unreached
  expect restart restart-same pass
  expect restart restart-changed contradiction
  expect cmd-h hidden pass
  expect cmd-h hidden-locks contradiction
  expect cmd-h hide-refused unreached

  if [ "$failures" -gt 0 ]; then
    die "observe-minimize-lock selftest: $failures of $cases cases failed"
  fi
  echo "observe-minimize-lock selftest: $cases cases"
}

case "${1:-}" in
  --selftest) selftest ;;
  --classify) [ $# -eq 3 ] || { usage >&2; exit 2; }; classify "$2" "$3" ;;
  -h|--help) usage ;;
  '') usage >&2; exit 2 ;;
  *)
    [ $# -ge 2 ] || { usage >&2; exit 2; }
    OBSERVER="$1"
    OUT="$2"
    shift 2
    run_all "$@"
    ;;
esac
