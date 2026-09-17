#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

usage() {
  cat <<'USAGE'
Usage: bash scripts/verify.sh [all|backend|desktop|records|scripts|workflows|integration|compat] [--list] [--prepare-only|--test-only]

all          Prepare both solutions and consistency tests, check scripts and process integration,
             then run all three test targets (default).
backend      Locked restore, format, Release build and backend tests.
desktop      The desktop solution AND the separate CLI/desktop consistency project.
records      Build and run the documentation row checks only.
scripts      Offline release/diagnostic fixtures and shell syntax; no build or credentials.
workflows    Workflow syntax, expressions and embedded shell checks (pinned actionlint via Docker).
integration  Prepare the backend and exercise real CLI/MCP processes and the demo pages.
compat       Prepare the backend and verify both directions against installed KeePassXC.

--list prints the commands without executing them. Backend/desktop accept --prepare-only
and --test-only for CI or a build already prepared by this command.
Use Git Bash on Windows. all needs dotnet, git, jq, GNU timeout and running Docker.
macOS can supply GNU timeout as gtimeout from coreutils. compat also needs keepassxc-cli
(or KPXC_CLI). Native AOT, packaging, other operating systems and live install checks stay in CI.
USAGE
}

profile=all
phase=all
list=false
profile_set=false
timeout_command=timeout
if command -v gtimeout >/dev/null 2>&1; then timeout_command=gtimeout; fi
for arg in "$@"; do
  case "$arg" in
    --help|-h) usage; exit 0 ;;
    --list) list=true ;;
    --prepare-only|--test-only)
      [ "$phase" = all ] || { echo 'choose only one phase' >&2; exit 2; }
      phase="${arg#--}"
      ;;
    all|backend|desktop|records|scripts|workflows|integration|compat)
      [ "$profile_set" = false ] || { echo 'choose one profile' >&2; exit 2; }
      profile="$arg"; profile_set=true
      ;;
    *) usage >&2; exit 2 ;;
  esac
done
if [ "$phase" != all ] && [ "$profile" != backend ] && [ "$profile" != desktop ]; then
  echo 'phase selection is only supported for backend and desktop' >&2
  exit 2
fi

run() {
  printf '+ '; printf '%q ' "$@"; printf '\n'
  if [ "$list" = false ]; then "$@"; fi
}

prepare() {
  run dotnet restore "$1" --locked-mode
  run dotnet format "$1" --no-restore --verify-no-changes --exclude third_party/
  run dotnet build "$1" --no-restore -c Release -warnaserror
}

prepare_backend() { prepare keypaste.slnx; }
prepare_desktop() {
  prepare keypaste.app.slnx
  prepare tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj
}
test_backend() {
  run dotnet test keypaste.slnx --no-build -c Release
}
test_desktop() {
  run dotnet test keypaste.app.slnx --no-build -c Release
  run dotnet test tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj --no-build -c Release
}

check_scripts() {
  local script
  for script in scripts/*.sh scripts/demo/*.sh; do run bash -n "$script"; done
  run bash scripts/verify-local-checks.sh
  run bash scripts/verify-release-destination.sh
  run bash scripts/verify-release-preflight.sh
  run bash scripts/verify-green-gates.sh
  run bash scripts/verify-release-matrix.sh
  run bash scripts/verify-site-disclosure.sh --selftest
  run bash scripts/verify-release-completion.sh
  run bash scripts/verify-provenance.sh --selftest
  run bash scripts/verify-desktop-candidate.sh --selftest
  run bash scripts/sign-windows.sh --selftest
  run bash scripts/verify-windows-signature.sh --selftest
  run bash scripts/fetch-pinned-asset.sh --selftest
  run bash scripts/verify-linux-appimage.sh --selftest
  run bash scripts/f9-timeline.sh --selftest
  run bash scripts/probe-results.sh --selftest
  run bash scripts/observe-minimize-lock.sh --selftest
  run bash scripts/exercise-desktop-install.sh --selftest
  run bash scripts/exercise-desktop-upgrade.sh --selftest
}

check_workflows() {
  local root="$PWD"
  case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) root="$(pwd -W)" ;; esac
  run env MSYS_NO_PATHCONV=1 docker run --rm -v "$root:/repo:ro" -w /repo \
    rhysd/actionlint@sha256:b1934ee5f1c509618f2508e6eb47ee0d3520686341fec936f3b79331f9315667 -color
  run env MSYS_NO_PATHCONV=1 docker run --rm -v "$root:/repo:ro" -w /repo --entrypoint shellcheck \
    rhysd/actionlint@sha256:b1934ee5f1c509618f2508e6eb47ee0d3520686341fec936f3b79331f9315667 \
    scripts/verify.sh scripts/verify-local-checks.sh scripts/probe-results.sh scripts/observe-minimize-lock.sh \
    scripts/verify-desktop-candidate.sh scripts/exercise-desktop-install.sh scripts/exercise-desktop-upgrade.sh \
    scripts/build-windows-installer.sh scripts/install-keepassxc-windows.sh scripts/fetch-pinned-asset.sh
}

integration_script() { run "$timeout_command" --verbose --kill-after=10s 8m bash "$1"; }

check_integration() {
  integration_script scripts/verify-run-injection.sh
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) echo 'Signal forwarding has no Windows local verifier; see SECURITY.md.' ;;
    *) integration_script scripts/verify-run-signals.sh ;;
  esac
  integration_script scripts/verify-mcp-stdio.sh
  integration_script scripts/verify-approval-e2e.sh
  integration_script scripts/verify-policy-e2e.sh
  integration_script scripts/verify-log-chain.sh
  integration_script scripts/verify-demo.sh
}

if [ "$list" = false ]; then
  case "$profile" in
    scripts) required=(git jq) ;;
    all) required=(dotnet git jq docker "$timeout_command") ;;
    workflows) required=(docker) ;;
    integration) required=(dotnet git jq "$timeout_command") ;;
    *) required=(dotnet git) ;;
  esac
  for tool in "${required[@]}"; do
    command -v "$tool" >/dev/null || { echo "missing $tool; use Git Bash on Windows" >&2; exit 1; }
  done
  if [ "$profile" = all ] || [ "$profile" = integration ]; then
    if ! "$timeout_command" --version 2>/dev/null | grep -F 'GNU coreutils' >/dev/null; then
      echo 'GNU timeout is required: use Git Bash on Windows or coreutils on macOS.' >&2
      exit 1
    fi
  fi
  if [ "$profile" = compat ]; then
    command -v "${KPXC_CLI:-keepassxc-cli}" >/dev/null || { echo 'set KPXC_CLI to an installed keepassxc-cli' >&2; exit 1; }
  fi
fi

case "$profile" in
  all)
    check_workflows
    prepare_backend
    prepare_desktop
    check_scripts
    check_integration
    test_backend
    test_desktop
    ;;
  backend)
    if [ "$phase" != test-only ]; then prepare_backend; fi
    if [ "$phase" != prepare-only ]; then test_backend; fi
    ;;
  desktop)
    if [ "$phase" != test-only ]; then prepare_desktop; fi
    if [ "$phase" != prepare-only ]; then test_desktop; fi
    ;;
  records)
    run dotnet restore tests/Keypaste.Core.Tests/Keypaste.Core.Tests.csproj --locked-mode
    run dotnet test tests/Keypaste.Core.Tests/Keypaste.Core.Tests.csproj --no-restore -c Release -- --filter-class Keypaste.Core.Tests.RecordRowsStaySkimmableTests
    ;;
  scripts) check_scripts ;;
  workflows) check_workflows ;;
  integration) prepare_backend; check_integration ;;
  compat)
    prepare_backend
    export KP_COMPAT_PASSWORD=keypaste-local-compat-fixture
    run bash scripts/make-compat-fixture.sh artifacts/compat/local.kdbx
    run bash scripts/verify-keepassxc-compat.sh artifacts/compat/local.kdbx
    run bash scripts/verify-keepassxc-writeback.sh artifacts/compat/local-writeback.kdbx
    ;;
esac
if [ "$list" = false ]; then printf 'Verification passed: %s (%s)\n' "$profile" "$phase"; fi
