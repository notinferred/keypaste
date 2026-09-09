#!/usr/bin/env bash
# Holds scripts/require-green-gates.sh to refusing a commit whose gates are not all green.
#
# The check it drives runs in release.yml's guard under `if: ref_type == 'tag'`, so nothing but a
# real tag executes it there - and a tag is the worst possible place to discover that a guard does
# not guard. This drives the same script against a fake `gh` on PATH, the technique
# verify-release-destination.sh uses with a fake `aws` and verify-install.sh with a fake `curl`.
# No token, no network, no repository is involved.
#
# What it holds:
#   - a commit with every required gate green is accepted, and it is the ONLY shape that is;
#   - a missing app run, a red app run and a commit with neither run each refuse;
#   - the required set is read from release-targets.json, so a component with its own gate is
#     required without an edit here, and the release workflow never requires itself;
#   - an empty or malformed reply refuses rather than counting as zero and passing something.
#
# A RED RUN AND AN ABSENT ONE ARE ONE ANSWER. The query asks only for successful runs, so a failed
# run is not in the reply at all. Both cases are still driven, because they arrive by different
# routes - one is a gate that ran and failed, the other a paths filter that skipped it - and a
# future edit that tried to tell them apart would have to break one of these two.
#
# Usage:
#   verify-green-gates.sh
#
# NEGATIVE CONTROL: the last phase runs a copy of the script with `app` removed from the required
# set - the shape the guard had before R.0a - and requires it to ACCEPT the commit whose app run is
# missing. If the weakened one refuses too, this gate proves nothing about the fix (D-0043).
set -euo pipefail

readonly SUBJECT="${KEYPASTE_REQUIRE_GATES:-scripts/require-green-gates.sh}"
readonly SHA='0123456789abcdef0123456789abcdef01234567'
readonly REPO='notinferred/keypaste-fixture'

die() { echo "::error::$*" >&2; exit 1; }

[ -f "$SUBJECT" ] || die "no script to check: $SUBJECT"
command -v jq >/dev/null 2>&1 || die "no jq; this gate reads a JSON reply and cannot run without it"

WORK="$(mktemp -d)"
trap 'chmod -R u+w "$WORK" 2>/dev/null || true; rm -rf "$WORK"' EXIT

readonly SHIM="$WORK/shim"
readonly OUT="$WORK/out.txt"
mkdir -p "$SHIM"

# The fake honours `status=success` the way the API does. Getting that wrong would let the "red"
# scenario answer with a failed run present, and the subject would then be judged against a reply
# the real API never sends.
cat > "$SHIM/gh" <<'FAKE'
#!/usr/bin/env bash
set -euo pipefail
[ "${1:-}" = "api" ] || { echo "fake gh: only 'api' is implemented" >&2; exit 64; }
url="${2:-}"
case "$KEYPASTE_FIXTURE_SCENARIO" in
  api-fails)  exit 1 ;;
  api-empty)  printf '' ; exit 0 ;;
  api-garbage) printf 'not json at all\n' ; exit 0 ;;
esac
runs="$KEYPASTE_FIXTURE_RUNS"
case "$url" in
  *status=success*) runs="$(printf '%s' "$runs" | jq -c '[.[] | select(.conclusion == "success")]')" ;;
esac
printf '%s' "$runs" | jq -c '{workflow_runs: [.[] | {name, conclusion}]}'
FAKE
chmod +x "$SHIM/gh"

export PATH="$SHIM:$PATH"
[ "$(command -v gh)" = "$SHIM/gh" ] \
  || { echo "::error::the fake gh is not first on PATH; this fixture would be asking the real one" >&2; exit 90; }

cases_run=0
accepted=0

# run_case <name> <scenario> <runs-json> <subject> <expect-rc> <expect-in-output>
run_case() {
  local name="$1" scenario="$2" runs="$3" subject="$4" want_rc="$5" want_text="$6" rc=0
  cases_run=$((cases_run + 1))
  (
    export GITHUB_REPOSITORY="$REPO"
    export KEYPASTE_FIXTURE_SCENARIO="$scenario"
    export KEYPASTE_FIXTURE_RUNS="$runs"
    bash "$subject" "$SHA"
  ) >"$OUT" 2>&1 || rc=$?
  [ "$rc" -eq 0 ] && accepted=$((accepted + 1))
  [ "$rc" -eq "$want_rc" ] \
    || { sed -n '1,20p' "$OUT" >&2; die "$name: exit $rc, expected $want_rc"; }
  grep -qF "$want_text" "$OUT" \
    || { sed -n '1,20p' "$OUT" >&2; die "$name: exited $rc but never said '$want_text'"; }
  printf '  %-14s %-16s exit %s\n' "$( [ "$rc" -eq 0 ] && echo accepted || echo refused )" "$name" "$rc"
}

readonly BOTH_GREEN='[{"name":"ci","conclusion":"success"},{"name":"app","conclusion":"success"}]'
readonly APP_ABSENT='[{"name":"ci","conclusion":"success"}]'
readonly APP_RED='[{"name":"ci","conclusion":"success"},{"name":"app","conclusion":"failure"}]'
readonly BOTH_ABSENT='[]'

echo "== the four shapes a tag can arrive in"
run_case "both-green"  ok "$BOTH_GREEN"  "$SUBJECT" 0 "every required gate is green"
run_case "app-absent"  ok "$APP_ABSENT"  "$SUBJECT" 1 "no successful app run"
run_case "app-red"     ok "$APP_RED"     "$SUBJECT" 1 "no successful app run"
run_case "both-absent" ok "$BOTH_ABSENT" "$SUBJECT" 1 "no successful ci run"

echo "== a reply that cannot be counted refuses rather than counting as zero"
run_case "api-fails"   api-fails   "$BOTH_GREEN" "$SUBJECT" 1 "could not ask"
run_case "api-empty"   api-empty   "$BOTH_GREEN" "$SUBJECT" 1 "refusing without a verified answer"
run_case "api-garbage" api-garbage "$BOTH_GREEN" "$SUBJECT" 1 "refusing without a verified answer"

echo "== negative control"
WEAK="$WORK/weakened.sh"
# Deleting the one line that reads the definition leaves `echo ci` behind, which is exactly the
# required set the guard had before R.0a. Asserted rather than assumed: a copy that still derived
# the set would make this control vacuous and the gate would pass while proving nothing.
sed '/jq -r --arg self/d' "$SUBJECT" > "$WEAK"
grep -q 'jq -r --arg self' "$WEAK" \
  && die "the weakened copy still derives the required set; the control would be vacuous"
run_case "weakened-accepts-app-absent" ok "$APP_ABSENT" "$WEAK" 0 "every required gate is green"
echo "  the weakened copy accepts a commit with no app run, so requiring app is what refuses it"

[ "$cases_run" -ge 8 ] || die "only $cases_run cases ran; the scenario table has lost rows"
[ "$accepted" -eq 2 ] \
  || die "$accepted cases were accepted, expected exactly 2 (both-green, and the weakened control)"

cat <<EOF
ok: $cases_run cases. One shape was accepted by the real script - every required gate green - and a
    missing app run, a red one, a commit with neither, a failed call, an empty reply and an
    unparseable one all refused. The pre-R.0a shape, same fixture and same fake, accepts the commit
    whose desktop gate never ran.
not proved here: that the guard step is reached on a tag, which only a tag shows; and that GitHub
    returns runs in the shape this fake does, which R.0c's first real tag observes.
EOF
