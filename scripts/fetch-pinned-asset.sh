#!/usr/bin/env bash
# Downloads a build tool and keeps it only if its SHA-256 is the pin (D-0142, D-0203).
#
# The file is written to <out> only after its digest matches; a mismatch leaves nothing behind for a
# later step to pick up by name.
#
# Usage: fetch-pinned-asset.sh <url> <sha256> <out>
#        fetch-pinned-asset.sh --selftest
set -euo pipefail

die() { echo "::error::$*" >&2; exit 1; }

# Read on stdin, so the digest is never prefixed: GNU coreutils escapes a name holding a backslash
# by putting one in front of the line, and a Windows destination path is full of them.
sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum < "$1" | cut -d' ' -f1
  else shasum -a 256 < "$1" | cut -d' ' -f1
  fi
}

fetch() {
  local url="$1" want="$2" out="$3" partial observed
  case "$want" in
    *[!0-9a-f]*) die "pin '$want' for $url is not a lowercase SHA-256" ;;
  esac
  [ "${#want}" -eq 64 ] || die "pin '$want' for $url is not a lowercase SHA-256"

  partial="$out.partial"
  rm -f "$partial" "$out"
  curl -fsSL --retry 3 -o "$partial" "$url" || { rm -f "$partial"; die "could not download $url"; }
  observed="$(sha256_of "$partial")"
  if [ "$observed" != "$want" ]; then
    rm -f "$partial"
    die "$url has SHA-256 $observed; the pin is $want"
  fi
  mv "$partial" "$out"
  echo "$(basename "$out"): $observed matches its pin"
}

selftest() {
  local url pin
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' EXIT
  printf 'pinned tool bytes\n' > "$work/tool"
  pin="$(sha256_of "$work/tool")"
  url="file://$work/tool"
  if command -v cygpath >/dev/null 2>&1; then url="file:///$(cygpath -m "$work/tool")"; fi

  ( fetch "$url" "$pin" "$work/accepted" ) >/dev/null || die "selftest: the pinned bytes were refused"
  cmp -s "$work/tool" "$work/accepted" || die "selftest: the accepted file is not the pinned bytes"

  printf 'pinned tool byteS\n' > "$work/tool"
  if ( fetch "$url" "$pin" "$work/changed" ) 2>/dev/null; then die "selftest: a changed byte was accepted"; fi
  [ ! -e "$work/changed" ] && [ ! -e "$work/changed.partial" ] || die "selftest: a refused download was left on disk"

  if ( fetch "$url" "${pin^^}" "$work/malformed" ) 2>/dev/null; then die "selftest: a malformed pin was accepted"; fi

  # A destination whose path holds backslashes, which is every Windows destination. Reading the
  # digest from the named file made coreutils escape it, and the pinned bytes were refused
  # (upgrade-desktop run 35260785520).
  printf 'pinned tool bytes\n' > "$work/tool"
  local backslashed
  if command -v cygpath >/dev/null 2>&1; then backslashed="$(cygpath -w "$work")\\accepted-windows"
  else backslashed="$work/back\\slashed"
  fi
  ( fetch "$url" "$pin" "$backslashed" ) >/dev/null || die "selftest: a destination path holding a backslash refused the pinned bytes"
  cmp -s "$work/tool" "$backslashed" || die "selftest: the file written to a backslashed path is not the pinned bytes"

  echo "fetch-pinned-asset.sh: pinned bytes accepted, including to a backslashed path; a changed byte and a malformed pin refused, leaving nothing"
}

case "${1:-}" in
  --selftest) [ $# -eq 1 ] || die "usage: fetch-pinned-asset.sh --selftest"; selftest ;;
  *) [ $# -eq 3 ] || die "usage: fetch-pinned-asset.sh <url> <sha256> <out>"; fetch "$1" "$2" "$3" ;;
esac
