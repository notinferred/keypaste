#!/usr/bin/env bash
# Enforces the admission rule and the lane structure that docs/STEPS.md and docs/verification.md depend on. Every other rule in this repository names an enforcer; until this script existed, the rules about the documents named none, which made them aspiration rather than rules.
#
# What it holds:
#   - every open step declares Build, Owner and Verify
#   - every verifier a step names exists in docs/verification.md, and every verifier there is named by a step - an orphan on either side means the two files disagree about what is being built
#   - every verifier carries a falsifier, and says to run it first
#   - every open step traces to docs/PRODUCT.md
#   - docs/IDEAS.md is append-only: no row that was there last commit has been removed
#   - the five pages scripts/verify-demo.sh pins are never skipped by ci.yml's paths-ignore
#   - no open step carries a Build lane over BUILD_WORD_CAP unless it is on the shrinking
#     GRANDFATHERED list, and nothing stays on that list once it is within the cap
#
# WHAT IT DELIBERATELY DOES NOT HOLD: whether a verifier is any good. A falsifier reading "check the file exists" satisfies every assertion below and proves nothing. Watching a new falsifier actually fire against the current tree, once, before trusting it, is [process] and belongs to whoever wrote it (D-0043).
set -euo pipefail

readonly STEPS='docs/STEPS.md'
readonly VERIF='docs/verification.md'
readonly IDEAS='docs/IDEAS.md'
readonly PRODUCT='docs/PRODUCT.md'
readonly CI='.github/workflows/ci.yml'

# The pages scripts/verify-demo.sh holds to the shipped binaries. Editing one is a code change wearing a markdown extension, so none of them may be skipped by paths-ignore.
readonly PINNED='README.md launch.md docs/demo.md docs/keepass-and-agents.md site/public/index.html'

die() { echo "::error::$*" >&2; exit 1; }

for f in "$STEPS" "$VERIF" "$IDEAS" "$PRODUCT" "$CI"; do
  [ -f "$f" ] || die "$f is missing, and this gate is meaningless without it"
done

# --- A. every open step declares all three lanes ------------------------------------------------
# An open step is a "### <n.n> — <title> [ ]" heading. Done steps are one line and carry no lanes.
# The trailing letter matters: a split produces 1.5a and 1.5b, and a pattern that only accepts
# <n.n> would drop both out of every check below rather than failing - the split would silently
# turn the gate off for the steps it just created.
open_steps=$(grep -oE '^### [0-9]+\.[0-9]+[a-z]? — .*\[ \]$' "$STEPS" | grep -oE '^### [0-9]+\.[0-9]+[a-z]?' \
             | awk '{print $2}' || true)
[ -n "$open_steps" ] || die "$STEPS declares no open steps. If the product is finished, say so there"

for id in $open_steps; do
  body=$(awk -v id="$id" '
    $0 ~ "^### " id " " { inb = 1; next }
    inb && /^#{2,3} / { exit }
    inb { print }
  ' "$STEPS")
  for lane in Build Owner Verify; do
    printf '%s\n' "$body" | grep -qE "^- \*\*$lane\*\*" \
      || die "step $id in $STEPS has no $lane lane. The admission rule needs all three: Build is the agent-runnable prompt, Owner is what only a human can do, Verify names an independent check"
  done
  printf '%s\n' "$body" | grep -qE 'docs/PRODUCT\.md' \
    || die "step $id in $STEPS traces to nothing in $PRODUCT. A step that does not serve a claim in the locked core is an IDEAS row, not a step"
done

# --- B. the two files agree on the set of verifiers ----------------------------------------------
named=$(grep -oE '`V-[0-9]{4}`' "$STEPS" | tr -d '`' | sort -u)
defined=$(grep -oE '^## V-[0-9]{4}' "$VERIF" | awk '{print $2}' | sort -u)

for v in $named; do
  printf '%s\n' "$defined" | grep -qx "$v" \
    || die "$STEPS names $v and $VERIF does not define it. A Verify lane pointing at nothing is the same as no Verify lane"
done
for v in $defined; do
  printf '%s\n' "$named" | grep -qx "$v" \
    || die "$VERIF defines $v and no step in $STEPS names it. Either a step lost its Verify lane or this verifier outlived the step it was written for"
done

# --- C. every verifier carries a falsifier, and says to run it first -----------------------------
for v in $defined; do
  body=$(awk -v id="$v" '
    $0 ~ "^## " id " " { inb = 1; next }
    inb && /^## / { exit }
    inb { print }
  ' "$VERIF")
  printf '%s\n' "$body" | grep -qi 'falsifier' \
    || die "$v in $VERIF has no falsifier. Every verifier carries the specific thing to try that would prove the step is NOT done"
  printf '%s\n' "$body" | grep -qiE 'run (it )?first|first\.' \
    || die "$v in $VERIF has a falsifier but does not say to run it first. Running it last is how a verifier talks itself into a pass"
done

# --- D. docs/IDEAS.md is append-only -------------------------------------------------------------
# Rows are keyed on the idea column. A row may change status; it may not vanish.
if git rev-parse --verify HEAD >/dev/null 2>&1 && git cat-file -e "HEAD:$IDEAS" 2>/dev/null; then
  key() { grep -E '^\| ' "$1" | grep -v '^| *idea *|' | grep -v '^|[ -]*|' | cut -d'|' -f2 \
          | sed 's/^ *//; s/ *$//' | grep -v '^$' | sort -u; }
  before=$(git show "HEAD:$IDEAS" | key /dev/stdin)
  after=$(key "$IDEAS")
  missing=$(comm -23 <(printf '%s\n' "$before") <(printf '%s\n' "$after") || true)
  [ -z "$missing" ] || die "$IDEAS lost rows, and it is append-only. Flip a status; never delete a row, because a deleted row cannot tell 'never proposed' from 'quietly dropped': $missing"
fi

# --- F. a step is one thing, not nine ------------------------------------------------------------
# Every step that shipped needed 66 to 107 words of Build. The open ones drifted to 165 to 277,
# which is five to nine deliverables behind a single verifier - and the admission rule asks for "an
# accept criterion", singular. Bundled, a partial pass has nowhere to be recorded, so sixty percent
# done reads exactly like not started.
#
# GRANDFATHERED is a debt register, not an exemption list, and it ratchets in one direction: an id
# leaves it by being split, and an id that no longer exceeds the cap MUST be removed or this fails.
# Nothing may be added to it - a new step over the cap is a new step that should have been two.
readonly BUILD_WORD_CAP=150
readonly GRANDFATHERED='1.4 4.5 4.6 8.1 8.2'

for g in $GRANDFATHERED; do
  printf '%s\n' $open_steps | grep -qx "$g" \
    || die "GRANDFATHERED in $0 names step $g, which is not an open step in $STEPS. A stale entry is an exemption nobody is reading"
done

for id in $open_steps; do
  words=$(awk -v id="$id" '
    $0 ~ "^### " id " " { inb = 1; next }
    inb && /^#{2,3} / { exit }
    inb && /^- \*\*Build\*\*/ { print NF - 2; exit }
  ' "$STEPS")
  [ -n "$words" ] || continue

  listed=no
  for g in $GRANDFATHERED; do [ "$g" = "$id" ] && listed=yes; done

  if [ "$words" -gt "$BUILD_WORD_CAP" ] && [ "$listed" = no ]; then
    die "step $id in $STEPS has a $words-word Build lane against a cap of $BUILD_WORD_CAP. That is several deliverables behind one verifier: split it into $id-a, $id-b and so on, each with its own accept criterion in $VERIF"
  fi
  if [ "$words" -le "$BUILD_WORD_CAP" ] && [ "$listed" = yes ]; then
    die "step $id in $STEPS is within the $BUILD_WORD_CAP-word cap but is still listed in GRANDFATHERED in $0. Remove it - the list only shrinks, and leaving a split step on it hands a permanent exemption to whatever grows there next"
  fi
done

# --- E. the pinned pages are never skipped -------------------------------------------------------
# NEGATIVE-SPACE CHECK: this asserts an absence. A docs/** entry would disable verify-demo.sh for two of these pages silently and fail open, which is the one failure mode here that goes unnoticed.
skips=$(awk '/paths-ignore:/{f=1;next} /^  [a-z_]+:/{f=0} f' "$CI" | grep -oE "'[^']+'" | tr -d "'")
for p in $PINNED; do
  for g in $skips; do
    case "$p" in
      $g|${g%/\*\*}/*) die "$CI's paths-ignore skips $p via '$g'. scripts/verify-demo.sh holds that page to what the shipped binaries print, so skipping it turns the gate off without turning anything red" ;;
    esac
  done
done
for g in $skips; do
  compgen -G "$g" >/dev/null 2>&1 || die "$CI's paths-ignore names '$g', which matches nothing. A stale skip is a skip nobody is reading"
done

echo "ok: every open step in $STEPS declares Build, Owner and Verify and traces to $PRODUCT;"
echo "ok: $STEPS and $VERIF name the same verifiers; every verifier carries a falsifier and says to"
echo "ok: run it first; $IDEAS has lost no rows; and $CI skips none of the pinned pages."
echo "note: whether a falsifier can actually fire is [process] and is not checked here."
