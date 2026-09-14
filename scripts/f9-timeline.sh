#!/usr/bin/env bash
# Reads the F.9 timelines a pool-probe dispatch leaves in its artifacts.
#
#   scripts/f9-timeline.sh <iteration-directory>...
#   scripts/f9-timeline.sh --saves <iteration-directory>...
#   scripts/f9-timeline.sh --selftest
#
# `--saves` reads the save-timing lines instead (F.10a): each save a test labelled with `save-op`, its
# first interval split into check, redirect, gate wait and first-attempt work with the largest named,
# the operations that held the gate while it waited, a tally of what dominated, and every gate hold
# kept through a retry sleep (F.12). A label whose operation has no save-timing line fails the reader
# rather than being left out.
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

# A save-timing mark is written as the save returns, so each save ends at its mark and starts `total`
# earlier. Since F.12 every list has one value per attempt, in the order gate wait, re-read, work,
# then the sleep after it, and `held` is each attempt's hold from taking the gate to releasing it;
# `-` is a step not taken. A hold spans a retry sleep when it outlasts its re-read and work by half
# the sleep after it. Lines without `held` held one gate, taken before attempt `gatedat` and kept
# until before the stamp, so any sleep after that attempt was spanned; lines without `gatedat`
# predate F.10b, when every save gated before its first attempt.
# shellcheck disable=SC2016
readonly READ_SAVES='
def lpad($n): tostring | if ($n - length) > 0 then (" " * ($n - length)) + . else . end;
def rpad($n): tostring | if ($n - length) > 0 then . + (" " * ($n - length)) else . end;
def whole: . + 0.5 | floor;
def fields: split(" ") | map(split("=") | {key: .[0], value: .[1]}) | from_entries;
def num: if . == "-" then 0 else tonumber end;
def list: if . == "-" then [] else split("/") | map(tonumber) end;
def slots: split("/") | map(if . == "-" then null else tonumber end);

def perattempt($f; $start):
  ($f.gate | slots) as $g | ($f.held | slots) as $h | ($f.heldby | slots) as $hb
  | ($f.rereads | slots) as $r | ($f.work | slots) as $w | ($f.waits | slots) as $ws
  | reduce range(0; [$g, $h, $r, $w, $ws] | map(length) | max) as $i ({t: $start, a: []};
      .t as $t | ($g[$i] // 0) as $gw
      | .a += [{ gated: ($h[$i] != null), waitStart: $t, holdStart: ($t + $gw),
                 holdEnd: (if $h[$i] == null then null else $t + $gw + $h[$i] end), heldby: $hb[$i],
                 spans: ($h[$i] != null and ($ws[$i] // 0) > 0
                   and ($h[$i] - ($r[$i] // 0) - ($w[$i] // 0)) >= (($ws[$i] // 0) / 2)) }]
      | .t = $t + $gw + ($r[$i] // 0) + ($w[$i] // 0) + ($ws[$i] // 0))
  | .a;

def single($f; $s):
  if $f.gate == "-" then [] else
    ($f.gatedat // "1" | tonumber) as $at
    | ($f.work | list) as $w | ($f.waits | list) as $ws | ($f.rereads | list) as $r
    | ([range(0; $at - 1)] | map(($w[.] // 0) + ($ws[.] // 0) + ($r[.] // 0)) | add // 0) as $before
    | ($s.end - $s.total + $s.check + $s.redirect + $before) as $waitStart
    | [ range(0; $at - 1) | {gated: false} ]
      + [ { gated: true, waitStart: $waitStart, holdStart: ($waitStart + ($f.gate | num)),
            holdEnd: ($s.end - $s.stamp), heldby: ($f.heldby | tonumber), spans: (($ws | length) >= $at) } ]
  end;

(map(.ticks) | min) as $base
| map(. + {ms: ((.ticks - $base) * 1000 / .freq)}) as $rows

| [ $rows[] | select(.event == "save-timing") | (.detail | fields) as $f
    | { pid, end: .ms, op: ($f.op | tonumber), ok: $f.ok,
        check: ($f.check | num), redirect: ($f.redirect | num), stamp: ($f.stamp | num), total: ($f.total | num),
        gate: (($f.gate | split("/"))[0] | num), work: (($f.work | split("/"))[0] | num) }
    | . + { attempts: (if $f.held == null then single($f; .) else perattempt($f; .end - .total + .check + .redirect) end) }
    | . + { gate: (if (.attempts[0].gated // false) then .gate else 0 end),
            holds: [ .attempts[] | select(.gated) ] } ] as $saves

| [ $rows[] | select(.event == "save-op") | (.detail | fields) as $f
    | { pid, label: $f.label, op: ($f.op | tonumber), at: .ms } ] | sort_by(.at) as $labels

| [ $labels[] as $l | select([ $saves[] | select(.pid == $l.pid and .op == $l.op) ] | length == 0)
    | "MISSING save-op \($l.label) names op \($l.op) in pid \($l.pid), which has no save-timing line" ] as $missing

| if ($missing | length) > 0 then $missing[] else

[ $labels[] as $l
    | ([ $saves[] | select(.pid == $l.pid and .op == $l.op) ] | first) as $s
    | { check: $s.check, redirect: $s.redirect, gate: $s.gate, work: $s.work } as $parts
    | ($s.holds | first) as $mine
    | $s + $parts + {
        label: $l.label,
        first: ($parts | add),
        dominant: ($parts | to_entries | max_by(.value) | if .value < 1 then "none" else .key end),
        holders: (if $mine == null then [] else
          [ $saves[] | select(.pid == $s.pid and .op != $s.op) | .op as $op | .holds[]
            | select(.holdStart < $mine.holdStart and .holdEnd > $mine.waitStart)
            | "op \($op) for \(([.holdEnd, $mine.holdStart] | min) - ([.holdStart, $mine.waitStart] | max) | whole) ms" ]
          end)
      } ] as $read

| [ $saves[] | . as $s | .holds[] | select(.spans) | "op \($s.op) in pid \($s.pid)" ] as $spanning

| ( "  \($saves | length) saves timed in \($saves | map(.pid) | unique | length) process(es), \($read | length) labelled",
  ($read[]
    | "    \(.label | rpad(8)) pid \(.pid | rpad(7)) op \(.op | rpad(5)) ok \(.ok)  total \(.total | whole | lpad(6)) ms  first \(.first | whole | lpad(6)) ms = check \(.check) + redirect \(.redirect) + gate \(.gate) + work \(.work)  dominant \(.dominant)",
      (if (.holders | length) > 0 then "        gate held by: \(.holders | join(", "))" else empty end)),
  (if ($read | length) > 0 then
    "  first intervals dominated by: \($read | group_by(.dominant) | map("\(.[0].dominant) \(length)") | join(", "))"
  else empty end),
  "  gate holds spanning a retry sleep: \($spanning | length)\(if ($spanning | length) > 0 then " (\($spanning | unique | join(", ")))" else "" end)" )
end
'

read_saves_directory() {
  local dir="$1" files
  shopt -s nullglob
  files=("$dir"/f9-timeline-*.jsonl)
  shopt -u nullglob

  if [ "${#files[@]}" -eq 0 ] || ! cat "${files[@]}" | tr -d "$CR" | grep -F '"event":"save-timing"' >/dev/null; then
    echo "$dir: no save-timing lines"
    return
  fi

  local read
  read="$(cat "${files[@]}" | jqr -s "$READ_SAVES")" || die "the save-timing lines in $dir could not be read"
  case "$read" in
    MISSING*) die "${read#MISSING }" ;;
  esac

  echo "$dir"
  printf '%s
' "$read"
}

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
  local reader="$1" dir
  shift
  [ "$#" -gt 0 ] || die "no iteration directory given"
  for dir in "$@"; do
    [ -d "$dir" ] || die "not a directory: $dir"
    "$reader" "$dir"
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

  # Op 1 holds the gate across a 2000 ms wait; op 2 waits 1500 ms of it and op 3 works alone. Op 3's
  # stamp keeps its hold from reaching its own mark. Op 4 runs inside op 1's hold without gating, so
  # it names no holder; op 1 predates `gatedat`. `unlabelled` names an op nothing timed.
  mkdir -p "$work/saves" "$work/unlabelled" "$work/retried"
  printf '%s\r\n' \
    '{"ticks":0,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"opened","detail":""}' \
    '{"ticks":3000,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-timing","detail":"op=1 heldby=0 ok=0 check=0 redirect=0 gate=0 work=0/0 waits=2000 rereads=0 stamp=- total=2000"}' \
    '{"ticks":3100,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-timing","detail":"op=2 heldby=1 ok=0 check=10 redirect=0 gate=1500 gatedat=1 work=5 waits=- rereads=- stamp=- total=1600"}' \
    '{"ticks":2500,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-timing","detail":"op=4 heldby=0 ok=0 check=0 redirect=0 gate=- gatedat=- work=3 waits=- rereads=- stamp=- total=5"}'     '{"ticks":2501,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-op","detail":"label=ungated op=4"}'     '{"ticks":3101,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-op","detail":"label=doomed op=2"}' \
    '{"ticks":5000,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-timing","detail":"op=3 heldby=0 ok=1 check=0 redirect=0 gate=0 gatedat=1 work=400 waits=- rereads=- stamp=20 total=430"}' \
    '{"ticks":5001,"freq":1000,"pid":111,"asm":"Keypaste.Core.Tests","event":"save-op","detail":"label=budget op=3"}' \
    > "$work/saves/f9-timeline-111.jsonl"
  printf '%s\n' \
    '{"ticks":0,"freq":1000,"pid":7,"asm":"Keypaste.Core.Tests","event":"save-timing","detail":"op=1 heldby=0 ok=1 check=0 redirect=0 gate=0 work=9 waits=- rereads=- stamp=1 total=10"}' \
    '{"ticks":1,"freq":1000,"pid":7,"asm":"Keypaste.Core.Tests","event":"save-op","detail":"label=doomed op=4"}' \
    > "$work/unlabelled/f9-timeline-7.jsonl"

  {
    echo "$work/saves"
    echo "  4 saves timed in 1 process(es), 3 labelled"
    echo "    ungated  pid 111     op 4     ok 0  total      5 ms  first      3 ms = check 0 + redirect 0 + gate 0 + work 3  dominant work"
    echo "    doomed   pid 111     op 2     ok 0  total   1600 ms  first   1515 ms = check 10 + redirect 0 + gate 1500 + work 5  dominant gate"
    echo "        gate held by: op 1 for 1490 ms"
    echo "    budget   pid 111     op 3     ok 1  total    430 ms  first    400 ms = check 0 + redirect 0 + gate 0 + work 400  dominant work"
    echo "  first intervals dominated by: gate 1, work 2"
    echo "  gate holds spanning a retry sleep: 1 (op 1 in pid 111)"
    echo
  } > "$work/expected-saves"
  # F.12's shape. Op 6 kept its first hold through the 600 ms sleep after it, which is the defect;
  # op 5 released before its sleep, and its first gate wait overlaps only op 6's second hold.
  printf '%s\n' \
    '{"ticks":0,"freq":1000,"pid":333,"asm":"Keypaste.Core.Tests","event":"opened","detail":""}' \
    '{"ticks":1790,"freq":1000,"pid":333,"asm":"Keypaste.Core.Tests","event":"save-timing","detail":"op=6 ok=0 check=0 redirect=0 gate=0/0 heldby=0/0 held=700/80 rereads=0/0 work=100/80 waits=600/- stamp=- total=790"}' \
    '{"ticks":2480,"freq":1000,"pid":333,"asm":"Keypaste.Core.Tests","event":"save-timing","detail":"op=5 ok=1 check=0 redirect=0 gate=30/2 heldby=6/0 held=100/90 rereads=0/1 work=100/88 waits=500/- stamp=5 total=730"}' \
    '{"ticks":2481,"freq":1000,"pid":333,"asm":"Keypaste.Core.Tests","event":"save-op","detail":"label=retried op=5"}' \
    > "$work/retried/f9-timeline-333.jsonl"
  {
    echo "$work/retried"
    echo "  2 saves timed in 1 process(es), 1 labelled"
    echo "    retried  pid 333     op 5     ok 1  total    730 ms  first    130 ms = check 0 + redirect 0 + gate 30 + work 100  dominant work"
    echo "        gate held by: op 6 for 30 ms"
    echo "  first intervals dominated by: work 1"
    echo "  gate holds spanning a retry sleep: 1 (op 6 in pid 333)"
    echo
  } > "$work/expected-retried"

  printf '%s\n' '::error::save-op doomed names op 4 in pid 7, which has no save-timing line' \
    > "$work/expected-unlabelled"
  printf '%s: no save-timing lines\n\n' "$work/empty" > "$work/expected-saves-empty"

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
    local name="$1" path="$2" fixture="$3" expected="$4"
    shift 4
    cases=$((cases + 1))
    PATH="$path" bash "$SELF" "$@" "$work/$fixture" > "$work/actual" 2>&1 || true
    if cmp -s "$work/expected-$expected" "$work/actual"; then
      echo "ok   $name"
    else
      failures=$((failures + 1))
      echo "FAIL $name"
      diff "$work/expected-$expected" "$work/actual" | sed 's/^/     /' || true
    fi
  }

  check "pairs, nests and overlaps across two processes and two frequencies" "$PATH" both both
  check "the same, through a jq that writes CRLF" "$fake" both both
  check "a process with marks and no interval says so" "$PATH" marks-only marks-only
  check "a directory with no timeline says so" "$PATH" empty empty
  check "splits first intervals, names gate holders and tallies what dominated" "$PATH" saves saves --saves
  check "the same, through a jq that writes CRLF" "$fake" saves saves --saves
  check "reads each attempt's hold and counts one kept through a retry sleep" "$PATH" retried retried --saves
  check "a labelled save with no timing fails the reader" "$PATH" unlabelled unlabelled --saves
  check "a directory with no save timings says so" "$PATH" empty saves-empty --saves

  echo "$((cases - failures)) of $cases cases passed"
  [ "$failures" -eq 0 ] || die "the F.9 timeline reader does not read what it claims"
}

case "${1:-}" in
  --selftest) selftest ;;
  --saves) shift; read_all read_saves_directory "$@" ;;
  "" | -h | --help)
    sed -n '4,6p' "$SELF" | sed 's/^# *//'
    [ -n "${1:-}" ] || exit 2
    ;;
  *) read_all read_directory "$@" ;;
esac
