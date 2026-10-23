# shellcheck shell=bash
# Drives an installed keypaste app from outside its process for the desktop exercises (D-0199): UI Automation
# on Windows (drive-desktop-windows.ps1), AT-SPI and xdotool on Linux (drive-desktop-linux.py), the accessibility
# API and System Events on macOS (drive-desktop-macos.js). Sourced after the exercise defines ROOT and platform;
# on Windows and macOS, APP_PID names the app to drive.
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

# The app's Ctrl+<digit> chords are Cmd+<digit> on macOS (docs/desktop.md).
shortcut() { if [ "$(platform)" = macos ]; then drive key "cmd+$1"; else drive key "ctrl+$1"; fi; }

# Avalonia publishes its AT-SPI tree only once the session's accessibility bus says a reader is present.
enable_accessibility() {
  local property
  for property in IsEnabled ScreenReaderEnabled; do
    gdbus call --session --dest org.a11y.Bus --object-path /org/a11y/bus \
      --method org.freedesktop.DBus.Properties.Set org.a11y.Status "$property" '<true>' || return 1
  done
  gdbus call --session --dest org.a11y.Bus --object-path /org/a11y/bus --method org.a11y.Bus.GetAddress
}
