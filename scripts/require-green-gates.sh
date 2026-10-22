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
# ONLY A DISPATCH, THE WEEKLY SCHEDULE OR A TAG'S OWN PUSH COUNTS (D-0403). Those are the runs this
# accepts, and cutting a release starts with dispatching ci.yml at the commit.
#
# A CI RUN COUNTS ONLY WITH ITS REAL-TOOLS JOBS GREEN (D-0438). ci.yml's tools jobs run on the schedule and
# on a dispatch whose tools input is true, so a plain dispatch is green without them. Each counted ci run's
# jobs are read, and one counts only when it lists a `tools (...)` job and every such job succeeded. A matrix
# job its condition skipped is listed once, with its name unexpanded and its conclusion skipped, so it never
# counts. The release's dispatch is `gh workflow run ci.yml -f tools=true` at the commit.
#
# A TAG'S OWN RUN IS WAITED FOR, NOT READ EARLY. release.yml starts with the tag push, as app.yml's run at the
# tag does, and asked at once the guard found that run still going and refused, so every tag needed a release
# re-run (G.5). --wait polls the workflow's push runs at the ref until each has finished, and refuses when none
# of them succeeded, when none appeared, or when one is still going after the last poll; the guard calls it
# before either question below (D-NEW-X61).
#
# Usage:
#   require-green-gates.sh <sha>
#   require-green-gates.sh --run-id <workflow> <sha> <ref>   # prints run_id=<n> for that workflow
#   require-green-gates.sh --wait <workflow> <sha> <ref>     # returns once that workflow's run at the ref finished green
#
# --run-id answers a second question off the same reply: WHICH run of a component's own workflow
# built the commit being released, so release.yml can take that run's packages rather than rebuild
# them (4.7c). It refuses unless exactly one successful run matches the workflow and the ref: two
# green runs at one commit is an ambiguity a release must not settle by picking.
#
# Environment:
#   GITHUB_REPOSITORY        owner/name to ask about        (required)
#   KEYPASTE_RELEASE_DEFINITION   definition to read        (default: release-targets.json)
#   KEYPASTE_RELEASE_WORKFLOW     this workflow's path      (default: .github/workflows/release.yml)
#   KEYPASTE_GATES_WAIT_POLLS     --wait's polls before it refuses   (default: 80)
#   KEYPASTE_GATES_WAIT_SECONDS   seconds between them               (default: 30, so 40 minutes in all)
set -euo pipefail

readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-release-targets.json}"
readonly SELF="${KEYPASTE_RELEASE_WORKFLOW:-.github/workflows/release.yml}"
readonly WAIT_POLLS="${KEYPASTE_GATES_WAIT_POLLS:-80}"
readonly WAIT_SECONDS="${KEYPASTE_GATES_WAIT_SECONDS:-30}"

die() { echo "::error::$*" >&2; exit 1; }

# Windows jq writes CRLF, and a CR riding on a run ID reads as "not a run ID" - the same strip
# require-release-assets.sh applies at every call it makes.
CR="$(printf '\r')"

MODE=gates
WANT_WORKFLOW=""
WANT_REF=""
case "${1:-}" in
  --run-id | --wait)
    MODE="${1#--}"
    usage="usage: require-green-gates.sh $1 <workflow> <sha> <ref>"
    WANT_WORKFLOW="${2:-}"
    shift 2 || die "$usage"
    [ -n "$WANT_WORKFLOW" ] || die "$usage"
    WANT_REF="${2:-}"
    [ -n "$WANT_REF" ] || die "$usage"
    ;;
esac

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
  jq -r --arg self "$SELF" '.components[] | select(.workflow != $self) | .workflow' "$DEFINITION" | sed -E 's#.*/##; s#\.ya?ml$##' | sort -u
}

required="$(required_workflows | tr '\n' ' ')"

# "<status> <conclusion>" of each push run of $WANT_WORKFLOW at $WANT_REF, one per line; nothing while none exists.
# Bound to the ref as --run-id is, so a pull request whose branch carries the tag's name is never waited for.
tag_runs() {
  local reply
  reply="$(gh api "repos/$GITHUB_REPOSITORY/actions/runs?head_sha=$SHA&per_page=100" 2>/dev/null)" \
    || die "could not ask $GITHUB_REPOSITORY which runs exist for $SHA"
  [ -n "$reply" ] || die "the API returned nothing about $SHA; refusing without a verified answer"
  printf '%s' "$reply" | jq -r --arg wf "$WANT_WORKFLOW" --arg ref "$WANT_REF" '
    .workflow_runs
    | if type == "array" then .[] else error("no workflow_runs") end
    | select(.name == $wf and .event == "push" and (.head_branch == $ref or .head_branch == ($ref | sub("^refs/tags/"; ""))))
    | "\(.status) \(.conclusion // "none")"' 2>/dev/null | tr -d "$CR" \
    || die "could not read which $WANT_WORKFLOW runs exist for $SHA; refusing without a verified answer"
}

wait_for_tag_runs() {
  local poll=1 states
  case "$WAIT_POLLS" in '' | 0 | *[!0-9]*) die "KEYPASTE_GATES_WAIT_POLLS is '$WAIT_POLLS', not a number of polls" ;; esac
  case "$WAIT_SECONDS" in '' | *[!0-9]*) die "KEYPASTE_GATES_WAIT_SECONDS is '$WAIT_SECONDS', not a number of seconds" ;; esac
  while :; do
    states="$(tag_runs)" || exit 1
    if [ -n "$states" ] && ! grep -qv '^completed ' <<< "$states"; then
      grep -qx 'completed success' <<< "$states" \
        || die "$WANT_WORKFLOW's run at $WANT_REF for $SHA finished $(cut -d' ' -f2 <<< "$states" | sort -u | tr '\n' ' ')- a tag must not ship a commit its gates have not passed"
      echo "  ok  $WANT_WORKFLOW's run at $WANT_REF finished green after $poll poll(s)"
      return 0
    fi
    if [ "$poll" -ge "$WAIT_POLLS" ]; then
      [ -n "$states" ] || die "no $WANT_WORKFLOW run at $WANT_REF for $SHA appeared in $WAIT_POLLS polls ${WAIT_SECONDS}s apart; refusing without the run a release takes its packages from"
      die "$WANT_WORKFLOW's run at $WANT_REF for $SHA is still going after $WAIT_POLLS polls ${WAIT_SECONDS}s apart; re-run this release once it has finished"
    fi
    poll=$((poll + 1))
    sleep "$WAIT_SECONDS"
  done
}

if [ "$MODE" = wait ]; then
  wait_for_tag_runs
  exit 0
fi

runs="$(gh api "repos/$GITHUB_REPOSITORY/actions/runs?head_sha=$SHA&status=success" 2>/dev/null)" \
  || die "could not ask $GITHUB_REPOSITORY which runs succeeded for $SHA"
[ -n "$runs" ] || die "the API returned nothing about $SHA; refusing without a verified answer"

if [ "$MODE" = run-id ]; then
  # Bound to the ref as well as the commit: a tag and the branch it points at are the same SHA, and
  # only the tag run carries the full version in the names it built (app.yml's dryrun suffix). A pull
  # request run's head_branch is its source branch, which may carry the tag's name.
  ids="$(printf '%s' "$runs" | jq -r --arg wf "$WANT_WORKFLOW" --arg ref "$WANT_REF"     '[.workflow_runs[]? | select(.name == $wf and .event != "pull_request" and (.head_branch == $ref or .head_branch == ($ref | sub("^refs/tags/"; "")))) | .id] | .[]' 2>/dev/null | tr -d "$CR" || true)"
  n=0
  for id in $ids; do
    case "$id" in '' | *[!0-9]*) die "the API named a $WANT_WORKFLOW run that is not a run ID; refusing without a verified answer" ;; esac
    n=$((n + 1))
  done
  [ "$n" -ge 1 ] || die "no successful $WANT_WORKFLOW run for $SHA at $WANT_REF - a release cannot take packages from a run that did not happen"
  [ "$n" -eq 1 ] || die "$n successful $WANT_WORKFLOW runs for $SHA at $WANT_REF; a release must not choose between them"
  echo "run_id=$ids"
  exit 0
fi

# The runs of workflow $1 that count, passed through jq filter $2. Only a dispatch, the schedule, or a
# tag's own push counts (D-0403).
counted() {
  printf '%s' "$runs" | jq --arg wf "$1" "[.workflow_runs[]? | select(.name == \$wf and (.event == \"workflow_dispatch\" or .event == \"schedule\" or (.event == \"push\" and .head_branch != \"main\")))] | $2" 2>/dev/null | tr -d "$CR" || true
}

# How many of the ci runs whose IDs are $1 ran tools jobs, passed every one of them and skipped no other
# job, which a tools-only dispatch does (D-0438).
green_tools_runs() {
  local id jobs count green=0
  for id in $1; do
    case "$id" in *[!0-9]*) die "the API named a ci run that is not a run ID; refusing without a verified answer" ;; esac
    jobs="$(gh api "repos/$GITHUB_REPOSITORY/actions/runs/$id/jobs?per_page=100" 2>/dev/null)" \
      || die "could not ask $GITHUB_REPOSITORY which jobs ci run $id ran"
    count="$(printf '%s' "$jobs" | jq '[.jobs[] | select(.name | startswith("tools (")) | .conclusion] as $tools | if ($tools | length) > 0 and ($tools | all(. == "success")) and ([.jobs[] | .conclusion] | all(. != "skipped")) then ($tools | length) else 0 end' 2>/dev/null | tr -d "$CR" || true)"
    case "$count" in
      '' | *[!0-9]*) die "could not count the tools jobs of ci run $id; refusing without a verified answer" ;;
    esac
    [ "$count" -eq 0 ] || green=$((green + 1))
  done
  echo "$green"
}

missing=0
for wf in $required; do
  # D-0106: the count is required to be digits. An empty or malformed reply must refuse rather than
  # arrive as "" and compare its way past -ge 1, which is how jq 1.6 let an empty answer reach an
  # upload. A refusal rests on what came back, never on how a tool chose to exit.
  n="$(counted "$wf" length)"
  case "$n" in
    '' | *[!0-9]*) die "could not count $wf runs for $SHA; refusing without a verified answer" ;;
  esac
  # One line, so the second negative control in verify-green-gates.sh can delete it and get the shape before D-0438.
  if [ "$wf" = ci ]; then n="$(green_tools_runs "$(counted ci '.[].id')")" || exit 1; fi
  dispatch="dispatch $wf at this commit"
  [ "$wf" != ci ] || dispatch='dispatch ci.yml at this commit with -f tools=true'
  if [ "$n" -ge 1 ]; then
    echo "  ok  $wf: $n full successful run(s) for $SHA"
  else
    echo "::error::no full successful $wf run for $SHA - a tag must not ship a commit its gates have not passed" >&2
    echo "::error::a red run, a skipped one and a push that ran only some lanes look the same here; $dispatch and re-tag once it is green" >&2
    missing=1
  fi
done

[ "$missing" -eq 0 ] || die "refusing to release $SHA"
echo "every required gate is green on $SHA: $required"
