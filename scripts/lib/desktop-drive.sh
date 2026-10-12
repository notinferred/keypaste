# shellcheck shell=bash
# Drives an installed keypaste app from outside its process for the desktop exercises (D-0199): UI Automation
# on Windows (drive-desktop-windows.ps1), AT-SPI and xdotool on Linux (drive-desktop-linux.py). Sourced after
# the exercise defines ROOT and platform; on Windows, APP_PID names the app to drive.

drive() {
  if [ "$(platform)" = windows ]; then
    local action="$1"
    shift
    powershell -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$ROOT/scripts/drive-desktop-windows.ps1")" \
      -Action "$action" -AppPid "$APP_PID" -First "${1:-}" -Second "${2:-}"
  else
    python3 "$ROOT/scripts/drive-desktop-linux.py" "$@"
  fi
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
