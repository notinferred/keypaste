#!/usr/bin/env bash
# Three assertions about facts, replacing a gate that asserted things about form.
#
# scripts/verify-docs.sh policed the SHAPE of the plan - lane symmetry, word caps, orphaned verifier
# ids - and generated more work than it caught, so it is gone. This does not do that. Each check
# below is a statement about the world that was FALSE at some point in this repository's history,
# and each one can go red.
#
#   A. Every "step N.N" reference in the documents points at a step docs/STEPS.md defines, as a
#      "### N.N —" heading or a "- [ ] **N.N" row.
#      Fired 2026-08-26 against the dangling "step 1.5" that splitting it into 1.5a and 1.5b left
#      behind - including in the lane naming what blocked the launch, which still read correctly
#      and pointed at nothing.
#
#   B. Nothing is sitting in the working tree untracked AND unignored.
#      Fired 2026-08-26 against a demo cast that a page described as committed and that was one
#      `git clean` from gone; and against .claude/ and graphify-out/, which were neither policy nor
#      preference. This is the failure .gitignore's own comment names: "a file sitting in the
#      working tree is one `git add -A` away from being in it".
#
#   C. Every "D-NNNN" and "H-NNNN" the documents cite resolves - a D- to a ledger row or a record
#      in DECISIONS.md, an H- to the step in docs/STEPS.md that carries it.
#      Added 2026-09-04 (D-0080) after an audit found two citations pointing at nothing: H-0020,
#      which named a human action whose Owner Queue was deleted at 9d48bd7, and D-0052, a forward
#      id reserved for a decision that was never written. Check A could not see either, because
#      neither is a step number, so both read correctly and meant nothing for weeks.
#
# HONEST LIMIT ON B, STATED RATHER THAN DISCOVERED: it is meaningful only in a working tree. CI
# checks out clean, so there every file is tracked by construction and B is green for a reason that
# has nothing to do with the repository being tidy. That is exactly the D-0043 failure - a check
# that cannot go red is an assertion about the world - so B SKIPS ITSELF under CI and says so,
# rather than contributing a green nobody earned. Run this locally before you commit; that is where
# it bites. A and C are meaningful anywhere, which is why D-0080 gave them a workflow of their own:
# every document they read sits in ci.yml's paths-ignore, so until docs.yml existed neither ran on
# a documents-only push - the only kind of push that can break them.
set -euo pipefail

readonly STEPS='docs/STEPS.md'
readonly LEDGER='DECISIONS.md'

die() { echo "::error::$*" >&2; exit 1; }

[ -f "$STEPS" ] || die "$STEPS is missing, and this gate is meaningless without it"
[ -f "$LEDGER" ] || die "$LEDGER is missing, and check C is meaningless without it"

# Citations live in every document, not only the plan; the plan itself defines steps rather than
# citing them. CLAUDE.md is here for C alone - it cites decisions and no steps.
readonly CITING='docs/*.md DECISIONS.md launch.md THREATS.md SECURITY.md README.md CHANGELOG.md CONTRIBUTING.md CLAUDE.md'

# --- A. a step reference points at a step that exists --------------------------------------------
# The trailing letter matters: a split produces 1.5a and 1.5b, and a pattern that only accepted
# <n.n> would silently stop checking the two steps the split just created.
referenced=$(grep -ohiE 'step \*\*[0-9]+\.[0-9]+[a-z]?\*\*|step [0-9]+\.[0-9]+[a-z]?' $CITING \
             | grep -oE '[0-9]+\.[0-9]+[a-z]?' | sort -u || true)
[ -n "$referenced" ] || die "no 'step N.N' citation found in any document, which means this check examined nothing"

for id in $referenced; do
  grep -qE "^(### $id — |- \[[ x]\] \*\*$id )" "$STEPS" \
    || die "a document refers to step $id, and no '### $id — ' heading or '- [ ] **$id ' row in $STEPS defines it. Either the step was renumbered - a split leaves the old number behind in prose that still reads correctly - or the reference is to something that never existed"
done

# --- C. a decision or owner citation points at something that exists -----------------------------
decisions=$(grep -ohE 'D-[0-9]{4}' $CITING | sort -u || true)
[ -n "$decisions" ] || die "no 'D-NNNN' citation found in any document, which means this check examined nothing"

for id in $decisions; do
  grep -qE "^## $id |^\| $id \|" "$LEDGER" \
    || die "a document cites $id, and $LEDGER has neither a '| $id |' ledger row nor a '## $id — ' record. A forward id reserved for a decision nobody wrote reads exactly like a decision that was made"
done

owners=$(grep -ohE 'H-[0-9]{4}' $CITING | sort -u || true)
for id in $owners; do
  grep -qF "$id" "$STEPS" \
    || die "a document cites $id, and $STEPS names it nowhere. H- ids are carried inline by the step that owns them - the Owner Queue that once listed them separately was deleted at 9d48bd7 - so an H- with no step behind it is a human action nobody is holding"
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
    die "commit them, or add them to .gitignore. Limbo is one 'git clean' from gone, and one 'git add -A' from committed"
  fi
fi

echo "ok: every step cited in the documents resolves to a step $STEPS defines ($(echo $referenced | wc -w) ids)."
echo "ok: every decision cited resolves to a row or a record in $LEDGER ($(echo $decisions | wc -w) ids), and every"
echo "ok: owner id resolves to the step that carries it ($(echo $owners | wc -w) ids)."
if [ "$b_ran" = yes ]; then
  echo "ok: nothing is sitting in the working tree untracked and unignored."
else
  echo "not checked: whether anything is untracked and unignored. B did not run here, and a"
  echo "not checked: summary that said otherwise would be the green this script exists to refuse."
fi
