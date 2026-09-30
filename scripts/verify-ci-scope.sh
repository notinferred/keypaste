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
#   - a build input, an unknown path, an empty change list and an unreadable range select everything,
#     and an unreadable range says git failed rather than passing for an empty one.
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
  n="$(grep -cE '^[[:space:]]*run_case ' "$SELF" || true)"
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
    got="$(unset VERIFY_CHANGED_PATHS; bash "$SUBJECT" --plan "$@" 2>"$ERR")" || die "$name: the planner failed"
  else
    got="$(VERIFY_CHANGED_PATHS="$paths" bash "$SUBJECT" --plan "$@" 2>"$ERR")" || die "$name: the planner failed"
  fi
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
    compiles or embeds selects that project too, a helper selects the test that starts it, and a
    build input, an unknown path, no change and an unreadable range each select everything, the
    last saying git failed. With linked files ignored the CLI harness loses desktop and the word
    list keeps only core.
not proved here: that the jobs a lane names run what it promises, which the workflow owns; and
    that a runtime dependency no project file or helper table records is found at all.
EOF
