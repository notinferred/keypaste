#!/usr/bin/env bash
# Runs the install instructions a stranger reads, on the operating system they read them for.
#
# The commands are not copied into this script. They are extracted from the documentation itself,
# between sentinel comments, and executed verbatim - so what is tested is the text on the page and
# not a second copy of it that can drift. Same principle as verify-demo.sh holding the documented
# transcripts to what the binaries actually print, applied to the one instruction a stranger runs
# before anything else. D-0036: a page may claim only what a gate or a citation can hold.
#
# Usage:
#   verify-install.sh <linux|macos|windows>
#   verify-install.sh <os> --negative     corrupt the download and require the block to reject it
#
# Environment:
#   KEYPASTE_INSTALL_DOC   document to read the block out of  (default: README.md)
#   KEYPASTE_EXPECT_VERSION  version the installed binary must print (default: from the csproj)
#
# NEGATIVE CONTROL: --negative runs the block twice - once untouched, which must succeed, and once
# with the downloaded archive corrupted, which must fail. Both halves are required. A corrupted run
# that fails on its own proves nothing, because a broken URL or a missing tool fails too; only the
# pair isolates the corruption as the single difference between them. This was not a hypothetical:
# the first version of this script asserted the failing half alone and passed while curl was
# failing for an unrelated reason.
set -euo pipefail

readonly DOC="${KEYPASTE_INSTALL_DOC:-README.md}"

die() { echo "::error::$*" >&2; exit 1; }

OS="${1:-}"
shift || true
NEGATIVE=""
RETARGET=""
while [ $# -gt 0 ]; do
  case "$1" in
    --negative) NEGATIVE="--negative"; shift ;;
    --version)  RETARGET="${2:-}"; [ -n "$RETARGET" ] || die "--version needs a value"; shift 2 ;;
    *) die "unknown argument: $1" ;;
  esac
done

# linux-arm64 is not a fourth block. README publishes ONE Linux block, naming linux-x64, and tells
# an arm64 reader to substitute the filename - so the arm64 instruction IS that block plus that
# substitution, and testing anything else would be testing a page nobody reads. BLOCK_OS is which
# block to extract; OS stays what is being checked.
BLOCK_OS="$OS"
SUBSTITUTE_RID=""
case "$OS" in
  linux|macos|windows) ;;
  linux-arm64) BLOCK_OS="linux"; SUBSTITUTE_RID="linux-arm64" ;;
  *) die "usage: verify-install.sh <linux|linux-arm64|macos|windows> [--negative] [--version <v>]" ;;
esac
[ -f "$DOC" ] || die "no such document: $DOC"

# ---------------------------------------------------------------------------
# Extract the block. Sentinels rather than "the first fence after the heading", because a heading
# can be renamed and a fence can be inserted above, and both would silently change what is tested.
# ---------------------------------------------------------------------------
open="<!-- install:$BLOCK_OS -->"
close="<!-- /install:$BLOCK_OS -->"

grep -qF "$open"  "$DOC" || die "$DOC has no '$open' marker - the install block for $BLOCK_OS is not gated"
grep -qF "$close" "$DOC" || die "$DOC has no '$close' marker"
[ "$(grep -cF "$open" "$DOC")" -eq 1 ] || die "$DOC has more than one '$open' marker"

BLOCK="$(mktemp)"
trap 'rm -f "$BLOCK"' EXIT

# Everything strictly between the markers, minus the code-fence lines themselves.
awk -v o="$open" -v c="$close" '
  index($0, o) { inblock = 1; next }
  index($0, c) { inblock = 0; next }
  inblock && $0 ~ /^```/ { next }
  inblock { print }
' "$DOC" > "$BLOCK"

grep -q '[^[:space:]]' "$BLOCK" || die "the $BLOCK_OS install block in $DOC is empty"

EXPECT="${KEYPASTE_EXPECT_VERSION:-}"
if [ -z "$EXPECT" ]; then
  EXPECT="$(dotnet msbuild src/Keypaste.Cli/Keypaste.Cli.csproj -getProperty:VersionPrefix -nologo 2>/dev/null | tr -d '[:space:]')"
fi
[ -n "$EXPECT" ] || die "could not determine the expected version; set KEYPASTE_EXPECT_VERSION"

# Both substitutions below change the text a stranger reads, which is this script's whole subject,
# so each is announced rather than applied quietly.
#
#   --version   retargets an UNADVERTISED candidate. README correctly names the advertised release;
#               a candidate has no page of its own by design, so the only way to exercise its assets
#               through the documented shape is to rewrite the version in the block. That is a
#               narrower claim than a verbatim run and the output says so.
#   arm64       exactly the substitution README instructs, and nothing else.
if [ -n "$RETARGET" ] && [ "$RETARGET" != "$EXPECT" ]; then
  die "--version $RETARGET disagrees with KEYPASTE_EXPECT_VERSION $EXPECT; the binary would be checked against the wrong number"
fi
if [ -n "$RETARGET" ]; then
  README_VERSION="$(grep -oE 'keypaste-[0-9][0-9A-Za-z.-]*-(linux|osx|win)-[a-z0-9]+\.(tar\.gz|zip)' "$BLOCK" \
    | head -1 | sed -E 's/^keypaste-(.*)-(linux|osx|win)-[a-z0-9]+\.(tar\.gz|zip)$/\1/')"
  [ -n "$README_VERSION" ] || die "could not read a version out of the $BLOCK_OS block to retarget it"
  if [ "$README_VERSION" != "$RETARGET" ]; then
    sed -i "s/$README_VERSION/$RETARGET/g" "$BLOCK"
    echo "!!! CANDIDATE RUN: $DOC's block, with $README_VERSION rewritten to $RETARGET."
    echo "!!! This tests the documented install SHAPE against an unadvertised release's assets."
    echo "!!! It is not evidence that $DOC is correct - verify-release-matrix.sh owns that."
  fi
fi
if [ -n "$SUBSTITUTE_RID" ]; then
  sed -i "s/linux-x64/$SUBSTITUTE_RID/g" "$BLOCK"
  echo "!!! ARM64 RUN: linux-x64 rewritten to $SUBSTITUTE_RID, the substitution $DOC instructs an"
  echo "!!! arm64 reader to make. The block is otherwise untouched."
fi

echo "--- the $BLOCK_OS block as it will be run ---"
cat "$BLOCK"
echo "---"

# A block that never verifies what it downloaded would pass the positive run happily. Require the
# step to be present before trusting either run, so a deletion is caught even if --negative is
# somehow not scheduled.
grep -qiE 'sha256|Get-FileHash' "$BLOCK" \
  || die "the $OS install block has no checksum step, which is not an install instruction this project publishes"

# ---------------------------------------------------------------------------
# Run it somewhere that has never seen keypaste. A scratch HOME matters as much as a scratch
# directory: the point is to reproduce a stranger's machine, and a stranger has no ~/.keypaste.
# ---------------------------------------------------------------------------
SCRATCH="$(mktemp -d)"
cleanup() { rm -f "$BLOCK"; chmod -R u+w "$SCRATCH" 2>/dev/null || true; rm -rf "$SCRATCH"; }
trap cleanup EXIT

# Runs the block in a fresh scratch machine. $1 is a directory prepended to PATH, used by the
# corrupting run to shadow curl; empty for the honest one.
run_block() {
  local shim="$1" work
  work="$(mktemp -d "$SCRATCH/work.XXXXXX")"
  mkdir -p "$SCRATCH/home"
  (
    cd "$work"
    export HOME="$SCRATCH/home"
    [ -n "$shim" ] && export PATH="$shim:$PATH"
    if [ "$OS" = "windows" ]; then
      pwsh -NoProfile -NonInteractive -Command "\$ErrorActionPreference='Stop'; $(cat "$BLOCK")"
    else
      bash -euo pipefail "$BLOCK"
    fi
  )
  local rc=$?
  printf '%s' "$work" > "$SCRATCH/last-work"
  return $rc
}

# Same, but keeps the combined output so the caller can ask *which step* failed. "It failed" is not
# good enough for the negative control: a corrupted archive also makes tar fail, so a block whose
# checksum step cannot fail still exits non-zero and would otherwise look like a pass.
run_block_capturing() {
  run_block "$1" > "$SCRATCH/neg-output" 2>&1
}

set +e
run_block ""
RC=$?
set -e
[ "$RC" -eq 0 ] || die "the $OS install block failed with exit $RC"
WORK="$(cat "$SCRATCH/last-work")"

# ---------------------------------------------------------------------------
# It claimed to install something. Check that from outside the block, not from inside it.
# ---------------------------------------------------------------------------
found=""
# $SCRATCH/home, not $HOME: the block ran with HOME reassigned, so that is where ~ resolved to.
for cand in "$WORK/keypaste" "$WORK/keypaste.exe" \
            /usr/local/bin/keypaste "$SCRATCH/home/.local/bin/keypaste"; do
  [ -x "$cand" ] && { found="$cand"; break; }
done
[ -n "$found" ] || found="$(command -v keypaste 2>/dev/null || true)"
[ -n "$found" ] || die "the $OS block ran cleanly but left no keypaste on PATH or in the working directory"

printed="$("$found" --version)"
[ "$printed" = "$EXPECT" ] \
  || die "installed binary reports '$printed', the documentation installs '$EXPECT'"

mcp="$(dirname "$found")/keypaste-mcp"
[ -x "$mcp" ] || [ -x "$mcp.exe" ] || mcp="$(command -v keypaste-mcp 2>/dev/null || true)"
[ -n "$mcp" ] || die "keypaste installed but keypaste-mcp did not; an MCP client needs both"

# ---------------------------------------------------------------------------
# It installed. Now make it do the two things the page says it is for, because "the binary starts"
# is not the claim on the front of this project - RELEASE requirement 4 asks for the component's
# advertised workflows, and a release that prints a version and cannot open a vault is not one.
#
# Everything here happens under the scratch HOME the block ran with, so a vault, a config and an
# audit log are all created where a stranger's would be and nowhere else.
# ---------------------------------------------------------------------------
exercise() {
  local bin="$1" home="$2" work pw='correct horse battery staple' out
  work="$(mktemp -d "$SCRATCH/use.XXXXXX")"

  export HOME="$home"
  export KEYPASTE_HOME="$work/kp"
  export KEYPASTE_VAULT="$work/vault.kdbx"

  # 1. A vault, made from nothing. This is the first thing a new user does and it exercises the
  #    KDBX writer, Argon2 and the file path handling in one step.
  printf '%s\n%s\n' "$pw" "$pw" | "$bin" init "$KEYPASTE_VAULT" >/dev/null 2>&1 \
    || die "$OS: the installed keypaste could not create a vault"
  [ -s "$KEYPASTE_VAULT" ] || die "$OS: init reported success and left no vault file"
  echo "  vault created: $(wc -c < "$KEYPASTE_VAULT") bytes"

  # 2. A value in, and out again through env injection - the workflow the README leads with. The
  #    child prints the variable it was given, so this fails if injection silently does nothing,
  #    which a check that only looked at keypaste's own exit code would not catch.
  printf '%s\n' "$pw" | "$bin" env set proj TOKEN=injected-ok >/dev/null 2>&1 \
    || die "$OS: could not set an environment variable in the new vault"

  # pwsh rather than cmd: MSYS rewrites a leading /flag into a Windows path, so `cmd /c` arrives at
  # the child as `cmd C:/...`, which starts an interactive shell that prints a banner and no value.
  if [ "$OS" = "windows" ]; then
    out="$(printf '%s\n' "$pw" | "$bin" run proj -- pwsh -NoProfile -NonInteractive -Command '[Console]::Out.Write($env:TOKEN)' 2>/dev/null | tr -d '\r')"
  else
    out="$(printf '%s\n' "$pw" | "$bin" run proj -- sh -c 'printf %s "$TOKEN"' 2>/dev/null)"
  fi
  case "$out" in
    *injected-ok*) echo "  run injection: the child saw the value" ;;
    *) die "$OS: 'keypaste run' did not put the value in the child's environment (child printed '$out')" ;;
  esac

  # 3. Nothing was written outside the scratch home. The page's promise is that injection touches
  #    no file; this is the cheap version of scripts/verify-run-injection.sh, run against the
  #    PUBLISHED binary rather than a build.
  [ ! -e "$work/.env" ] || die "$OS: run left a .env behind"
}

exercise "$found" "$SCRATCH/home"

echo "the $OS install block works: keypaste $printed and keypaste-mcp, from a clean machine,"
echo "and the installed binary creates a vault and injects into a child process."

[ "$NEGATIVE" = "--negative" ] || exit 0

# ---------------------------------------------------------------------------
# The honest run has just succeeded, so the URLs resolve and the tools exist. Run it again with a
# single byte of the downloaded archive flipped, changing nothing else. Now a failure can only come
# from the checksum step, and a success can only mean that step does not fail closed.
# ---------------------------------------------------------------------------
echo
echo "NEGATIVE CONTROL: same block, the archive swapped for a different but VALID one."

if [ "$OS" = "windows" ]; then
  die "the windows negative control is not implemented; it needs an Invoke-WebRequest shim"
fi

# Substituting a *valid* archive rather than corrupting bytes is the whole point, and getting this
# wrong is the easiest mistake here. A one-byte flip produces an invalid gzip, which tar rejects on
# its own - so a block whose checksum step cannot fail still exits non-zero and looks like a pass.
# It also is not the threat: an attacker who can replace the download substitutes something that
# extracts cleanly. Against a valid decoy, only the checksum can object, so the block either fails
# because verification worked or succeeds and installs the decoy. There is no third outcome.
DECOY="$SCRATCH/decoy"; mkdir -p "$DECOY/payload"
cat > "$DECOY/payload/keypaste" <<'DEC'
#!/usr/bin/env bash
echo "9.9.9-decoy"
DEC
cp "$DECOY/payload/keypaste" "$DECOY/payload/keypaste-mcp"
chmod +x "$DECOY/payload/keypaste" "$DECOY/payload/keypaste-mcp"
tar -czf "$DECOY/decoy.tar.gz" -C "$DECOY/payload" .

SHIM="$SCRATCH/shim"; mkdir -p "$SHIM"
REAL_CURL="$(command -v curl)"
cat > "$SHIM/curl" <<WRAP
#!/usr/bin/env bash
"$REAL_CURL" "\$@"
rc=\$?
shopt -s nullglob
for f in *.tar.gz; do
  cp "$DECOY/decoy.tar.gz" "\$f"
done
exit \$rc
WRAP
chmod +x "$SHIM/curl"

set +e
run_block_capturing "$SHIM"
NRC=$?
set -e
cat "$SCRATCH/neg-output"

if [ "$NRC" -eq 0 ]; then
  echo "::error::the $OS install block installed an archive that does not match its checksum" >&2
  echo "The decoy extracted cleanly and the block reported success, so the verification line is" >&2
  echo "decorative: it prints a complaint and carries on. A reader who deleted that line would be" >&2
  echo "no worse off, which is the opposite of what it is there for. Make it fail closed." >&2
  exit 1
fi

NWORK="$(cat "$SCRATCH/last-work")"
if [ -x "$NWORK/keypaste" ] && [ "$("$NWORK/keypaste" --version 2>/dev/null)" = "9.9.9-decoy" ]; then
  die "the $OS block exited non-zero but still left the decoy binary in place"
fi

echo "negative control passed: the checksum rejected a valid decoy and nothing was installed."
