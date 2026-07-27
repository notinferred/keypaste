#!/usr/bin/env bash
#
# build-demo-binaries.sh
#
# Builds the Linux keypaste binaries the recording runs against, from a clone at HEAD.
#
# From a CLONE, and never from the working tree, for three reasons. The recording is a documentary
# of a commit, so it should be traceable to one - the SHA is written to BUILT_FROM beside the
# binaries. A working tree may be dirty, and "the GIF was recorded against something not in the
# repository" is exactly the claim a demo must not invite. And building on /mnt/c means building
# over a 9p mount, which is slow and would interleave artifacts/obj with the Windows build.
#
# Usage:  scripts/demo/build-demo-binaries.sh
set -euo pipefail

cd "$(dirname "$0")/../.."
REPO="$PWD"
# shellcheck source=scripts/demo/demo-env.sh
. scripts/demo/demo-env.sh

refuse_outside_wsl
refuse_inside_repo "$DEMO_ROOT"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
command -v dotnet >/dev/null 2>&1 || die "no dotnet on PATH (run scripts/demo/install-recording-tools.sh)"

mkdir -p "$DEMO_ROOT"

# ------------------------------------------------------------------------------------- the clone
say "==> cloning at HEAD"
if [ -d "$DEMO_SRC/.git" ]; then
  git -C "$DEMO_SRC" fetch --quiet origin || git -C "$DEMO_SRC" fetch --quiet "$REPO"
else
  # --no-hardlinks because --local's default is to hardlink the object store, and the repository
  # usually lives on a Windows drive while the clone lands on ext4. Hardlinks cannot cross that
  # boundary, and git fails with "Invalid cross-device link" rather than falling back.
  git clone --quiet --local --no-hardlinks "$REPO" "$DEMO_SRC"
fi

sha="$(git -C "$REPO" rev-parse HEAD)"
git -C "$DEMO_SRC" checkout --quiet --detach "$sha" 2>/dev/null \
  || die "could not check $sha out in the clone; commit your work first"

dirty=""
git -C "$REPO" diff --quiet || dirty=" (the working tree had uncommitted changes, which are NOT in this build)"
say "    $sha$dirty"

# ---------------------------------------------------------------------------------- the publish
# Framework-dependent, and deliberately no -r. Adding a RID changes the restore graph, and with
# RestorePackagesWithLockFile=true that either rewrites packages.lock.json or fails --locked-mode
# outright. On Linux the SDK still emits a native apphost named `keypaste`, which is all the
# recording needs.
say "==> publishing"
dotnet restore "$DEMO_SRC/keypaste.slnx" --locked-mode >/dev/null \
  || die "restore failed. If it complains about the SDK, install the version global.json pins."

rm -rf "$DEMO_BIN"
mkdir -p "$DEMO_BIN"
dotnet publish "$DEMO_SRC/src/Keypaste.Cli" -c Release --no-restore -o "$DEMO_BIN" >/dev/null \
  || die "could not publish the CLI"
dotnet publish "$DEMO_SRC/src/Keypaste.Mcp" -c Release --no-restore -o "$DEMO_BIN" >/dev/null \
  || die "could not publish the bridge"

chmod +x "$DEMO_BIN/keypaste" "$DEMO_BIN/keypaste-mcp"
printf '%s\n' "$sha" > "$DEMO_ROOT/BUILT_FROM"

# ------------------------------------------------------------------------------------ smoke test
# Discovering either of these inside the tmux session costs a take.
say "==> smoke test"
"$DEMO_BIN/keypaste" version >/dev/null 2>&1 || die "the CLI does not run"

probe="$(mktemp -d)"
{
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"probe","version":"1.0"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
  # Hold stdin open. A stdio server's stdin closing is its shutdown signal, so without this the
  # probe races the answer and reads nothing - which looks exactly like a broken binary.
  sleep 3
} | "$DEMO_BIN/keypaste-mcp" --audit-log "$probe/a.jsonl" 2>/dev/null \
  | grep -q request_credential \
  || { rm -rf "$probe"; die "the bridge did not answer tools/list"; }
rm -rf "$probe"

say ""
say "    $DEMO_BIN"
say "    built from $sha"
say "    next: scripts/demo/make-demo-fixture.sh"
