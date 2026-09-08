#!/usr/bin/env bash
# Holds the packaged binaries to who this project says publishes them.
#
# F.4b's defect was not visible in Directory.Build.props. `Company` was never set there; the SDK
# filled it from `Authors`, and the shipped executables reported a person's name in a field no
# source file mentioned. So this reads the bytes that get archived, on the machine that built
# them, rather than the property they came from.
#
# It is one half of V-F.4b. The other half is tests/Keypaste.Core.Tests/PublisherMetadata.cs,
# compiled into each front end's test project, which asserts the built assembly's
# Company/Product/Copyright on every operating system CI runs. This is the packaged half,
# including the NativeAOT binaries that have no managed assembly left to reflect over.
#
# What it can assert, per target, said out loud rather than implied:
#   - everywhere: the expected copyright line is in the file that carries metadata, and no
#     unaccounted-for copyright line is;
#   - Windows only: CompanyName, LegalCopyright and ProductName in the Win32 version resource,
#     exactly. There is no such resource on Linux or macOS and nothing to read there instead.
#
# A .NET apphost - `keypaste-app` beside `keypaste-app.dll` - is a launcher, not the program. On
# Windows the SDK stamps the version resource into it and it is checked; on Linux and macOS it
# carries no metadata at all, and demanding some would be demanding a field that does not exist.
# The managed assembly beside it is the artifact that carries the identity. A NativeAOT binary has
# no such sidecar, which is exactly how release.yml already tells the two apart.
#
# Usage: verify-publisher-metadata.sh <staged-dir> [<staged-dir>...]
#
# NEGATIVE CONTROL: put a person's name in Directory.Build.props' Authors or Copyright, publish,
# and this must fail. The published v0.1.0 archives fail it too; that is the point.
set -euo pipefail

readonly PROJECT='keypaste'
readonly PROJECT_COPYRIGHT='Copyright (c) 2026 keypaste'
readonly UPSTREAM='Dominik Reichl'
readonly UPSTREAM_COPYRIGHT='Copyright (c) 2003-2021 Dominik Reichl'

die() { echo "::error::$*" >&2; exit 1; }

[ $# -ge 1 ] || die "usage: verify-publisher-metadata.sh <staged-dir> [<staged-dir>...]"

# A copyright notice reaches a binary as ASCII in the managed or ILC metadata and as UTF-16LE in a
# Windows version resource. Read both, rather than picking one and calling the other absent.
readable() { { LC_ALL=C cat "$1"; LC_ALL=C tr -d '\000' < "$1"; } | LC_ALL=C tr -c '[:print:]' '\n'; }

# Deliberately not `grep -q`: it exits at the first match, and the SIGPIPE that sends back up the
# pipeline is a failure under `pipefail`, so the answer would depend on how fast `cat` was.
holds() { readable "$1" | LC_ALL=C grep -cF -- "$2" > /dev/null; }

# Every copyright-shaped string must be one this repository can account for. Comparing the set
# rather than searching for a name is what lets the check reject an identity nobody predicted -
# and what keeps the name it is removing out of the repository.
only_expected_copyrights() {
  local file="$1" line
  while IFS= read -r line; do
    case "$line" in
      "$PROJECT_COPYRIGHT"*|"$UPSTREAM_COPYRIGHT"*|'Copyright (c) .NET Foundation'*|'Copyright (c) Microsoft'*) ;;
      *) die "$file carries an unexpected copyright: $line" ;;
    esac
  done < <(readable "$file" | LC_ALL=C grep -oE 'Copyright \(c\) [0-9]{4}[ -][^ ].{0,48}' | sort -u)
}

windows() { case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) return 0 ;; *) return 1 ;; esac; }

version_resource() {
  powershell -NoProfile -NonInteractive -Command \
    "\$ErrorActionPreference='Stop'; (Get-Item -LiteralPath '$1').VersionInfo.$2" | tr -d '\r'
}

check_windows_resource() {
  local file="$1" company="$2" copyright="$3" product="$4" actual
  windows || return 0
  command -v powershell >/dev/null 2>&1 || die "no powershell on a Windows runner; the resource would go unchecked"

  actual="$(version_resource "$file" CompanyName)"
  [ "$actual" = "$company" ] || die "$file reports CompanyName '$actual', expected '$company'"
  actual="$(version_resource "$file" LegalCopyright)"
  [ "$actual" = "$copyright" ] || die "$file reports LegalCopyright '$actual', expected '$copyright'"
  actual="$(version_resource "$file" ProductName)"
  [ "$actual" = "$product" ] || die "$file reports ProductName '$actual', expected '$product'"
}

# Counted rather than echoed: `die` inside $( ) would only kill the subshell, and the script
# would carry on with an empty count and a confusing arithmetic error instead of the message.
checked=0

check_metadata() {
  local file="$1"
  holds "$file" "$PROJECT_COPYRIGHT" || die "$file does not carry '$PROJECT_COPYRIGHT'"
  only_expected_copyrights "$file"
  checked=$((checked + 1))
}

# One shipped program: either a NativeAOT binary, or an apphost plus the managed assembly it loads.
check_program() {
  local dir="$1" name="$2" host=""

  if   [ -f "$dir/$name.exe" ]; then host="$dir/$name.exe"
  elif [ -f "$dir/$name" ];     then host="$dir/$name"
  else return 0
  fi

  # The managed assembly beside an apphost is where the identity lives; a NativeAOT binary has no
  # such sidecar and carries it itself.
  if [ -f "$dir/$name.dll" ]; then
    check_metadata "$dir/$name.dll"
  else
    check_metadata "$host"
  fi

  case "$host" in *.exe) check_windows_resource "$host" "$PROJECT" "$PROJECT_COPYRIGHT" "$PROJECT" ;; esac
}

check_library() {
  local file="$1"
  [ -f "$file" ] || return 0
  check_metadata "$file"
  check_windows_resource "$file" "$PROJECT" "$PROJECT_COPYRIGHT" "$PROJECT"
}

check_vendored() {
  local file="$1"
  [ -f "$file" ] || return 0
  holds "$file" "$UPSTREAM_COPYRIGHT" || die "$file does not carry upstream's '$UPSTREAM_COPYRIGHT'"
  if holds "$file" "$PROJECT_COPYRIGHT"; then die "$file claims keypaste's copyright over vendored code"; fi
  only_expected_copyrights "$file"
  check_windows_resource "$file" "$UPSTREAM" "$UPSTREAM_COPYRIGHT" 'KeePassLib'
  checked=$((checked + 1))
}

check_notices() {
  local dir="$1"
  [ -s "$dir/LICENSE" ] || die "$dir ships no LICENSE"
  [ -s "$dir/THIRD_PARTY_NOTICES.md" ] || die "$dir ships no THIRD_PARTY_NOTICES.md"
  grep -qF 'KeePassLib' "$dir/THIRD_PARTY_NOTICES.md" || die "$dir's notices do not name KeePassLib"
  grep -qF "$UPSTREAM" "$dir/THIRD_PARTY_NOTICES.md" || die "$dir's notices do not name upstream's holder"
}

for dir in "$@"; do
  [ -d "$dir" ] || die "not a directory: $dir"

  checked=0
  for name in keypaste keypaste-mcp keypaste-app; do
    check_program "$dir" "$name"
  done
  check_library "$dir/Keypaste.Core.dll"
  check_vendored "$dir/KeePassLib.dll"

  # Zero would mean the staging directory moved and every assertion above was vacuously satisfied.
  [ "$checked" -gt 0 ] || die "$dir holds none of the binaries this gate exists to check"

  check_notices "$dir"
  echo "$dir: $checked binaries publish as $PROJECT, with upstream's notice intact."
done
