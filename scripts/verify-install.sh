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
NEGATIVE="${2:-}"
case "$OS" in
  linux|macos|windows) ;;
  *) die "usage: verify-install.sh <linux|macos|windows> [--negative]" ;;
esac
[ -f "$DOC" ] || die "no such document: $DOC"

# ---------------------------------------------------------------------------
# Extract the block. Sentinels rather than "the first fence after the heading", because a heading
# can be renamed and a fence can be inserted above, and both would silently change what is tested.
# ---------------------------------------------------------------------------
open="<!-- install:$OS -->"
close="<!-- /install:$OS -->"

grep -qF "$open"  "$DOC" || die "$DOC has no '$open' marker - the install block for $OS is not gated"
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

grep -q '[^[:space:]]' "$BLOCK" || die "the $OS install block in $DOC is empty"

echo "--- the $OS block, exactly as $DOC prints it ---"
cat "$BLOCK"
echo "---"

# A block that never verifies what it downloaded would pass the positive run happily. Require the
# step to be present before trusting either run, so a deletion is caught even if --negative is
# somehow not scheduled.
grep -qiE 'sha256|Get-FileHash' "$BLOCK" \
  || die "the $OS install block has no checksum step, which is not an install instruction this project publishes"

EXPECT="${KEYPASTE_EXPECT_VERSION:-}"
if [ -z "$EXPECT" ]; then
  EXPECT="$(dotnet msbuild src/Keypaste.Cli/Keypaste.Cli.csproj -getProperty:VersionPrefix -nologo 2>/dev/null | tr -d '[:space:]')"
fi
[ -n "$EXPECT" ] || die "could not determine the expected version; set KEYPASTE_EXPECT_VERSION"

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
for cand in "$WORK/keypaste" "$WORK/keypaste.exe" \
            /usr/local/bin/keypaste "$HOME/.local/bin/keypaste"; do
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

echo "the $OS install block works: keypaste $printed and keypaste-mcp, from a clean machine."

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
