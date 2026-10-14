#!/bin/sh
# shellcheck shell=sh
# Terminal runs this for a Run or Open terminal confirmed in the keypaste app (D-0451): it claims the oldest request the
# app wrote and starts this bundle's keypaste with the request's fields as arguments and variables, evaluating none of them.

# A Terminal profile may close the window as soon as this exits, so a refusal waits for Return when someone can type in it.
refuse() {
  printf 'keypaste: %s, so nothing was started. Run it again from the app.\n' "$1" >&2
  if [ -t 0 ]; then
    printf 'Press Return to finish.' >&2
    read -r _
  fi
  exit 1
}
unusable='the run from the app could not be started here'

# The app writes each field as one line of printable ASCII, every byte outside it and every backslash as \0 and three octal
# digits, which printf's %b turns back into that byte; any other escape is refused, and the x keeps a trailing line break.
decode() {
  case $1 in
    *\\[!0]* | *\\) refuse "$unusable" ;;
  esac
  decoded=$(printf '%bx' "$1") || refuse "$unusable"
  decoded=${decoded%x}
}

program=${0%/*}/../MacOS/keypaste
[ -x "$program" ] || refuse "$unusable"

temporary=$(/usr/bin/getconf DARWIN_USER_TEMP_DIR) || refuse "$unusable"
folder=${temporary%/}/keypaste-launch
[ ! -L "$folder" ] || refuse "$unusable"

# A rename is atomic, so two windows never claim the same request.
claimed=
for request in "$folder"/*.request; do
  if /bin/mv -f -- "$request" "$request.$$" 2>/dev/null; then
    claimed=$request.$$
    break
  fi
done
[ -n "$claimed" ] || refuse 'no run from the app is waiting for this window'

{
  IFS= read -r format && IFS= read -r pass && IFS= read -r home && IFS= read -r vault && IFS= read -r approver &&
    IFS= read -r tmp && IFS= read -r project && IFS= read -r profile && IFS= read -r directory &&
    IFS= read -r kind && IFS= read -r command
} < "$claimed"
status=$?
/bin/rm -f -- "$claimed"
[ "$status" -eq 0 ] || refuse "$unusable"

[ "$format" = 'keypaste-launch 2' ] || refuse "$unusable"
case $pass in
  *[!ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-]*) refuse "$unusable" ;;
esac
[ "${#pass}" -eq 43 ] || refuse "$unusable"

decode "$home"; home=$decoded
decode "$vault"; vault=$decoded
decode "$approver"; approver=$decoded
decode "$tmp"; tmp=$decoded
decode "$project"; project=$decoded
decode "$profile"; profile=$decoded
decode "$directory"; directory=$decoded
decode "$command"; command=$decoded

# The scripts are TerminalLaunch.RunScript and OpenScript byte for byte; the owner refuses any other command.
# shellcheck disable=SC2016 # the run's shell expands these, not this one.
case $kind in
  run) set -- /bin/sh -c 'eval "$1"; exec "${SHELL:-/bin/sh}"' keypaste "$command" ;;
  open) set -- /bin/sh -c 'exec "${SHELL:-/bin/sh}"' keypaste ;;
  *) refuse "$unusable" ;;
esac

cd -- "$directory" || refuse "$unusable"

KEYPASTE_HOME=$home TMPDIR=$tmp KEYPASTE_LAUNCH=$pass
export KEYPASTE_HOME TMPDIR KEYPASTE_LAUNCH
exec "$program" run --session --vault "$vault" --approver "$approver" -p "$profile" --project="$project" -- "$@"
