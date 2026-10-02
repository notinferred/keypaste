#!/usr/bin/env bash
# Holds scripts/verify.sh --plan to selecting the CI lanes a change needs.
#
# The plan decides which jobs a push runs, so a lane it drops is a check that silently stops
# running. Each case feeds the planner a changed path through VERIFY_CHANGED_PATHS and compares
# every line it would append to GITHUB_OUTPUT. The graph is read from this checkout's project
# files, so a linked source file or a new reference is exercised as it really is.
#
# What it holds:
#   - documents nothing reads select no lane, and pages and workflows select only the Linux lanes
#     that read them;
#   - a test project's file selects that project alone, and a file another project compiles in
#     selects that project too;
#   - a helper a test starts at run time selects the test that starts it;
#   - the gates' shared library selects every lane whose gates source it;
#   - a build input, an unknown path, an empty change list and an unreadable range select everything,
#     and an unreadable range says git failed rather than passing for an empty one;
#   - each test runner, and each app.yml job, keeps only the runners an earlier trusted run has not
#     already passed, and a desktop lane already passed leaves no desktop tests.
#
# Usage:
#   verify-ci-scope.sh
#
# NEGATIVE CONTROL: the last phase plans with linked files ignored (VERIFY_SCOPE_NO_LINKS=1) and
# requires the CLI harness the consistency tests compile in to LOSE desktop, and the word list Core
# embeds to lose everything but core. If either keeps its closure, the linked-file case above passes
# for some other reason and proves nothing.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

readonly SELF="scripts/verify-ci-scope.sh"
declared_cases() {
  local n
  n="$(grep -cE '^[[:space:]]*run_(matrix_|app_)?case ' "$SELF" || true)"
  case "$n" in "" | 0 | *[!0-9]*) echo "" ;; *) echo "$n" ;; esac
}

readonly SUBJECT="${KEYPASTE_VERIFY:-scripts/verify.sh}"
readonly EVERY=' core cli mcp rules pages integration compat aot scripts desktop appcompat markers package '
readonly THREE='["ubuntu-24.04","windows-2025","macos-15"]'
readonly LINUX='["ubuntu-24.04"]'

die() { echo "::error::$*" >&2; exit 1; }

[ -f "$SUBJECT" ] || die "no planner to check: $SUBJECT"
ERR="$(mktemp)"
trap 'rm -f "$ERR"' EXIT

cases_run=0

# run_case <name> <changed paths | -git> <lanes> <backend_os> <backend_tests> <desktop_tests> <filter> [verify.sh args]
run_case() {
  local name="$1" paths="$2" want got
  want="$(printf 'lanes=%s\nbackend_os=%s\nbackend_tests=%s\ndesktop_tests=%s\nfilter=%s' "$3" "$4" "$5" "$6" "$7")"
  shift 7
  cases_run=$((cases_run + 1))
  if [ "$paths" = -git ]; then
    got="$(unset VERIFY_CHANGED_PATHS; bash "$SUBJECT" --plan "$@" 2>"$ERR" | sed '/^test_matrix=/d; /^app_[a-z]*_os=/d')" || die "$name: the planner failed"
  else
    got="$(VERIFY_CHANGED_PATHS="$paths" bash "$SUBJECT" --plan "$@" 2>"$ERR" | sed '/^test_matrix=/d; /^app_[a-z]*_os=/d')" || die "$name: the planner failed"
  fi
  if [ "$got" != "$want" ]; then
    printf 'expected:\n%s\ngot:\n%s\n' "$want" "$got" >&2
    die "$name: the plan differs"
  fi
  printf '  %-34s%s\n' "$name" "$(printf '%s\n' "$got" | sed -n 1p)"
}

# run_matrix_case <name> <changed paths> <runner:lane pairs already passed> <lanes> <test_matrix>
run_matrix_case() {
  local name="$1" paths="$2" satisfied="$3" want got
  want="$(printf 'lanes=%s\ntest_matrix=%s' "$4" "$5")"
  cases_run=$((cases_run + 1))
  got="$(VERIFY_CHANGED_PATHS="$paths" VERIFY_SATISFIED="$satisfied" bash "$SUBJECT" --plan 2>"$ERR" \
    | sed -n '/^lanes=/p; /^test_matrix=/p')" || die "$name: the planner failed"
  if [ "$got" != "$want" ]; then
    printf 'expected:\n%s\ngot:\n%s\n' "$want" "$got" >&2
    die "$name: the plan differs"
  fi
  printf '  %-34s%s\n' "$name" "$(printf '%s\n' "$got" | sed -n 1p)"
}

# run_app_case <name> <changed paths> <runner:mark pairs already passed> <lanes> <desktop_tests>
#   <compat runners> <first-run runners> <clipboard runners> <package runners>
run_app_case() {
  local name="$1" paths="$2" satisfied="$3" want got
  want="$(printf 'lanes=%s\ndesktop_tests=%s\napp_compat_os=%s\napp_firstrun_os=%s\napp_markers_os=%s\napp_package_os=%s' \
    "$4" "$5" "$6" "$7" "$8" "$9")"
  cases_run=$((cases_run + 1))
  got="$(VERIFY_CHANGED_PATHS="$paths" VERIFY_SATISFIED="$satisfied" bash "$SUBJECT" --plan 2>"$ERR" \
    | sed -n '/^lanes=/p; /^desktop_tests=/p; /^app_[a-z]*_os=/p')" || die "$name: the planner failed"
  if [ "$got" != "$want" ]; then
    printf 'expected:\n%s\ngot:\n%s\n' "$want" "$got" >&2
    die "$name: the plan differs"
  fi
  printf '  %-34s%s\n' "$name" "$(printf '%s\n' "$got" | sed -n 1p)"
}

echo "== documents, pages and workflows"
run_case "a plan nothing reads" docs/STEPS.md ' ' '[]' '' '' ''
run_case "a page the demo checks" README.md ' pages scripts ' "$LINUX" '' '' ''
run_case "a workflow the rules read" .github/workflows/install.yml ' rules scripts ' "$LINUX" \
  tests/Keypaste.Core.Tests '' Keypaste.Core.Tests.WorkflowRulesTests
run_case "the backend workflow" .github/workflows/ci.yml ' core cli mcp rules pages integration compat aot scripts ' \
  "$THREE" all '' ''
run_case "rules beside a test project" "$(printf '%s\n' .github/workflows/install.yml tests/Keypaste.Cli.Tests/New.cs)" \
  ' cli rules scripts ' "$THREE" 'tests/Keypaste.Core.Tests tests/Keypaste.Cli.Tests' '' ''

echo "== the gates' shared library"
run_case "the library every gate sources" scripts/lib/common.sh ' rules pages integration compat aot scripts desktop appcompat ' \
  "$THREE" tests/Keypaste.Core.Tests all Keypaste.Core.Tests.WorkflowRulesTests
run_case "the KeePassXC gates' library" scripts/lib/kpxc.sh ' rules compat aot scripts appcompat ' "$THREE" \
  tests/Keypaste.Core.Tests '' Keypaste.Core.Tests.WorkflowRulesTests

echo "== projects, linked files and helpers"
run_case "a new core test" tests/Keypaste.Core.Tests/SomeNew.cs ' core ' "$THREE" tests/Keypaste.Core.Tests '' ''
run_case "the CLI harness" tests/Keypaste.Cli.Tests/CliHarness.cs ' cli desktop ' "$THREE" \
  tests/Keypaste.Cli.Tests tests/Keypaste.Consistency.Tests ''
run_case "the publisher statement" tests/Keypaste.Core.Tests/PublisherMetadata.cs ' core cli mcp desktop ' "$THREE" \
  all tests/Keypaste.App.Tests ''
run_case "the software YubiKey" tests/Keypaste.Core.Tests/HardwareKeys/SoftwareYubiKey.cs ' core desktop ' "$THREE" \
  tests/Keypaste.Core.Tests tests/Keypaste.App.Tests ''
run_case "the pool reporter" tests/Keypaste.Core.Tests/Reporter.cs ' core mcp ' "$THREE" \
  'tests/Keypaste.Core.Tests tests/Keypaste.Mcp.Tests' '' ''
run_case "the share server fake" tests/Keypaste.Core.Tests/FakeShareServer.cs ' core cli desktop ' "$THREE" \
  'tests/Keypaste.Core.Tests tests/Keypaste.Cli.Tests' tests/Keypaste.App.Tests ''
run_case "the app's clipboard fake" tests/Keypaste.App.Tests/Clipboard/FakeClipboard.cs ' desktop ' '[]' \
  '' 'tests/Keypaste.App.Tests tests/Keypaste.Consistency.Tests' ''
run_case "the pool starver" tests/Keypaste.PoolStarver/Program.cs ' mcp ' "$THREE" tests/Keypaste.Mcp.Tests '' ''
run_case "the environment reporter" tests/Keypaste.EnvReporter/Program.cs ' core mcp desktop ' "$THREE" \
  'tests/Keypaste.Core.Tests tests/Keypaste.Mcp.Tests' tests/Keypaste.App.Tests ''
run_case "the app" src/Keypaste.App/App.axaml.cs ' desktop appcompat markers package ' '[]' '' all ''
run_case "the core" src/Keypaste.Core/Vault.cs ' core cli mcp integration compat aot desktop appcompat markers package ' \
  "$THREE" all all ''
run_case "the word list Core embeds" third_party/eff-large-wordlist/eff_large_wordlist.txt \
  ' core cli mcp integration compat aot desktop appcompat markers package ' "$THREE" all all ''

echo "== what selects everything"
run_case "the SDK pin" global.json "$EVERY" "$THREE" all all ''
run_case "a path no lane claims" tools/new.cs "$EVERY" "$THREE" all all ''
run_case "no changed path" '' "$EVERY" "$THREE" all all ''
run_case "a range git cannot read" -git "$EVERY" "$THREE" all all '' --since no-such-ref
grep -qF 'git could not list changes' "$ERR" || die "a range git cannot read: everything was selected without saying git failed"
run_case "--all" -git "$EVERY" "$THREE" all all '' --all

echo "== each runner's lanes, less what an earlier run already passed"
readonly CORE_LANES=' core cli mcp integration compat aot desktop appcompat markers package '
run_matrix_case "a core change, nothing passed" src/Keypaste.Core/Vault.cs '' "$CORE_LANES" \
  '[{"os":"ubuntu-24.04","lanes":" core cli mcp integration compat ","backend_tests":"all","filter":""},{"os":"windows-2025","lanes":" core cli mcp integration compat ","backend_tests":"all","filter":""},{"os":"macos-15","lanes":" core cli mcp integration compat ","backend_tests":"all","filter":""}]'
run_matrix_case "a core change Linux already passed" src/Keypaste.Core/Vault.cs 'ubuntu-24.04:core ubuntu-24.04:cli ubuntu-24.04:mcp ubuntu-24.04:integration ubuntu-24.04:compat' "$CORE_LANES" \
  '[{"os":"windows-2025","lanes":" core cli mcp integration compat ","backend_tests":"all","filter":""},{"os":"macos-15","lanes":" core cli mcp integration compat ","backend_tests":"all","filter":""}]'
run_matrix_case "a page edit already passed" README.md 'ubuntu-24.04:pages ubuntu-24.04:scripts' ' ' '[]'
run_matrix_case "rules beside the CLI, unfiltered" "$(printf '%s\n' .github/workflows/install.yml tests/Keypaste.Cli.Tests/New.cs)" '' \
  ' cli rules scripts ' '[{"os":"ubuntu-24.04","lanes":" cli rules ","backend_tests":"tests/Keypaste.Core.Tests tests/Keypaste.Cli.Tests","filter":""},{"os":"windows-2025","lanes":" cli ","backend_tests":"tests/Keypaste.Cli.Tests","filter":""},{"os":"macos-15","lanes":" cli ","backend_tests":"tests/Keypaste.Cli.Tests","filter":""}]'
run_matrix_case "rules alone keep their filter" .github/workflows/install.yml '' ' rules scripts ' \
  '[{"os":"ubuntu-24.04","lanes":" rules ","backend_tests":"tests/Keypaste.Core.Tests","filter":"Keypaste.Core.Tests.WorkflowRulesTests"}]'

echo "== each app job's runners, less what an earlier run already passed"
readonly APP_LANES=' desktop appcompat markers package '
readonly TWO='["ubuntu-24.04","windows-2025"]'
readonly CLIPBOARD='["ubuntu-24.04","macos-15"]'
readonly TARGETS='["ubuntu-24.04","macos-15","windows-2025"]'
readonly EVERY_APP_JOB='ubuntu-24.04:desktop ubuntu-24.04:appcompat.workflows windows-2025:appcompat.workflows ubuntu-24.04:appcompat.firstrun windows-2025:appcompat.firstrun macos-15:appcompat.firstrun ubuntu-24.04:markers macos-15:markers ubuntu-24.04:package macos-15:package windows-2025:package'
run_app_case "the app, nothing passed" src/Keypaste.App/App.axaml.cs '' "$APP_LANES" all \
  "$TWO" "$THREE" "$CLIPBOARD" "$TARGETS"
run_app_case "the app, Linux's first run passed" src/Keypaste.App/App.axaml.cs 'ubuntu-24.04:appcompat.firstrun' "$APP_LANES" all \
  "$TWO" '["windows-2025","macos-15"]' "$CLIPBOARD" "$TARGETS"
run_app_case "the app, every job passed" src/Keypaste.App/App.axaml.cs "$EVERY_APP_JOB" ' ' '' '[]' '[]' '[]' '[]'
run_app_case "a desktop edit already passed" tests/Keypaste.App.Tests/Clipboard/FakeClipboard.cs 'ubuntu-24.04:desktop' \
  ' ' '' '[]' '[]' '[]' '[]'

echo "== negative control"
export VERIFY_SCOPE_NO_LINKS=1
run_case "the CLI harness, links ignored" tests/Keypaste.Cli.Tests/CliHarness.cs ' cli ' "$THREE" \
  tests/Keypaste.Cli.Tests '' ''
run_case "the word list, links ignored" third_party/eff-large-wordlist/eff_large_wordlist.txt ' core ' "$THREE" \
  tests/Keypaste.Core.Tests '' ''
unset VERIFY_SCOPE_NO_LINKS
echo "  with linked files ignored the harness loses desktop and the word list loses Core's dependents,"
echo "  so reading compiled and embedded links is what selects them"

DECLARED="$(declared_cases)"
[ -n "$DECLARED" ] || die "no case lines found in $SELF; the count this gate checks itself against is derived from them"
[ "$cases_run" -eq "$DECLARED" ] || die "$cases_run cases ran, but $DECLARED are written in $SELF; a case is defined and not driven"

cat <<EOF
ok: $cases_run cases. Documents nothing reads select no lane, pages and workflows select the Linux
    lanes that read them, a test project's file selects that project, a file another project
    compiles or embeds selects that project too, a helper selects the test that starts it, the
    gates' shared library selects every lane whose gates source it, and a build input, an
    unknown path, no change and an unreadable range each select everything, the last saying git
    failed. Each runner, and each app.yml job, keeps only the runners an earlier run has not
    passed. With linked files ignored the CLI harness loses desktop and the word list keeps only
    core.
not proved here: that the jobs a lane names run what it promises, which the workflow owns; and
    that a runtime dependency no project file or helper table records is found at all.
EOF
