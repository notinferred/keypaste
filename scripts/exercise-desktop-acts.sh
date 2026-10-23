#!/usr/bin/env bash
# R.1a: performs R.1a's acts (docs/STEPS.md) in the desktop app exercise-desktop-install.sh installed on this runner,
# on a disposable vault KeePassXC made, with a real AI tool's registration, a real MCP client and a real child
# process, and classifies each act from what the app, the vault, the child, the client and the audit log recorded.
#
# The app carries no automation hook (D-0199). It is driven from outside through scripts/lib/desktop-drive.sh's
# verbs, which each platform's driver implements, and a secret-bearing control, which has no automation name, is
# only typed into. A value is read only from an act's own evidence: the child's report, the client's reply, the
# audit log or the vault through the CLI, and only its SHA-256 is recorded.
#
# The acts run in the default home, ~/.keypaste, as a person's first run does: the first unlock chooses the vault,
# so Agents' Connect registers the bridge without a vault path (D-0389). KeePassXC makes the vault and opens it
# once, as verify-keepassxc-first-run.sh has it do, so the first run offers it from KeePassXC's recent databases.
#
# The acts, in order, and the acts each needs:
#   first-run      KeePassXC's database offered on the first run, chosen and unlocked
#   field          a key kept as a custom field from the item's pane                                  first-run
#   notes-flag     a KEY=value line in notes flagged in Settings' Recommendations and moved to a field first-run
#   project-run    the item tagged into a project, whose Run starts a child that receives the field   field
#   edit-rerun     the value replaced, and the next Run's child receives the new one                  project-run
#   connect        Agents' Connect registers Claude Code without a vault path; that command answers   first-run
#   approve-deny   its request allowed once in the app's prompt, and a second one denied              connect, field
#   restore        a revision, an item from Trash and a backup restored                               field
#   change-access  the master password changed in Settings: the old one refused, the new one unlocks  first-run
#   lock-refused   Lock, and a request after it refused as vault-locked                               connect
#   timing         first-run's unlock and field's save, timed                                         first-run
# An act passes only on its evidence. It is unreached when one of its steps could not be done, a verb this
# platform's driver does not implement among them, or an act it needs did not pass; a contradiction when its steps
# completed and the evidence disagrees; and person when only a person can do it on a hosted runner, with the reason.
# Every act but a pass or a person fails the run; a person's act leaves R.1a's row open for that person.
#
# Usage:
#   exercise-desktop-acts.sh <rid> <candidate-file> <install-output-dir> <cli-dir> <output-dir>
#   exercise-desktop-acts.sh --classify <output-dir>
#   exercise-desktop-acts.sh --summary <output-dir>
#   exercise-desktop-acts.sh --selftest
# Environment: KPXC_CLI         keepassxc-cli (default: PATH), with KeePassXC's app beside it or KPXC_APP
#              REHEARSAL        the app.yml run whose branch build the candidate is, for a rehearsal
#              PACKAGE_COMMIT   the commit the candidate was built from
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
ROOT="$(cd "$(dirname "$SELF")/.." && pwd)"
readonly ROOT
readonly ACTS=(first-run field notes-flag project-run edit-rerun connect approve-deny restore change-access lock-refused timing)
readonly MASTER='ActsMaster1a7'
readonly NEW_MASTER='ActsChanged1a7'
readonly VAULT_NAME='acts.kdbx'
readonly ENTRY_FIELD='acts-field'
readonly FIELD='ACTS_FIELD'
readonly ENTRY_NOTES='acts-notes'
readonly NOTE_KEY='ACTS_NOTE_KEY'
readonly ENTRY_SCRATCH='acts-scratch'
readonly PROJECT='acts'
readonly TOOL='Claude Code'
readonly PROMPT='keypaste: an agent is asking for a credential'
readonly CR=$'\r'
readonly TAB=$'\t'

# shellcheck source=scripts/lib/common.sh
. "$ROOT/scripts/lib/common.sh"
# shellcheck source=scripts/lib/desktop-drive.sh
. "$ROOT/scripts/lib/desktop-drive.sh"
# shellcheck source=scripts/lib/macos-install.sh
. "$ROOT/scripts/lib/macos-install.sh"

usage() {
  echo 'usage: bash scripts/exercise-desktop-acts.sh <rid> <candidate-file> <install-output-dir> <cli-dir> <output-dir> | --classify <output-dir> | --summary <output-dir> | --selftest'
}

# The acts an act needs to have passed.
needs() {
  case "$1" in
    field | notes-flag | connect | change-access | timing) echo first-run ;;
    project-run | restore) echo field ;;
    edit-rerun) echo project-run ;;
    approve-deny) echo connect field ;;
    lock-refused) echo connect ;;
  esac
}

# --- classification -------------------------------------------------------------------------------

verdict() { jq -cn --arg act "$1" --arg result "$2" --arg why "$3" '{act: $act, result: $result, why: $why}'; }

fact_of() {
  [ -s "$1/facts.jsonl" ] || return 0
  jq -rs --arg a "$2" --arg k "$3" '[.[] | select(.act == $a and .fact == $k) | .value] | last // empty' "$1/facts.jsonl" | tr -d "$CR"
}

step_detail() {
  [ -s "$1/steps.jsonl" ] || return 0
  jq -rs --arg a "$2" --arg s "$3" '[.[] | select(.act == $a and .step == $s) | .detail] | last // empty' "$1/steps.jsonl" | tr -d "$CR"
}

# The first step of an act that did not finish, as <status><TAB><step><TAB><detail>, or nothing.
unfinished() {
  [ -s "$1/steps.jsonl" ] || return 0
  jq -rs --arg a "$2" '[.[] | select(.act == $a and .status != "ok")] | first | if . == null then "" else "\(.status)\t\(.step)\t\(.detail)" end' \
    "$1/steps.jsonl" | tr -d "$CR"
}

why_unfinished() {
  local status="${1%%"$TAB"*}" rest="${1#*"$TAB"}" step detail
  step="${rest%%"$TAB"*}"
  detail="${rest#*"$TAB"}"
  if [ "$status" = not-implemented ]; then
    echo "$step needs what this platform's driver does not implement: $detail"
  else
    echo "$step: $detail"
  fi
}

pass() { result=pass; why="$1"; }
contradict() { result=contradiction; why="$1"; }
measured() { [[ $1 =~ ^[0-9]+$ ]]; }
lowercase() { printf '%s' "$1" | tr '[:upper:]' '[:lower:]'; }

# A path as two spellings of it compare: forward slashes, and on Windows without regard to case.
path_key() {
  local path
  path="$(printf '%s' "$1" | tr '\\' '/')"
  if [ "$2" = windows ]; then lowercase "$path"; else printf '%s' "$path"; fi
}

evidence_first_run() {
  local chosen vault platform
  chosen="$(fact_of "$1" first-run chosen_vault)"
  vault="$(fact_of "$1" setup vault)"
  platform="$(fact_of "$1" run platform)"
  if [ -z "$chosen" ]; then contradict 'the app unlocked and chose no vault for agents and the CLI in app.toml'
  elif [ "$(path_key "$chosen" "$platform")" != "$(path_key "$vault" "$platform")" ]; then
    contradict "the app chose $chosen, not KeePassXC's $vault"
  else pass "KeePassXC's recent databases offered $VAULT_NAME on the first run, and the app unlocked it and chose it: $vault; KeePassXC $(fact_of "$1" first-run keepassxc_closed)"
  fi
}

evidence_field() {
  local saved got want
  saved="$(fact_of "$1" field save_ms)"
  got="$(fact_of "$1" field field_sha256)"
  want="$(fact_of "$1" setup value1_sha256)"
  if ! measured "$saved"; then contradict 'Add completed and the vault file did not change'
  elif [ -z "$got" ] || [ "$got" = absent ]; then contradict "the vault, read through the CLI, holds no $FIELD on $ENTRY_FIELD"
  elif [ "$got" != "$want" ]; then contradict "the CLI reads a $FIELD whose SHA-256 is $got, not the typed value's $want"
  else pass "the CLI reads the value typed into the item's pane from $ENTRY_FIELD's $FIELD field"
  fi
}

evidence_notes_flag() {
  local moved got want notes
  moved="$(fact_of "$1" notes-flag moved_ms)"
  got="$(fact_of "$1" notes-flag field_sha256)"
  want="$(fact_of "$1" setup note_sha256)"
  notes="$(fact_of "$1" notes-flag notes_hold_line)"
  if ! measured "$moved"; then contradict 'Move to a field completed and the vault file did not change'
  elif [ "$got" != "$want" ]; then contradict "after the move the CLI reads ${got:-no} $NOTE_KEY on $ENTRY_NOTES, not the note's value ($want)"
  elif [ "$notes" = true ]; then contradict "the notes still hold the $NOTE_KEY= line after the move"
  elif [ "$notes" != false ]; then result=unreached; why="KeePassXC could not read the notes after the move: $notes"
  else pass "Recommendations flagged '$(step_detail "$1" notes-flag flagged)', and the move made $NOTE_KEY a field with the note's value and took the line from the notes"
  fi
}

evidence_project_run() {
  local tagged got want
  tagged="$(fact_of "$1" project-run tagged_ms)"
  got="$(fact_of "$1" project-run child_field_sha256)"
  want="$(fact_of "$1" setup value1_sha256)"
  if ! measured "$tagged"; then contradict 'adding the project tag completed and the vault file did not change'
  elif [ -z "$got" ] || [ "$got" = unset ]; then contradict "the child the Run started ran without $FIELD: $(fact_of "$1" project-run child)"
  elif [ "$got" != "$want" ]; then contradict "the child received a $FIELD whose SHA-256 is $got, not the field's $want"
  else pass "the project's Run started a child that received $FIELD with the field's value"
  fi
}

evidence_edit_rerun() {
  local replaced stored got old new
  replaced="$(fact_of "$1" edit-rerun replaced_ms)"
  stored="$(fact_of "$1" edit-rerun field_sha256)"
  got="$(fact_of "$1" edit-rerun child_field_sha256)"
  old="$(fact_of "$1" setup value1_sha256)"
  new="$(fact_of "$1" setup value2_sha256)"
  if ! measured "$replaced"; then contradict 'Replace completed and the vault file did not change'
  elif [ "$stored" != "$new" ]; then contradict "after the replace the CLI reads a $FIELD whose SHA-256 is ${stored:-nothing}, not the new value's"
  elif [ "$got" = "$old" ]; then contradict 'the next Run'\''s child still received the old value'
  elif [ "$got" != "$new" ]; then contradict "the next Run's child received a $FIELD whose SHA-256 is ${got:-nothing}, not the new value's"
  else pass "after the value was replaced in the app, the next Run's child received the new one"
  fi
}

evidence_connect() {
  local registration vaulted tools
  registration="$(fact_of "$1" connect registration)"
  vaulted="$(fact_of "$1" connect has_vault)"
  tools="$(fact_of "$1" connect tools)"
  if [ -z "$registration" ]; then contradict "Run it completed and Claude Code's configuration holds no keypaste"
  elif [ "$vaulted" != false ]; then contradict "Claude Code was registered with a vault path: $registration"
  elif [[ " $tools " != *' request_credential '* ]]; then contradict "the registered command answered without request_credential among its tools: ${tools:-none}"
  else pass "Agents' Connect registered '$(fact_of "$1" connect command)' for Claude Code with no vault path, and that command answered as $(fact_of "$1" connect server)"
  fi
}

evidence_approve_deny() {
  local d="$1" expected allowed
  expected="$(fact_of "$d" approve-deny expected_sha256)"
  allowed="$(fact_of "$d" approve-deny allow_sha256)"
  if [ "$(fact_of "$d" approve-deny allow_error)" != false ]; then contradict 'the request allowed once came back as an error'
  elif [ "$allowed" != "$expected" ]; then contradict "the client received a value whose SHA-256 is ${allowed:-none}, not the field's $expected"
  elif [ "$(fact_of "$d" approve-deny allow_audit)" != granted/prompt ]; then
    contradict "the audit log records the allowed request as $(fact_of "$d" approve-deny allow_audit), not granted/prompt"
  elif [ "$(fact_of "$d" approve-deny deny_error)" != true ]; then contradict 'the denied request was not answered as an error'
  elif [ "$(fact_of "$d" approve-deny deny_value)" != none ]; then contradict 'the denied request carried a value'
  elif [ "$(fact_of "$d" approve-deny deny_audit)" != denied/prompt ]; then
    contradict "the audit log records the denied request as $(fact_of "$d" approve-deny deny_audit), not denied/prompt"
  else pass "Allow once released the field's value to the client and Deny released nothing, each read back from the client's reply and the audit log"
  fi
}

evidence_restore() {
  local d="$1" revision before after match
  revision="$(fact_of "$d" restore revision_sha256)"
  before="$(fact_of "$d" restore fingerprint_before)"
  after="$(fact_of "$d" restore fingerprint_after)"
  match="$(fact_of "$d" restore backup_match)"
  if [ "$revision" != "$(fact_of "$d" setup value1_sha256)" ]; then
    contradict "after the revision was restored the CLI reads a $FIELD whose SHA-256 is ${revision:-nothing}, not the earlier value's"
  elif [ "$(fact_of "$d" restore deleted_listed)" != false ]; then contradict "$ENTRY_SCRATCH was still listed outside the Trash once deleted"
  elif [ "$(fact_of "$d" restore restored_listed)" != true ]; then contradict "$ENTRY_SCRATCH, restored from Trash, is not listed where it was"
  elif [ "$after" = "$before" ]; then contradict "restoring the backup left the vault as it was: $after"
  elif [ -z "$match" ]; then contradict "after the restore the vault holds $after, which no backup held"
  else pass "a revision, $ENTRY_SCRATCH from Trash and the backup $match were each restored, as the CLI reads them"
  fi
}

evidence_change_access() {
  local old new
  old="$(fact_of "$1" change-access old_password)"
  new="$(fact_of "$1" change-access new_password)"
  if [[ $old != refused* ]]; then contradict "the old master password still opens the vault: ${old:-not tried}"
  elif [ "$new" != opens ]; then contradict "the new master password does not open the vault: ${new:-not tried}"
  else pass "after the change the CLI refuses the old master password ($old), opens the vault with the new one, and the app unlocks with it"
  fi
}

evidence_lock_refused() {
  local answer
  answer="$(fact_of "$1" lock-refused audit_answer)"
  if [ "$(fact_of "$1" lock-refused value)" != none ]; then contradict 'a request after the lock received a value'
  elif [ "$(fact_of "$1" lock-refused error)" != true ]; then contradict 'a request after the lock was not refused'
  elif [ "${answer%%/*}" != denied ]; then contradict "the audit log records the request after the lock as ${answer:-nothing}, not a denial"
  elif [ "${answer#*/}" != vault-locked ]; then contradict "the audit log records the request after the lock as denied by ${answer#*/}, not vault-locked"
  else pass 'after Lock a request was refused, and the audit log records it as vault-locked'
  fi
}

evidence_timing() {
  local unlock save low database
  unlock="$(fact_of "$1" first-run unlock_ms)"
  save="$(fact_of "$1" field save_ms)"
  low="$(fact_of "$1" field save_ms_low)"
  database="$(fact_of "$1" first-run database)"
  # R.1a times the libargon2 the package carries, which a vault deriving its key another way never calls.
  if [[ $database != *'KDF: Argon2'* ]]; then result=unreached; why="the vault does not derive its key with Argon2: $database"
  elif ! measured "$unlock"; then result=unreached; why='no unlock was timed'
  elif ! measured "$save" || ! measured "$low"; then result=unreached; why='no save was timed'
  else
    pass "unlock: ${unlock} ms from Return to the item list, timed by the driver; save: ${low} to ${save} ms from Add to the vault file's change, polled every 250 ms; $(fact_of "$1" timing argon2)"
  fi
}

classify() {
  local dir="$1" act need setup='' passed=' ' failed person result why
  if [ "$(fact_of "$dir" run finished)" != true ]; then
    for act in "${ACTS[@]}"; do verdict "$act" harness-failure 'the run left no finished record'; done
    return
  fi
  failed="$(unfinished "$dir" setup)"
  [ -z "$failed" ] || setup="setup's $(why_unfinished "$failed")"
  for act in "${ACTS[@]}"; do
    result='' why=''
    if [ -n "$setup" ]; then result=unreached why="$setup"; fi
    for need in $(needs "$act"); do
      [ -z "$result" ] || break
      case "$passed" in *" $need "*) ;; *) result=unreached why="$need did not pass" ;; esac
    done
    if [ -z "$result" ]; then
      person="$(fact_of "$dir" "$act" person)"
      failed="$(unfinished "$dir" "$act")"
      if [ -n "$person" ]; then result=person why="$person"
      elif [ -n "$failed" ]; then result=unreached why="$(why_unfinished "$failed")"
      elif [ -z "$(fact_of "$dir" "$act" performed)" ]; then result=unreached why="$act was never attempted"
      else "evidence_$(printf '%s' "$act" | tr - _)" "$dir"
      fi
    fi
    verdict "$act" "$result" "$why"
    [ "$result" != pass ] || passed="$passed$act "
  done
}

# Each act that fails the run, as an annotation; a person's act is a warning, since it waits for that person.
refusals() { jq -r 'select(.result != "pass" and .result != "person") | "::error::\(.act): \(.result): \(.why)"' "$1/verdicts.jsonl"; }
persons() { jq -r 'select(.result == "person") | "::warning::\(.act): a person'\''s act: \(.why)"' "$1/verdicts.jsonl"; }

summary() {
  [ -s "$1/verdicts.jsonl" ] || { echo "no verdicts in $1"; return 1; }
  printf '### R.1a acts on %s\n\n| Act | Result | Why |\n|---|---|---|\n' "$(fact_of "$1" run platform)"
  jq -r '"| \(.act) | \(.result) | \(.why | gsub("[|]"; "\\|") | gsub("\n"; " ")) |"' "$1/verdicts.jsonl"
}

# --- recording ------------------------------------------------------------------------------------

fact() { jq -cn --arg act "$CURRENT" --arg fact "$1" --arg value "$2" '{act: $act, fact: $fact, value: $value}' >> "$OUT/facts.jsonl"; }

# attempt <step> <command...>: runs it, records ok, refused or not-implemented with its output, and returns its status.
attempt() {
  local name="$1" code=0 status=ok
  shift
  # Not a subshell: what the command sets is kept for the steps after it.
  "$@" > "$OUT/step.log" 2>&1 || code=$?
  if [ "$code" != 0 ]; then
    status=refused
    if [ "$code" = 2 ] && grep -q ' not implemented on ' "$OUT/step.log"; then status=not-implemented; fi
  fi
  jq -cn --arg act "$CURRENT" --arg step "$name" --arg status "$status" --arg detail "$(tail -n 20 "$OUT/step.log" | tr -d "$CR")" \
    '{act: $act, step: $step, status: $status, detail: $detail}' >> "$OUT/steps.jsonl"
  [ "$code" = 0 ]
}

# perform <act>: does it once the acts it needs were done, then records its screenshot and its audit lines.
perform() {
  local act="$1" need lines
  CURRENT="$act"
  for need in $(needs "$act"); do
    if [ "$(fact_of "$OUT" "$need" performed)" != true ]; then
      fact skipped "$need was not done"
      return 0
    fi
  done
  lines="$(lines_in "$AUDIT")"
  if "act_$(printf '%s' "$act" | tr - _)"; then fact performed true; else fact performed false; fi
  screenshot "$act"
  fact audit "$(lines_after "$AUDIT" "$lines" 2> /dev/null | tr -d "$CR" || true)"
}

# screenshot <name> [window title]: the app's window, or the one titled, as <name>.png.
screenshot() {
  local said window=()
  [ -z "${2:-}" ] || window=(--window "$2")
  if said="$(drive ${window[@]+"${window[@]}"} screenshot "$(native "$OUT/$1.png")" 2>&1)"; then
    fact screenshot "$1.png: $said"
  else
    fact screenshot "none: $said"
  fi
}

platform() {
  case "$RID" in
    win-x64) echo windows ;;
    linux-x64) echo linux ;;
    osx-arm64) echo macos ;;
    *) die "no desktop acts for $RID" ;;
  esac
}

now_ms() { perl -MTime::HiRes=time -e 'printf "%d\n", time * 1000'; }

sha256_of() { if command -v sha256sum > /dev/null; then sha256sum < "$1"; else shasum -a 256 < "$1"; fi | awk '{print $1}'; }

sha256_text() { printf '%s' "$1" | if command -v sha256sum > /dev/null; then sha256sum; else shasum -a 256; fi | awk '{print $1}'; }

random_word() { od -An -N8 -tx1 /dev/urandom | tr -d ' \n'; }

# changed_since <sha256>: waits up to 30 s for the vault file to change; prints when it saw the change, in ms.
changed_since() {
  local _
  for _ in $(seq 1 120); do
    if [ "$(sha256_of "$VAULT")" != "$1" ]; then
      now_ms
      return 0
    fi
    sleep 0.25
  done
  return 1
}

# saved <fact> <step> <command...>: does the step, then records how long the vault file took to change: <fact>
# counted from before the driver started, at most, and <fact>_low from when it returned, at least.
saved() {
  local name="$1" step_name="$2" before pressed returned seen
  shift 2
  before="$(sha256_of "$VAULT")"
  pressed="$(now_ms)"
  attempt "$step_name" "$@" || return 1
  returned="$(now_ms)"
  if seen="$(changed_since "$before")"; then
    fact "$name" "$((seen - pressed))"
    fact "${name}_low" "$((seen - returned))"
  else
    fact "$name" unchanged
    return 1
  fi
}

# --- setup ----------------------------------------------------------------------------------------

installed_app() {
  local recorded
  recorded="$(jq -rs '[.[] | select(.fact == "installed_app") | .value] | last // empty' "$INSTALL/facts.jsonl" 2> /dev/null | tr -d "$CR")"
  [ -n "$recorded" ] || { echo "the install exercise recorded no installed app in $INSTALL/facts.jsonl"; return 1; }
  if [ "$PLATFORM" = windows ]; then APP="$(cygpath -u "$recorded")"; else APP="$recorded"; fi
  [ -e "$APP" ] || { echo "$APP, which the install exercise installed, is not there"; return 1; }
  fact installed_app "$recorded"
  echo "the install exercise installed $recorded"
}

find_cli() {
  KP="$(find "$CLI_DIR" -maxdepth 3 -type f \( -name keypaste -o -name keypaste.exe \) | head -n 1)"
  [ -n "$KP" ] || { echo "no keypaste under $CLI_DIR"; return 1; }
  chmod +x "$KP" 2> /dev/null || true
  fact cli "$("$KP" --version | tr -d "$CR")"
  echo "the vault is read through $KP"
}

# A person's first run: no keypaste home, and no AI tool holding keypaste yet.
fresh_home() {
  if [ "$PLATFORM" = windows ]; then HOME_DIR="$(cygpath -u "$USERPROFILE")/.keypaste"; else HOME_DIR="$HOME/.keypaste"; fi
  AUDIT="$HOME_DIR/audit.jsonl"
  [ -z "${KEYPASTE_HOME:-}" ] || { echo "KEYPASTE_HOME is $KEYPASTE_HOME; the acts run in the default home"; return 1; }
  [ ! -e "$HOME_DIR" ] || { echo "$HOME_DIR already exists, so the app's start would not be a first run"; return 1; }
  if [ -f "$HOME/.claude.json" ] && jq -e '.mcpServers.keypaste' "$HOME/.claude.json" > /dev/null 2>&1; then
    echo "$HOME/.claude.json already registers keypaste"
    return 1
  fi
  echo "no $HOME_DIR yet"
}

# The values the acts type, known here only to compare SHA-256s with what the evidence gives.
make_values() {
  VALUE1="acts-one-$(random_word)"
  VALUE2="acts-two-$(random_word)"
  NOTE_VALUE="acts-note-$(random_word)"
  PASSWORD="$MASTER"
  VAULT_DIR="$OUT/vault"
  VAULT="$VAULT_DIR/$VAULT_NAME"
  VAULT_NATIVE="$(native "$VAULT")"
  RUN_DIR="$OUT/run"
  mkdir -p "$VAULT_DIR" "$RUN_DIR"
  fact vault "$VAULT_NATIVE"
  fact value1_sha256 "$(sha256_text "$VALUE1")"
  fact value2_sha256 "$(sha256_text "$VALUE2")"
  fact note_sha256 "$(sha256_text "$NOTE_VALUE")"
  child_scripts
  echo "values made; the vault is $VAULT_NATIVE"
}

# The child a project's Run starts: it writes the SHA-256 of each variable it is named, or unset, and its process id.
child_scripts() {
  # shellcheck disable=SC2016 # the child expands these, not this script.
  printf '%s\n' \
    '#!/bin/sh' \
    'out=$1; shift' \
    'printf "pid\t%s\n" "$$" > "$out.partial"' \
    'for name in "$@"; do' \
    '  if eval "[ -n \"\${$name+x}\" ]"; then' \
    '    eval "value=\$$name"' \
    '    hash=$(printf %s "$value" | { sha256sum 2>/dev/null || shasum -a 256; } | cut -d" " -f1)' \
    '    printf "%s\t%s\n" "$name" "$hash" >> "$out.partial"' \
    '  else' \
    '    printf "%s\tunset\n" "$name" >> "$out.partial"' \
    '  fi' \
    'done' \
    'mv "$out.partial" "$out"' > "$RUN_DIR/child.sh"
  # shellcheck disable=SC2016 # PowerShell expands these, not this script.
  printf '%s\r\n' \
    '$out = $args[0]' \
    '$sha = [Security.Cryptography.SHA256]::Create()' \
    '$lines = @("pid`t$PID")' \
    'foreach ($name in $args[1..($args.Count - 1)]) {' \
    '  $value = [Environment]::GetEnvironmentVariable($name, "Process")' \
    '  if ($null -eq $value) { $lines += "$name`tunset" }' \
    '  else { $lines += "$name`t" + (-join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($value)) | ForEach-Object { $_.ToString("x2") })) }' \
    '}' \
    'Set-Content -LiteralPath "$out.partial" -Value $lines -Encoding ascii' \
    'Move-Item -Force -LiteralPath "$out.partial" -Destination $out' > "$RUN_DIR/child.ps1"
}

# The command the project runs, as a person types it into the app; it names a variable outside the project too.
child_command() {
  if [ "$PLATFORM" = windows ]; then
    printf 'powershell -NoProfile -ExecutionPolicy Bypass -File "%s" "%s" %s %s' \
      "$(native "$RUN_DIR/child.ps1")" "$(native "$RUN_DIR/report.txt")" "$FIELD" "$NOTE_KEY"
  else
    printf "sh '%s' '%s' %s %s" "$RUN_DIR/child.sh" "$RUN_DIR/report.txt" "$FIELD" "$NOTE_KEY"
  fi
}

setup() {
  CURRENT=setup
  attempt installed installed_app && attempt cli find_cli && attempt home fresh_home && attempt values make_values
}

# --- the app --------------------------------------------------------------------------------------

app_launch() {
  local log="$OUT/app.log"
  case "$PLATFORM" in
    windows)
      # To a file, not a pipe: the app inherits PowerShell's handles, and an inherited pipe would hold this shell.
      powershell -NoProfile -Command "(Start-Process -FilePath '$(cygpath -w "$APP")' -RedirectStandardOutput '$(cygpath -w "$log.out")' -RedirectStandardError '$(cygpath -w "$log")' -PassThru).Id" \
        > "$OUT/app.pid"
      APP_PID="$(tr -d "$CR" < "$OUT/app.pid")"
      ;;
    macos)
      # Where the driver keeps the window's tree when a step cannot be done.
      export DRIVE_TREE_DIR="$OUT"
      APP_PID="$(mac_start "$APP" "$log")" || { echo "$APP_PID"; return 1; }
      ;;
    linux)
      "$APP" > "$log" 2>&1 &
      APP_PID=$!
      ;;
  esac
  [ -n "$APP_PID" ] || { echo "$APP started with no process id"; return 1; }
  echo "started $APP as process $APP_PID in the default home"
}

app_quit() {
  case "$PLATFORM" in
    windows) taskkill //F //PID "$APP_PID" > /dev/null 2>&1 || true ;;
    macos) mac_quit ;;
    linux)
      pkill -f usr/bin/keypaste-app 2> /dev/null || true
      kill "$APP_PID" 2> /dev/null || true
      wait "$APP_PID" 2> /dev/null || true
      ;;
  esac
}

# The unlock screen's password field is clicked first, as a person does, since the focus may be elsewhere.
unlock_with() { drive focus id:Password && drive type "$1" && drive key Return && drive wait 'Lock now' 120; }

# The vault the app chose for agents and the CLI: app.toml's vault, whose line ends in a comment and whose value holds
# no quote or backslash (TomlWriter).
chosen_vault() { sed -n 's/^vault = "\([^"]*\)".*$/\1/p' "$HOME_DIR/app.toml" 2> /dev/null | head -n 1; }

# --- the vault through the CLI --------------------------------------------------------------------

# field_hash <item> <field> [vault] [password]: the SHA-256 of the field's value as the CLI reads it, or absent.
field_hash() {
  local value
  if value="$(printf '%s\n' "${4:-$PASSWORD}" | "$KP" get "$1" --field "$2" --reveal --vault "${3:-$VAULT_NATIVE}" 2> /dev/null | tr -d "$CR")" \
    && [ -n "$value" ]; then
    sha256_text "$value"
  else
    echo absent
  fi
}

# listed <item> [vault]: whether the CLI lists the item outside the recycle bin.
listed() {
  local all
  all="$(printf '%s\n' "$PASSWORD" | "$KP" ls --flat --vault "${2:-$VAULT_NATIVE}" 2> /dev/null | tr -d "$CR")" || { echo unread; return; }
  if printf '%s\n' "$all" | grep -vi 'recycle' | grep -qE "(^|/)$1\$"; then echo true; else echo false; fi
}

opens_with() {
  local said
  if said="$(printf '%s\n' "$1" | "$KP" ls --flat --vault "$VAULT_NATIVE" 2>&1)"; then
    echo opens
  else
    echo "refused: $(printf '%s' "$said" | tr -d "$CR" | tail -n 1)"
  fi
}

notes_hold_line() {
  local notes
  notes="$(kx show "$VAULT" "$ENTRY_NOTES" -a Notes 2>&1)" || { echo "unread: $notes"; return; }
  if printf '%s' "$notes" | grep -qF "$NOTE_KEY="; then echo true; else echo false; fi
}

# What the CLI reads in a vault file: the field, whether the scratch item is listed, and the moved note key.
fingerprint() {
  printf 'field=%s scratch=%s note=%s' "$(field_hash "$ENTRY_FIELD" "$FIELD" "$1")" "$(listed "$ENTRY_SCRATCH" "$1")" \
    "$(field_hash "$ENTRY_NOTES" "$NOTE_KEY" "$1")"
}

backups() {
  local file
  BACKUPS=()
  for file in "$VAULT_DIR/$VAULT_NAME.backups"/*; do
    [ -f "$file" ] || continue
    BACKUPS+=("$file")
    fact backup "$(basename "$file")$TAB$(fingerprint "$(native "$file")")"
  done
  fact fingerprint_before "$(fingerprint "$VAULT_NATIVE")"
  [ "${#BACKUPS[@]}" -gt 0 ] || { echo "no backups beside the vault in $VAULT_DIR/$VAULT_NAME.backups"; return 1; }
  echo "${#BACKUPS[@]} backups read"
}

# --- KeePassXC ------------------------------------------------------------------------------------

keepassxc_vault() {
  local kpxc_cli="${KPXC_CLI:-keepassxc-cli}"
  command -v "$kpxc_cli" > /dev/null 2>&1 || [ -x "$kpxc_cli" ] || { echo "no keepassxc-cli ('$kpxc_cli') to make the first run's database"; return 1; }
  export KP_COMPAT_PASSWORD="$MASTER"
  # shellcheck source=scripts/lib/kpxc.sh
  . "$ROOT/scripts/lib/kpxc.sh"
  # shellcheck disable=SC2154 # kpxc.sh sets cli and pw.
  printf '%s\n%s\n' "$pw" "$pw" | "$cli" db-create -q -p "$(native "$VAULT")" > /dev/null || { echo 'keepassxc-cli db-create failed'; return 1; }
  kx add "$VAULT" "$ENTRY_FIELD" -u acts --url https://example.test > /dev/null || { echo "keepassxc-cli add $ENTRY_FIELD failed"; return 1; }
  kx add "$VAULT" "$ENTRY_NOTES" --notes "$NOTE_KEY=$NOTE_VALUE" > /dev/null || { echo "keepassxc-cli add $ENTRY_NOTES failed"; return 1; }
  kx add "$VAULT" "$ENTRY_SCRATCH" -u scratch > /dev/null || { echo "keepassxc-cli add $ENTRY_SCRATCH failed"; return 1; }
  fact keepassxc "$("$cli" --version | tr -d "$CR")"
  fact database "$(kx db-info "$VAULT" 2>&1 | tr '\n' ' ')"
  echo "keepassxc-cli made $VAULT_NAME with $ENTRY_FIELD, $ENTRY_NOTES and $ENTRY_SCRATCH"
}

# KeePassXC opens the database once and closes as a person does, which leaves it in its recent databases where it
# always keeps them (src/core/Config.cpp), as verify-keepassxc-first-run.sh has it do on a runner.
keepassxc_recent() {
  local gui roaming local_ini pid code
  gui="$(keepassxc_app)" || { echo "no KeePassXC app: $gui"; return 1; }
  case "$PLATFORM" in
    windows)
      roaming="$(cygpath -u "$APPDATA")/KeePassXC/keepassxc.ini"
      local_ini="$(cygpath -u "$LOCALAPPDATA")/KeePassXC/keepassxc.ini"
      # The pinned archive may be the portable build, which keeps its settings beside itself.
      rm -f "$(dirname "$gui")/.portable"
      ;;
    macos)
      roaming="$HOME/Library/Application Support/KeePassXC/keepassxc.ini"
      local_ini="$HOME/Library/Caches/KeePassXC/keepassxc.ini"
      ;;
    linux)
      roaming="${XDG_CONFIG_HOME:-$HOME/.config}/keepassxc/keepassxc.ini"
      local_ini="${XDG_CACHE_HOME:-$HOME/.cache}/keepassxc/keepassxc.ini"
      ;;
  esac
  [ ! -e "$local_ini" ] || { echo "KeePassXC's $local_ini already exists, so the first run would offer what it lists"; return 1; }
  mkdir -p "$(dirname "$roaming")"
  printf '[General]\nSingleInstance=false\nUpdateCheckMessageShown=true\n\n[GUI]\nCheckForUpdates=false\n' > "$roaming"
  printf '%s\n' "$pw" | "$gui" --pw-stdin "$(native "$VAULT")" > "$OUT/keepassxc.log" 2>&1 &
  pid=$!
  for _ in $(seq 1 120); do
    if [ -f "$local_ini" ] && grep -q "LastDatabases=.*$VAULT_NAME" "$local_ini"; then break; fi
    kill -0 "$pid" 2> /dev/null || { echo "KeePassXC exited before opening $VAULT_NAME: $(cat "$OUT/keepassxc.log")$(crash_report)"; return 1; }
    sleep 0.5
  done
  grep -q "LastDatabases=.*$VAULT_NAME" "$local_ini" 2> /dev/null || {
    kill -9 "$pid" 2> /dev/null || true
    echo "KeePassXC never listed $VAULT_NAME in $local_ini"
    return 1
  }
  sleep 2
  # KeePassXC records a database in LastDatabases as it opens it, and the first run offers what that lists, so a
  # KeePassXC that ends on its own after that is recorded with what the platform says of it, and the act goes on.
  if kill -0 "$pid" 2> /dev/null; then
    close_keepassxc "$pid" || return 1
    grep -q '^LastOpenedDatabases=' "$local_ini" || { echo "KeePassXC closed without writing LastOpenedDatabases to $local_ini"; return 1; }
    fact keepassxc_closed 'closed as a person closes it'
  else
    code=0
    wait "$pid" 2> /dev/null || code=$?
    fact keepassxc_closed "ended by itself, status $code, after it recorded $VAULT_NAME$(crash_report)"
  fi
  fact keepassxc_recent "$(grep '^Last' "$local_ini" | tr '\n' ' ')"
  echo "KeePassXC opened $VAULT_NAME, leaving it in $local_ini; $(fact_of "$OUT" first-run keepassxc_closed)"
}

# What the platform recorded of KeePassXC ending by itself: Windows' newest crash events and macOS's newest crash
# report since the file given, keepassxc.log by default, which is kept with the records; Linux records none.
crash_report() {
  local report
  case "$PLATFORM" in
    windows)
      # shellcheck disable=SC2016 # PowerShell expands its own variables, not bash.
      printf '; Windows recorded: %s' "$(powershell -NoProfile -Command 'Get-WinEvent -FilterHashtable @{LogName = "Application"; ProviderName = "Application Error", "Windows Error Reporting"; StartTime = (Get-Date).AddMinutes(-10)} -MaxEvents 2 -ErrorAction SilentlyContinue | ForEach-Object { $_.ProviderName + ": " + ($_.Message -replace "\s+", " ") }' | tr -d "$CR")"
      ;;
    macos)
      # ReportCrash writes the report a few seconds after the process ends.
      for _ in $(seq 1 30); do
        report="$(find "$HOME/Library/Logs/DiagnosticReports" -maxdepth 1 -name 'KeePassXC*' -newer "${1:-$OUT/keepassxc.log}" \
          -exec stat -f '%m %N' {} + 2> /dev/null | sort -rn | head -n 1 | cut -d ' ' -f 2-)"
        [ -z "$report" ] || break
        sleep 0.5
      done
      [ -n "$report" ] || { printf '; macOS kept no crash report within 15 s'; return 0; }
      cp "$report" "$OUT/"
      printf '; macOS kept %s: %s' "$(basename "$report")" "$(grep -o '"exception" *: *{[^}]*}' "$report" | head -n 1)"
      ;;
  esac
}

close_keepassxc() {
  if [ "$PLATFORM" = windows ]; then taskkill //PID "$(cat "/proc/$1/winpid")" > /dev/null; else kill -TERM "$1"; fi
  for _ in $(seq 1 60); do
    kill -0 "$1" 2> /dev/null || break
    sleep 0.5
  done
  if kill -0 "$1" 2> /dev/null; then
    kill -9 "$1" 2> /dev/null || true
    echo 'KeePassXC did not close within 30 s'
    return 1
  fi
  wait "$1" 2> /dev/null || true
}

# macOS: keepassxc-cli makes a database that derives its key with AES-KDF, and R.1a times the libargon2 the package
# carries, so KeePassXC's own Database Security then changes it to Argon2d, the key derivation KeePassXC gives a new
# database, as a person can in KeePassXC. KeePassXC 2.7.12 has ended by itself soon after opening a database on this
# runner, so a session that ends before the change is saved is followed by another, up to three.
mac_keepassxc_argon2() {
  local gui try pid said code
  gui="$(keepassxc_app)" || { echo "no KeePassXC app: $gui"; return 1; }
  export DRIVE_TREE_DIR="$OUT"
  for try in 1 2 3; do
    printf '%s\n' "$pw" | "$gui" --pw-stdin "$VAULT" > "$OUT/keepassxc-argon2-$try.log" 2>&1 &
    pid=$!
    if said="$(mac_keepassxc_kdf "$pid")"; then
      close_keepassxc "$pid" || return 1
      echo "session $try: $said"
      return 0
    fi
    echo "session $try: $said"
    if kill -0 "$pid" 2> /dev/null; then
      kill -9 "$pid" 2> /dev/null || true
      wait "$pid" 2> /dev/null || true
    else
      code=0
      wait "$pid" 2> /dev/null || code=$?
      echo "session $try: KeePassXC ended by itself, status $code$(crash_report "$OUT/keepassxc-argon2-$try.log")"
    fi
  done
  return 1
}

readonly KPXC_ARGON2D='Argon2d (KDBX 4 – recommended)'

# mac_kpxc <pid> <verb> [args...]: the macOS driver acting in KeePassXC's window for the vault.
mac_kpxc() { (export DRIVE_WINDOW="$KPXC_WINDOW"; mac_osascript 120 "$MAC_DRIVER" "$@"); }

# mac_kpxc_choose <pid> <combo box> <Up|Down> <item>: opens the combo box, takes the item above or below the one it
# shows, and checks that the box shows <item>. Its list opens as a window with no title, and the keys are sent only
# once it has, since a Return the dialog takes instead presses OK.
mac_kpxc_choose() {
  local pid="$1" shown i
  for i in 1 2 3; do
    mac_kpxc "$pid" click "$2" || return 1
    mac_kpxc_listed "$pid" && break
    [ "$i" != 3 ] || { echo "the list of '$2' did not open after three clicks"; return 1; }
  done
  mac_kpxc "$pid" key "$3" && mac_kpxc "$pid" key Return || return 1
  # A combo box comes before the items of its list in the tree, so the first element named <item> is the box only when
  # the box shows it.
  shown="$(mac_kpxc "$pid" find "$4")" || { echo "$shown"; return 1; }
  case "$shown" in
    *' as AXComboBox') echo "$shown" ;;
    *) echo "'$2' does not show '$4' after it was chosen: $shown"; return 1 ;;
  esac
}

# Whether a combo box's list, a KeePassXC window with no title, opens within 5 s.
mac_kpxc_listed() {
  local i
  for i in $(seq 1 10); do
    if mac_kpxc "$1" windows 2> /dev/null | grep -q '^$'; then return 0; fi
    sleep 0.5
  done
  return 1
}

mac_keepassxc_kdf() {
  local pid="$1" i
  KPXC_WINDOW=''
  for i in $(seq 1 60); do
    KPXC_WINDOW="$(mac_kpxc "$pid" windows 2> /dev/null | grep -F "$VAULT_NAME" | head -n 1)"
    [ -z "$KPXC_WINDOW" ] || break
    kill -0 "$pid" 2> /dev/null || { echo "KeePassXC ended before a window showed $VAULT_NAME"; return 1; }
    sleep 0.5
  done
  [ -n "$KPXC_WINDOW" ] || { echo "no KeePassXC window showed $VAULT_NAME within 30 s: $(mac_kpxc "$pid" windows 2>&1 | tr '\n' ';')"; return 1; }
  # A Qt combo box is named on macOS by the item it shows, and takes no focus from the accessibility API, so each is
  # clicked open and its item chosen from the keyboard, as a person can. keepassxc-cli's database is KDBX 3, whose one
  # key derivation is AES-KDF, so the format becomes KDBX 4 first, whose first is Argon2d. A key derivation chosen from
  # its list gets KeePassXC's defaults for it and a benchmark of its rounds, so Argon2id and back gives Argon2d those.
  mac_kpxc "$pid" menu Database 'Database Security…' || return 1
  mac_kpxc "$pid" invoke 'Encryption Settings' || return 1
  mac_kpxc "$pid" invoke Advanced || return 1
  mac_kpxc_choose "$pid" 'KDBX 3' Up 'KDBX 4 (recommended)' || return 1
  mac_kpxc_choose "$pid" "$KPXC_ARGON2D" Down 'Argon2id (KDBX 4)' || return 1
  # KeePassXC benchmarks the rounds for a second after each choice of key derivation.
  sleep 3
  mac_kpxc_choose "$pid" 'Argon2id (KDBX 4)' Up "$KPXC_ARGON2D" || return 1
  sleep 3
  mac_kpxc "$pid" invoke OK || return 1
  # KeePassXC saves after every change unless told otherwise; Save Database is asked for once when it has not.
  for i in $(seq 1 60); do
    if kx db-info "$VAULT" 2> /dev/null | grep -q 'KDF: Argon2d'; then
      echo "KeePassXC's Database Security set $KPXC_ARGON2D and saved $VAULT_NAME: $(kx db-info "$VAULT" 2>&1 | grep 'KDF')"
      return 0
    fi
    kill -0 "$pid" 2> /dev/null || { echo "KeePassXC ended before it saved the change"; return 1; }
    [ "$i" != 20 ] || mac_kpxc "$pid" menu Database 'Save Database' || true
    sleep 0.5
  done
  echo "$VAULT_NAME does not derive with Argon2d 30 s after OK: $(kx db-info "$VAULT" 2>&1 | grep 'KDF')"
  return 1
}

# --- the child and its terminal -------------------------------------------------------------------

wait_for_file() {
  for _ in $(seq 1 $(($2 * 4))); do
    [ ! -s "$1" ] || { echo "$1 appeared"; return 0; }
    sleep 0.25
  done
  echo "no $1 within $2 s"
  return 1
}

# run_child: Run in a terminal and Start, then what the child reported.
run_child() {
  rm -f "$RUN_DIR/report.txt"
  attempt run drive invoke 'Run in a terminal' || return 1
  attempt start drive invoke Start || return 1
  attempt child wait_for_file "$RUN_DIR/report.txt" 60 || return 1
  fact child "$(tr '\n' ' ' < "$RUN_DIR/report.txt" | tr -d "$CR")"
  fact child_field_sha256 "$(awk -F '\t' -v k="$FIELD" '$1 == k { print $2 }' "$RUN_DIR/report.txt" | tr -d "$CR")"
  close_terminals
}

# The terminal a Run leaves open after its command.
close_terminals() {
  case "$PLATFORM" in
    windows)
      powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name = 'cmd.exe'\" | Where-Object { \$_.CommandLine -like '*child.ps1*' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }" \
        > /dev/null 2>&1 || true
      ;;
    macos) pkill -x Terminal 2> /dev/null || true ;;
    linux) pkill -x xterm 2> /dev/null || true ;;
  esac
}

# --- the MCP client -------------------------------------------------------------------------------

# Claude Code's registration of keypaste, as `claude mcp add --scope user` keeps it in ~/.claude.json.
registration_json() { jq -c '.mcpServers.keypaste // empty' "$HOME/.claude.json" 2> /dev/null | tr -d "$CR"; }

wait_registration() {
  for _ in $(seq 1 $(($1 * 2))); do
    [ -z "$(registration_json)" ] || { echo "registered: $(registration_json)"; return 0; }
    sleep 0.5
  done
  echo "Claude Code's $HOME/.claude.json holds no keypaste within $1 s"
  return 1
}

# Reads the registered command into REG_CMD and what it adds to the environment into REG_ENV.
read_registration() {
  local entry line
  entry="$(registration_json)"
  [ -n "$entry" ] || { echo 'no registration'; return 1; }
  REG_CMD=("$(jq -r '.command' <<< "$entry" | tr -d "$CR")")
  while IFS= read -r line; do REG_CMD+=("$line"); done < <(jq -r '.args[]?' <<< "$entry" | tr -d "$CR")
  REG_ENV=()
  while IFS= read -r line; do [ -z "$line" ] || REG_ENV+=("$line"); done < <(jq -r '.env // {} | to_entries[] | "\(.key)=\(.value)"' <<< "$entry" | tr -d "$CR")
  fact command "${REG_CMD[*]}"
  if jq -e '[.args[]?] | any(. == "--vault" or startswith("--vault="))' <<< "$entry" > /dev/null; then fact has_vault true; else fact has_vault false; fi
  # Git Bash starts a Windows path once it is its own spelling of it.
  if [ "$PLATFORM" = windows ]; then REG_CMD[0]="$(cygpath -u "${REG_CMD[0]}")"; fi
  echo "the registration starts ${REG_CMD[*]}"
}

# mcp_start <name>: starts the registered command as Claude Code would, its input on descriptor 8, and initializes.
# Its replies stay outside the records until mcp_finish copies them there with any released value taken out.
mcp_start() {
  MCP_NAME="$1"
  MCP_OUT="$MCP_DIR/mcp-$1.jsonl"
  MCP_ERR="$OUT/mcp-$1-stderr.txt"
  : > "$MCP_OUT"
  exec 8> >(exec env ${REG_ENV[@]+"${REG_ENV[@]}"} "${REG_CMD[@]}" > "$MCP_OUT" 2> "$MCP_ERR")
  mcp_say "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"claude-code\",\"version\":\"${CLAUDE_VERSION:-unknown}\"}}}"
  replied 30 1 || { echo "the registered command did not answer initialize: $(cat "$MCP_ERR")"; return 1; }
  mcp_say '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  echo "the registered command answered: $(jq -c 'select(.id == 1) | .result.serverInfo' "$MCP_OUT" | tr -d "$CR")"
}

mcp_say() { printf '%s\n' "$1" >&8; }

mcp_request() {
  mcp_say "{\"jsonrpc\":\"2.0\",\"id\":$1,\"method\":\"tools/call\",\"params\":{\"name\":\"request_credential\",\"arguments\":{\"entry\":\"$ENTRY_FIELD\",\"field\":\"$FIELD\",\"reason\":\"$2\",\"ttl_seconds\":60}}}"
}

replied() {
  await_replies "$1" "$MCP_OUT" "$2" || { echo "no reply to request $2 within $1 s"; return 1; }
  echo "request $2 answered"
}

reply_of() { jq -rc --argjson id "$1" "select(.id == \$id) | $2" "$MCP_OUT" 2> /dev/null | tr -d "$CR"; }

# The SHA-256 of the value a reply released, or none.
reply_value() {
  local value
  value="$(reply_of "$1" '.result.structuredContent.value // empty')"
  if [ -n "$value" ]; then sha256_text "$value"; else echo none; fi
}

# Ends the client and records its replies with any released value taken out.
mcp_finish() {
  exec 8>&-
  [ -s "${MCP_OUT:-}" ] || return 0
  jq -c 'if (.result.structuredContent.value? | type) == "string" then .result.structuredContent.value = "<released, recorded as its SHA-256>" | .result.content = [] else . end' \
    "$MCP_OUT" > "$OUT/mcp-$MCP_NAME.jsonl" 2> /dev/null || echo "the replies to $MCP_NAME were not JSON" > "$OUT/mcp-$MCP_NAME.jsonl"
}

# The decision and method of the latest request_credential line after line $1 of the audit log.
audit_answer() {
  lines_after "$AUDIT" "$1" 2> /dev/null | tr -d "$CR" \
    | jq -rs '[.[] | select(.tool == "request_credential")] | last | if . == null then "" else "\(.decision)/\(.method)" end'
}

# --- the acts -------------------------------------------------------------------------------------

act_first_run() {
  attempt keepassxc-vault keepassxc_vault || return 1
  attempt keepassxc-recent keepassxc_recent || return 1
  if [ "$PLATFORM" = macos ]; then
    fact keepassxc_argon2 "$(mac_keepassxc_argon2 2>&1 | tr -d "$CR" | tr '\n' ' ')"
    fact database "$(kx db-info "$VAULT" 2>&1 | tr '\n' ' ')"
  fi
  if [ "$PLATFORM" = linux ]; then attempt accessibility enable_accessibility || return 1; fi
  attempt launch app_launch || return 1
  attempt window drive window 120 || return 1
  attempt keepassxc-list drive find 'KEEPASSXC LAST OPENED' || return 1
  attempt choose drive select "$VAULT_NAME" || return 1
  # Choosing the database leaves the focus on its list, so the password field is clicked as a person does.
  attempt password-focus drive focus id:Password || return 1
  attempt password drive type "$PASSWORD" || return 1
  # Timed inside the driver, from Return to the item list, since starting a driver can take longer than an unlock.
  attempt unlock drive key-wait Return "$ENTRY_FIELD" 120 || return 1
  fact unlock_ms "$(step_detail "$OUT" first-run unlock | sed -n 's/.* \([0-9][0-9]*\) ms after .*/\1/p')"
  attempt unlocked drive find 'Lock now' || return 1
  fact chosen_vault "$(chosen_vault)"
}

act_field() {
  attempt items shortcut 1 || return 1
  attempt choose drive select "$ENTRY_FIELD" || return 1
  attempt add-field drive invoke 'Add field' || return 1
  attempt name drive set-named id:NewFieldName "$FIELD" || return 1
  attempt value-focus drive focus id:NewFieldValue || return 1
  attempt value drive type "$VALUE1" || return 1
  saved save_ms add drive invoke Add || return 1
  fact field_sha256 "$(field_hash "$ENTRY_FIELD" "$FIELD")"
}

act_notes_flag() {
  attempt settings shortcut 4 || return 1
  attempt flagged drive find-prefix "$NOTE_KEY " || return 1
  saved moved_ms move drive invoke 'Move to a field' || return 1
  fact field_sha256 "$(field_hash "$ENTRY_NOTES" "$NOTE_KEY")"
  fact notes_hold_line "$(notes_hold_line)"
}

act_project_run() {
  attempt items shortcut 1 || return 1
  attempt choose drive select "$ENTRY_FIELD" || return 1
  attempt tag drive set-named id:NewTag "env:$PROJECT" || return 1
  attempt add-tag drive invoke 'Add tag' || return 1
  saved tagged_ms confirm-tag drive invoke 'Add tag[2]' || return 1
  # The sidebar's Projects is a heading that cannot be chosen; the project is the row beneath it.
  attempt projects drive select "$PROJECT" || return 1
  attempt key-row drive find "$FIELD" || return 1
  attempt directory drive set-named 'Project directory' "$(native "$RUN_DIR")" || return 1
  attempt command drive set-named Command "$(child_command)" || return 1
  attempt save-command drive invoke Save || return 1
  fact command "$(child_command)"
  run_child
}

act_edit_rerun() {
  attempt items shortcut 1 || return 1
  attempt choose drive select "$ENTRY_FIELD" || return 1
  # The item's one custom field's Replace, then the form's.
  attempt replace drive invoke Replace || return 1
  attempt value-focus drive focus id:ReplacementFieldValue || return 1
  attempt value drive type "$VALUE2" || return 1
  saved replaced_ms confirm drive invoke 'Replace[2]' || return 1
  fact field_sha256 "$(field_hash "$ENTRY_FIELD" "$FIELD")"
  attempt projects drive select "$PROJECT" || return 1
  run_child
}

act_connect() {
  CLAUDE_VERSION="$(claude --version 2> /dev/null | head -n 1 | tr -d "$CR" || true)"
  fact claude "${CLAUDE_VERSION:-not on PATH}"
  attempt agents shortcut 2 || return 1
  attempt offered drive wait "Connect $TOOL" 90 || return 1
  attempt connect drive invoke "Connect $TOOL" || return 1
  # A TextBlock's accessible name is its text, so the plan is found by its automation id.
  attempt plan drive read id:ConnectPlan || return 1
  attempt run-it drive invoke 'Run it' || return 1
  attempt registered wait_registration 60 || return 1
  fact registration "$(registration_json)"
  attempt registration read_registration || return 1
  attempt client mcp_start connect || return 1
  fact server "$(reply_of 1 '.result.serverInfo')"
  mcp_say '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
  attempt tools replied 30 2 || { mcp_finish; return 1; }
  fact tools "$(reply_of 2 '[.result.tools[]?.name] | join(" ")')"
  mcp_finish
}

act_approve_deny() {
  local mark
  fact expected_sha256 "$(field_hash "$ENTRY_FIELD" "$FIELD")"
  attempt client mcp_start approve || return 1
  mark="$(lines_in "$AUDIT")"
  mcp_request 3 'R.1a: a request a person allows once'
  attempt prompt drive --window "$PROMPT" window 60 || { mcp_finish; return 1; }
  screenshot approve-prompt "$PROMPT"
  attempt allow drive --window "$PROMPT" invoke 'Allow once' || { mcp_finish; return 1; }
  attempt allowed replied 60 3 || { mcp_finish; return 1; }
  fact allow_error "$(reply_of 3 '.result.isError // false')"
  fact allow_sha256 "$(reply_value 3)"
  fact allow_audit "$(audit_answer "$mark")"
  mark="$(lines_in "$AUDIT")"
  mcp_request 4 'R.1a: a request a person denies'
  attempt prompt-again drive --window "$PROMPT" window 60 || { mcp_finish; return 1; }
  attempt deny drive --window "$PROMPT" invoke Deny || { mcp_finish; return 1; }
  attempt denied replied 60 4 || { mcp_finish; return 1; }
  fact deny_error "$(reply_of 4 '.result.isError // false')"
  fact deny_value "$(reply_value 4)"
  fact deny_audit "$(audit_answer "$mark")"
  mcp_finish
}

act_restore() {
  local file
  # A revision: the newest the item's history keeps holds the value edit-rerun replaced.
  attempt items shortcut 1 || return 1
  attempt choose drive select "$ENTRY_FIELD" || return 1
  attempt actions drive toggle 'More actions' || return 1
  attempt history drive invoke id:HistoryToggle || return 1
  attempt revision drive select-nth id:Revisions 1 || return 1
  # That revision holds the item's project tags as they are, so Restore this writes it without asking first (D-0415).
  saved revision_ms restore-revision drive invoke id:RestoreRevision || return 1
  fact revision_sha256 "$(field_hash "$ENTRY_FIELD" "$FIELD")"
  # An open history takes the whole view, so it is closed for the list to show again.
  attempt hide-history drive invoke 'Hide history' || return 1
  # A deletion: the scratch item deleted, then restored from Trash.
  attempt choose-scratch drive select "$ENTRY_SCRATCH" || return 1
  attempt scratch-actions drive toggle 'More actions' || return 1
  attempt delete drive invoke id:DeleteEntry || return 1
  saved deleted_ms confirm-delete drive invoke Delete || return 1
  fact deleted_listed "$(listed "$ENTRY_SCRATCH")"
  attempt trash shortcut 3 || return 1
  attempt choose-trashed drive select "$ENTRY_SCRATCH" || return 1
  saved untrashed_ms restore-trashed drive invoke id:RestoreButton || return 1
  fact restored_listed "$(listed "$ENTRY_SCRATCH")"
  # A backup: what each backup and the vault hold, then the first the unlock screen lists restored.
  attempt backups backups || return 1
  attempt lock drive invoke 'Lock now' || return 1
  attempt restore-screen drive invoke 'Restore a backup' || return 1
  attempt backup drive select-nth id:BackupList 1 || return 1
  attempt backup-focus drive focus id:BackupPassword || return 1
  attempt backup-password drive type "$PASSWORD" || return 1
  attempt check drive invoke 'Check this backup' || return 1
  saved restored_ms restore-backup drive invoke 'Restore this backup' || return 1
  fact fingerprint_after "$(fingerprint "$VAULT_NATIVE")"
  for file in ${BACKUPS[@]+"${BACKUPS[@]}"}; do
    if [ "$(fingerprint "$(native "$file")")" = "$(fact_of "$OUT" restore fingerprint_after)" ]; then fact backup_match "$(basename "$file")"; fi
  done
  # The check unlocked the backup with its password, so the restored vault opens without asking again.
  attempt reopened drive wait 'Lock now' 60 || return 1
}

act_change_access() {
  attempt settings shortcut 4 || return 1
  attempt current-focus drive focus id:CurrentPassword || return 1
  attempt current drive type "$PASSWORD" || return 1
  attempt set-new drive check 'Set a new master password' on || return 1
  attempt new-focus drive focus id:AccessNewPassword || return 1
  attempt new drive type "$NEW_MASTER" || return 1
  attempt confirm-focus drive focus id:AccessConfirmPassword || return 1
  attempt confirm drive type "$NEW_MASTER" || return 1
  attempt review drive invoke 'Change…' || return 1
  saved changed_ms change drive invoke 'Change access' || return 1
  fact old_password "$(opens_with "$PASSWORD")"
  fact new_password "$(opens_with "$NEW_MASTER")"
  PASSWORD="$NEW_MASTER"
  attempt lock drive invoke 'Lock now' || return 1
  attempt unlock-new unlock_with "$PASSWORD" || return 1
}

act_lock_refused() {
  local mark
  attempt lock drive invoke 'Lock now' || return 1
  attempt locked drive find Unlock || return 1
  mark="$(lines_in "$AUDIT")"
  attempt client mcp_start locked || return 1
  mcp_request 5 'R.1a: a request after the lock'
  attempt answered replied 60 5 || { mcp_finish; return 1; }
  fact error "$(reply_of 5 '.result.isError // false')"
  fact value "$(reply_value 5)"
  fact refusal "$(reply_of 5 '[.result.content[]?.text] | join(" ")')"
  fact audit_answer "$(audit_answer "$mark")"
  fact windows "$(drive windows 2>&1 | tr -d "$CR" | tr '\n' ';' || true)"
  mcp_finish
}

# The times are first-run's and field's facts; this records what derived the key.
act_timing() {
  case "$PLATFORM" in
    windows) fact argon2 "the package carries: $(find "$(dirname "$APP")" -maxdepth 1 -iname '*argon2*' -exec basename {} \; | tr '\n' ' ')" ;;
    macos) fact argon2 "the package carries: $(find "$APP/Contents" -iname '*argon2*' -exec basename {} \; | tr '\n' ' ')" ;;
    linux) fact argon2 "on Linux the package's own KeePassLib derives the key (D-0522)" ;;
  esac
  fact database "$(fact_of "$OUT" first-run database)"
}

# --- the run --------------------------------------------------------------------------------------

# shellcheck disable=SC2016 # PowerShell expands its own variables, not bash.
environment() {
  local os
  case "$PLATFORM" in
    linux) os="$(sed -n 's/^PRETTY_NAME="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' /etc/os-release) $(uname -r)" ;;
    windows) os="$(powershell -NoProfile -Command '(Get-CimInstance Win32_OperatingSystem | ForEach-Object { "$($_.Caption) $($_.Version)" })' | tr -d "$CR")" ;;
    macos) os="$(sw_vers -productName) $(sw_vers -productVersion) ($(sw_vers -buildVersion))" ;;
  esac
  jq -n \
    --arg platform "$PLATFORM" \
    --arg os "$os" \
    --arg image "${ImageOS:-local} ${ImageVersion:-}" \
    --arg package "$(basename "$CANDIDATE")" \
    --arg packageSha256 "$(sha256_of "$CANDIDATE")" \
    --arg packageCommit "${PACKAGE_COMMIT:-unknown}" \
    --arg harnessCommit "${GITHUB_SHA:-$(git -C "$ROOT" rev-parse HEAD 2> /dev/null || echo unknown)}" \
    --arg run "${GITHUB_RUN_ID:-local}" \
    --arg appRun "${REHEARSAL:-}" \
    '{platform: $platform, os: $os, runnerImage: $image, package: $package, packageSha256: $packageSha256, packageCommit: $packageCommit,
      harnessCommit: $harnessCommit, run: $run, appRun: (if $appRun == "" then null else $appRun end)}'
}

# Each act's verdict with the environment, its facts and its audit lines.
observation() {
  jq -n --argjson environment "$(environment)" --slurpfile verdicts "$OUT/verdicts.jsonl" --slurpfile facts "$OUT/facts.jsonl" '
    $verdicts | map(. as $v | $environment + {act: $v.act, result: $v.result, why: $v.why,
      facts: ([$facts[] | select(.act == $v.act and .fact != "audit")] | map({(.fact): .value}) | add // {}),
      audit: [$facts[] | select(.act == $v.act and .fact == "audit") | .value | split("\n")[] | select(length > 0) | (fromjson? // .)]})'
}

run_all() {
  local act refused
  mkdir -p "$OUT"
  : > "$OUT/facts.jsonl"
  : > "$OUT/steps.jsonl"
  [ -f "$CANDIDATE" ] || die "no candidate at $CANDIDATE"
  [ -d "$INSTALL" ] || die "no install exercise output at $INSTALL"
  PLATFORM="$(platform)"
  if [ "$PLATFORM" = linux ] && [ -z "${DISPLAY:-}" ]; then die 'DISPLAY is unset; start Xvfb and a window manager first'; fi
  APP_PID=''
  AUDIT="$OUT/no-audit-yet"
  BACKUPS=()
  MCP_DIR="$(mktemp -d)"
  CURRENT=run
  fact platform "$PLATFORM"
  if setup; then
    for act in "${ACTS[@]}"; do perform "$act"; done
  fi
  CURRENT=run
  exec 8>&- 2> /dev/null || true
  [ -z "$APP_PID" ] || app_quit
  close_terminals
  rm -rf "$MCP_DIR" 2> /dev/null || true
  if [ -f "$AUDIT" ]; then cp "$AUDIT" "$OUT/audit.jsonl"; fi
  fact finished true

  classify "$OUT" > "$OUT/verdicts.jsonl"
  observation > "$OUT/acts.json"
  summary "$OUT"
  persons "$OUT" >&2
  refused="$(refusals "$OUT")"
  if [ -n "$refused" ]; then
    printf '%s\n' "$refused" >&2
    return 1
  fi
}

# --- self-test ------------------------------------------------------------------------------------

selftest() {
  local work cases=0 failures=0 act
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN

  # A complete, passing run's records; each fixture below changes one thing.
  passing() {
    local dir="$work/$1" a
    mkdir -p "$dir"
    : > "$dir/steps.jsonl"
    for a in setup "${ACTS[@]}"; do
      [ "$a" = timing ] || jq -cn --arg a "$a" '{act: $a, step: "done", status: "ok", detail: "ok"}' >> "$dir/steps.jsonl"
    done
    {
      for a in "${ACTS[@]}"; do jq -cn --arg a "$a" '{act: $a, fact: "performed", value: "true"}'; done
      printf '%s\n' \
        '{"act":"run","fact":"platform","value":"linux"}' \
        '{"act":"setup","fact":"vault","value":"/home/runner/work/acts/vault/acts.kdbx"}' \
        '{"act":"setup","fact":"value1_sha256","value":"a1"}' \
        '{"act":"setup","fact":"value2_sha256","value":"b2"}' \
        '{"act":"setup","fact":"note_sha256","value":"c3"}' \
        '{"act":"first-run","fact":"chosen_vault","value":"/home/runner/work/acts/vault/acts.kdbx"}' \
        '{"act":"first-run","fact":"unlock_ms","value":"2100"}' \
        '{"act":"first-run","fact":"database","value":"Cipher: AES 256-bit KDF: Argon2d (8 rounds, 65536 KB) Recycle bin is enabled."}' \
        '{"act":"field","fact":"save_ms","value":"420"}' \
        '{"act":"field","fact":"save_ms_low","value":"150"}' \
        '{"act":"field","fact":"field_sha256","value":"a1"}' \
        '{"act":"notes-flag","fact":"moved_ms","value":"300"}' \
        '{"act":"notes-flag","fact":"field_sha256","value":"c3"}' \
        '{"act":"notes-flag","fact":"notes_hold_line","value":"false"}' \
        '{"act":"project-run","fact":"tagged_ms","value":"280"}' \
        '{"act":"project-run","fact":"child_field_sha256","value":"a1"}' \
        '{"act":"edit-rerun","fact":"replaced_ms","value":"310"}' \
        '{"act":"edit-rerun","fact":"field_sha256","value":"b2"}' \
        '{"act":"edit-rerun","fact":"child_field_sha256","value":"b2"}' \
        '{"act":"connect","fact":"registration","value":"{\"type\":\"stdio\",\"command\":\"/home/runner/.local/bin/keypaste\",\"args\":[\"mcp\"]}"}' \
        '{"act":"connect","fact":"command","value":"/home/runner/.local/bin/keypaste mcp"}' \
        '{"act":"connect","fact":"has_vault","value":"false"}' \
        '{"act":"connect","fact":"server","value":"{\"name\":\"keypaste\"}"}' \
        '{"act":"connect","fact":"tools","value":"list_entries request_credential request_access_change"}' \
        '{"act":"approve-deny","fact":"expected_sha256","value":"b2"}' \
        '{"act":"approve-deny","fact":"allow_error","value":"false"}' \
        '{"act":"approve-deny","fact":"allow_sha256","value":"b2"}' \
        '{"act":"approve-deny","fact":"allow_audit","value":"granted/prompt"}' \
        '{"act":"approve-deny","fact":"deny_error","value":"true"}' \
        '{"act":"approve-deny","fact":"deny_value","value":"none"}' \
        '{"act":"approve-deny","fact":"deny_audit","value":"denied/prompt"}' \
        '{"act":"restore","fact":"revision_sha256","value":"a1"}' \
        '{"act":"restore","fact":"deleted_listed","value":"false"}' \
        '{"act":"restore","fact":"restored_listed","value":"true"}' \
        '{"act":"restore","fact":"fingerprint_before","value":"field=a1 scratch=true note=c3"}' \
        '{"act":"restore","fact":"fingerprint_after","value":"field=absent scratch=true note=absent"}' \
        '{"act":"restore","fact":"backup_match","value":"acts.20261022T120000Z.kdbx"}' \
        '{"act":"change-access","fact":"old_password","value":"refused: keypaste: the master password is wrong"}' \
        '{"act":"change-access","fact":"new_password","value":"opens"}' \
        '{"act":"lock-refused","fact":"error","value":"true"}' \
        '{"act":"lock-refused","fact":"value","value":"none"}' \
        '{"act":"lock-refused","fact":"audit_answer","value":"denied/vault-locked"}' \
        '{"act":"timing","fact":"argon2","value":"the package carries: libargon2.dylib"}' \
        '{"act":"run","fact":"finished","value":"true"}'
    } > "$dir/facts.jsonl"
  }
  set_fact() { jq -cn --arg a "$2" --arg f "$3" --arg v "$4" '{act: $a, fact: $f, value: $v}' >> "$work/$1/facts.jsonl"; }
  drop_fact() { jq -c --arg a "$2" --arg f "$3" 'select(.act != $a or .fact != $f)' "$work/$1/facts.jsonl" > "$work/t" && mv "$work/t" "$work/$1/facts.jsonl"; }
  add_step() { jq -cn --arg a "$2" --arg s "$3" --arg st "$4" --arg d "$5" '{act: $a, step: $s, status: $st, detail: $d}' >> "$work/$1/steps.jsonl"; }

  expect() { # expect <fixture> <act> <result> [text the why holds]
    local line actual
    cases=$((cases + 1))
    line="$("$BASH" "$SELF" --classify "$work/$1" | jq -c --arg a "$2" 'select(.act == $a)')"
    actual="$(jq -r '.result' <<< "$line" 2> /dev/null || true)"
    [ -n "$actual" ] || actual=missing
    if [ "$actual" != "$3" ]; then
      echo "::error::selftest $1: $2 classified $actual, expected $3: $line" >&2
      failures=$((failures + 1))
    elif [ -n "${4:-}" ] && [[ $(jq -r .why <<< "$line") != *"$4"* ]]; then
      echo "::error::selftest $1: $2's why does not hold '$4': $line" >&2
      failures=$((failures + 1))
    fi
  }
  expect_run() { # expect_run <fixture> <accepted|refused>
    local actual=accepted
    cases=$((cases + 1))
    "$BASH" "$SELF" --classify "$work/$1" > "$work/$1/verdicts.jsonl"
    [ -z "$(refusals "$work/$1")" ] || actual=refused
    if [ "$actual" != "$2" ]; then
      echo "::error::selftest $1: the run is $actual, expected $2" >&2
      failures=$((failures + 1))
    fi
  }

  passing all-pass
  passing unfinished
  drop_fact unfinished run finished
  passing setup-refused
  add_step setup-refused setup home refused '/home/runner/.keypaste already exists'
  passing driver-gap
  add_step driver-gap first-run item-list not-implemented 'the verb wait not implemented on linux'
  passing chose-other
  set_fact chose-other first-run chosen_vault /home/runner/other.kdbx
  passing windows-spelling
  set_fact windows-spelling run platform windows
  set_fact windows-spelling setup vault 'D:\a\keypaste\keypaste\acts\vault\acts.kdbx'
  set_fact windows-spelling first-run chosen_vault 'd:/a/keypaste/keypaste/acts/vault/acts.kdbx'
  passing field-other
  set_fact field-other field field_sha256 ff
  passing field-unsaved
  set_fact field-unsaved field save_ms unchanged
  passing notes-kept
  set_fact notes-kept notes-flag notes_hold_line true
  passing notes-unread
  set_fact notes-unread notes-flag notes_hold_line 'unread: no such entry'
  passing child-unset
  set_fact child-unset project-run child_field_sha256 unset
  passing rerun-old
  set_fact rerun-old edit-rerun child_field_sha256 a1
  passing vault-path
  set_fact vault-path connect has_vault true
  passing no-tool
  set_fact no-tool connect tools 'list_entries'
  passing automatic
  set_fact automatic approve-deny allow_audit granted/automatic
  passing deny-leaks
  set_fact deny-leaks approve-deny deny_value ab
  passing allow-other
  set_fact allow-other approve-deny allow_sha256 ff
  passing backup-unchanged
  set_fact backup-unchanged restore fingerprint_after 'field=a1 scratch=true note=c3'
  passing backup-unmatched
  drop_fact backup-unmatched restore backup_match
  passing trash-kept
  set_fact trash-kept restore restored_listed false
  passing old-opens
  set_fact old-opens change-access old_password opens
  passing no-approver
  set_fact no-approver lock-refused audit_answer denied/no-approver
  passing lock-leaks
  set_fact lock-leaks lock-refused value ab
  passing person
  set_fact person notes-flag person 'only a person can judge how the flagged row reads'
  passing no-save-time
  drop_fact no-save-time field save_ms
  passing aes-kdf
  set_fact aes-kdf first-run database 'Cipher: AES 256-bit KDF: AES (1000000 rounds)'
  passing skipped
  drop_fact skipped project-run performed
  jq -c 'select(.act != "project-run")' "$work/skipped/steps.jsonl" > "$work/t" && mv "$work/t" "$work/skipped/steps.jsonl"

  for act in "${ACTS[@]}"; do expect all-pass "$act" pass; done
  expect unfinished first-run harness-failure
  expect unfinished timing harness-failure
  expect setup-refused first-run unreached 'already exists'
  expect setup-refused connect unreached 'already exists'
  expect driver-gap first-run unreached 'does not implement'
  expect driver-gap field unreached 'first-run did not pass'
  expect driver-gap timing unreached 'first-run did not pass'
  expect chose-other first-run contradiction 'other.kdbx'
  expect windows-spelling first-run pass
  expect field-other field contradiction 'not the typed'
  expect field-other project-run unreached 'field did not pass'
  expect field-other edit-rerun unreached 'project-run did not pass'
  expect field-other approve-deny unreached 'field did not pass'
  expect field-other notes-flag pass
  expect field-other connect pass
  expect field-other timing pass
  expect field-unsaved field contradiction 'did not change'
  expect notes-kept notes-flag contradiction 'still hold'
  expect notes-unread notes-flag unreached 'could not read'
  expect child-unset project-run contradiction 'without ACTS_FIELD'
  expect child-unset edit-rerun unreached
  expect rerun-old edit-rerun contradiction 'old value'
  expect vault-path connect contradiction 'vault path'
  expect vault-path approve-deny unreached 'connect did not pass'
  expect vault-path lock-refused unreached 'connect did not pass'
  expect no-tool connect contradiction 'request_credential'
  expect automatic approve-deny contradiction 'granted/automatic'
  expect deny-leaks approve-deny contradiction 'carried a value'
  expect allow-other approve-deny contradiction 'not the field'
  expect backup-unchanged restore contradiction 'as it was'
  expect backup-unmatched restore contradiction 'no backup held'
  expect trash-kept restore contradiction 'Trash'
  expect old-opens change-access contradiction 'still opens'
  expect no-approver lock-refused contradiction 'no-approver, not vault-locked'
  expect lock-leaks lock-refused contradiction 'received a value'
  expect person notes-flag person 'only a person'
  expect no-save-time timing unreached 'no save'
  expect aes-kdf timing unreached 'Argon2'
  expect aes-kdf first-run pass
  expect skipped project-run unreached 'never attempted'
  expect skipped edit-rerun unreached 'project-run did not pass'

  expect_run all-pass accepted
  expect_run person accepted
  expect_run driver-gap refused
  expect_run no-approver refused
  expect_run unfinished refused

  if [ "$failures" -gt 0 ]; then
    die "exercise-desktop-acts selftest: $failures of $cases cases failed"
  fi
  echo "exercise-desktop-acts selftest: $cases cases"
}

case "${1:-}" in
  --selftest) selftest ;;
  --classify) [ $# -eq 2 ] || { usage >&2; exit 2; }; classify "$2" ;;
  --summary) [ $# -eq 2 ] || { usage >&2; exit 2; }; summary "$2" ;;
  -h | --help) usage ;;
  '') usage >&2; exit 2 ;;
  *)
    [ $# -eq 5 ] || { usage >&2; exit 2; }
    RID="$1"
    CANDIDATE="$2"
    INSTALL="$3"
    CLI_DIR="$4"
    mkdir -p "$5"
    # Absolute, because LaunchServices starts the macOS app from / and the app reads the paths the acts wrote.
    OUT="$(cd "$5" && pwd -P)"
    run_all
    ;;
esac
