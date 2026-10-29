#!/usr/bin/env bash
# Builds the libsodium Keypaste.Browser seals and opens NaCl boxes with (D-0611, D-0612) from upstream's release, kept only
# when the archive matches its SHA-256 pin and upstream's Minisign key signed it.
#
# Each target builds on its own runner with upstream's release settings, never for the build machine's own CPU: configure
# and make on Linux and macOS, and on Windows MSVC over every source with the settings of upstream's ReleaseLIB project, the
# C runtime linked in with /MT. The NativeAOT keypaste links the static archive (Keypaste.Cli.csproj); a build that runs in
# the .NET runtime, such as the tests', loads the shared library beside it. Both land in artifacts/native/<rid>/.
#
# The archive's path is the only line on stdout; build logs go to stderr.
#
# Usage: build-libsodium.sh [<rid>]   builds for this machine's RID, which <rid>, when given, must be
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly VERSION='1.0.22'
readonly SHA256='adbdd8f16149e81ac6078a03aca6fc03b592b89ef7b5ed83841c086191be3349'
readonly URL="https://github.com/jedisct1/libsodium/releases/download/$VERSION-RELEASE/libsodium-$VERSION.tar.gz"
# The key upstream signs its releases with, as its installation guide gives it.
readonly PUBLIC_KEY='RWQf6LRCGA9i53mlYecO4IzT51TGPpvWucNSCh1CBM0QTaLn73Y7GFO3'
# release-targets.json's floor for osx-arm64.
readonly MACOS_FLOOR='14.0'

die() { echo "::error::$*" >&2; exit 1; }

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

# An OpenSSL 3 or later, which knows Ed25519 and BLAKE2b: the runners' own on Linux and in Git Bash, Homebrew's on macOS,
# whose /usr/bin/openssl is LibreSSL.
openssl_3() {
  local candidate
  for candidate in openssl /mingw64/bin/openssl /usr/bin/openssl /opt/homebrew/opt/openssl@3/bin/openssl /usr/local/opt/openssl@3/bin/openssl; do
    case "$("$candidate" version 2>/dev/null)" in "OpenSSL "[3-9]*) echo "$candidate"; return ;; esac
  done
  die 'no OpenSSL 3 to check the release signature with; on macOS, brew install openssl@3'
}

ed25519_verifies() {
  "$1" pkeyutl -verify -pubin -keyform DER -inkey "$2" -rawin -in "$3" -sigfile "$4" >/dev/null 2>&1
}

# Minisign's prehashed form: an Ed25519 signature over the archive's BLAKE2b-512, and a second over that signature followed
# by the trusted comment.
verify_signature() {
  local archive="$1" minisig="$2" dir="$3" openssl comment
  openssl="$(openssl_3)"
  printf '%s' "$PUBLIC_KEY" | "$openssl" base64 -d -A > "$dir/key"
  sed -n 2p "$minisig" | tr -d '\r\n' | "$openssl" base64 -d -A > "$dir/signature"
  sed -n 4p "$minisig" | tr -d '\r\n' | "$openssl" base64 -d -A > "$dir/global"
  comment="$(sed -n 3p "$minisig" | tr -d '\r')"
  case "$comment" in "trusted comment: "*) comment="${comment#trusted comment: }" ;; *) die "$URL.minisig has no trusted comment" ;; esac
  [ "$(head -c 2 "$dir/key")" = Ed ] && [ "$(wc -c < "$dir/key" | tr -d ' ')" = 42 ] \
    || die 'the pinned key is not a Minisign Ed25519 key'
  [ "$(head -c 2 "$dir/signature")" = ED ] && [ "$(wc -c < "$dir/signature" | tr -d ' ')" = 74 ] \
    || die "$URL.minisig is not a prehashed Minisign signature"
  [ "$(head -c 10 "$dir/key" | tail -c 8 | od -An -tx1)" = "$(head -c 10 "$dir/signature" | tail -c 8 | od -An -tx1)" ] \
    || die "$URL.minisig names another key than upstream's"
  # The DER prefix of an Ed25519 SubjectPublicKeyInfo, then the key.
  { printf '\060\052\060\005\006\003\053\145\160\003\041\000'; tail -c 32 "$dir/key"; } > "$dir/key.der"
  tail -c 64 "$dir/signature" > "$dir/signature.raw"
  "$openssl" dgst -blake2b512 -binary "$archive" > "$dir/hash"
  ed25519_verifies "$openssl" "$dir/key.der" "$dir/hash" "$dir/signature.raw" \
    || die "libsodium-$VERSION.tar.gz's signature does not verify under upstream's key"
  { cat "$dir/signature.raw"; printf '%s' "$comment"; } > "$dir/commented"
  ed25519_verifies "$openssl" "$dir/key.der" "$dir/commented" "$dir/global" \
    || die "$URL.minisig's trusted comment does not verify under upstream's key"
  # A check that let a changed hash through would let anything through.
  { cat "$dir/hash"; printf '\000'; } > "$dir/changed"
  if ed25519_verifies "$openssl" "$dir/key.der" "$dir/changed" "$dir/signature.raw"; then
    die 'the signature check accepted a changed hash'
  fi
  echo "libsodium-$VERSION.tar.gz: signed by upstream's Minisign key" >&2
}

build_unix() {
  local src="$1" out="$2" jobs
  jobs="$(getconf _NPROCESSORS_ONLN 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 2)"
  (
    cd "$src"
    if [ "${rid%%-*}" = osx ]; then export MACOSX_DEPLOYMENT_TARGET="$MACOS_FLOOR"; fi
    # Upstream's defaults: configure optimizes for the native CPU only when asked to with --enable-opt.
    ./configure --disable-dependency-tracking
    make -j"$jobs"
  ) >&2
  cp "$src/src/libsodium/.libs/libsodium.a" "$out/libsodium.a"
  case "$rid" in
    osx-*) cp -L "$src/src/libsodium/.libs/libsodium.dylib" "$out/libsodium.dylib" ;;
    *) cp -L "$src/src/libsodium/.libs/libsodium.so" "$out/libsodium.so" ;;
  esac
}

# Upstream's ReleaseLIB settings, from builds/msvc/properties and vs2022/libsodium/libsodium.props, without the program
# database; the DLL is linked from the same objects and exports only what Keypaste.Browser calls.
build_windows() {
  local src="$1" out="$2" vswhere vcvars bat
  vswhere='/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
  [ -x "$vswhere" ] || die "no vswhere.exe at $vswhere: the Windows build needs Visual Studio's C compiler"
  vcvars="$("$vswhere" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 \
    -find 'VC\Auxiliary\Build\vcvars64.bat' | tr -d '\r' | head -n 1)"
  [ -n "$vcvars" ] || die "Visual Studio has no x64 C compiler"
  cp "$src/builds/msvc/version.h" "$src/src/libsodium/include/sodium/version.h"
  mkdir -p "$src/obj"
  (
    cd "$src"
    printf '%s\r\n' /nologo /c /MP /O2 /Oi /Ob2 /Oy- /Gy /MT /W3 /wd4146 /wd4244 /UndefIntOverflow- \
      '/Isrc\libsodium\include' '/Isrc\libsodium\include\sodium' "/Foobj\\" \
      /DSODIUM_STATIC /Dinline=__inline /DNATIVE_LITTLE_ENDIAN /D_CRT_SECURE_NO_WARNINGS /DNDEBUG /D_LIB \
      /DUNICODE /D_UNICODE /DWIN32 /D_WIN32 /DWIN64 /D_WIN64 > compile.rsp
    find src/libsodium -name '*.c' | sort | sed 's:/:\\:g; s/$/\r/' >> compile.rsp
    find src/libsodium -name '*.c' | sort | sed 's:.*/::; s:\.c$:.obj:; s:^:obj\\:; s/$/\r/' > objects.rsp
    printf '%s\r\n' 'LIBRARY libsodium' EXPORTS '  sodium_init' '  crypto_box_keypair' '  crypto_box_easy' '  crypto_box_open_easy' > libsodium.def
  )
  bat="$src/build.bat"
  printf '%s\r\n' '@echo off' \
    "call \"$vcvars\" >nul || exit /b 1" \
    "cd /d \"$(cygpath -w "$src")\" || exit /b 1" \
    'cl @compile.rsp || exit /b 1' \
    'lib /nologo /OUT:libsodium.lib @objects.rsp || exit /b 1' \
    'link /nologo /DLL /OUT:libsodium.dll /IMPLIB:obj\libsodium-dll.lib /DEF:libsodium.def libsodium.lib advapi32.lib || exit /b 1' > "$bat"
  MSYS_NO_PATHCONV=1 cmd.exe /d /c "$(cygpath -w "$bat")" >&2 || die "MSVC did not build libsodium"
  cp "$src/libsodium.lib" "$src/libsodium.dll" "$out/"
}

[ $# -le 1 ] || die 'usage: build-libsodium.sh [<rid>]'

rid="$(host_rid)"
[ -z "${1:-}" ] || [ "$1" = "$rid" ] || die "this builds for this machine's $rid, not $1"
case "$rid" in
  win-*) archive=libsodium.lib shared=libsodium.dll ;;
  osx-*) archive=libsodium.a shared=libsodium.dylib ;;
  *) archive=libsodium.a shared=libsodium.so ;;
esac

dest="$ROOT/artifacts/native/$rid"
stamp="$VERSION $SHA256 $(sha256_of "$ROOT/scripts/build-libsodium.sh")"
if [ -f "$dest/$archive" ] && [ -f "$dest/$shared" ] && [ "$(cat "$dest/libsodium.stamp" 2>/dev/null)" = "$stamp" ]; then
  echo "$dest/$archive"
  exit 0
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
"$ROOT/scripts/fetch-pinned-asset.sh" "$URL" "$SHA256" "$work/libsodium.tar.gz" >&2
curl -fsSL --retry 3 -o "$work/libsodium.tar.gz.minisig" "$URL.minisig" || die "could not download $URL.minisig"
mkdir "$work/signature" "$work/out"
verify_signature "$work/libsodium.tar.gz" "$work/libsodium.tar.gz.minisig" "$work/signature"
tar -xzf "$work/libsodium.tar.gz" -C "$work"
src="$work/libsodium-$VERSION"
[ -f "$src/src/libsodium/include/sodium.h" ] || die "the pinned archive holds no libsodium-$VERSION/src/libsodium/include/sodium.h"

case "$rid" in
  win-*) build_windows "$src" "$work/out" ;;
  *) build_unix "$src" "$work/out" ;;
esac

mkdir -p "$dest"
rm -f "$dest/libsodium.stamp"
cp "$work/out/$archive" "$work/out/$shared" "$dest/"
echo "$stamp" > "$dest/libsodium.stamp"
echo "$rid: libsodium $VERSION built from its signed release" >&2
echo "$dest/$archive"
