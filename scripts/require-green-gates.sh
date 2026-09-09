#!/usr/bin/env bash
# Refuses a commit whose gates have not gone green, and does it somewhere a fixture can reach.
#
# The decision lives here rather than in release.yml's guard for the reason F.4a paid for: a step
# body is only reachable by the event that runs it, and this one runs on `if: ref_type == 'tag'`.
# A check that only a real tag can execute is a check nobody has watched fail. F.4a's destination
# guard fell open for two releases exactly that way (D-0104), and the repair was to move the
# decision into a script and drive it with a fake `aws`. This is the same move with a fake `gh`.
#
# WHAT IT DECIDES. release-targets.json advertises seven targets across two components, and three
# of them are checked by app.yml alone. A guard that reads `ci` and not `app` lets a tag publish
# while the desktop gate is red or never ran, which makes "every advertised target has a matching
# check" false rather than incomplete (R.0a).
#
# WHICH WORKFLOWS ARE REQUIRED is read from the definition, not written out here: every component's
# workflow except the release workflow itself, plus `ci`. Self-exclusion is the point - requiring a
# green release.yml at the commit a release is running on cannot ever be satisfied. Add a component
# with its own gate and this starts requiring it without an edit.
#
# A RED RUN AND AN ABSENT RUN ARE THE SAME ANSWER, deliberately. The query asks for successful runs,
# so a failed one simply is not in the reply, and no count can tell "it failed" from "it never ran".
# Both mean the same thing to a release and both refuse. app.yml's paths filter makes the absent
# case ordinary rather than exotic: a push touching only src/Keypaste.Mcp or scripts/ matches none
# of its paths, so the commit has no app run at all. That fails the tag on purpose - dispatch
# app.yml at the commit, let it go green, then tag.
#
# Usage:
#   require-green-gates.sh <sha>
#
# Environment:
#   GITHUB_REPOSITORY        owner/name to ask about        (required)
#   KEYPASTE_RELEASE_DEFINITION   definition to read        (default: release-targets.json)
#   KEYPASTE_RELEASE_WORKFLOW     this workflow's path      (default: .github/workflows/release.yml)
set -euo pipefail

readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-release-targets.json}"
readonly SELF="${KEYPASTE_RELEASE_WORKFLOW:-.github/workflows/release.yml}"

die() { echo "::error::$*" >&2; exit 1; }

SHA="${1:-}"
[ -n "$SHA" ] || die "usage: require-green-gates.sh <sha>"
[ -n "${GITHUB_REPOSITORY:-}" ] || die "GITHUB_REPOSITORY is not set; nothing to ask about"
[ -f "$DEFINITION" ] || die "no release definition at $DEFINITION"
command -v jq >/dev/null 2>&1 || die "no jq; this gate reads a JSON reply and cannot run without it"
command -v gh >/dev/null 2>&1 || die "no gh; this gate asks the API and cannot run without it"

# ci is the shared gate every component depends on and is named here because no component owns it.
# Everything else comes out of the definition, on ONE line, so the negative control in
# verify-green-gates.sh can delete exactly that line and get the pre-R.0a shape back rather
# than a syntax error. A control that cannot reproduce the old behaviour proves nothing.
required_workflows() {
  echo ci
  jq -r --arg self "$SELF" '.components[] | select(.workflow != $self) | .workflow' "$DEFINITION" | sed 's#.*/##; s#\.ya\?ml$##' | sort -u
}

required="$(required_workflows | tr '\n' ' ')"

runs="$(gh api "repos/$GITHUB_REPOSITORY/actions/runs?head_sha=$SHA&status=success" 2>/dev/null)" \
  || die "could not ask $GITHUB_REPOSITORY which runs succeeded for $SHA"
[ -n "$runs" ] || die "the API returned nothing about $SHA; refusing without a verified answer"

missing=0
for wf in $required; do
  # D-0106: the count is required to be digits. An empty or malformed reply must refuse rather than
  # arrive as "" and compare its way past -ge 1, which is how jq 1.6 let an empty answer reach an
  # upload. A refusal rests on what came back, never on how a tool chose to exit.
  n="$(printf '%s' "$runs" | jq --arg wf "$wf" '[.workflow_runs[]? | select(.name == $wf)] | length' 2>/dev/null || true)"
  case "$n" in
    '' | *[!0-9]*) die "could not count $wf runs for $SHA; refusing without a verified answer" ;;
  esac
  if [ "$n" -ge 1 ]; then
    echo "  ok  $wf: $n successful run(s) for $SHA"
  else
    echo "::error::no successful $wf run for $SHA - a tag must not ship a commit its gates have not passed" >&2
    echo "::error::a red run and a skipped one look the same here; if $wf's paths filter skipped this commit, dispatch $wf at it and re-tag once it is green" >&2
    missing=1
  fi
done

[ "$missing" -eq 0 ] || die "refusing to release $SHA"
echo "every required gate is green on $SHA: $required"
