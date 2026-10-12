#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

usage() {
  cat <<'USAGE'
Usage: bash scripts/verify.sh [--from <profile>] [--list]
       bash scripts/verify.sh <profile> [--list] [--prepare-only|--test-only]
       bash scripts/verify.sh backend|desktop [--prepare-only|--test-only] --project <dir>... [--filter-class <class>]

With no profile, runs every profile but compat: workflows and scripts beside the dotnet
profiles, which run in sequence. A failed run names every failed profile, prints their held
output and the command that resumes; --from <profile> resumes at that profile, in the order
workflows, scripts, format, backend, integration, desktop, desktop-gates. Each profile's
output is held in artifacts/verify/<profile>.log. Hosted CI runs the same profiles as jobs.

workflows      Workflow syntax, expressions and embedded shell checks (pinned actionlint via Docker).
scripts        Offline release/diagnostic fixtures and shell syntax, run in parallel; no build or
               credentials. On Windows they run in a Linux container, where they take a tenth of
               the time; VERIFY_SCRIPTS_NATIVE=1 keeps them in Git Bash.
format         dotnet format over both solutions and the consistency project; nothing builds.
backend        Locked restore, Release build and backend tests.
integration    Prepare the backend and exercise real CLI/MCP processes and the demo pages.
desktop        Prepare the desktop solution and the consistency project, and run their tests.
desktop-gates  Prepare as desktop does, and drive the app, the CLI and an agent as real processes.
compat         Prepare the backend and the app driver, and verify creation, write-back, history, recovery, organization,
               keyfiles, custom fields, project tags and every workflow on vaults KeePassXC made, through the CLI, the app and
               the site's Worker on loopback, and
               the first run's offer of the databases KeePassXC last opened, against installed KeePassXC.
               Never run without being named.

--list prints the selection and commands without executing them. Backend, desktop, desktop-gates
and integration accept --prepare-only and --test-only for CI or a build already prepared by this
command. --project narrows backend or desktop to those test projects and the helpers they start,
and --filter-class runs one test class in each.
Use Git Bash on Windows. A full run needs dotnet, git, jq, GNU timeout and running Docker.
macOS can supply GNU timeout as gtimeout from coreutils. compat also needs keepassxc-cli
(or KPXC_CLI) with KeePassXC's app beside it or on PATH (or KPXC_APP), and Node 22 with npm and
curl for the site's Worker. Native AOT, packaging, other operating systems and live install checks stay in CI.
USAGE
}

bad_usage() { echo "$1" >&2; usage >&2; exit 2; }

sequence=(workflows scripts format backend integration desktop desktop-gates)
docker_lane=(workflows scripts)
# Both solutions build Keypaste.Core into one artifacts/ tree, so dotnet work never overlaps.
dotnet_lane=(format backend integration desktop desktop-gates)

profile=''
phase=all
list=false
from=''
projects=()
filter_class=''
timeout_command=timeout
if command -v gtimeout >/dev/null 2>&1; then timeout_command=gtimeout; fi
while [ "$#" -gt 0 ]; do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --list) list=true ;;
    --from)
      [ -z "$from" ] || bad_usage 'choose one --from profile'
      [ "$#" -ge 2 ] || bad_usage '--from needs a profile'
      from="$2"; shift
      ;;
    --project)
      [ -n "${2:-}" ] || bad_usage '--project needs a test project directory'
      projects+=("${2%/}"); shift
      ;;
    --filter-class)
      [ -z "$filter_class" ] || bad_usage 'choose one --filter-class'
      [ -n "${2:-}" ] || bad_usage '--filter-class needs a test class'
      filter_class="$2"; shift
      ;;
    --prepare-only|--test-only)
      [ "$phase" = all ] || bad_usage 'choose only one phase'
      phase="${1#--}"
      ;;
    format|backend|desktop|desktop-gates|scripts|workflows|integration|compat)
      [ -z "$profile" ] || bad_usage 'choose one profile'
      profile="$1"
      ;;
    *) bad_usage "unknown argument: $1" ;;
  esac
  shift
done
case "$phase:$profile" in
  all:*|*:backend|*:desktop|*:desktop-gates|*:integration) ;;
  *) bad_usage 'phase selection is only supported for backend, desktop, desktop-gates and integration' ;;
esac
if [ -n "$profile" ] && [ -n "$from" ]; then bad_usage '--from resumes a full run; it cannot accompany a named profile'; fi
project_file() {
  case "$1" in *.csproj) echo "$1" ;; *) echo "$1/${1##*/}.csproj" ;; esac
}
if [ "${#projects[@]}" -gt 0 ]; then
  if [ "$profile" != backend ] && [ "$profile" != desktop ]; then bad_usage '--project narrows backend or desktop'; fi
  for dir in "${projects[@]}"; do
    [ -f "$(project_file "$dir")" ] || bad_usage "no project at $(project_file "$dir")"
  done
fi
if [ -n "$filter_class" ] && [ "${#projects[@]}" -eq 0 ]; then
  bad_usage '--filter-class needs --project'
fi
if [ -n "$from" ]; then
  case " ${sequence[*]} " in
    *" $from "*) ;;
    *) bad_usage "--from takes one of: ${sequence[*]}" ;;
  esac
fi

log_dir=''

run() {
  printf '+ '; printf '%q ' "$@"; printf '\n'
  if [ "$list" = false ]; then "$@"; fi
}

prepare() {
  run dotnet restore "$1" --locked-mode
  run dotnet build "$1" --no-restore -c Release -warnaserror
}

# Test runner lines a failed run prints, repeated as annotations so the run's summary names them.
annotate_failures() {
  local escape
  escape="$(printf '\033')"
  sed -e "s/$escape\[[0-9;]*[A-Za-z]//g" "$1" | grep -E '^[[:space:]]*failed[[:space:]]' | head -n 10 \
    | sed -E 's/^[[:space:]]*failed[[:space:]]+/::error::failed /; s/[[:space:]]+\([^()]*\)[[:space:]]*$//' || true
}

run_tests() {
  local held code=0
  printf '+ '; printf '%q ' "$@"; printf '\n'
  [ "$list" = false ] || return 0
  if [ "${GITHUB_ACTIONS:-}" != true ]; then "$@"; return; fi
  held="$(mktemp)"
  "$@" 2>&1 | tee "$held" || code=$?
  if [ "$code" != 0 ]; then annotate_failures "$held"; fi
  rm -f "$held"
  return "$code"
}

# Test executables a test project starts from artifacts/bin without referencing them.
runtime_helpers() {
  case "$1" in
    tests/Keypaste.Core.Tests) echo tests/Keypaste.TxfContender tests/Keypaste.VaultSaver ;;
    tests/Keypaste.Mcp.Tests) echo tests/Keypaste.PoolStarver ;;
    tests/Keypaste.App.Tests) echo tests/Keypaste.EnvReporter ;;
  esac
}

prepare_projects() {
  local dir helper project targets=()
  for dir in "${projects[@]}"; do
    targets+=("$dir")
    for helper in $(runtime_helpers "$dir"); do targets+=("$helper"); done
  done
  for project in "${targets[@]}"; do run dotnet restore "$(project_file "$project")" --locked-mode; done
  for project in "${targets[@]}"; do run dotnet build "$(project_file "$project")" --no-restore -c Release -warnaserror; done
}
test_projects() {
  local dir options
  for dir in "${projects[@]}"; do
    options=()
    if [ "$dir" = tests/Keypaste.App.Tests ]; then options+=(--long-running 120); fi
    if [ -n "$filter_class" ]; then options+=(--filter-class "$filter_class"); fi
    if [ "${#options[@]}" -gt 0 ]; then
      run_tests dotnet test "$(project_file "$dir")" --no-build -c Release -- "${options[@]}"
    else
      run_tests dotnet test "$(project_file "$dir")" --no-build -c Release
    fi
  done
}

prepare_backend() { prepare keypaste.slnx; }
# The consistency project builds the CLI the desktop gates start beside the app.
prepare_desktop() {
  prepare keypaste.app.slnx
  prepare tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj
}
test_backend() {
  run_tests dotnet test keypaste.slnx --no-build -c Release
}
test_desktop() {
  run_tests dotnet test keypaste.app.slnx --no-build -c Release -- --long-running 120
  run_tests dotnet test tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj --no-build -c Release
}
check_desktop_gates() {
  integration_script scripts/verify-session-authority.sh
  integration_script scripts/verify-lock-boundary.sh
  integration_script scripts/verify-current-state.sh
  integration_script scripts/verify-session-lifecycle.sh
  integration_script scripts/verify-desktop-approval.sh
  integration_script scripts/verify-agent-activity.sh
  integration_script scripts/verify-connect-client.sh
  integration_script scripts/verify-run-session.sh
  integration_script scripts/verify-held-saves.sh
}

selftests=(
  'scripts/verify-release-destination.sh'
  'scripts/verify-release-preflight.sh'
  'scripts/verify-green-gates.sh'
  'scripts/verify-release-matrix.sh'
  'scripts/verify-site-disclosure.sh --selftest'
  'scripts/verify-release-completion.sh'
  'scripts/verify-provenance.sh --selftest'
  'scripts/verify-desktop-candidate.sh --selftest'
  'scripts/sign-windows.sh --selftest'
  'scripts/verify-windows-signature.sh --selftest'
  'scripts/fetch-pinned-asset.sh --selftest'
  'scripts/verify-linux-appimage.sh --selftest'
  'scripts/verify-clipboard-markers.sh --selftest'
  'scripts/exercise-desktop-install.sh --selftest'
  'scripts/exercise-desktop-upgrade.sh --selftest'
)

check_scripts() {
  local script work i code first_code=0 failed=()
  for script in scripts/*.sh scripts/lib/*.sh scripts/demo/*.sh; do run bash -n "$script"; done
  if [ "$list" = true ]; then
    # shellcheck disable=SC2086
    for script in "${selftests[@]}"; do run bash $script; done
    return
  fi
  work="$(mktemp -d)"
  for i in "${!selftests[@]}"; do
    (
      code=0
      # shellcheck disable=SC2086
      bash ${selftests[$i]} > "$work/$i.log" 2>&1 || code=$?
      echo "$code" > "$work/$i.code"
    ) &
  done
  wait
  for i in "${!selftests[@]}"; do
    printf '+ bash %s\n' "${selftests[$i]}"
    cat "$work/$i.log"
    code="$(cat "$work/$i.code")"
    if [ "$code" != 0 ]; then
      failed+=("${selftests[$i]} (exit $code)")
      if [ "$first_code" = 0 ]; then first_code="$code"; fi
    fi
  done
  rm -rf "$work"
  if [ "${#failed[@]}" -gt 0 ]; then
    printf 'failed selftest: %s\n' "${failed[@]}" >&2
    return "$first_code"
  fi
}

host_root() {
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) pwd -W 2>/dev/null || pwd ;;
    *) pwd ;;
  esac
}

scripts_in_container() {
  [ "${VERIFY_SCRIPTS_NATIVE:-}" != 1 ] || return 1
  case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) ;; *) return 1 ;; esac
  command -v docker >/dev/null 2>&1 || return 1
  # A linked worktree keeps its history outside the mount, and the release matrix reads history.
  [ -d .git ]
}

scripts_image=keypaste-verify-scripts

profile_scripts() {
  if ! scripts_in_container; then check_scripts; return; fi
  run docker build -q -t "$scripts_image" scripts/container
  run env MSYS_NO_PATHCONV=1 docker run --rm --user 1000:1000 -e HOME=/tmp -e VERIFY_SCRIPTS_NATIVE=1 \
    -e GIT_CONFIG_COUNT=1 -e GIT_CONFIG_KEY_0=safe.directory -e GIT_CONFIG_VALUE_0=/repo \
    -v "$(host_root):/repo:ro" -w /repo "$scripts_image" bash scripts/verify.sh scripts
  if [ "$list" = true ]; then check_scripts; fi
}

profile_workflows() {
  local root
  root="$(host_root)"
  run env MSYS_NO_PATHCONV=1 docker run --rm -v "$root:/repo:ro" -w /repo \
    rhysd/actionlint@sha256:b1934ee5f1c509618f2508e6eb47ee0d3520686341fec936f3b79331f9315667 -color
  run env MSYS_NO_PATHCONV=1 docker run --rm -v "$root:/repo:ro" -w /repo --entrypoint shellcheck \
    rhysd/actionlint@sha256:b1934ee5f1c509618f2508e6eb47ee0d3520686341fec936f3b79331f9315667 \
    scripts/verify.sh scripts/verify-clipboard-markers.sh \
    scripts/verify-desktop-candidate.sh scripts/exercise-desktop-install.sh scripts/exercise-desktop-upgrade.sh \
    scripts/build-windows-installer.sh scripts/build-macos-app.sh scripts/build-macos-dmg.sh scripts/verify-macos-cli-link.sh scripts/install-keepassxc-windows.sh scripts/fetch-pinned-asset.sh scripts/apt-install.sh \
    scripts/build-upgrade-candidates.sh scripts/break-msi-cabinet.sh scripts/lib/*.sh
}

integration_script() { run "$timeout_command" --verbose --kill-after=10s 8m bash "$1"; }

# dotnet format runs no build and reads only the projects it is given; Core is in both solutions.
profile_format() {
  local target
  for target in keypaste.slnx keypaste.app.slnx tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj; do
    run dotnet restore "$target" --locked-mode
    run dotnet format "$target" --no-restore --verify-no-changes --exclude third_party/
  done
}

check_integration() {
  integration_script scripts/verify-run-injection.sh
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) echo 'Signal forwarding has no Windows local verifier; see SECURITY.md.' ;;
    *) integration_script scripts/verify-run-signals.sh ;;
  esac
  integration_script scripts/verify-mcp-stdio.sh
  integration_script scripts/verify-approval-e2e.sh
  integration_script scripts/verify-mcp-run.sh
  integration_script scripts/verify-policy-e2e.sh
  integration_script scripts/verify-log-chain.sh
  integration_script scripts/verify-demo.sh
}

backend_prepared=false
profile_backend() {
  if [ "$phase" != test-only ]; then
    if [ "${#projects[@]}" -gt 0 ]; then prepare_projects; else prepare_backend; fi
  fi
  backend_prepared=true
  if [ -n "$log_dir" ]; then : > "$log_dir/backend.prepared"; fi
  if [ "$phase" != prepare-only ]; then
    if [ "${#projects[@]}" -gt 0 ]; then test_projects; else test_backend; fi
  fi
}
desktop_prepared=false
profile_desktop() {
  if [ "$phase" != test-only ]; then
    if [ "${#projects[@]}" -gt 0 ]; then
      prepare_projects
    else
      prepare_desktop
      desktop_prepared=true
      if [ -n "$log_dir" ]; then : > "$log_dir/desktop.prepared"; fi
    fi
  fi
  if [ "$phase" != prepare-only ]; then
    if [ "${#projects[@]}" -gt 0 ]; then test_projects; else test_desktop; fi
  fi
}
profile_desktop_gates() {
  if [ "$phase" != test-only ] && [ "$desktop_prepared" = false ] \
    && { [ -z "$log_dir" ] || [ ! -e "$log_dir/desktop.prepared" ]; }; then
    prepare_desktop
  fi
  if [ "$phase" != prepare-only ]; then check_desktop_gates; fi
}
profile_integration() {
  if [ "$phase" != test-only ] && [ "$backend_prepared" = false ] \
    && { [ -z "$log_dir" ] || [ ! -e "$log_dir/backend.prepared" ]; }; then
    prepare_backend
  fi
  if [ "$phase" != prepare-only ]; then check_integration; fi
}
profile_compat() {
  prepare_backend
  export KP_COMPAT_PASSWORD=keypaste-local-compat-fixture
  run bash scripts/make-compat-fixture.sh artifacts/compat/local.kdbx
  run bash scripts/verify-keepassxc-compat.sh artifacts/compat/local.kdbx
  run bash scripts/verify-keepassxc-writeback.sh artifacts/compat/local-writeback.kdbx
  run bash scripts/verify-keepassxc-history.sh artifacts/compat/local-history.kdbx
  run bash scripts/verify-keepassxc-recyclebin.sh artifacts/compat/local-recyclebin.kdbx
  run bash scripts/verify-keepassxc-run.sh artifacts/compat/local-run
  run bash scripts/verify-keepassxc-backup.sh artifacts/compat/local-backup.kdbx
  run bash scripts/verify-keepassxc-organize.sh artifacts/compat/local-organize.kdbx
  run bash scripts/verify-keepassxc-import.sh artifacts/compat/local-import
  prepare tests/Keypaste.AppDriver/Keypaste.AppDriver.csproj
  run npm ci --prefix site --no-audit --no-fund
  run bash scripts/verify-keepassxc-fields.sh artifacts/compat/local-fields
  run bash scripts/verify-keepassxc-projects.sh artifacts/compat/local-projects
  run bash scripts/verify-keepassxc-keyfile.sh artifacts/compat/local-keyfile.kdbx
  run bash scripts/verify-keepassxc-xml-attach.sh artifacts/compat/local-xml-attach
  run bash scripts/verify-keepassxc-workflows.sh artifacts/compat/local-workflows
  run bash scripts/verify-keepassxc-first-run.sh artifacts/compat/local-first-run
}

planned=()
is_planned() {
  case " ${planned[*]:-} " in *" $1 "*) return 0 ;; *) return 1 ;; esac
}

plan_profiles() {
  local p reached=false
  [ -n "$from" ] || reached=true
  for p in "${sequence[@]}"; do
    if [ "$p" = "$from" ]; then reached=true; fi
    if [ "$reached" = false ]; then
      printf 'verify: skip %-13s before --from %s\n' "$p" "$from"
    else
      planned+=("$p")
    fi
  done
  printf 'verify: skip %-13s needs an installed KeePassXC; run it by name\n' compat
}

require_tools() {
  local tool
  for tool in "$@"; do
    command -v "$tool" >/dev/null || { echo "missing $tool; use Git Bash on Windows" >&2; exit 1; }
  done
}
require_gnu_timeout() {
  require_tools "$timeout_command"
  if ! "$timeout_command" --version 2>/dev/null | grep -F 'GNU coreutils' >/dev/null; then
    echo 'GNU timeout is required: use Git Bash on Windows or coreutils on macOS.' >&2
    exit 1
  fi
}
require_for() {
  case "$1" in
    workflows) require_tools docker ;;
    scripts) if scripts_in_container; then require_tools docker; else require_tools git jq; fi ;;
    integration|desktop-gates) require_tools dotnet git jq; require_gnu_timeout ;;
    compat)
      require_tools dotnet git
      command -v "${KPXC_CLI:-keepassxc-cli}" >/dev/null || { echo 'set KPXC_CLI to an installed keepassxc-cli' >&2; exit 1; }
      ;;
    *) require_tools dotnet git ;;
  esac
}

execute_profile() {
  local p="$1" start=$SECONDS code built=''
  case "$p" in
    integration) built=backend ;;
    desktop-gates) built=desktop ;;
  esac
  if [ -n "$built" ] && [ -e "$log_dir/$built.status" ] && [ ! -e "$log_dir/$built.prepared" ]; then
    echo blocked > "$log_dir/$p.status"
    echo "verify: not run $p: $built did not build"
    return
  fi
  echo "verify: start $p"
  set +e
  ( set -e; "profile_${p//-/_}" ) > "$log_dir/$p.log" 2>&1
  code=$?
  set -e
  echo "$code" > "$log_dir/$p.status"
  if [ "$code" = 0 ]; then
    echo "verify: ok    $p $((SECONDS - start))s"
  else
    echo "verify: FAIL  $p $((SECONDS - start))s (exit $code)"
  fi
}

run_lane() {
  local p
  for p in "$@"; do
    if is_planned "$p"; then execute_profile "$p"; fi
  done
}

run_selection() {
  local p status failed=() blocked=() first_code=0 start=$SECONDS
  plan_profiles
  if [ "$list" = true ]; then
    for p in "${planned[@]}"; do "profile_${p//-/_}"; done
    return
  fi
  for p in "${planned[@]}"; do require_for "$p"; done
  log_dir="${VERIFY_LOG_DIR:-artifacts/verify}"
  mkdir -p "$log_dir"
  rm -f "$log_dir"/*.log "$log_dir"/*.status "$log_dir"/*.prepared

  run_lane "${docker_lane[@]}" &
  run_lane "${dotnet_lane[@]}" &
  wait

  for p in "${planned[@]}"; do
    status="$(cat "$log_dir/$p.status" 2>/dev/null || echo 1)"
    case "$status" in
      0) ;;
      blocked) blocked+=("$p") ;;
      *)
        failed+=("$p")
        if [ "$first_code" = 0 ]; then first_code="$status"; fi
        printf '\n==== %s (exit %s), held in %s ====\n' "$p" "$status" "$log_dir/$p.log"
        cat "$log_dir/$p.log" 2>/dev/null || true
        ;;
    esac
  done
  if [ "${#failed[@]}" -eq 0 ]; then
    printf 'Verification passed: %s (%ss); output held in %s\n' "${planned[*]}" "$((SECONDS - start))" "$log_dir"
    return
  fi
  printf '\nVerification failed: %s\n' "${failed[*]}"
  if [ "${#blocked[@]}" -gt 0 ]; then printf 'Not run: %s\n' "${blocked[*]}"; fi
  printf 'Fix, then resume with: bash scripts/verify.sh --from %s\n' "${failed[0]}"
  exit "$first_code"
}

if [ -z "$profile" ]; then
  run_selection
  exit 0
fi
if [ "$list" = false ]; then require_for "$profile"; fi
"profile_${profile//-/_}"
if [ "$list" = false ]; then printf 'Verification passed: %s (%s)\n' "$profile" "$phase"; fi
