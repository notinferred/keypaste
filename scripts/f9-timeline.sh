#!/usr/bin/env bash
# Reads the F.9 timelines a pool-probe dispatch leaves in its artifacts.
#
#   scripts/f9-timeline.sh <iteration-directory>...
#   scripts/f9-timeline.sh --selftest
#
# One iteration directory holds one f9-timeline-<pid>.jsonl per test process that ran, written by
# tests/Keypaste.Core.Tests/PoolSnapshot.cs. This puts every process on one millisecond axis and
# reports each instrumented interval, longest first: what OTHER processes had open across it, and what
# else was open alongside it in the SAME process. The first lines a producer in one assembly up against
# a victim in another; the second is the one that matters for a pool, which is one per process - a
# `stall` beside a `derive` is a key derivation holding the workers that the stall was waiting for.
#
# Any `<family>-enter` / `<family>-exit` pair is an interval, so a new mark needs no change here.
#
# Read `depth` before trusting a duration. Enter and exit are paired first-in-first-out within one
# process and family, because xunit runs classes in parallel and an exit does not name the enter it
# closes. At depth 1 the pairing is certain; above it the durations are a guess and the raw lines
# are the answer. Depth above 1 is also a reading in its own right: two saves open at once in one
# process is what queueing behind the process-wide save gate looks like from outside (F.10).
#
# `--selftest` is offline and runs in ci.yml, including through a jq that writes CRLF.
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
readonly CR=$'\r'

die() { echo "::error::$*" >&2; exit 1; }

# jq.exe on Windows ends its lines with CRLF; verify-site-disclosure.sh records what that cost twice.
jqr() { command jq -r "$@" | tr -d "$CR"; }

# Ticks are QPC, which is machine-wide, so two processes' lines sort into one order. Each line is
# converted with its own frequency rather than one assumed from another process's line.
# shellcheck disable=SC2016
readonly READ='
def rpad($n): tostring | if ($n - length) > 0 then . + (" " * ($n - length)) else . end;
def lpad($n): tostring | if ($n - length) > 0 then (" " * ($n - length)) + . else . end;
def whole: . + 0.5 | floor;

(map(.ticks) | min) as $base
| (map(. + {ms: ((.ticks - $base) * 1000 / .freq)}) | sort_by(.ms)) as $rows

| (reduce $rows[] as $row ({open: {}, out: []};
    ($row.event | split("-")) as $edge
    | if ($edge | length) != 2 or ($edge[1] != "enter" and $edge[1] != "exit") then .
      else "\($row.pid)|\($edge[0])" as $key
      | if $edge[1] == "enter" then .open[$key] += [$row]
        elif $edge[1] == "exit" and ((.open[$key] // []) | length) > 0 then
          .open[$key][0] as $start
          | .open[$key] |= .[1:]
          | .out += [{
              pid: $row.pid, asm: $row.asm, family: $edge[0],
              start: $start.ms, end: $row.ms, ms: ($row.ms - $start.ms),
              label: $start.detail, detail: $row.detail,
              depth: ((.open[$key] | length) + 1)
            }]
        else . end
      end)
  ).out as $paired

| [ $paired[] as $it
    | $it + {
        overlaps: ([ $paired[]
          | select(.pid != $it.pid and .start < $it.end and .end > $it.start)
          | "\(.asm)(pid \(.pid)) \(.family)" ] | unique),
        alongside: ([ $paired[]
          | select(.pid == $it.pid and .family != $it.family and .start < $it.end and .end > $it.start)
          | .family ] | unique)
      } ]
| sort_by(-.ms) as $intervals

| ($rows | unique_by([.pid, .asm])) as $processes

| "  \($rows | length) lines, \($processes | length) process(es) over \(($rows[-1].ms - $rows[0].ms) | whole) ms",
  ($processes[] | "    pid \(.pid | rpad(7)) \(.asm)"),
  if ($intervals | length) == 0 then "  no complete intervals (only opened/guard marks)"
  else
    "  intervals, longest first:",
    ($intervals[]
      | "    \(.family | rpad(10)) \(.ms | whole | lpad(8)) ms  pid \(.pid | rpad(7)) \(.asm | rpad(26)) label=\((if .label == "" then "-" else .label end) | rpad(8)) depth=\(.depth)",
        (if .detail != "" then "        detail: \(.detail)" else empty end),
        (if (.overlaps | length) > 0 then "        overlapped by: \(.overlaps | join(", "))" else empty end),
        (if (.alongside | length) > 0 then "        alongside in this process: \(.alongside | join(", "))" else empty end)),
    ([ $intervals[] | select(.depth > 1) ] | length) as $queued
    | if $queued > 0 then
        "  QUEUEING: \($queued) interval(s) ran with another of the same family open in the SAME process -- this is what the save gate looks like"
      else empty end
  end
'

read_directory() {
  local dir="$1" files
  shopt -s nullglob
  files=("$dir"/f9-timeline-*.jsonl)
  shopt -u nullglob

  # Emptiness is decided here rather than inside jq, and the directory is printed from here: MSYS
  # rewrites a POSIX path handed to a native jq.exe as an argument, so a name passed through --arg
  # comes back as C:/... on Windows and as written everywhere else.
  if [ "${#files[@]}" -eq 0 ] || [ -z "$(cat "${files[@]}" | tr -d " \t\r\n" | head -c 1)" ]; then
    echo "$dir: no timeline lines"
    return
  fi

  echo "$dir"
  cat "${files[@]}" | jqr -s "$READ"
}

read_all() {
  command -v jq >/dev/null 2>&1 || die "no jq on PATH - run this under Git Bash, not WSL"
  local dir
  for dir in "$@"; do
    [ -d "$dir" ] || die "not a directory: $dir"
    read_directory "$dir"
    echo
  done
}

# ---------------------------------------------------------------------------
# Self-test
# ---------------------------------------------------------------------------
selftest() {
  local work real_jq fake cases=0 failures=0
  command -v jq >/dev/null 2>&1 || die "no jq on PATH - run this under Git Bash, not WSL"
  real_jq="$(command -v jq)"
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN

  # Two processes. In the Core one a save queues behind another save, so the first exit closes the
  # OLDER enter and that pairing sits at depth 2; in the Mcp one a listing is open across both, and a
  # stall is open across part of that listing. The Core process ticks at 1000 per second and the Mcp
  # one at 2000, so a reader converting every line with one process's frequency would put the listing
  # seconds away from the saves it overlaps.
  mkdir -p "$work/both" "$work/marks-only" "$work/empty"
  printf '%s\n' \
    '{"ticks":0,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"opened","detail":""}' \
    '{"ticks":100,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-enter","detail":"other"}' \
    '{"ticks":200,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-enter","detail":"doomed"}' \
    '{"ticks":4600,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-exit","detail":"4500"}' \
    '{"ticks":7000,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-exit","detail":"attempts 7; work 4100/9"}' \
    '{"ticks":7100,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"guard","detail":""}' \
    > "$work/both/f9-timeline-111.jsonl"
  # CRLF, as PoolTimeline writes on Windows, and an exit with no enter, which must be ignored.
  printf '%s\r\n' \
    '{"ticks":100,"freq":2000,"pid":222,"asm":"Keypaste.Mcp.Tests","event":"opened","detail":""}' \
    '{"ticks":2000,"freq":2000,"pid":222,"asm":"Keypaste.Mcp.Tests","event":"listing-enter","detail":""}' \
    '{"ticks":3000,"freq":2000,"pid":222,"asm":"Keypaste.Mcp.Tests","event":"stall-enter","detail":"threads 4; free 0"}'     '{"ticks":9000,"freq":2000,"pid":222,"asm":"Keypaste.Mcp.Tests","event":"stall-exit","detail":"waited 3000; threads 4->5"}'     '{"ticks":13668,"freq":2000,"pid":222,"asm":"Keypaste.Mcp.Tests","event":"listing-exit","detail":"5834"}' \
    '{"ticks":13700,"freq":2000,"pid":222,"asm":"Keypaste.Mcp.Tests","event":"credential-exit","detail":""}' \
    > "$work/both/f9-timeline-222.jsonl"
  printf '%s\n' '{"ticks":5,"freq":1000,"pid":7,"asm":"Keypaste.Core.Tests","event":"opened","detail":""}' \
    > "$work/marks-only/f9-timeline-7.jsonl"

  {
    echo "$work/both"
    echo "  12 lines, 2 process(es) over 7100 ms"
    echo "    pid 111     Keypaste.Core.Tests"
    echo "    pid 222     Keypaste.Mcp.Tests"
    echo "  intervals, longest first:"
    echo "    save           6800 ms  pid 111     Keypaste.Core.Tests        label=doomed   depth=1"
    echo "        detail: attempts 7; work 4100/9"
    echo "        overlapped by: Keypaste.Mcp.Tests(pid 222) listing, Keypaste.Mcp.Tests(pid 222) stall"
    echo "    listing        5834 ms  pid 222     Keypaste.Mcp.Tests         label=-        depth=1"
    echo "        detail: 5834"
    echo "        overlapped by: Keypaste.Core.Tests(pid 111) save"
    echo "        alongside in this process: stall"
    echo "    save           4500 ms  pid 111     Keypaste.Core.Tests        label=other    depth=2"
    echo "        detail: 4500"
    echo "        overlapped by: Keypaste.Mcp.Tests(pid 222) listing, Keypaste.Mcp.Tests(pid 222) stall"
    echo "    stall          3000 ms  pid 222     Keypaste.Mcp.Tests         label=threads 4; free 0 depth=1"
    echo "        detail: waited 3000; threads 4->5"
    echo "        overlapped by: Keypaste.Core.Tests(pid 111) save"
    echo "        alongside in this process: listing"
    echo "  QUEUEING: 1 interval(s) ran with another of the same family open in the SAME process -- this is what the save gate looks like"
    echo
  } > "$work/expected-both"
  {
    echo "$work/marks-only"
    echo "  1 lines, 1 process(es) over 0 ms"
    echo "    pid 7       Keypaste.Core.Tests"
    echo "  no complete intervals (only opened/guard marks)"
    echo
  } > "$work/expected-marks-only"
  printf '%s: no timeline lines\n\n' "$work/empty" > "$work/expected-empty"

  mkdir -p "$work/fakebin"
  printf '%s\n' \
    '#!/usr/bin/env bash' \
    'set -euo pipefail' \
    '"$KEYPASTE_REAL_JQ" "$@" | tr -d '"'"'\r'"'"' | awk '"'"'{ printf "%s\r\n", $0 }'"'"'' \
    > "$work/fakebin/jq"
  chmod +x "$work/fakebin/jq"
  export KEYPASTE_REAL_JQ="$real_jq"
  fake="$work/fakebin:$PATH"

  # Proof the fake jq really writes CRLF, counted in bytes: MSYS grep strips \r before matching.
  printf '{}' | PATH="$fake" jq -r '"x"' > "$work/probe"
  [ "$(wc -c < "$work/probe")" -gt "$(tr -d "$CR" < "$work/probe" | wc -c)" ] \
    || die "the CRLF jq shim writes no carriage return, so the CRLF case below would prove nothing"

  check() {
    local name="$1" path="$2" fixture="$3"
    cases=$((cases + 1))
    PATH="$path" bash "$SELF" "$work/$fixture" > "$work/actual" 2>&1 || true
    if cmp -s "$work/expected-$fixture" "$work/actual"; then
      echo "ok   $name"
    else
      failures=$((failures + 1))
      echo "FAIL $name"
      diff "$work/expected-$fixture" "$work/actual" | sed 's/^/     /' || true
    fi
  }

  check "pairs, nests and overlaps across two processes and two frequencies" "$PATH" both
  check "the same, through a jq that writes CRLF" "$fake" both
  check "a process with marks and no interval says so" "$PATH" marks-only
  check "a directory with no timeline says so" "$PATH" empty

  echo "$((cases - failures)) of $cases cases passed"
  [ "$failures" -eq 0 ] || die "the F.9 timeline reader does not read what it claims"
}

case "${1:-}" in
  --selftest) selftest ;;
  "" | -h | --help)
    sed -n '4,5p' "$SELF" | sed 's/^# *//'
    [ -n "${1:-}" ] || exit 2
    ;;
  *) read_all "$@" ;;
esac
