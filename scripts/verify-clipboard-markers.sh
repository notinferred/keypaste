#!/usr/bin/env bash
# N.12: what the platform clipboard offers after the app copies a secret, after its clear and after a
# plain copy. Reads type names only, never a value.
set -euo pipefail

readonly CONCEALED=org.nspasteboard.ConcealedType
readonly TRANSIENT=org.nspasteboard.TransientType
readonly HINT=x-kde-passwordManagerHint

usage() {
  echo 'usage: bash scripts/verify-clipboard-markers.sh <observer-executable> <output-dir> | --selftest'
}

die() { echo "::error::$*" >&2; exit 1; }

# --- judging a reading ----------------------------------------------------------------------------

# $1 phase (ready, cleared, plain), $2 the pasteboard's items as a JSON array of type-name arrays.
check_macos() {
  local phase="$1" items="$2" marked texts
  marked="$(jq --arg c "$CONCEALED" --arg t "$TRANSIENT" '[.[] | select(index($c) or index($t))] | length' <<<"$items")"
  texts="$(jq '[.[] | select(index("public.utf8-plain-text"))] | length' <<<"$items")"

  case "$phase" in
    ready)
      [ "$(jq length <<<"$items")" -eq 1 ] || { echo "a secret should be one pasteboard item: $items"; return 1; }
      jq -e --arg c "$CONCEALED" --arg t "$TRANSIENT" '.[0] | index("public.utf8-plain-text") and index($c) and index($t)' <<<"$items" >/dev/null \
        || { echo "the secret's item lacks the text or a marker: $items"; return 1; } ;;
    cleared)
      [ "$marked" -eq 0 ] || { echo "a marker outlived the clear: $items"; return 1; } ;;
    plain)
      [ "$texts" -ge 1 ] || { echo "the plain copy is not on the pasteboard: $items"; return 1; }
      [ "$marked" -eq 0 ] || { echo "a plain copy carries a marker: $items"; return 1; } ;;
    *) die "no phase $phase" ;;
  esac
}

# $1 phase, $2 the CLIPBOARD selection's TARGETS one per line (empty when nobody owns it), $3 the hint's content.
check_linux() {
  local phase="$1" targets="$2" hint="$3"
  local has_text=false has_hint=false
  grep -qx 'UTF8_STRING' <<<"$targets" && has_text=true
  grep -qx "$HINT" <<<"$targets" && has_hint=true

  case "$phase" in
    ready)
      [ "$has_text" = true ] && [ "$has_hint" = true ] || { echo "the secret's targets lack the text or the hint: $(tr '\n' ' ' <<<"$targets")"; return 1; }
      [ "$hint" = secret ] || { echo "the hint reads '$hint', not 'secret'"; return 1; } ;;
    cleared)
      [ "$has_hint" = false ] || { echo "the hint outlived the clear"; return 1; } ;;
    plain)
      [ "$has_text" = true ] || { echo "the plain copy is not on the clipboard: $(tr '\n' ' ' <<<"$targets")"; return 1; }
      [ "$has_hint" = false ] || { echo "a plain copy carries the hint"; return 1; } ;;
    *) die "no phase $phase" ;;
  esac
}

# --- reading the platform -------------------------------------------------------------------------

read_macos() {
  osascript -l JavaScript -e '
    ObjC.import("AppKit");
    const items = $.NSPasteboard.generalPasteboard.pasteboardItems;
    const out = [];
    for (let i = 0; i < items.count; i++) {
      const types = items.objectAtIndex(i).types;
      const names = [];
      for (let j = 0; j < types.count; j++) names.push(ObjC.unwrap(types.objectAtIndex(j)));
      out.push(names);
    }
    JSON.stringify(out);'
}

read_targets() { xclip -selection clipboard -o -t TARGETS 2>/dev/null || true; }

read_hint() { xclip -selection clipboard -o -t "$HINT" 2>/dev/null || true; }

# $1 phase, $2 output dir: reads, records the type names and judges them.
observe() {
  local phase="$1" out="$2" items targets hint verdict=pass why=''
  case "$(uname -s)" in
    Darwin)
      items="$(read_macos)"
      why="$(check_macos "$phase" "$items")" || verdict=fail
      jq -cn --arg phase "$phase" --argjson items "$items" --arg verdict "$verdict" --arg why "$why" \
        '{phase: $phase, platform: "macos", items: $items, verdict: $verdict, why: $why}' >> "$out/readings.jsonl" ;;
    Linux)
      targets="$(read_targets)"
      hint="$(read_hint)"
      why="$(check_linux "$phase" "$targets" "$hint")" || verdict=fail
      jq -cn --arg phase "$phase" --arg targets "$targets" --arg hint "$hint" --arg verdict "$verdict" --arg why "$why" \
        '{phase: $phase, platform: "linux", targets: ($targets | split("\n") | map(select(length > 0))), hint: $hint, verdict: $verdict, why: $why}' >> "$out/readings.jsonl" ;;
    *) die "no clipboard reader for $(uname -s)" ;;
  esac
  echo "$phase: $verdict${why:+ ($why)}"
  [ "$verdict" = pass ]
}

wait_for() {
  local event="$1" events="$2" _
  for _ in $(seq 1 600); do
    grep -q "\"event\":\"$event\"" "$events" && return 0
    if grep -q '"event":"refused"\|"event":"exit"' "$events"; then
      die "the observer stopped before $event: $(tail -n 3 "$events")"
    fi
    sleep 0.25
  done
  die "the observer never reported $event"
}

run_observer() {
  local observer="$1" out="$2" home failed=0 pid
  mkdir -p "$out"
  : > "$out/readings.jsonl"
  home="$(mktemp -d)"
  rm -f "$out/quit" "$out/quit.plain"

  KEYPASTE_HOME="$home" "$observer" --scenario markers --quit-file "$out/quit" --deadline-seconds 150 \
    > "$out/events.jsonl" 2> "$out/observer.stderr" &
  pid=$!

  wait_for ready "$out/events.jsonl"
  observe ready "$out" || failed=1
  wait_for cleared "$out/events.jsonl"
  observe cleared "$out" || failed=1
  touch "$out/quit.plain"
  wait_for plain "$out/events.jsonl"
  observe plain "$out" || failed=1

  touch "$out/quit"
  wait "$pid" || die "the observer exited $?"
  rm -rf "$home"
  cat "$out/readings.jsonl"
  [ "$failed" -eq 0 ] || die "the clipboard did not offer what V-N.12 requires; see $out/readings.jsonl"
  echo "ok: each reading in $out/readings.jsonl passed"
}

# --- self-test ------------------------------------------------------------------------------------

expect() {
  local want="$1" got; shift
  if "$@" >/dev/null; then got=pass; else got=fail; fi
  [ "$got" = "$want" ] || die "selftest: expected $want from $*"
}

selftest() {
  local secret='[["public.utf8-plain-text","org.nspasteboard.ConcealedType","org.nspasteboard.TransientType","ExcludeClipboardContentFromMonitorProcessing"]]'
  expect pass check_macos ready "$secret"
  expect fail check_macos ready '[["public.utf8-plain-text","org.nspasteboard.ConcealedType"]]'
  expect fail check_macos ready '[["public.utf8-plain-text"],["org.nspasteboard.ConcealedType","org.nspasteboard.TransientType"]]'
  expect pass check_macos cleared '[]'
  expect fail check_macos cleared "$secret"
  expect pass check_macos plain '[["public.utf8-plain-text"]]'
  expect fail check_macos plain "$secret"

  local targets=$'TARGETS\nUTF8_STRING\nx-kde-passwordManagerHint\nCanIncludeInClipboardHistory'
  expect pass check_linux ready "$targets" secret
  expect fail check_linux ready "$targets" ''
  expect fail check_linux ready $'TARGETS\nUTF8_STRING' ''
  expect pass check_linux cleared '' ''
  expect fail check_linux cleared "$targets" secret
  expect pass check_linux plain $'TARGETS\nUTF8_STRING' ''
  expect fail check_linux plain "$targets" secret
  echo 'ok: verify-clipboard-markers judges its fixtures as expected'
}

case "${1:-}" in
  --selftest) selftest ;;
  -h|--help|'') usage ;;
  *) [ "$#" -eq 2 ] || { usage; exit 2; }; run_observer "$1" "$2" ;;
esac
