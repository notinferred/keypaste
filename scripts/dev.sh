#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

usage() {
  cat <<'USAGE'
Usage: bash scripts/dev.sh [--wait]
       bash scripts/dev.sh --class FQN [--os O] [--wait]
       bash scripts/dev.sh --relock

Pushes the current branch and follows GitHub's verdict on it. Needs only git and gh; nothing builds
on this machine.

With no option it opens a draft pull request for the branch if there is none, and follows the ci and
app runs each push to it starts: every job on all three runners. Each job is printed as it finishes.
The first failure ends the wait with that job's log; the runs carry on until the next push cancels them.

--class   a fully qualified test class, run through dev.yml in its own project
--os      the runner for --class: linux (default), windows, macos or all
--wait    wait for every job and print each failed one's log, rather than stopping at the first
--relock  regenerate the lock files of the pushed commit with the CI SDK, build nothing, and write
          the ones that changed into this tree to review and commit
USAGE
}

bad_usage() { echo "$1" >&2; usage >&2; exit 2; }
die() { echo "dev: $1" >&2; exit 1; }

class='' os='' relock=false wait_all=false
while [ "$#" -gt 0 ]; do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --relock) relock=true ;;
    --wait) wait_all=true ;;
    --class|--os)
      [ "$#" -ge 2 ] || bad_usage "$1 needs a value"
      case "$1" in
        --class) class="$2" ;;
        --os) os="$2" ;;
      esac
      shift
      ;;
    *) bad_usage "unknown argument: $1" ;;
  esac
  shift
done
[ -z "$os" ] || [ -n "$class" ] || bad_usage '--os chooses the runner for --class'
[ -z "$class" ] || [ "$relock" = false ] || bad_usage '--class and --relock are separate runs'
case "${os:-linux}" in
  linux) runner=ubuntu-24.04 ;;
  windows) runner=windows-2025 ;;
  macos) runner=macos-15 ;;
  all) runner=all ;;
  *) bad_usage "unknown --os ${os}" ;;
esac

branch="$(git symbolic-ref --short -q HEAD)" || die 'HEAD is detached; check out a branch'
[ "$branch" != main ] || die 'refusing to run on main; work on a branch'
[ -z "$(git status --porcelain)" ] || die 'the tree has uncommitted or untracked files; commit first; the runner tests the pushed commit'

start=$SECONDS
git push -u origin HEAD
sha="$(git rev-parse HEAD)"

runs=()
if [ -n "$class" ] || [ "$relock" = true ]; then
  nonce="$(date +%s)-$$"
  gh workflow run dev.yml --ref "$branch" -f filter_class="$class" -f os="$runner" -f nonce="$nonce" -f relock="$relock"
  found='' tries=0
  while [ -z "$found" ] && [ "$tries" -lt 30 ]; do
    sleep 3
    tries=$((tries + 1))
    found="$(gh run list --workflow dev.yml --branch "$branch" --event workflow_dispatch --limit 20 \
      --json databaseId,displayTitle,headSha \
      --jq ".[] | select(.displayTitle | endswith(\"$nonce\")) | \"\(.databaseId) \(.headSha)\"" || true)"
  done
  [ -n "$found" ] || die "no dev run carrying $nonce appeared within 90s"
  read -r id head <<< "$found"
  [ "$head" = "$sha" ] || die "run $id tests $head, not HEAD $sha"
  runs=("$id")
else
  pr="$(gh pr view "$branch" --json url,state --jq 'select(.state == "OPEN") | .url' 2>/dev/null || true)"
  if [ -n "$pr" ]; then echo "$pr"; else gh pr create --draft --base main --head "$branch" --fill-first; fi
  # By workflow file, since a run whose file does not parse is named after its path.
  for workflow in ci.yml app.yml; do
    found='' tries=0
    while [ -z "$found" ] && [ "$tries" -lt 40 ]; do
      sleep 3
      tries=$((tries + 1))
      found="$(gh run list --workflow "$workflow" --branch "$branch" --commit "$sha" --event pull_request --limit 1 \
        --json databaseId --jq '.[].databaseId' || true)"
    done
    [ -n "$found" ] || die "no $workflow run started for $sha within 2 minutes"
    runs+=("$found")
  done
fi
for id in "${runs[@]}"; do gh run view "$id" --json url --jq .url; done

# The end of a finished job's log, read while the run goes on, which gh run view refuses. Its colours and
# other control characters are dropped, and with them the timestamps and the runner's cleanup.
job_log() {
  local esc=$'\033' log='' tries=0
  # A job's log is published a few seconds after the job completes.
  while [ -z "$log" ] && [ "$tries" -lt 6 ]; do
    [ "$tries" -eq 0 ] || sleep 5
    tries=$((tries + 1))
    log="$(gh api --allow-escape-sequences "repos/{owner}/{repo}/actions/jobs/$1/logs" 2>/dev/null || true)"
  done
  printf '%s\n' "$log" \
    | sed "s/${esc}\[[0-9;]*[A-Za-z]//g" | LC_ALL=C tr -d '\000-\010\013-\037\177' \
    | sed -E 's/^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:.]+Z //' | sed '/^Post job cleanup\./,$d' | tail -n 150 || true
}

# Each poll prints the jobs that finished since the last; a job a condition skipped prints nothing.
seen=' ' failed=() finished=false
while [ "$finished" = false ]; do
  sleep 10
  finished=true
  for id in "${runs[@]}"; do
    report="$(gh run view "$id" --json status,conclusion,workflowName,jobs --jq '"\(.status) \(.conclusion) \(.jobs | length)", (.workflowName as $w | .jobs[]
      | select(.status == "completed" and .conclusion != "skipped")
      | "\(.databaseId) \(.conclusion) \(try ((.completedAt | fromdateiso8601) - (.startedAt | fromdateiso8601)) catch 0) \($w) \(.name)")')" \
      || { finished=false; continue; }
    read -r status conclusion jobs <<< "$(sed -n 1p <<< "$report")"
    [ "$status" = completed ] || finished=false
    # A run GitHub refused before any job started, such as one whose workflow file is broken.
    if [ "$status" = completed ] && [ "$jobs" = 0 ] && [ "$conclusion" != success ]; then
      case "$seen" in *" run$id "*) ;; *) seen="${seen}run$id "; failed+=("run:$id run $id ended $conclusion with no job") ;; esac
    fi
    while read -r job conclusion seconds workflow name; do
      [ -n "$job" ] || continue
      case "$seen" in *" $job "*) continue ;; esac
      seen="$seen$job "
      printf '%-4s %-9s %5ss  %s\n' "$workflow" "$conclusion" "$seconds" "$name"
      if [ "$conclusion" != success ]; then failed+=("$job $workflow $name"); fi
    done < <(sed 1d <<< "$report")
  done
  if [ "${#failed[@]}" -gt 0 ] && [ "$wait_all" = false ]; then break; fi
done

if [ "${#failed[@]}" -gt 0 ]; then
  for entry in "${failed[@]}"; do
    printf '\n==== %s ====\n' "${entry#* }"
    case "$entry" in
      run:*) id="${entry%% *}"; gh run view "${id#run:}" || true ;;
      *) job_log "${entry%% *}" ;;
    esac
  done
  echo "dev: ${failed[0]#* } failed after $((SECONDS - start))s. Exit code 8 from the test runner means the filter matched zero tests." >&2
  exit 1
fi

if [ "$relock" = true ]; then
  relocked="$(mktemp -d)"
  trap 'rm -rf "$relocked"' EXIT
  gh run download "${runs[0]}" -n relock -D "$relocked"
  [ -f "$relocked/changed.txt" ] || die "run ${runs[0]} uploaded no list of changed lock files"
  while IFS= read -r file; do cp "$relocked/$file" "$file"; done < "$relocked/changed.txt"
  if [ -s "$relocked/changed.txt" ]; then
    echo "regenerated in $((SECONDS - start))s; review and commit:"
    cat "$relocked/changed.txt"
  else
    echo "every lock file already matches its project"
  fi
  exit 0
fi

echo "green in $((SECONDS - start))s"
