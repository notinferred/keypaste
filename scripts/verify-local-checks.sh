#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
scratch=$(mktemp -d)
trap 'rm -f "$scratch/dotnet" "$scratch/commands" "$scratch/output" "$scratch/plan"; rmdir "$scratch"' EXIT
cat > "$scratch/dotnet" <<'FAKE'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$VERIFY_COMMAND_LOG"
if [ "${VERIFY_FAIL:-}" = "$1" ]; then exit 23; fi
FAKE
chmod +x "$scratch/dotnet"
export PATH="$scratch:$PATH"
export VERIFY_COMMAND_LOG="$scratch/commands"
unset VERIFY_FAIL
die() { echo "verification fixture: $*" >&2; exit 1; }

bash scripts/verify.sh --list > "$scratch/plan"
[ ! -e "$VERIFY_COMMAND_LOG" ] || die '--list executed dotnet'
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

bash scripts/verify.sh desktop > "$scratch/output"
[ "$(wc -l < "$VERIFY_COMMAND_LOG" | tr -d '[:space:]')" = 8 ] || die 'desktop must prepare and test both targets'
grep -F 'test tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj --no-build -c Release' "$VERIFY_COMMAND_LOG" >/dev/null || die 'consistency tests were not executed'

: > "$VERIFY_COMMAND_LOG"
if VERIFY_FAIL=build bash scripts/verify.sh backend > "$scratch/output" 2>&1; then
  die 'a failed build passed verification'
else
  code=$?
  [ "$code" = 23 ] || die "build exit status was lost: $code"
fi
if grep -E '^test ' "$VERIFY_COMMAND_LOG" >/dev/null; then die 'tests ran after a failed build'; fi

: > "$VERIFY_COMMAND_LOG"
if VERIFY_FAIL='test' bash scripts/verify.sh desktop --test-only > "$scratch/output" 2>&1; then
  die 'failed tests passed verification'
fi
[ "$(wc -l < "$VERIFY_COMMAND_LOG" | tr -d '[:space:]')" = 1 ] || die 'test-only built or continued after failure'

for bad in 'unknown' 'backend desktop' 'all --prepare-only'; do
  # Word splitting creates the malformed argument lists under test.
  # shellcheck disable=SC2086
  if bash scripts/verify.sh $bad > "$scratch/output" 2>&1; then die "accepted invalid options: $bad"; fi
done
echo 'verification fixtures passed: complete target selection, dry run, failure propagation and option validation'
