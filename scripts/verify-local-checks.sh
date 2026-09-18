#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
die() { echo "verification fixture: $*" >&2; exit 1; }

# The subject runs from a scratch root so every tool it starts, bash included, is a recording fake.
root="$scratch/root"
fakes="$scratch/fakes"
mkdir -p "$root/scripts" "$fakes"
cp "${VERIFY_SUBJECT:-scripts/verify.sh}" "$root/scripts/verify.sh"
cat > "$fakes/dotnet" <<'FAKE'
#!/bin/sh
tool=$(basename "$0")
if [ "$tool" = timeout ] && [ "${1:-}" = --version ]; then echo 'timeout (GNU coreutils) fake'; exit 0; fi
if [ "$tool" = uname ]; then echo "$VERIFY_FAKE_UNAME"; exit 0; fi
if [ "$tool" = git ]; then
  [ -z "${VERIFY_FAKE_GIT_FAILS:-}" ] || exit 128
  case "$*" in *' diff '*) echo 'src/Keypaste.App/tracked.cs' ;; *ls-files*) echo 'launch.md' ;; esac
  exit 0
fi
printf '%s %s\n' "$tool" "$*" >> "$VERIFY_COMMAND_LOG"
echo "ran: $tool $*"
if [ "${VERIFY_FAIL:-}" = "${1:-}" ]; then exit 23; fi
if [ -n "${VERIFY_FAIL_MATCH:-}" ] && printf '%s %s\n' "$tool" "$*" | grep -E -- "$VERIFY_FAIL_MATCH" >/dev/null; then exit 23; fi
FAKE
chmod +x "$fakes/dotnet"
for tool in docker timeout bash; do cp "$fakes/dotnet" "$fakes/$tool"; done
export PATH="$fakes:$PATH"
export VERIFY_COMMAND_LOG="$scratch/commands"
export VERIFY_LOG_DIR="$scratch/logs"
export VERIFY_SCRIPTS_NATIVE=1
unset VERIFY_FAIL VERIFY_FAIL_MATCH VERIFY_CHANGED_PATHS

verify() { "$BASH" "$root/scripts/verify.sh" "$@"; }
ran() { grep -F -- "$1" "$VERIFY_COMMAND_LOG" >/dev/null 2>&1; }
selected() { grep -E '^verify: run ' "$scratch/plan" | awk '{ printf "%s ", $3 }'; }

verify --all --list > "$scratch/plan"
[ ! -e "$VERIFY_COMMAND_LOG" ] || die '--list executed a command'
for target in keypaste.slnx keypaste.app.slnx tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj; do
  grep -F "dotnet test $target --no-build -c Release" "$scratch/plan" >/dev/null || die "all omits $target tests"
done
grep -F 'bash scripts/verify-demo.sh' "$scratch/plan" >/dev/null || die 'all omits process/document checks'
grep -F 'bash scripts/probe-results.sh --selftest' "$scratch/plan" >/dev/null || die 'all omits diagnostic fixtures'
grep -F 'bash scripts/observe-minimize-lock.sh --selftest' "$scratch/plan" >/dev/null || die 'all omits the minimize-lock classifier fixtures'
grep -F 'bash scripts/verify-desktop-candidate.sh --selftest' "$scratch/plan" >/dev/null || die 'all omits the desktop candidate refusals'
grep -F 'bash scripts/exercise-desktop-install.sh --selftest' "$scratch/plan" >/dev/null || die 'all omits the desktop installation classifier fixtures'
grep -F 'bash scripts/exercise-desktop-upgrade.sh --selftest' "$scratch/plan" >/dev/null || die 'all omits the desktop upgrade classifier fixtures'
grep -F 'bash scripts/probe-msi-interruption.sh --selftest' "$scratch/plan" >/dev/null || die 'all omits the interruption probe reader fixtures'
grep -F 'actionlint' "$scratch/plan" >/dev/null || die 'all omits the workflow checks'
verify all --list > "$scratch/plan-positional"
cmp -s "$scratch/plan" "$scratch/plan-positional" || die 'all and --all select differently'

verify desktop > "$scratch/output"
[ "$(grep -c '^dotnet ' "$VERIFY_COMMAND_LOG")" = 8 ] || die 'desktop must prepare and test both targets'
ran 'dotnet test tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj --no-build -c Release' || die 'consistency tests were not executed'

: > "$VERIFY_COMMAND_LOG"
if VERIFY_FAIL=build verify backend > "$scratch/output" 2>&1; then
  die 'a failed build passed verification'
else
  code=$?
  [ "$code" = 23 ] || die "build exit status was lost: $code"
fi
if ran 'dotnet test '; then die 'tests ran after a failed build'; fi

: > "$VERIFY_COMMAND_LOG"
if VERIFY_FAIL='test' verify desktop --test-only > "$scratch/output" 2>&1; then
  die 'failed tests passed verification'
fi
[ "$(wc -l < "$VERIFY_COMMAND_LOG" | tr -d '[:space:]')" = 1 ] || die 'test-only built or continued after failure'

# Each changed path selects the profiles whose inputs it is; workflows and records always run.
expect_selection() {
  local paths="$1" expected="$2"
  VERIFY_CHANGED_PATHS="$paths" verify --list > "$scratch/plan"
  [ "$(selected)" = "$expected " ] || die "changed '$paths' selected '$(selected)', expected '$expected'"
}
expect_selection '' 'workflows records'
expect_selection 'docs/STEPS.md' 'workflows records'
expect_selection 'src/Keypaste.App/Views/EntriesView.axaml' 'workflows records desktop'
expect_selection 'tests/Keypaste.Consistency.Tests/A.cs' 'workflows records desktop'
expect_selection 'src/Keypaste.Core/SecretInput.cs' 'workflows backend integration desktop'
expect_selection 'tests/Keypaste.Cli.Tests/A.cs' 'workflows backend'
expect_selection 'scripts/publish-release.sh' 'workflows scripts backend integration'
expect_selection '.github/workflows/ci.yml' 'workflows scripts backend'
expect_selection 'README.md' 'workflows records scripts integration'
expect_selection 'docs/demo.md' 'workflows records integration'
expect_selection $'docs/STEPS.md\nsrc/Keypaste.App/App.axaml.cs\nCHANGELOG.md' 'workflows records scripts desktop'
expect_selection 'a-new-directory/file' 'workflows scripts backend integration desktop'
grep -E '^verify: run +scripts +a-new-directory/file, which no profile claims' "$scratch/plan" >/dev/null || die 'an unmapped path was not named as the reason everything ran'

VERIFY_CHANGED_PATHS='src/Keypaste.App/A.cs' verify --list > "$scratch/plan"
grep -E '^verify: skip +scripts +no changed path maps to it' "$scratch/plan" >/dev/null || die 'a skipped profile was not logged with its reason'
grep -E '^verify: skip +compat ' "$scratch/plan" >/dev/null || die 'compat was dropped without a logged reason'
grep -E '^verify: run +desktop +src/Keypaste.App/A.cs' "$scratch/plan" >/dev/null || die 'a selected profile does not name the path that selected it'
if grep -F 'dotnet test keypaste.slnx' "$scratch/plan" >/dev/null; then die 'a desktop change planned the backend tests'; fi
VERIFY_CHANGED_PATHS='src/Keypaste.Core/A.cs' verify --list > "$scratch/plan"
grep -E '^verify: skip +records +backend runs the same row checks' "$scratch/plan" >/dev/null || die 'records ran beside the backend suite that contains it'
VERIFY_CHANGED_PATHS='' verify --all --list > "$scratch/plan"
[ "$(selected)" = 'workflows scripts backend integration desktop ' ] || die "--all selected '$(selected)'"

# Without the override the selection reads tracked changes and untracked files from git.
cp "$fakes/dotnet" "$fakes/git"
verify --list > "$scratch/plan"
[ "$(selected)" = 'workflows records integration desktop ' ] || die "git diff plus untracked selected '$(selected)'"
VERIFY_FAKE_GIT_FAILS=1 verify --list > "$scratch/plan"
[ "$(selected)" = 'workflows scripts backend integration desktop ' ] || die "an unreadable working tree selected '$(selected)'"
rm "$fakes/git"

# --from resumes at a profile, and a resumed integration prepares the backend it no longer follows.
VERIFY_CHANGED_PATHS='src/Keypaste.Core/A.cs' verify --from integration --list > "$scratch/plan"
[ "$(selected)" = 'integration desktop ' ] || die "--from integration selected '$(selected)'"
grep -E '^verify: skip +backend +before --from integration' "$scratch/plan" >/dev/null || die '--from skipped a profile without saying so'
grep -F 'dotnet build keypaste.slnx' "$scratch/plan" >/dev/null || die 'integration resumed without a prepared backend'
VERIFY_CHANGED_PATHS='src/Keypaste.Core/A.cs' verify --list > "$scratch/plan"
[ "$(grep -c -F 'dotnet build keypaste.slnx' "$scratch/plan")" = 1 ] || die 'integration rebuilt the backend that had just been prepared'
: > "$VERIFY_COMMAND_LOG"
verify --all --from desktop > "$scratch/output" 2>&1 || die "--from desktop failed: $(cat "$scratch/output")"
[ "$(grep -c '^dotnet ' "$VERIFY_COMMAND_LOG")" = 8 ] || die '--from desktop did not run exactly the desktop commands'
if ran 'keypaste.slnx' || ran 'docker ' || ran 'timeout '; then die '--from desktop ran an earlier profile'; fi

# A passing run holds its output; a failing run names every failed profile and how to resume.
: > "$VERIFY_COMMAND_LOG"
verify --all > "$scratch/output" 2>&1 || die "a passing full run failed: $(cat "$scratch/output")"
grep -E '^Verification passed: workflows scripts backend integration desktop ' "$scratch/output" >/dev/null || die 'a passing run did not name what it ran'
if grep -F 'ran: ' "$scratch/output" >/dev/null; then die 'a passing run printed held output'; fi
grep -F 'ran: dotnet test keypaste.slnx' "$VERIFY_LOG_DIR/backend.log" >/dev/null || die 'held output was not kept'
ran 'bash scripts/verify-release-matrix.sh' || die 'the scripts profile did not run its selftests'
ran 'timeout --verbose --kill-after=10s 8m bash scripts/verify-demo.sh' || die 'integration lost its deadline'

: > "$VERIFY_COMMAND_LOG"
if VERIFY_FAIL_MATCH='format keypaste.app.slnx|verify-green-gates|verify-provenance' verify --all > "$scratch/output" 2>&1; then
  die 'a run with two failed profiles passed'
else
  code=$?
  [ "$code" = 23 ] || die "the failing exit status was lost: $code"
fi
grep -x 'Verification failed: scripts desktop' "$scratch/output" >/dev/null || die "failed profiles were not all named: $(tail -5 "$scratch/output")"
grep -x 'Fix, then resume with: bash scripts/verify.sh --all --from scripts' "$scratch/output" >/dev/null || die 'no resume command for the first failed profile'
grep -F 'failed selftest: scripts/verify-green-gates.sh (exit 23)' "$scratch/output" >/dev/null || die 'a failed selftest was not named'
grep -F 'failed selftest: scripts/verify-provenance.sh --selftest (exit 23)' "$scratch/output" >/dev/null || die 'a second failed selftest was not named'
grep -F 'ran: dotnet format keypaste.app.slnx' "$scratch/output" >/dev/null || die 'the failed profile held its output back'
if grep -F 'ran: dotnet test keypaste.slnx' "$scratch/output" >/dev/null; then die 'a passing profile printed beside the failures'; fi
ran 'dotnet test keypaste.slnx' || die 'one failure stopped the independent profiles'
if ran 'dotnet test keypaste.app.slnx'; then die 'desktop tests ran after its format check failed'; fi

: > "$VERIFY_COMMAND_LOG"
if VERIFY_CHANGED_PATHS='src/Keypaste.Core/A.cs' VERIFY_FAIL_MATCH='build keypaste.slnx' verify > "$scratch/output" 2>&1; then
  die 'a failed backend build passed'
fi
grep -x 'Verification failed: backend' "$scratch/output" >/dev/null || die 'the failed backend was not named'
grep -x 'Not run: integration' "$scratch/output" >/dev/null || die 'integration was not reported as waiting on the backend'
grep -x 'Fix, then resume with: bash scripts/verify.sh --from backend' "$scratch/output" >/dev/null || die 'an affected-only failure resumes with the wrong command'
if ran 'timeout '; then die 'integration ran against a backend that did not build'; fi
ran 'dotnet test keypaste.app.slnx' || die 'a failed backend stopped the desktop profile'

# On Windows the fixtures run in a Linux container, unless the checkout is a linked worktree.
: > "$VERIFY_COMMAND_LOG"
cp "$fakes/dotnet" "$fakes/uname"
mkdir "$root/.git"
VERIFY_FAKE_UNAME=MINGW64_NT-10.0 VERIFY_SCRIPTS_NATIVE='' verify scripts > "$scratch/output" 2>&1 || die "the container route failed: $(cat "$scratch/output")"
ran 'keypaste-verify-scripts bash scripts/verify.sh scripts' || die 'Windows did not send the fixtures to the container'
if ran 'bash scripts/verify-release-matrix.sh'; then die 'Windows ran the fixtures twice'; fi
rmdir "$root/.git"
: > "$VERIFY_COMMAND_LOG"
VERIFY_FAKE_UNAME=MINGW64_NT-10.0 VERIFY_SCRIPTS_NATIVE='' verify scripts > "$scratch/output" 2>&1
ran 'bash scripts/verify-release-matrix.sh' || die 'a linked worktree lost its fixtures instead of running them natively'
rm "$fakes/uname"

for bad in 'unknown' 'backend desktop' 'all --prepare-only' '--prepare-only' '--from' '--from nowhere' '--from compat' \
  '--from backend --from desktop' 'desktop --from backend' 'desktop --all' '--test-only --prepare-only backend'; do
  code=0
  # Word splitting creates the malformed argument lists under test.
  # shellcheck disable=SC2086
  verify $bad > "$scratch/output" 2>&1 || code=$?
  [ "$code" = 2 ] || die "invalid options were not refused as usage: '$bad' exited $code"
done
echo 'verification fixtures passed: path selection, dry run, resume, held output, failure reporting and option validation'
