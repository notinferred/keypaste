# shellcheck shell=bash
# Drives an installed keypaste app from outside its process for the desktop exercises: UI Automation
# on Windows (drive-desktop-windows.ps1), AT-SPI and xdotool on Linux (drive-desktop-linux.py), the accessibility
# API and System Events on macOS (drive-desktop-macos.js), with the app's start and stop around them. Sourced after
# the exercise defines ROOT and RID, and lib/macos-install.sh; on Windows and macOS, APP_PID names the app to drive.
#
#   drive [--window <title>] <verb> [arguments...]
#
# The verbs are one interface the three drivers implement. Each acts in the app's window titled keypaste, or in the
# app's window --window names, such as a prompt's. A <locator> names one element of that window:
#   <name>             its accessible name: AutomationProperties.Name, or a button's or a text's own text
#   id:<id>            its automation id, which Avalonia takes from the control's x:Name
#   <locator>[<n>]     the nth element it names, in tree order, counting from 1
#
#   window <seconds>               waits for the window; prints window '<title>' and where it is
#   windows                        prints the title of each window the app shows, one a line
#   screenshot <png>               captures the window; prints colours=<n> size=<w>x<h>
#   type <text>                    types into what has focus; prints typed <n> characters
#   key <chord>                    Return, Escape, Tab, shift+Tab, Delete, ctrl+<key> or cmd+<key>; prints sent <chord>
#   key-wait <chord> <locator> <seconds>
#                                  sends the chord, then waits up to <seconds> for the locator, timed inside the
#                                  driver; prints found '<locator>' <n> ms after <chord>
#   find <locator>                 waits up to 30 s for it; prints found '<locator>' as <role>
#   enabled <locator>              waits up to 30 s for it; prints enabled or not enabled, without waiting for either
#   find-prefix <text>             waits up to 30 s for a name starting <text>; prints that whole name
#   wait <locator> <seconds>       waits up to <seconds> for it; prints found '<locator>' as <role>
#   wait-gone <locator> <seconds>  waits up to <seconds> until it is gone; prints gone '<locator>'
#   read <locator>                 prints its text or value; a secret-bearing control has no name to give it
#   select <text>                  selects the list item holding the text <text>; prints selected '<text>'
#   select-nth <locator> <n>       selects the nth item of the list it names; prints selected item <n> of '<locator>'
#   invoke <locator>               performs its default action, a button's press; prints invoked '<locator>'
#   toggle <locator>               flips a toggle button or check box; prints toggled '<locator>'
#   check <locator> <on|off>       sets a check box or switch; prints '<locator>' is <on|off>
#   click <locator>                a real pointer click at its centre; prints clicked '<locator>' at <x>,<y>
#   right-click <locator>          the same with the secondary button; prints right-clicked '<locator>' at <x>,<y>
#   focus <locator>                gives it keyboard focus, as a click on a field does; prints focused '<locator>'
#   set-after <label> <text>       sets the first text field after the label; prints set the field after '<label>'
#   set-named <locator> <text>     sets the text field; prints set the field named '<locator>'
#
# A driver exits 0 when it did the verb, 1 when it could not, saying why, and 2 when this platform's driver does not
# implement the verb, the chord or the locator's form, saying "<what> not implemented on <os>".

drive() {
  local window=keypaste
  if [ "${1:-}" = --window ]; then
    window="$2"
    shift 2
  fi
  # A subshell, so the title reaches the driver whether it is a program or, on macOS, a function.
  (
    export DRIVE_WINDOW="$window"
    case "$(platform)" in
      windows)
        local action="$1"
        shift
        powershell -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$ROOT/scripts/drive-desktop-windows.ps1")" \
          -Action "$action" -AppPid "$APP_PID" -First "${1:-}" -Second "${2:-}" -Third "${3:-}"
        ;;
      macos) mac_osascript 180 "$ROOT/scripts/drive-desktop-macos.js" "$APP_PID" "$@" ;;
      *) python3 "$ROOT/scripts/drive-desktop-linux.py" "$@" ;;
    esac
  )
}

# mac_bounded <seconds> <command...>: bounded with its status kept, as macOS has no timeout(1) and an Apple Event
# or a LaunchServices open can wait on a dialog nobody answers; past the bound it is killed and reports 142.
mac_bounded() {
  local seconds="$1"
  shift
  perl -e 'alarm shift; exec @ARGV or die "exec $ARGV[0]: $!\n"' "$seconds" "$@"
}

mac_osascript() {
  local seconds="$1"
  shift
  mac_bounded "$seconds" osascript -l JavaScript "$@"
}

# The desktop platform of the exercise's RID; die is the exercise's own.
platform() {
  case "$RID" in
    win-x64) echo windows ;;
    linux-x64) echo linux ;;
    osx-arm64) echo macos ;;
    *) die "no desktop candidate for $RID" ;;
  esac
}

sha256_of() { if command -v sha256sum >/dev/null; then sha256sum < "$1"; else shasum -a 256 < "$1"; fi | awk '{print $1}'; }

# go <id>: a click on the rail's place or the sidebar's row, as a person reaches a screen: RailEntries, RailAgentActivity,
# RailRecommendations and RailSettings on the rail; Place<place> and Project:<name> in the sidebar.
go() { drive click "id:$1"; }

# says <locator> <prefix>: waits up to 30 s until the element's text starts with <prefix>, and prints it.
says() {
  local said=''
  for _ in $(seq 1 30); do
    said="$(drive read "$1" 2>&1 | tr -d '\r')"
    case "$said" in "$2"*) printf '%s\n' "$said"; return 0 ;; esac
    sleep 1
  done
  echo "$1 says '$said', not '$2...'"
  return 1
}

# app_start <app> <output path without extension>: starts the installed app with this shell's environment, its output
# in <path>.out and <path>.log, and sets APP_PID to the process whose window is driven. The Windows and macOS drivers
# write the window's tree beside them whenever a step cannot be done.
app_start() {
  case "$(platform)" in
    windows)
      DRIVE_TREE_DIR="$(cygpath -w "$(dirname "$2")")"
      export DRIVE_TREE_DIR
      windows_start "$1" "$2"
      ;;
    macos)
      DRIVE_TREE_DIR="$(dirname "$2")"
      export DRIVE_TREE_DIR
      APP_PID="$(mac_start "$1" "$2.log")" || { echo "$APP_PID"; APP_PID=''; return 1; }
      ;;
    *)
      "$1" > "$2.log" 2>&1 &
      APP_PID=$!
      ;;
  esac
}

# Stops what app_start started, if anything.
app_stop() {
  [ -n "${APP_PID:-}" ] || return 0
  case "$(platform)" in
    windows) windows_stop ;;
    macos) mac_quit ;;
    *)
      pkill -f usr/bin/keypaste-app 2>/dev/null || true
      kill "$APP_PID" 2>/dev/null || true
      wait "$APP_PID" 2>/dev/null || true
      ;;
  esac
  APP_PID=''
  sleep 2
}

# windows_start <exe> <output path without extension>: sets LAUNCH_PID to the started app and APP_PID to the copy it
# starts of itself with .NET's diagnostics port off and waits for, whose window is driven, or to LAUNCH_PID where no
# copy appears.
windows_start() {
  local cr=$'\r'
  # To a file, not a pipe: the app inherits PowerShell's handles, and an inherited pipe would hold this shell until the app exits.
  powershell -NoProfile -NonInteractive -Command "(Start-Process -FilePath '$(cygpath -w "$1")' -RedirectStandardOutput '$(cygpath -w "$2.out")' -RedirectStandardError '$(cygpath -w "$2.log")' -PassThru).Id" \
    > "$2.pid" || { echo "PowerShell could not start $1"; return 1; }
  LAUNCH_PID="$(tr -d "$cr" < "$2.pid")"
  case "$LAUNCH_PID" in '' | *[!0-9]*) echo "starting $1 gave no process ID: $LAUNCH_PID"; LAUNCH_PID=''; return 1 ;; esac
  APP_PID="$(powershell -NoProfile -NonInteractive -Command "\$deadline = (Get-Date).AddSeconds(15)
    do {
      \$child = Get-CimInstance Win32_Process -Filter 'ParentProcessId=$LAUNCH_PID' | Where-Object Name -eq 'keypaste-app.exe' | Select-Object -First 1
      if (-not \$child) { Start-Sleep -Milliseconds 250 }
    } while (-not \$child -and (Get-Date) -lt \$deadline)
    if (\$child) { \$child.ProcessId } else { $LAUNCH_PID }" | tr -d "$cr")"
}

windows_stop() {
  taskkill //F //T //PID "${LAUNCH_PID:-$APP_PID}" > /dev/null 2>&1 || true
  taskkill //F //PID "$APP_PID" > /dev/null 2>&1 || true
  LAUNCH_PID=''
}

# Avalonia publishes its AT-SPI tree only once the session's accessibility bus says a reader is present.
enable_accessibility() {
  local property
  for property in IsEnabled ScreenReaderEnabled; do
    gdbus call --session --dest org.a11y.Bus --object-path /org/a11y/bus \
      --method org.freedesktop.DBus.Properties.Set org.a11y.Status "$property" '<true>' || return 1
  done
  gdbus call --session --dest org.a11y.Bus --object-path /org/a11y/bus --method org.a11y.Bus.GetAddress
}
