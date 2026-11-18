# shellcheck shell=bash
# The KeePassXC gates' setup and readers, sourced after common.sh. Sourcing it sets pw and cli and fails
# the gate when either is missing, because a gate that passes without KeePassXC proves nothing (D-0008).
#
# Env: KP_COMPAT_PASSWORD  the master password of every vault a gate makes (required)
#      KPXC_CLI            path to keepassxc-cli (default: PATH lookup)
#      KPXC_APP            path to KeePassXC's app (default: beside keepassxc-cli, then PATH)

pw=${KP_COMPAT_PASSWORD:-}
[ -n "$pw" ] || die "KP_COMPAT_PASSWORD is not set"
cli=${KPXC_CLI:-keepassxc-cli}
if ! command -v "$cli" >/dev/null 2>&1 && [ ! -x "$cli" ]; then
  die "keepassxc-cli not found (KPXC_CLI='${cli}'). This gate must never be skipped or soft-passed."
fi

# KeePassXC's app sits beside its CLI in the Windows archive and the macOS bundle; apt puts it on PATH.
keepassxc_app() {
  local app=${KPXC_APP:-} cli_dir candidate
  if [ -z "$app" ]; then
    cli_dir=$(dirname "$(realpath "$(command -v "$cli" || printf '%s' "$cli")")")
    for candidate in "$cli_dir/KeePassXC.exe" "$cli_dir/KeePassXC" "$(command -v keepassxc || true)"; do
      if [ -n "$candidate" ] && [ -x "$candidate" ]; then app=$candidate; break; fi
    done
  fi
  if [ -z "$app" ] || [ ! -x "$app" ]; then die "KeePassXC's app not found beside '$cli' (set KPXC_APP)"; fi
  printf '%s' "$app"
}

# keepassxc-cli takes a password only on stdin, one line per prompt, and Qt writes CRLF on Windows.
# No -q: it hides the reason for a failure along with the prompt.
kpxc() { printf '%s\n' "$pw" | "$cli" "$@" | tr -d '\r'; }

# keepassxc-cli <command> -q <the vault at $2> <the other arguments>.
kx() {
  local sub=$1 db=$2
  shift 2
  printf '%s\n' "$pw" | "$cli" "$sub" -q "$(native "$db")" "$@" | tr -d '\r'
}

# The KDBX signature, then the minor and major version, in hex.
header() { od -An -v -tx1 -N12 "$1" | tr -d ' \n' | tr '[:upper:]' '[:lower:]'; }

# A fixed UUID, from a name of up to 16 characters, for a document KeePassXC imports.
uuid() { printf '%-16.16s' "$1" | base64; }

copies() { if [ -d "$1.backups" ]; then find "$1.backups" -maxdepth 1 -type f | wc -l | tr -d ' '; else echo 0; fi; }

# The entry's own <UUID> line, the last before the first <History>. A flag rather than exit or head,
# because leaving the stream early breaks tr's pipe and pipefail then fails the gate (F.16).
entry_uuid() {
  kpxc export -f xml "$1" \
    | tr -d '\t' \
    | awk '/<History>/{seen=1} !seen && /<UUID>/{last=$0} END{print last}'
}
