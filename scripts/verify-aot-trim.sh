#!/usr/bin/env bash
# Holds the AOT publish to a known set of trim diagnostics.
#
# third_party/Directory.Build.props turns the trim and AOT analyzers off for vendored source. That
# is deliberate (D-0005 installed them to choose dependencies, not to lint 2007-era code), but it
# also disarms the gate for the one assembly most likely to break under NativeAOT - which is
# exactly what O-0006 complained about. This script re-arms it somewhere else: the publish reports
# its diagnostics, and they are diffed character for character against a committed baseline.
#
# The effect is that an upstream re-merge which introduces a new AOT-hostile construct turns the
# build red with a diff, instead of disappearing into a quarantine. Same idea as verify-demo.sh
# holding the documented transcripts to what the binaries actually print.
#
# Usage: verify-aot-trim.sh <publish-log> [<publish-log>...]
#
# NEGATIVE CONTROL: delete a line from the baseline, or add an AOT-hostile call to src/, and this
# must fail. A gate never observed failing is not known to be a gate.
set -euo pipefail

readonly BASELINE='scripts/aot-trim-baseline.txt'

die() { echo "::error::$*" >&2; exit 1; }

[ $# -ge 1 ] || die "usage: verify-aot-trim.sh <publish-log> [<publish-log>...]"
[ -f "$BASELINE" ] || die "missing baseline: $BASELINE"

for log in "$@"; do
  [ -f "$log" ] || die "publish log not found: $log"
done

# Normalise away everything that legitimately varies between machines and runs: the absolute
# checkout path, the path separator, the trailing "[project]" attribution MSBuild appends, and
# ordering (ILC does not promise a stable emission order across parallel compilation).
normalise() {
  # `|| true` on the grep, because no matches is a legitimate outcome to report rather than an error
  # to die on: under `set -euo pipefail` an empty grep took the whole script down with exit 1 and no
  # message at all, which is a worse failure than the one it was guarding. The empty case is checked
  # explicitly below, where it can be explained.
  { cat "$@" \
    | grep -E 'Trim analysis (error|warning) IL[0-9]+|AOT analysis (error|warning) IL[0-9]+' \
    || true; } \
    | sed -E 's/\r$//' \
    | sed -E 's/ \[[^][]*\.csproj\]$//' \
    | tr '\134' '/' \
    | sed -E 's#^.*/(third_party|src)/#\1/#' \
    | sed -E 's/Trim analysis error/Trim analysis warning/' \
    | sed -E 's/AOT analysis error/AOT analysis warning/' \
    | sort -u
}

ACTUAL="$(mktemp)"
EXPECTED="$(mktemp)"
trap 'rm -f "$ACTUAL" "$EXPECTED"' EXIT

normalise "$@" > "$ACTUAL"
grep -vE '^\s*#|^\s*$' "$BASELINE" | sed -E 's/\r$//' | sort -u > "$EXPECTED"

# A log with no diagnostics at all is not a pass, it is a log that did not analyse anything. The
# usual cause is an incremental publish: ILC only re-runs when its inputs changed, so a second
# publish into a warm obj/ emits none of the eleven and would otherwise read as "the baseline is
# satisfied". Say what happened instead of diffing eleven lines against nothing.
if [ ! -s "$ACTUAL" ] && [ -s "$EXPECTED" ]; then
  echo "::error::no trim or AOT diagnostics in the publish log, and the baseline expects some" >&2
  echo "This usually means ILC did not re-run because nothing it depends on changed. Delete" >&2
  echo "artifacts/ (or obj/) and publish again so the analysis is actually performed." >&2
  exit 1
fi

# Our own code is held to a stricter rule than the vendored tree: it may not appear here at all.
# Checked before the diff, because "you broke src/" is a more useful sentence than "the diff moved".
if grep -q '^src/' "$ACTUAL"; then
  echo "::error::a trim diagnostic was raised against src/, which is never acceptable" >&2
  grep '^src/' "$ACTUAL" >&2
  exit 1
fi

if ! diff -u "$EXPECTED" "$ACTUAL"; then
  echo "::error::the AOT publish does not emit the diagnostics $BASELINE records" >&2
  echo "A line only in the baseline means a diagnostic was fixed - delete that line." >&2
  echo "A line only in the publish means new AOT-hostile code arrived. Read it before" >&2
  echo "adding it: a construct that fails at runtime under NativeAOT is a shipped bug," >&2
  echo "not a lint. See DECISIONS.md D-0040 for how each existing line was cleared." >&2
  exit 1
fi

echo "AOT trim diagnostics match the baseline ($(wc -l < "$ACTUAL") entries, all vendored)."
