#!/usr/bin/env bash
# Holds scripts/lane-cache.sh to skipping only lanes a trusted run already passed on the same inputs.
#
# lane-cache.sh decides which tests a push does not repeat, so a marker it wrongly honours is a test
# that silently stops running. This drives it against a fake `gh` and a fake `git` on PATH, the
# technique verify-green-gates.sh uses: no token, no network.
#
# What it holds:
#   - a trusted marker on an identical tree satisfies every lane it names;
#   - a marker whose commit differs only in paths no lane reads, or in paths of other lanes, satisfies
#     the lanes those paths leave alone, and only those;
#   - a path no lane claims satisfies nothing;
#   - markers from a pull request, a fork, a push to another branch or an expired artifact are never
#     read, and a failed call prints nothing, which runs everything.
#
# Usage:
#   verify-lane-cache.sh
#
# NEGATIVE CONTROL: a copy whose trust check accepts any event must honour the pull request's marker.
# If it does not, the refusal above came from somewhere else and proves nothing.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

readonly SUBJECT="${KEYPASTE_LANE_CACHE:-scripts/lane-cache.sh}"
readonly REPO='notinferred/keypaste'

die() { echo "::error::$*" >&2; exit 1; }

[ -f "$SUBJECT" ] || die "no script to check: $SUBJECT"
command -v jq >/dev/null 2>&1 || die "no jq; the fake gh applies --jq with it"
REAL_GIT="$(command -v git)" || die "no git"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/shim" "$WORK/runs" "$WORK/diffs"

marker() { printf '{"name":"%s","expired":%s,"workflow_run":{"id":%s,"head_sha":"%s"}}' "$3" "$4" "$1" "$2"; }
run() { printf '{"event":"%s","path":".github/workflows/%s","head_branch":"%s","head_repository":{"full_name":"%s"}}' "$2" "$3" "$4" "$5" > "$WORK/runs/$1.json"; }

{
  printf '{"artifacts":['
  marker 101 aaaa 'pass--ubuntu-24.04--core+cli+integration' false; printf ','
  marker 102 bbbb 'pass--windows-2025--core' false; printf ','
  marker 103 cccc 'pass--macos-15--core+cli' false; printf ','
  marker 104 dddd 'pass--ubuntu-24.04--compat' false; printf ','
  marker 105 eeee 'pass--windows-2025--compat' false; printf ','
  marker 106 ffff 'pass--macos-15--compat' false; printf ','
  marker 107 gggg 'pass--ubuntu-22.04--aot' false; printf ','
  marker 108 hhhh 'pass--ubuntu-24.04--scripts' true; printf ','
  marker 109 iiii 'packages-linux-x64' false
  printf ']}'
} > "$WORK/artifacts.json"

run 101 workflow_dispatch dev.yml ci-cache "$REPO"
run 102 push ci.yml main "$REPO"
run 103 workflow_dispatch ci.yml ci-cache "$REPO"
run 104 pull_request ci.yml ci-cache "$REPO"
run 105 workflow_dispatch dev.yml main 'someone/keypaste'
run 106 push ci.yml feature "$REPO"
run 107 schedule ci.yml main "$REPO"
run 108 workflow_dispatch dev.yml ci-cache "$REPO"

# What each marker's commit differs from the commit under test by.
: > "$WORK/diffs/aaaa"
printf 'docs/STEPS.md\n' > "$WORK/diffs/bbbb"
printf 'tests/Keypaste.Cli.Tests/New.cs\n' > "$WORK/diffs/cccc"
: > "$WORK/diffs/dddd"
: > "$WORK/diffs/eeee"
: > "$WORK/diffs/ffff"
printf 'tools/new.cs\n' > "$WORK/diffs/gggg"
: > "$WORK/diffs/hhhh"

cat > "$WORK/shim/gh" <<'FAKE'
#!/usr/bin/env bash
set -euo pipefail
[ "${1:-}" = api ] || { echo "fake gh: only 'api' is implemented" >&2; exit 64; }
[ "${LANE_FIXTURE_SCENARIO:-ok}" != api-fails ] || exit 1
url="$2" filter=''
[ "${3:-}" = --jq ] && filter="$4"
case "$url" in
  */actions/artifacts\?*page=1) body="$(cat "$LANE_FIXTURE/artifacts.json")" ;;
  */actions/artifacts\?*) body='{"artifacts":[]}' ;;
  */actions/runs/*) body="$(cat "$LANE_FIXTURE/runs/${url##*/}.json")" ;;
  *) echo "fake gh: no answer for $url" >&2; exit 65 ;;
esac
if [ -n "$filter" ]; then printf '%s' "$body" | jq -r "$filter"; else printf '%s' "$body"; fi
FAKE

cat > "$WORK/shim/git" <<'FAKE'
#!/usr/bin/env bash
set -euo pipefail
args=("$@")
for ((i = 0; i < ${#args[@]}; i++)); do
  case "${args[$i]}" in
    cat-file)
      sha="${args[$((i + 2))]%^\{commit\}}"
      [ -f "$LANE_FIXTURE/diffs/$sha" ]; exit ;;
    fetch) exit 1 ;;
    diff)
      for ((j = i + 1; j < ${#args[@]}; j++)); do
        case "${args[$j]}" in -*) ;; *) cat "$LANE_FIXTURE/diffs/${args[$j]}"; exit 0 ;; esac
      done ;;
  esac
done
exec "$LANE_REAL_GIT" "$@"
FAKE
chmod +x "$WORK/shim/gh" "$WORK/shim/git"

ask() {
  local subject="$1" scenario="$2" repo="${3-$REPO}"
  PATH="$WORK/shim:$PATH" LANE_FIXTURE="$WORK" LANE_REAL_GIT="$REAL_GIT" LANE_FIXTURE_SCENARIO="$scenario" \
    GITHUB_REPOSITORY="$repo" bash "$subject" HEADSHA
}

cases=0
expect() {
  local name="$1" got="$2" want="$3"
  cases=$((cases + 1))
  [ "$got" = "$want" ] || { printf 'expected:\n%s\ngot:\n%s\n' "$want" "$got" >&2; die "$name"; }
  printf '  ok  %s\n' "$name"
}

want="$(printf '%s\n' macos-15:core ubuntu-24.04:cli ubuntu-24.04:core ubuntu-24.04:integration windows-2025:core)"
expect "trusted markers on unchanged inputs, and nothing else" "$(ask "$SUBJECT" ok)" "$want"
expect "a failed call prints nothing" "$(ask "$SUBJECT" api-fails)" ''
expect "no repository prints nothing" "$(ask "$SUBJECT" ok '')" ''

WEAK="$WORK/weakened.sh"
sed 's/^    \*) continue ;;$/    *) ;;/' "$SUBJECT" > "$WEAK"
cmp -s "$SUBJECT" "$WEAK" && die "the weakened copy is the subject; the control would be vacuous"
grep -qx 'ubuntu-24.04:compat' <<<"$(ask "$WEAK" ok)" \
  || die "the copy trusting every event still ignored the pull request's marker, so the trust check proves nothing"
cases=$((cases + 1))
echo "  ok  the copy trusting every event honours the pull request's marker"

echo "ok: $cases cases. Trusted markers on unchanged inputs satisfy their lanes; changed inputs, an unclaimed path, a pull request, a fork, another branch's push, an expired artifact and a failed call satisfy nothing."
