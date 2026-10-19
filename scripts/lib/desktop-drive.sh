# shellcheck shell=bash
# Drives an installed keypaste app from outside its process for the desktop exercises (D-0199): UI Automation
# on Windows (drive-desktop-windows.ps1), AT-SPI and xdotool on Linux (drive-desktop-linux.py), the accessibility
# API and System Events on macOS (drive-desktop-macos.js). Sourced after the exercise defines ROOT and platform;
# on Windows and macOS, APP_PID names the app to drive.

drive() {
  case "$(platform)" in
    windows)
      local action="$1"
      shift
      powershell -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$ROOT/scripts/drive-desktop-windows.ps1")" \
        -Action "$action" -AppPid "$APP_PID" -First "${1:-}" -Second "${2:-}"
      ;;
    macos) mac_osascript 180 "$ROOT/scripts/drive-desktop-macos.js" "$APP_PID" "$@" ;;
    *) python3 "$ROOT/scripts/drive-desktop-linux.py" "$@" ;;
  esac
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
