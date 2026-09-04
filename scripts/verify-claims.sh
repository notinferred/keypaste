#!/usr/bin/env bash
# Two assertions about facts, replacing a gate that asserted things about form.
#
# scripts/verify-docs.sh was deleted on 2026-09-04 because it policed the SHAPE of the plan - lane
# symmetry, word caps, orphaned verifier ids - on a list of a dozen items, and that generated more
# work than it caught. This does not do that. Each check below is a statement about the world that
# was FALSE at some point in this repository's history, and each one can go red.
#
#   A. Every "step N.N" reference in docs/STEPS.md points at a step that exists.
#      Fired 2026-08-26 against the dangling "step 1.5" that splitting it into 1.5a and 1.5b left
#      behind - including in the Owner lane naming what blocks the launch, which still read
#      correctly and pointed at nothing.
#
#   B. Nothing is sitting in the working tree untracked AND unignored.
#      Fired 2026-08-26 against docs/demo/keypaste-demo.cast, which docs/ARTIFACTS.md described as
#      committed and which was one `git clean` from gone; and against .claude/ and graphify-out/,
#      which were neither policy nor preference. This is the failure .gitignore's own comment names:
#      "a file sitting in the working tree is one `git add -A` away from being in it".
#
# HONEST LIMIT ON B, STATED RATHER THAN DISCOVERED: it is meaningful only in a working tree. CI
# checks out clean, so there every file is tracked by construction and B is green for a reason that
# has nothing to do with the repository being tidy. That is exactly the D-0043 failure - a check
# that cannot go red is an assertion about the world - so B SKIPS ITSELF under CI and says so,
# rather than contributing a green nobody earned. Run this locally before you commit; that is where
# it bites.
set -euo pipefail

readonly STEPS='docs/STEPS.md'

die() { echo "::error::$*" >&2; exit 1; }

[ -f "$STEPS" ] || die "$STEPS is missing, and this gate is meaningless without it"

# --- A. a step reference points at a step that exists --------------------------------------------
# The trailing letter matters: a split produces 1.5a and 1.5b, and a pattern that only accepted
# <n.n> would silently stop checking the two steps the split just created.
referenced=$(grep -oiE 'step \*\*[0-9]+\.[0-9]+[a-z]?\*\*|step [0-9]+\.[0-9]+[a-z]?' "$STEPS" \
             | grep -oE '[0-9]+\.[0-9]+[a-z]?' | sort -u)

for id in $referenced; do
  grep -qE "^### $id — " "$STEPS" \
    || die "$STEPS refers to step $id, and no '### $id — ' heading defines it. Either the step was renumbered - a split leaves the old number behind in prose that still reads correctly - or the reference is to something that never existed"
done

# --- B. nothing is untracked and unignored -------------------------------------------------------
b_ran=no
if [ -n "${CI:-}${GITHUB_ACTIONS:-}" ]; then
  echo "note: check B skipped under CI, where a clean checkout makes it green by construction."
else
  b_ran=yes
  stray=$(git ls-files --others --exclude-standard)
  if [ -n "$stray" ]; then
    echo "::error::these files are in the working tree, tracked by nothing and ignored by nothing:" >&2
    printf '  %s\n' $stray >&2
    die "commit them, or add them to .gitignore. Limbo is the state docs/ARTIFACTS.md described the demo cast as being in while it was one 'git clean' from gone"
  fi
fi

echo "ok: every step reference in $STEPS resolves to a step that exists."
if [ "$b_ran" = yes ]; then
  echo "ok: nothing is sitting in the working tree untracked and unignored."
else
  echo "not checked: whether anything is untracked and unignored. B did not run here, and a"
  echo "not checked: summary that said otherwise would be the green this script exists to refuse."
fi
