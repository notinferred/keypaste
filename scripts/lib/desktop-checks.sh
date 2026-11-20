# shellcheck shell=bash
# The records the install and upgrade exercises keep in <output-dir> and classify into checks: driver.jsonl, each
# action's status and output, and facts.jsonl, what was observed. Sourced after the exercise defines OUT.

die() { echo "::error::$*" >&2; exit 1; }

# A path as the Windows programs these exercises start read it, with forward slashes.
native() { if [ "$(platform)" = windows ]; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

verdict() { jq -cn --arg check "$1" --arg result "$2" --arg why "$3" '{check: $check, result: $result, why: $why}'; }

fact_of() { [ -s "$1/facts.jsonl" ] && jq -rs --arg k "$2" '[.[] | select(.fact == $k) | .value] | last // empty' "$1/facts.jsonl" || true; }

step() { [ -s "$1/driver.jsonl" ] && jq -rs --arg a "$2" '[.[] | select(.action == $a)] | last | if . == null then "" else "\(.status)\t\(.detail)" end' "$1/driver.jsonl" || true; }

fact() { jq -cn --arg fact "$1" --arg value "$2" '{fact: $fact, value: $value}' >> "$OUT/facts.jsonl"; }

record() { jq -cn --arg action "$1" --arg status "$2" --arg detail "$3" '{action: $action, status: $status, detail: $detail}' >> "$OUT/driver.jsonl"; }

# act <name> <command...>: runs it, records ok or refused with its output, and returns its status.
act() {
  local name="$1" status=ok
  shift
  # Not a subshell: what the command sets is kept for the steps after it.
  "$@" > "$OUT/act.log" 2>&1 || status=refused
  record "$name" "$status" "$(tail -n 20 "$OUT/act.log" | tr -d '\r')"
  [ "$status" = ok ]
}

# A fresh home's first-launch screen agreed to, then the vault unlocked with <password> until the shell shows.
agree_and_unlock() {
  act agree-terms drive invoke id:Agree && act password-focus drive focus id:Password \
    && act unlocked drive type "$1" && act unlocked-enter drive key Return && act unlocked drive find id:LockButton
}

# Settings links keypaste, once unlocked, as a person does: on macOS the link to <bundle>'s keypaste is made with
# sudo while Link is offered, since a runner has nobody to answer the administrator dialog, so Link then finds this
# copy already linked and asks for nothing.
link_from_settings() { # link_from_settings <bundle>
  act settings go RailSettings || return 1
  if [ "$(platform)" = macos ]; then
    act link-offered drive find id:LinkCli && act link-made mac_cli_link "$1/Contents/MacOS/keypaste" || return 1
  fi
  act link-cli drive invoke id:LinkCli && act linked says id:CliLinkLine "A terminal runs this app's keypaste from "
}
