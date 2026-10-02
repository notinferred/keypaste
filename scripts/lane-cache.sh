#!/usr/bin/env bash
# Prints the runner:lane pairs a trusted earlier CI run already passed on inputs this commit shares,
# for verify.sh's VERIFY_SATISFIED (D-0404).
#
# A CI job that passes leaves an artifact named pass--<runner>--<lane>+<lane>... on its run. A lane
# counts as passed here when such a marker names it, the run that left it is trusted, and the tree of
# the marker's commit differs from this commit's only in paths verify.sh maps to other lanes. Git's
# trees are content hashes, so unchanged inputs are the very same objects.
#
# TRUSTED means this repository's own runs that only someone with write access can start: dev.yml
# dispatches, and ci.yml dispatches, schedules and pushes to main. A pull request can name an
# artifact anything it likes, a fork's above all, so its markers are never read.
#
# Any failure prints nothing, and nothing printed means every lane the plan selects runs.
#
# Usage:        lane-cache.sh [<commit>]     (default: HEAD)
# Environment:  GITHUB_REPOSITORY, and GH_TOKEN with actions: read
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

head="${1:-HEAD}"
repo="${GITHUB_REPOSITORY:-}"
[ -n "$repo" ] || exit 0
readonly PAGES="${LANE_CACHE_PAGES:-3}"
readonly NEWEST="${LANE_CACHE_COMMITS:-20}"

markers=''
page=1
while [ "$page" -le "$PAGES" ]; do
  batch="$(gh api "repos/$repo/actions/artifacts?per_page=100&page=$page" \
    --jq '.artifacts[] | select(.expired == false and (.name | startswith("pass--"))) | "\(.workflow_run.id) \(.workflow_run.head_sha) \(.name)"' 2>/dev/null)" || exit 0
  [ -n "$batch" ] || break
  markers="$markers$batch"$'\n'
  page=$((page + 1))
done
[ -n "${markers//[$'\n' ]/}" ] || exit 0

trusted=' '
for run in $(printf '%s' "$markers" | awk 'NF { print $1 }' | sort -u); do
  info="$(gh api "repos/$repo/actions/runs/$run" \
    --jq '[.event, .path, .head_branch, .head_repository.full_name] | @tsv' 2>/dev/null)" || continue
  IFS=$'\t' read -r event path branch from <<<"$info"
  [ "$from" = "$repo" ] || continue
  case "$path:$event" in
    .github/workflows/dev.yml:workflow_dispatch | .github/workflows/ci.yml:workflow_dispatch | .github/workflows/ci.yml:schedule) ;;
    .github/workflows/ci.yml:push) [ "$branch" = main ] || continue ;;
    *) continue ;;
  esac
  trusted="$trusted$run "
done

# The newest commits first, so a long history costs no more than recent work does.
for sha in $(printf '%s' "$markers" | awk -v t="$trusted" 'NF && index(t, " " $1 " ") && !seen[$2]++ { print $2 }' | head -n "$NEWEST"); do
  git cat-file -e "$sha^{commit}" 2>/dev/null || git fetch --quiet --no-tags --depth=1 origin "$sha" 2>/dev/null || continue
  changed="$(git -c core.quotepath=off diff --name-only --no-renames "$sha" "$head" --)" || continue
  moved=' '
  if [ -n "$changed" ]; then
    moved="$(env -u VERIFY_SATISFIED VERIFY_CHANGED_PATHS="$changed" bash scripts/verify.sh --plan 2>/dev/null | sed -n 's/^lanes=//p')" || continue
    [ -n "$moved" ] || continue
  fi
  printf '%s' "$markers" | while read -r run marked name; do
    [ "$marked" = "$sha" ] || continue
    case "$trusted" in *" $run "*) ;; *) continue ;; esac
    rest="${name#pass--}"
    runner="${rest%%--*}"
    for lane in $(printf '%s' "${rest#*--}" | tr '+' ' '); do
      case " $moved " in *" $lane "*) ;; *) echo "$runner:$lane" ;; esac
    done
  done
done | sort -u
