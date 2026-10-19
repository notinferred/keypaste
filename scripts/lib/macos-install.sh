# shellcheck shell=bash
# The macOS steps of the desktop exercises (4.7e): a DMG quarantined as a browser download, attached and its
# keypaste.app copied to /Applications, what Gatekeeper shows for that copy, the installed app started and quit
# through LaunchServices, and a bundle moved to the Trash by Finder. Sourced after lib/desktop-drive.sh and once the exercise defines ROOT, OUT and fact;
# written for the stock bash 3.2 (D-0398).
#
#   mac_quarantine <file>       stamps a browser download's quarantine through NSURLQuarantinePropertiesKey
#   mac_mountpoint              a fresh mount point in a private temporary folder, outside any records folder, so a
#                               volume left attached is never uploaded with them: the image links /Applications
#   mac_attach <dmg> <dir>      attaches it read-only and unbrowsed at <dir>, and detaches it again when it is not
#                               mounted there
#   mac_detach <dir>            detaches; a refusal is kept with what holds the volume, never retried (F.64)
#   mac_install <dmg> <app>     attaches the image at a mount point of its own, copies its keypaste.app to <app> with
#                               ditto, which keeps the quarantine a Finder drag keeps, and detaches it
#   mac_trash <app>             has Finder move it to the Trash, as a person deletes an app
#   mac_bundles                 how many keypaste bundles /Applications holds
#   mac_quarantined <path>      how many files under <path> carry com.apple.quarantine
#   mac_gatekeeper <app>        opens it through LaunchServices, records what Gatekeeper shows and cancels it
#   mac_launcher <app>          opens its Terminal launcher on a planted request; says whether Terminal ran it
#   mac_start <app> <log>       starts it through LaunchServices, which gives it this environment; prints its pid
#   mac_quit                    quits every keypaste-app and waits for it to go

readonly MAC_DRIVER="$ROOT/scripts/drive-desktop-macos.js"

mac_quarantine() {
  # shellcheck disable=SC2016 # JXA's $ is its Objective-C bridge, which the shell must not expand.
  mac_osascript 30 -e 'ObjC.import("Foundation");
    function run(argv) {
      const key = typeof $.NSURLQuarantinePropertiesKey === "undefined" ? $("NSURLQuarantinePropertiesKey") : $.NSURLQuarantinePropertiesKey;
      const properties = $.NSMutableDictionary.alloc.init;
      properties.setObjectForKey($("Safari"), $("LSQuarantineAgentName"));
      properties.setObjectForKey($("com.apple.Safari"), $("LSQuarantineAgentBundleIdentifier"));
      properties.setObjectForKey($("LSQuarantineTypeWebDownload"), $("LSQuarantineType"));
      const error = Ref();
      if (!$.NSURL.fileURLWithPath(argv[0]).setResourceValueForKeyError(properties, key, error)) throw new Error(ObjC.unwrap(error[0].localizedDescription));
      return "quarantined";
    }' "$1" > /dev/null || return 1
  xattr -p com.apple.quarantine "$1" || { echo "$1 carries no quarantine after it was stamped"; return 1; }
}

# Resolved, because mount(8) reports /var/folders under /private.
mac_mountpoint() { local dir; dir="$(cd "$(mktemp -d)" && pwd -P)" && printf '%s/volume' "$dir"; }

mac_attach() {
  mkdir -p "$2"
  hdiutil attach "$1" -readonly -nobrowse -noautoopen -mountpoint "$2" > /dev/null || return 1
  mount | grep -qF " on $2 (" && return 0
  echo "$1 is not mounted at $2"
  hdiutil detach "$2" > /dev/null 2>&1 || true
  return 1
}

mac_detach() {
  hdiutil detach "$1" > /dev/null && return 0
  echo "$1 did not detach; what holds it:"
  lsof "$1" 2>&1 | head -n 20
  return 1
}

mac_install() {
  local volume
  volume="$(mac_mountpoint)" || return 1
  mac_attach "$1" "$volume" || return 1
  if [ ! -d "$volume/keypaste.app" ] || [ "$(readlink "$volume/Applications")" != /Applications ]; then
    echo 'the image holds no keypaste.app beside a link to /Applications'
    mac_detach "$volume"
    return 1
  fi
  ditto "$volume/keypaste.app" "$2" || { mac_detach "$volume"; return 1; }
  mac_detach "$volume"
}

mac_trash() {
  mac_bounded 60 osascript -e "tell application \"Finder\" to delete (POSIX file \"$1\" as alias)" > /dev/null || return 1
  [ ! -e "$1" ] || { echo "$1 is still there after Finder deleted it"; return 1; }
}

mac_bundles() { find /Applications -maxdepth 1 -name 'keypaste*.app' | grep -c . || true; }

mac_quarantined() { xattr -lr "$1" 2>/dev/null | grep -c 'com.apple.quarantine' || true; }

# With their state, which shows a start Gatekeeper holds stopped.
mac_processes() {
  local pids
  pids="$(pgrep -d, -f 'keypaste.app/Contents/MacOS/keypaste-app')" || return 0
  ps -o pid=,stat=,command= -p "$pids"
}

# On-screen windows of any keypaste-app, by process id, or unread when the window list could not be read.
mac_windows() {
  local pids
  pids="$(pgrep -d, -f 'keypaste.app/Contents/MacOS/keypaste-app')" || { echo 0; return; }
  # shellcheck disable=SC2016 # JXA's $ is its Objective-C bridge, which the shell must not expand.
  mac_osascript 30 -e 'ObjC.import("CoreGraphics");
    function run(argv) {
      const pids = argv[0].split(",").map(Number);
      const all = ObjC.deepUnwrap(ObjC.castRefToObject($.CGWindowListCopyWindowInfo(1 | 16, 0))) || [];
      return String(all.filter(w => pids.includes(w.kCGWindowOwnerPID) && w.kCGWindowLayer === 0).length);
    }' "$pids" || echo unread
}

mac_dialog() { mac_osascript 30 "$MAC_DRIVER" - dialog "$1"; }

# Dismisses a dialog the way a person who declines does; Move to Trash is never pressed.
mac_decline() {
  local button
  for button in Cancel Done; do
    mac_osascript 30 "$MAC_DRIVER" - press "$1" "$button" > /dev/null && return 0
  done
  return 1
}

# The opener waits as long as the dialog stays, so it runs beside the reading and ends with it.
mac_gatekeeper() {
  local status opener dialog='' declined=true
  status="$(spctl --status 2>/dev/null)"
  if [ "$status" != 'assessments enabled' ]; then
    if sudo -n /usr/sbin/spctl --global-enable > /dev/null 2>&1; then fact gatekeeper_enabled_by_leg true; fi
    status="$(spctl --status 2>/dev/null)"
  fi
  fact gatekeeper_assessments "$status"
  fact sip "$(csrutil status 2>&1)"
  [ "$status" = 'assessments enabled' ] || { echo "Gatekeeper reports '$status'"; return 0; }

  /usr/bin/open "$1" > "$OUT/gatekeeper-open.log" 2>&1 &
  opener=$!
  for _ in $(seq 1 60); do
    dialog="$(mac_dialog CoreServicesUIAgent)"
    [ "$dialog" = '[]' ] || break
    sleep 0.5
  done
  fact gatekeeper_dialog "$(jq -r '[.[].texts[]] | join(" ")' <<< "$dialog")"
  fact gatekeeper_buttons "$(jq -r '[.[].buttons[] | select(. != "")] | join(", ")' <<< "$dialog")"
  fact gatekeeper_process "$(mac_processes)"
  fact gatekeeper_windows "$(mac_windows)"
  mac_decline CoreServicesUIAgent || declined=false
  sleep 3
  fact gatekeeper_after "$(mac_processes)"
  kill "$opener" 2>/dev/null || true
  wait "$opener" 2>/dev/null || true
  echo "dialog: $dialog"
  if [ "$declined" = false ]; then
    pkill -9 -f 'keypaste.app/Contents/MacOS/keypaste-app' || true
    echo 'the dialog took no Cancel or Done'
    return 1
  fi
}

# A request the launcher claims is consumed whether or not it is usable, so its absence means Terminal ran it.
mac_launcher() {
  local temporary folder request opener said
  temporary="$(getconf DARWIN_USER_TEMP_DIR)"
  folder="${temporary%/}/keypaste-launch"
  mkdir -p "$folder" && chmod 700 "$folder"
  request="$folder/exercise-$$.request"
  printf 'exercise\n' > "$request"
  /usr/bin/open -b com.apple.Terminal "$1/Contents/Resources/keypaste-launch.command" > /dev/null 2>&1 &
  opener=$!
  for _ in $(seq 1 40); do [ -e "$request" ] || break; sleep 0.5; done
  if [ -e "$request" ]; then
    said="Terminal did not run the launcher; CoreServicesUIAgent showed $(mac_dialog CoreServicesUIAgent | jq -r '[.[].texts[]] | join(" ")')"
    mac_decline CoreServicesUIAgent || true
  else
    said='Terminal ran the launcher'
  fi
  pkill -x Terminal || true
  kill "$opener" 2>/dev/null || true
  wait "$opener" 2>/dev/null || true
  rm -f "$folder"/exercise-*
  echo "$said"
}

mac_start() {
  local pid=''
  mac_bounded 30 /usr/bin/open -n --stdout "$2.out" --stderr "$2" "$1" || { echo "open -n $1 did not return within 30 s"; return 1; }
  for _ in $(seq 1 60); do
    pid="$(pgrep -f "$1/Contents/MacOS/keypaste-app" | head -n 1)"
    [ -z "$pid" ] || break
    sleep 0.5
  done
  [ -n "$pid" ] || { echo "no keypaste-app from $1 within 30 s"; return 1; }
  printf '%s' "$pid"
}

mac_quit() {
  pkill -x keypaste-app 2>/dev/null || true
  for _ in $(seq 1 40); do pgrep -x keypaste-app > /dev/null || return 0; sleep 0.25; done
  pkill -9 -x keypaste-app 2>/dev/null || true
}
