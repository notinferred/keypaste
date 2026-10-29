#!/usr/bin/env bash
# The desktop app's crash reports across real processes (4.11c): tests/Keypaste.AppDriver runs keypaste-app's own
# start, crash reports composed as that start composes them, on a headless display, and stands in on loopback for
# keypaste.com's /api/crash, where KEYPASTE_CRASH_URL points the app.
#
# A fresh home shows the first run's terms and then its choice before the unlock screen, and Send crash reports writes
# crash_reports = 1 to app.toml. With an item's password copied, an exception thrown on the UI thread clears the
# clipboard, locks and quits with exit code 70, prints nothing of its message and leaves one crash report and no run
# marker: the report names the exception's types, Items and an uptime under a minute, and holds none of the vault's
# name, the item's name or password, the exception's messages or the home's path. Started again, the app shows no first
# run and sends that report's exact bytes to the endpoint, with no cookie, user agent or trace header, and the outbox
# empties.
#
# Switched off: Don't send writes crash_reports = 0, and the same crash still clears, locks and quits but keeps no
# folder; a report put in that home's outbox is deleted at the next start, and nothing from it reaches the endpoint
# while that app runs past the sender's first pass.
#
# Offline: with an endpoint that takes the connection and never answers, a lock and an unlock while the app's request
# is held each finish within 8 seconds, quitting during it takes under 5, and the report is still waiting afterwards.
#
# An app killed while it runs, with no hook able to run, leaves its run marker, and the next start reports the run as
# ended-unexpectedly with no exception, marks its own run, and removes the mark when it quits. A failed task nobody
# awaited is reported as unobserved-task once collected, and the app runs on.
#
# NEGATIVE CONTROL: this fails if the first run is skipped or recorded wrongly, if a UI-thread exception leaves the
# clipboard holding the copy, the app running or its message on standard error, if a report holds a sentinel, a path,
# another kind, screen or exception, or is not sent byte for byte, if any header but the content type identifies the
# sender, if a report is kept or sent while switched off, if the held request delays an unlock or the quit or takes the
# report off the outbox, if a killed run or an unobserved task is not reported, or if the unobserved task ends the app.
# These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MASTER='ci-crash-master-pw'
readonly VAULT_NAME='SENTINEL-VAULT-6f1c2a'
readonly ENTRY='SENTINEL-ENTRY-2b7d90'
readonly SECRET='SENTINEL-PASSWORD-8e4a13'
readonly MESSAGE='SENTINEL-MESSAGE-3c9f57'

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_FILES='APP_OUT APP_ERR ENDPOINT_OUT SILENT_OUT'
require jq
require perl

CLI="$(keypaste_bin)"
DRV="$(app_driver)"

WORK="$(mktemp -d)"
VAULT="$(native "$WORK/$VAULT_NAME.kdbx")"
export KEYPASTE_DRIVER_PASSWORD="$MASTER"
APP_PIDS=''

# Fixed descriptors, as macOS's bash 3.2 needs (D-0398): 4 is the endpoint's input, 5 the silent endpoint's, 6 to 8 the apps'.
for fd in 4 5 6 7 8; do eval "exec $fd>/dev/null"; done
cleanup() {
  local pid
  for fd in 8 7 6 5 4; do eval "exec $fd>&-" 2>/dev/null || true; done
  for pid in $APP_PIDS; do kill_process "$pid"; done
  rm -rf "$WORK"
}
trap cleanup EXIT

now_ms() { perl -MTime::HiRes=time -e 'printf "%d\n", time * 1000'; }

# Starts the app as keypaste-app starts, in home $2 with its standard input on descriptor $1 and its output in $3, sending
# crash reports to $4 when one is given; waits until it runs and sets APP_PID, APP_OUT and APP_ERR.
start_app() {
  local fd="$1"
  START_HOME="$(native "$2")"
  APP_OUT="$3"
  APP_ERR="$3.err"
  START_URL="${4:-}"
  mkdir -p "$2"
  : >"$APP_OUT"
  eval "exec $fd>&-"
  eval "exec $fd> >(exec 4>&- 5>&- 6>&- 7>&- 8>&-; KEYPASTE_HOME=\"\$START_HOME\" KEYPASTE_CRASH_URL=\"\$START_URL\" exec \"\$DRV\" launch >\"\$APP_OUT\" 2>\"\$APP_ERR\")"
  wait_for '^running as process' "$APP_OUT"
  APP_PID="$(sed -nE 's/^running as process ([0-9]+) .*/\1/p' "$APP_OUT" | tr -d '\r' | tail -n 1)"
  [ -n "$APP_PID" ] || die "the app started with no process id"
  APP_PIDS="$APP_PIDS $APP_PID"
}

# say <descriptor> <line>: one line to the app on that descriptor.
say() { eval "printf '%s\n' \"\$2\" >&$1"; }

# Ends the app on descriptor $1 by closing its input, and waits for its exit code $2 in $3.
quit_app() {
  eval "exec $1>&-"
  wait_for "^shut down with exit code $2" "$3"
}

# The reports waiting in home $1, one file a line.
waiting() { find "$1/crash-reports" -name '*.json' -type f 2>/dev/null | sort; }

# The first report waiting in home $1 whose kind is $2, or nothing.
report_of() {
  local file
  waiting "$1" | while read -r file; do
    if jq -e --arg kind "$2" '.kind == $kind' "$file" >/dev/null 2>&1; then
      echo "$file"
      break
    fi
  done
}

# Fails if file $1 holds a sentinel, the work folder's name or an exception's message; $2 says what it is.
holds_nothing() {
  local needle
  for needle in "$VAULT_NAME" "$ENTRY" "$SECRET" "$MESSAGE" "$(basename "$WORK")"; do
    grep -qF -- "$needle" "$1" && die "$2 holds '$needle'"
  done
  return 0
}

# A first start in home $1 on descriptor 6, answering the first run with $2 and crashing with an item's password copied.
crash_with_copy() {
  local home="$1" answer="$2" url="${3:-}"
  start_app 6 "$home" "$home.out" "$url"
  wait_for '^screen first-run' "$APP_OUT"
  grep -q '^screen unlock' "$APP_OUT" && die "a fresh home showed the unlock screen before the first run's terms"
  say 6 "first-run $answer"
  wait_for '^screen unlock' "$APP_OUT"
  say 6 "unlock $VAULT"
  wait_for '^screen shell' "$APP_OUT"
  say 6 "copy $ENTRY"
  wait_for '^clipboard set' "$APP_OUT"
  say 6 "crash $MESSAGE $VAULT $ENTRY $SECRET"
  wait_for '^shut down with exit code 70' "$APP_OUT"
  grep -q '^clipboard cleared' "$APP_OUT" || die "a crash with the password copied did not clear the clipboard"
  grep -qF -- "$MESSAGE" "$APP_ERR" && die "a crash printed its message on standard error"
  holds_nothing "$APP_OUT" "the app's output"
  return 0
}

# The app.toml in home $1 says crash_reports = $2 under a terms date.
chose() {
  grep -q "^crash_reports = $2" "$1/app.toml" || die "app.toml in $1 does not say crash_reports = $2: $(cat "$1/app.toml" 2>/dev/null)"
  grep -Eq '^terms = "[0-9]{4}-[0-9]{2}-[0-9]{2}"' "$1/app.toml" || die "app.toml in $1 records no terms date"
}

# ------------------------------------------------------------- a vault whose names and value are sentinels
mkdir -p "$WORK/cli-home"
export KEYPASTE_HOME
KEYPASTE_HOME="$(native "$WORK/cli-home")"
printf '%s\n%s\n' "$MASTER" "$MASTER" | "$CLI" init "$VAULT" >/dev/null || die "could not create the vault"
printf '%s\n%s\n' "$MASTER" "$SECRET" | "$CLI" add "$ENTRY" --vault "$VAULT" >/dev/null || die "could not store the item"

ENDPOINT_OUT="$WORK/endpoint.txt"
exec 4>&-
exec 4> >(exec 5>&- 6>&- 7>&- 8>&-; exec "$DRV" crash-endpoint "$(native "$WORK/received")" >"$ENDPOINT_OUT" 2>&1)
wait_for '^listening on http://127.0.0.1:' "$ENDPOINT_OUT"
ENDPOINT="$(sed -n 's/^listening on //p' "$ENDPOINT_OUT" | tr -d '\r')"

SILENT_OUT="$WORK/silent.txt"
exec 5>&-
exec 5> >(exec 4>&- 6>&- 7>&- 8>&-; exec "$DRV" crash-endpoint "$(native "$WORK/held")" --silent >"$SILENT_OUT" 2>&1)
wait_for '^listening on http://127.0.0.1:' "$SILENT_OUT"
SILENT="$(sed -n 's/^listening on //p' "$SILENT_OUT" | tr -d '\r')"

# ------------------------------------------------------------- sending: a UI-thread crash is reported
step 'Send crash reports, then a crash on the UI thread with a password copied'
ON="$WORK/on"
crash_with_copy "$ON" send "$ENDPOINT"
chose "$ON" 1
[ "$(waiting "$ON" | wc -l | tr -d ' ')" = 1 ] || die "the crash left $(waiting "$ON" | wc -l | tr -d ' ') reports, not one: $(waiting "$ON")"
REPORT="$(waiting "$ON")"
cp "$REPORT" "$WORK/sent.json"
holds_nothing "$REPORT" "the crash report"
jq -e '.kind == "crash" and .site == null and .screen == "items" and .uptime == "<1m" and .count == 1
  and .exception.type == "System.InvalidOperationException" and (.exception.frames | length) > 0
  and (.exception.inner | length) == 1 and .exception.inner[0].type == "System.IO.IOException"' "$REPORT" >/dev/null \
  || die "the crash report is not the UI-thread crash it should be: $(cat "$REPORT")"
[ -e "$ON/crash-reports/running" ] && die "the run a crash ended left its run marker, though its report was kept"
[ -e "$WORK/received/1.json" ] && die "the report was sent before the sender's first pass"

step "Don't send, then the same crash"
OFF="$WORK/off"
crash_with_copy "$OFF" dont-send "$ENDPOINT"
chose "$OFF" 0
[ -e "$OFF/crash-reports" ] && die "switched off, the crash left $(ls "$OFF/crash-reports")"

step 'Sending, then a crash an endpoint that never answers will hold'
HELD="$WORK/held-home"
start_app 6 "$HELD" "$HELD.out" "$SILENT"
wait_for '^screen first-run' "$APP_OUT"
say 6 'first-run send'
wait_for '^screen unlock' "$APP_OUT"
say 6 "unlock $VAULT"
wait_for '^screen shell' "$APP_OUT"
say 6 "crash $MESSAGE"
wait_for '^shut down with exit code 70' "$APP_OUT"
[ "$(waiting "$HELD" | wc -l | tr -d ' ')" = 1 ] || die "the crash in the held endpoint's home left no single report"

# ------------------------------------------------------------- next starts, side by side past the first pass
step 'A report put where the switch is off, which its next start deletes unsent'
mkdir -p "$OFF/crash-reports"
cp "$WORK/sent.json" "$OFF/crash-reports/$(basename "$REPORT")"
start_app 7 "$OFF" "$OFF-2.out" "$ENDPOINT"
OFF_OUT="$APP_OUT"
wait_for '^screen unlock' "$OFF_OUT"
grep -q '^screen first-run' "$OFF_OUT" && die "the first run showed again once Don't send was chosen"
for _ in $(seq 1 50); do [ -e "$OFF/crash-reports" ] || break; sleep 0.2; done
[ -e "$OFF/crash-reports" ] && die "switched off, the next start kept the outbox: $(ls "$OFF/crash-reports")"

step 'The next start sends the report'
start_app 6 "$ON" "$ON-2.out" "$ENDPOINT"
ON_OUT="$APP_OUT"
wait_for '^screen unlock' "$ON_OUT"
grep -q '^screen first-run' "$ON_OUT" && die "the first run showed again once Send crash reports was chosen"

step 'The next start where the endpoint holds the request'
start_app 8 "$HELD" "$HELD-2.out" "$SILENT"
HELD_OUT="$APP_OUT"
wait_for '^screen unlock' "$HELD_OUT"
say 8 "unlock $VAULT"
wait_for '^screen shell' "$HELD_OUT"

WAIT_SECONDS=75 wait_for '^received 1' "$ENDPOINT_OUT"
cmp -s "$WORK/received/1.json" "$WORK/sent.json" || die "the endpoint received other bytes than the outbox kept"
head -n 1 "$WORK/received/1.head" | tr -d '\r' | grep -qx 'POST /api/crash HTTP/1.1' \
  || die "the report was not posted to /api/crash: $(head -n 1 "$WORK/received/1.head")"
grep -qi '^content-type: application/json' "$WORK/received/1.head" || die "the report was not sent as application/json"
grep -Eqi '^(cookie|user-agent|traceparent|tracestate|request-id|authorization):' "$WORK/received/1.head" \
  && die "the request carried a header that is not the content type's: $(cat "$WORK/received/1.head")"
for _ in $(seq 1 50); do [ -z "$(waiting "$ON")" ] && break; sleep 0.2; done
[ -z "$(waiting "$ON")" ] || die "the sent report is still waiting: $(waiting "$ON")"

WAIT_SECONDS=75 wait_for '^connected' "$SILENT_OUT"
say 8 lock
wait_for '^screen unlock' "$HELD_OUT" 2
started="$(now_ms)"
say 8 "unlock $VAULT"
wait_for '^screen shell' "$HELD_OUT" 2
took=$(($(now_ms) - started))
[ "$took" -lt 8000 ] || die "an unlock while the endpoint held the request took ${took} ms"
started="$(now_ms)"
quit_app 8 0 "$HELD_OUT"
took=$(($(now_ms) - started))
[ "$took" -lt 5000 ] || die "quitting while the endpoint held the request took ${took} ms"
[ "$(waiting "$HELD" | wc -l | tr -d ' ')" = 1 ] || die "the report the endpoint never answered is no longer waiting"

quit_app 6 0 "$ON_OUT"
quit_app 7 0 "$OFF_OUT"
[ -e "$WORK/received/2.json" ] && die "the endpoint received a second report, which only the switched-off home held"
[ -e "$OFF/crash-reports" ] && die "switched off, the app wrote an outbox while it ran"

# ------------------------------------------------------------- a run that ended with nothing able to report it
step 'A killed run, reported at the next start'
KILLED="$WORK/killed"
start_app 6 "$KILLED" "$KILLED.out"
wait_for '^screen first-run' "$APP_OUT"
say 6 'first-run send'
wait_for '^screen unlock' "$APP_OUT"
[ -e "$KILLED/crash-reports/running" ] || die "a run sending reports set no run marker"
kill_process "$APP_PID"
exec 6>&-
[ -e "$KILLED/crash-reports/running" ] || die "the killed run's marker is gone"
[ -z "$(waiting "$KILLED")" ] || die "the killed run left a report, though no hook could run"

start_app 6 "$KILLED" "$KILLED-2.out"
wait_for '^screen unlock' "$APP_OUT"
[ "$(waiting "$KILLED" | wc -l | tr -d ' ')" = 1 ] || die "the start after a killed run reported $(waiting "$KILLED" | wc -l | tr -d ' ') runs, not one"
ENDED="$(waiting "$KILLED")"
jq -e '.kind == "ended-unexpectedly" and .exception == null and .site == null and .screen == null and .uptime == null' "$ENDED" >/dev/null \
  || die "the killed run's report is not an ended-unexpectedly one: $(cat "$ENDED")"
holds_nothing "$ENDED" "the killed run's report"
[ -e "$KILLED/crash-reports/running" ] || die "the start after a killed run set no marker of its own"
quit_app 6 0 "$APP_OUT"
[ -e "$KILLED/crash-reports/running" ] && die "a run that quit cleanly left its marker"

step 'A failed task nobody awaited, reported while the app runs on'
start_app 6 "$KILLED" "$KILLED-3.out"
wait_for '^screen unlock' "$APP_OUT"
say 6 "unobserved $MESSAGE"
wait_for '^collected' "$APP_OUT"
UNOBSERVED=''
for _ in $(seq 1 50); do
  UNOBSERVED="$(report_of "$KILLED" unobserved-task)"
  [ -n "$UNOBSERVED" ] && break
  sleep 0.2
done
[ -n "$UNOBSERVED" ] || die "a failed task nobody awaited left no report"
holds_nothing "$UNOBSERVED" "the unobserved task's report"
grep -q '^shut down' "$APP_OUT" && die "a failed task nobody awaited ended the app"
quit_app 6 0 "$APP_OUT"

echo "ok: a UI-thread crash cleared the clipboard, locked, quit and was reported without a sentinel, then sent byte for byte;"
echo "    switched off nothing was kept or sent; a held request delayed neither an unlock nor the quit and kept its report;"
echo "    a killed run was reported as ended-unexpectedly at the next start, and a failed task nobody awaited as unobserved"
