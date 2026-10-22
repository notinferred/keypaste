#!/usr/bin/env bash
# Builds the libargon2 the app and the CLI carry beside their binaries on Windows and macOS, from the reference
# implementation's release archive pinned below, and keeps it only once upstream's own known answers pass (D-0522).
#
# Linux keeps the system's libargon2, which KeePassLib finds by its sonames, so nothing is built there. x64 compiles
# upstream's SSE2 code for baseline x86-64 and arm64 its portable reference code, never for the build machine's own CPU,
# so the file runs on every machine the release supports. Windows links the C runtime in with MSVC's /MT, so it needs
# no Visual C++ redistributable. Keypaste.Core's project copies artifacts/native/<rid>/ into every build and publish of
# the projects that reference it; a build without the file derives the same keys in managed code.
#
# The path of the library is the only line on stdout, and nothing where none is carried; build logs go to stderr.
#
# Usage: build-argon2.sh [<rid>]        builds for this machine's RID, which <rid>, when given, must be
#        build-argon2.sh --name <rid>   prints the file <rid> carries, or nothing
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly VERSION='20190702'
# GitHub's archive of the release tag; the pin is what keeps the tag and the archive from changing under it.
readonly SHA256='daf972a89577f8772602bf2eb38b6a3dd3d922bf5724d45e7f9589b5e830442c'
readonly URL="https://github.com/P-H-C/phc-winner-argon2/archive/refs/tags/$VERSION.tar.gz"
# Upstream's Makefile's library sources, beside opt.c or ref.c.
readonly SOURCES='src/argon2.c src/core.c src/blake2/blake2b.c src/thread.c src/encoding.c'
# release-targets.json's floor for osx-arm64.
readonly MACOS_FLOOR='14.0'

die() { echo "::error::$*" >&2; exit 1; }

file_for() {
  case "$1" in
    win-x64) echo argon2.dll ;;
    osx-arm64 | osx-x64) echo libargon2.1.dylib ;;
    linux-*) ;;
    *) die "keypaste carries no libargon2 for $1" ;;
  esac
}

host_rid() {
  local os arch
  case "$(uname -s)" in
    Darwin) os=osx ;;
    Linux) os=linux ;;
    MINGW* | MSYS* | CYGWIN*) os=win ;;
    *) die "no RID for $(uname -s)" ;;
  esac
  case "$(uname -m)" in
    x86_64 | amd64) arch=x64 ;;
    arm64 | aarch64) arch=arm64 ;;
    *) die "no RID for $(uname -m)" ;;
  esac
  echo "$os-$arch"
}

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum < "$1" | cut -d' ' -f1
  else shasum -a 256 < "$1" | cut -d' ' -f1
  fi
}

# Upstream's Makefile flags, less -g and -march=native; the library exports only argon2.h's API, as its Linux build does.
build_macos() {
  local src="$1" name="$2" impl
  local flags=(-std=c89 -O3 -Wall -Iinclude -Isrc -pthread "-mmacosx-version-min=$MACOS_FLOOR")
  case "$rid" in
    osx-arm64) flags+=(-arch arm64); impl=src/ref.c ;;
    *) flags+=(-arch x86_64 -march=x86-64); impl=src/opt.c ;;
  esac
  # shellcheck disable=SC2086 # SOURCES is a list of paths without spaces
  (
    cd "$src"
    cc "${flags[@]}" -fvisibility=hidden -DA2_VISCTL=1 -dynamiclib -install_name "@rpath/$name" -o "$name" $SOURCES "$impl"
    cc "${flags[@]}" -o argon2-test src/test.c $SOURCES "$impl"
    cc "${flags[@]}" -DGENKAT -o genkat src/genkat.c $SOURCES "$impl"
  ) >&2
}

# Upstream's Visual Studio release settings, with the C runtime linked in.
build_windows() {
  local src="$1" name="$2" vswhere vcvars bat sources cl
  vswhere='/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
  [ -x "$vswhere" ] || die "no vswhere.exe at $vswhere: the Windows build needs Visual Studio's C compiler"
  vcvars="$("$vswhere" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 \
    -find 'VC\Auxiliary\Build\vcvars64.bat' | tr -d '\r' | head -n 1)"
  [ -n "$vcvars" ] || die "Visual Studio has no x64 C compiler"
  sources="${SOURCES//\//\\} src\\opt.c"
  cl='cl /nologo /O2 /W3 /sdl /MT /Iinclude /Isrc /D_CRT_SECURE_NO_WARNINGS'
  bat="$src/build.bat"
  printf '%s\r\n' '@echo off' \
    "call \"$vcvars\" >nul || exit /b 1" \
    "cd /d \"$(cygpath -w "$src")\" || exit /b 1" \
    "$cl /LD $sources /Fe$name || exit /b 1" \
    "$cl src\\test.c $sources /Feargon2-test.exe || exit /b 1" \
    "$cl /DGENKAT src\\genkat.c $sources /Fegenkat.exe || exit /b 1" > "$bat"
  MSYS_NO_PATHCONV=1 cmd.exe /d /c "$(cygpath -w "$bat")" >&2 || die "MSVC did not build libargon2"
}

# Upstream's test.c asserts argon2_hash's answers for Argon2i and Argon2id, and genkat must print RFC 9106's vectors.
check_known_answers() {
  local src="$1" type
  (cd "$src" && ./argon2-test) >&2 || die "upstream's test.c failed against this build"
  for type in d id; do
    (cd "$src" && ./genkat "$type" 19) | tr -d '\r' > "$src/kat-$type" || die "genkat did not run for Argon2$type"
    cmp -s "$src/kat-$type" "$src/kats/argon2$type" || die "this build's Argon2$type does not give RFC 9106's known answer"
  done
}

if [ "${1:-}" = --name ]; then
  [ $# -eq 2 ] || die 'usage: build-argon2.sh --name <rid>'
  file_for "$2"
  exit 0
fi
[ $# -le 1 ] || die 'usage: build-argon2.sh [<rid>] | --name <rid>'

rid="$(host_rid)"
[ -z "${1:-}" ] || [ "$1" = "$rid" ] || die "this builds for this machine's $rid, not $1"
name="$(file_for "$rid")"
if [ -z "$name" ]; then
  echo "$rid: KeePassLib finds the system's libargon2, so keypaste carries none" >&2
  exit 0
fi

dest="$ROOT/artifacts/native/$rid"
stamp="$VERSION $SHA256 $(sha256_of "$ROOT/scripts/build-argon2.sh")"
if [ -f "$dest/$name" ] && [ "$(cat "$dest/$name.stamp" 2>/dev/null)" = "$stamp" ]; then
  echo "$dest/$name"
  exit 0
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
"$ROOT/scripts/fetch-pinned-asset.sh" "$URL" "$SHA256" "$work/argon2.tar.gz" >&2
tar -xzf "$work/argon2.tar.gz" -C "$work"
src="$work/phc-winner-argon2-$VERSION"
[ -f "$src/include/argon2.h" ] || die "the pinned archive holds no phc-winner-argon2-$VERSION/include/argon2.h"

case "$rid" in
  win-*) build_windows "$src" "$name" ;;
  *) build_macos "$src" "$name" ;;
esac
check_known_answers "$src"

mkdir -p "$dest"
rm -f "$dest/$name.stamp"
cp "$src/$name" "$dest/$name"
echo "$stamp" > "$dest/$name.stamp"
echo "$rid: libargon2 $VERSION built and checked against upstream's and RFC 9106's known answers" >&2
echo "$dest/$name"
