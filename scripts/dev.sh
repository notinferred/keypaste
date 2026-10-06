#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

usage() {
  cat <<'USAGE'
Usage: bash scripts/dev.sh [--class FQN] [--target T] [--os O] [--gates G] [--wait]
       bash scripts/dev.sh --relock

Pushes the current branch, dispatches .github/workflows/dev.yml on it and prints each job as it
finishes. The first failed job ends the wait with its log; the run's other jobs carry on, and the
next dispatch on the branch cancels them. Needs only git and gh; nothing builds on this machine.

--class   a fully qualified test class; runs only it, in its own project
--target  auto (default: what the branch changed against origin/main, including the gates,
          script fixtures and workflow checks it needs), build, or a comma list of core, cli,
          mcp, app, consistency
--os      linux (default), windows, macos, linux+windows, linux+macos or all
--gates   none (default), integration, compat or both; added to what auto selects
--wait    wait for every job and print each failed one's log, rather than stopping at the first
--relock  regenerate the lock files of the pushed commit with the CI SDK, build nothing, and write
          the ones that changed into this tree to review and commit
USAGE
}

bad_usage() { echo "$1" >&2; usage >&2; exit 2; }
die() { echo "dev: $1" >&2; exit 1; }

class='' target=auto os=linux gates=none relock=false wait_all=false
while [ "$#" -gt 0 ]; do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --relock) relock=true ;;
    --wait) wait_all=true ;;
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
gh workflow run dev.yml --ref "$branch" -f target="$target" -f filter_class="$class" -f os="$os" -f gates="$gates" -f nonce="$nonce" -f relock="$relock"

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

# The end of a finished job's log, without the timestamps or the runner's cleanup after the failure.
job_log() {
  gh api "repos/{owner}/{repo}/actions/jobs/$1/logs" 2>/dev/null \
    | sed -E 's/^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:.]+Z //' | sed '/^Post job cleanup\./,$d' | tail -n 150 || true
}

# Each poll prints the jobs that finished since the last; a job the plan skipped prints nothing.
status='' seen=' ' failed=()
while [ "$status" != completed ]; do
  sleep 10
  report="$(gh run view "$id" --json status,jobs --jq '.status, (.jobs[]
    | select(.status == "completed" and .conclusion != "skipped")
    | "\(.databaseId) \(.conclusion) \(try ((.completedAt | fromdateiso8601) - (.startedAt | fromdateiso8601)) catch 0) \(.name)")')" || continue
  status="$(sed -n 1p <<<"$report")"
  while read -r job conclusion seconds name; do
    [ -n "$job" ] || continue
    case "$seen" in *" $job "*) continue ;; esac
    seen="$seen$job "
    printf '%-9s %5ss  %s\n' "$conclusion" "$seconds" "$name"
    if [ "$conclusion" != success ]; then failed+=("$job $name"); fi
  done < <(sed 1d <<<"$report")
  if [ "${#failed[@]}" -gt 0 ] && [ "$wait_all" = false ]; then break; fi
done

if [ "${#failed[@]}" -gt 0 ]; then
  for entry in "${failed[@]}"; do
    printf '\n==== %s ====\n' "${entry#* }"
    job_log "${entry%% *}"
  done
  echo "dev: ${failed[0]#* } failed after $((SECONDS - start))s; run $id holds the rest. Exit code 8 from the test runner means the filter matched zero tests." >&2
  exit 1
fi

if [ "$relock" = true ]; then
  relocked="$(mktemp -d)"
  trap 'rm -rf "$relocked"' EXIT
  gh run download "$id" -n relock -D "$relocked"
  [ -f "$relocked/changed.txt" ] || die "run $id uploaded no list of changed lock files"
  while IFS= read -r file; do cp "$relocked/$file" "$file"; done < "$relocked/changed.txt"
  if [ -s "$relocked/changed.txt" ]; then
    echo "regenerated in $((SECONDS - start))s; review and commit:"
    cat "$relocked/changed.txt"
  else
    echo "every lock file already matches its project"
  fi
  exit 0
fi

# The summary carries the plan's warnings: lanes and runners this run left out, or nothing selected.
gh run view "$id" || true
echo "green in $((SECONDS - start))s; any warning above names what this run left untested"
