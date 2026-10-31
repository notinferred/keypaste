#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

usage() {
  cat <<'USAGE'
Usage: bash scripts/dev.sh [--tools] [--wait]
       bash scripts/dev.sh --profile P [--os O] [--wait]
       bash scripts/dev.sh --acts APP_RUN_ID [--os O] [--wait]

Pushes the current branch and follows GitHub's verdict on it. Needs only git and gh; nothing builds
on this machine.

With no option it dispatches ci.yml and app.yml on the branch and follows their runs: every job on all
three runners. No pull request is opened. Each job is printed as it finishes.
The first failure ends the wait with that job's log; the runs carry on until the next push cancels them.

--tools   also run ci.yml's tools jobs, which install the real AI tools on every runner and connect keypaste
          to them
--profile one verify.sh profile, backend, integration, desktop or desktop-gates, run through dev.yml, to retry a
          failure only one runner showed
--acts    install a successful app.yml run's packages, built at any commit, through install-desktop.yml, and do
          the install exercise and R.1a's acts with this branch's scripts; never evidence
--os      the runner for --profile or --acts: linux (default), windows, macos or all
--wait    wait for every job and print each failed one's log, rather than stopping at the first
USAGE
}

bad_usage() { echo "$1" >&2; usage >&2; exit 2; }
die() { echo "dev: $1" >&2; exit 1; }

profile='' os='' acts='' wait_all=false tools=false
while [ "$#" -gt 0 ]; do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --tools) tools=true ;;
    --wait) wait_all=true ;;
    --profile|--os|--acts)
      [ "$#" -ge 2 ] || bad_usage "$1 needs a value"
      case "$1" in
        --profile) profile="$2" ;;
        --os) os="$2" ;;
        --acts) acts="$2" ;;
      esac
      shift
      ;;
    *) bad_usage "unknown argument: $1" ;;
  esac
  shift
done
[ -z "$os" ] || [ -n "$profile" ] || [ -n "$acts" ] || bad_usage '--os chooses the runner for --profile or --acts'
[ -z "$acts" ] || { [ -z "$profile" ] && [ "$tools" = false ]; } \
  || bad_usage '--acts is a run of its own; it cannot accompany --profile or --tools'
case "$acts" in *[!0-9]*) bad_usage "--acts takes an app.yml run ID, not $acts" ;; esac
[ -z "$profile" ] || [ "$tools" = false ] || bad_usage '--profile is a run of its own; it cannot accompany --tools'
case "$profile" in ''|backend|integration|desktop|desktop-gates) ;; *) bad_usage "unknown --profile $profile" ;; esac
case "${os:-linux}" in
  linux) runner=ubuntu-24.04 ;;
  windows) runner=windows-2025 ;;
  macos) runner=macos-15 ;;
  all) runner=all ;;
  *) bad_usage "unknown --os ${os}" ;;
esac
mode=''
[ -z "$profile" ] || mode=profile
[ -z "$acts" ] || mode=acts

branch="$(git symbolic-ref --short -q HEAD)" || die 'HEAD is detached; check out a branch'
[ "$branch" != main ] || die 'refusing to run on main; work on a branch'
[ -z "$(git status --porcelain)" ] || die 'the tree has uncommitted or untracked files; commit first; the runner tests the pushed commit'

start=$SECONDS
git push -u origin HEAD
sha="$(git rev-parse HEAD)"

runs=()
if [ -n "$mode" ]; then
  nonce="$(date +%s)-$$"
  # Only acts mode names app_run, so a branch whose dev.yml predates it still takes the other modes.
  acts_input=()
  [ -z "$acts" ] || acts_input=(-f app_run="$acts")
  gh workflow run dev.yml --ref "$branch" -f mode="$mode" -f profile="$profile" -f os="$runner" -f nonce="$nonce" \
    ${acts_input[@]+"${acts_input[@]}"}
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
  # By workflow file, since a run whose file does not parse is named after its path.
  for workflow in ci.yml app.yml; do
    before="$(gh run list --workflow "$workflow" --branch "$branch" --event workflow_dispatch --limit 1 --json databaseId --jq '.[].databaseId' || true)"
    if [ "$workflow" = ci.yml ] && [ "$tools" = true ]; then
      gh workflow run "$workflow" --ref "$branch" -f tools=true > /dev/null
    else
      gh workflow run "$workflow" --ref "$branch" > /dev/null
    fi
    found='' tries=0
    while [ -z "$found" ] && [ "$tries" -lt 40 ]; do
      sleep 3
      tries=$((tries + 1))
      found="$(gh run list --workflow "$workflow" --branch "$branch" --commit "$sha" --event workflow_dispatch --limit 1 \
        --json databaseId --jq ".[] | select(.databaseId != ${before:-0}) | .databaseId" || true)"
    done
    [ -n "$found" ] || die "no $workflow run started for $sha within 2 minutes"
    runs+=("$found")
  done
fi
for id in "${runs[@]}"; do gh run view "$id" --json url --jq .url; done
[ -z "$acts" ] || echo "acts mode installs app run $acts's packages, whatever commit built them; each job's summary and its acts-dev-<rid> artifact hold the verdicts, which are never evidence"

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
    report="$(gh run view "$id" --json status,conclusion,workflowName,jobs --jq '"\(.status) \(.conclusion)", (.workflowName as $w | .jobs[]
      | select(.status == "completed" and .conclusion != "skipped")
      | "\(.databaseId) \(.conclusion) \(try ((.completedAt | fromdateiso8601) - (.startedAt | fromdateiso8601)) catch 0) \($w) \(.name)")')" \
      || { finished=false; continue; }
    read -r status conclusion <<< "$(sed -n 1p <<< "$report")"
    [ "$status" = completed ] || finished=false
    # A run GitHub failed with no failed job: a broken workflow file, or a job it never created.
    if [ "$status" = completed ] && [ "$conclusion" != success ] && [ -z "$(sed 1d <<< "$report" | awk '$2 != "success"')" ]; then
      case "$seen" in *" run$id "*) ;; *) seen="${seen}run$id "; failed+=("run:$id run $id ended $conclusion with no failed job") ;; esac
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
  echo "dev: ${failed[0]#* } failed after $((SECONDS - start))s." >&2
  exit 1
fi

echo "green in $((SECONDS - start))s"
