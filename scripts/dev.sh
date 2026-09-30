#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

usage() {
  cat <<'USAGE'
Usage: bash scripts/dev.sh [--class FQN] [--target T] [--os O] [--gates G]

Pushes the current branch, dispatches .github/workflows/dev.yml on it and waits for the verdict.
Needs only git and gh; nothing builds on this machine.

--class   a fully qualified test class; runs only it, in its own project
--target  auto (default: what the branch changed against origin/main, including the gates,
          script fixtures and workflow checks it needs), build, or a comma list of core, cli,
          mcp, app, consistency
--os      linux (default), windows, macos, linux+windows, linux+macos or all
--gates   none (default), integration, compat or both; added to what auto selects
USAGE
}

bad_usage() { echo "$1" >&2; usage >&2; exit 2; }
die() { echo "dev: $1" >&2; exit 1; }

class='' target=auto os=linux gates=none
while [ "$#" -gt 0 ]; do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --class|--target|--os|--gates)
      [ "$#" -ge 2 ] || bad_usage "$1 needs a value"
      case "$1" in
        --class) class="$2" ;;
        --target) target="$2" ;;
        --os) os="$2" ;;
        --gates) gates="$2" ;;
      esac
      shift
      ;;
    *) bad_usage "unknown argument: $1" ;;
  esac
  shift
done

branch="$(git symbolic-ref --short -q HEAD)" || die 'HEAD is detached; check out a branch'
[ "$branch" != main ] || die 'refusing to run on main; work on a branch'
[ -z "$(git status --porcelain)" ] || die 'the tree has uncommitted or untracked files; commit first; the runner tests the pushed commit'

start=$SECONDS
git push -u origin HEAD
sha="$(git rev-parse HEAD)"
nonce="$(date +%s)-$$"
gh workflow run dev.yml --ref "$branch" -f target="$target" -f filter_class="$class" -f os="$os" -f gates="$gates" -f nonce="$nonce"

run=''
tries=0
while [ -z "$run" ] && [ "$tries" -lt 30 ]; do
  sleep 3
  tries=$((tries + 1))
  run="$(gh run list --workflow dev.yml --branch "$branch" --event workflow_dispatch --limit 20 \
    --json databaseId,displayTitle,headSha \
    --jq ".[] | select(.displayTitle | endswith(\"$nonce\")) | \"\(.databaseId) \(.headSha)\"" || true)"
done
[ -n "$run" ] || die "no dev run carrying $nonce appeared within 90s"
read -r id head <<< "$run"
[ "$head" = "$sha" ] || die "run $id tests $head, not HEAD $sha"
gh run view "$id" --json url --jq .url

if gh run watch "$id" --exit-status --interval 10 >/dev/null; then
  # The summary carries the plan's warnings: lanes and runners this run left out, or nothing selected.
  gh run view "$id" || true
  echo "green in $((SECONDS - start))s; any warning above names what this run left untested"
  exit 0
fi
gh run view "$id" || true
gh run view "$id" --log-failed | tail -n 200 || true
echo "dev: run $id failed after $((SECONDS - start))s. Exit code 8 from the test runner means the filter matched zero tests." >&2
exit 1
