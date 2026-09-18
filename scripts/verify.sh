#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

usage() {
  cat <<'USAGE'
Usage: bash scripts/verify.sh [--all] [--from <profile>] [--list]
       bash scripts/verify.sh <profile> [--list] [--prepare-only|--test-only]

With no profile, runs what the working tree changed: git diff against HEAD plus untracked
files, mapped to profiles. workflows and records always run, a path the map does not know
runs everything, and every skipped profile is logged with its reason. --all (or all) runs
everything. Hosted CI remains the full gate.

--from <profile> resumes at that profile after a failure, in the order
workflows, records, scripts, backend, integration, desktop. A failed run names every failed
profile, prints their held output and the command that resumes. scripts and workflows run
beside the dotnet profiles; each profile's output is held in artifacts/verify/<profile>.log.

workflows    Workflow syntax, expressions and embedded shell checks (pinned actionlint via Docker).
records      Build and run the documentation row checks only.
scripts      Offline release/diagnostic fixtures and shell syntax, run in parallel; no build or
             credentials. On Windows they run in a Linux container, where they take a tenth of
             the time; VERIFY_SCRIPTS_NATIVE=1 keeps them in Git Bash.
backend      Locked restore, format, Release build and backend tests.
integration  Prepare the backend and exercise real CLI/MCP processes and the demo pages.
desktop      The desktop solution AND the separate CLI/desktop consistency project.
compat       Prepare the backend and verify creation, write-back and history against installed KeePassXC.
             Never selected automatically; run it by name.

--list prints the selection and commands without executing them. Backend/desktop accept
--prepare-only and --test-only for CI or a build already prepared by this command.
Use Git Bash on Windows. A full run needs dotnet, git, jq, GNU timeout and running Docker.
macOS can supply GNU timeout as gtimeout from coreutils. compat also needs keepassxc-cli
(or KPXC_CLI). Native AOT, packaging, other operating systems and live install checks stay in CI.
USAGE
}

bad_usage() { echo "$1" >&2; usage >&2; exit 2; }

sequence=(workflows records scripts backend integration desktop)
heavy=(scripts backend integration desktop)
docker_lane=(workflows scripts)
# Both solutions build Keypaste.Core into one artifacts/ tree, so dotnet work never overlaps.
dotnet_lane=(records backend integration desktop)

profile=''
phase=all
list=false
everything=false
from=''
timeout_command=timeout
if command -v gtimeout >/dev/null 2>&1; then timeout_command=gtimeout; fi
while [ "$#" -gt 0 ]; do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --list) list=true ;;
    --all) everything=true ;;
    --from)
      [ -z "$from" ] || bad_usage 'choose one --from profile'
      [ "$#" -ge 2 ] || bad_usage '--from needs a profile'
      from="$2"; shift
      ;;
    --prepare-only|--test-only)
      [ "$phase" = all ] || bad_usage 'choose only one phase'
      phase="${1#--}"
      ;;
    all|backend|desktop|records|scripts|workflows|integration|compat)
      [ -z "$profile" ] || bad_usage 'choose one profile'
      profile="$1"
      ;;
    *) bad_usage "unknown argument: $1" ;;
  esac
  shift
done
if [ "$profile" = all ]; then profile=''; everything=true; fi
if [ "$phase" != all ] && [ "$profile" != backend ] && [ "$profile" != desktop ]; then
  bad_usage 'phase selection is only supported for backend and desktop'
fi
if [ -n "$profile" ] && { [ "$everything" = true ] || [ -n "$from" ]; }; then
  bad_usage '--all and --from select among profiles; they cannot accompany a named one'
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

selftests=(
  'scripts/verify-local-checks.sh'
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
  'scripts/f9-timeline.sh --selftest'
  'scripts/probe-results.sh --selftest'
  'scripts/observe-minimize-lock.sh --selftest'
  'scripts/exercise-desktop-install.sh --selftest'
  'scripts/exercise-desktop-upgrade.sh --selftest'
  'scripts/probe-msi-interruption.sh --selftest'
)

check_scripts() {
  local script work i code first_code=0 failed=()
  for script in scripts/*.sh scripts/demo/*.sh; do run bash -n "$script"; done
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
    scripts/verify.sh scripts/verify-local-checks.sh scripts/probe-results.sh scripts/observe-minimize-lock.sh \
    scripts/verify-desktop-candidate.sh scripts/exercise-desktop-install.sh scripts/exercise-desktop-upgrade.sh \
    scripts/build-windows-installer.sh scripts/install-keepassxc-windows.sh scripts/fetch-pinned-asset.sh     scripts/build-upgrade-candidates.sh scripts/probe-msi-interruption.sh scripts/break-msi-cabinet.sh
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

profile_records() {
  run dotnet restore tests/Keypaste.Core.Tests/Keypaste.Core.Tests.csproj --locked-mode
  run dotnet test tests/Keypaste.Core.Tests/Keypaste.Core.Tests.csproj --no-restore -c Release -- --filter-class Keypaste.Core.Tests.RecordRowsStaySkimmableTests
}

backend_prepared=false
profile_backend() {
  if [ "$phase" != test-only ]; then prepare_backend; fi
  backend_prepared=true
  if [ -n "$log_dir" ]; then : > "$log_dir/backend.prepared"; fi
  if [ "$phase" != prepare-only ]; then test_backend; fi
}
profile_desktop() {
  if [ "$phase" != test-only ]; then prepare_desktop; fi
  if [ "$phase" != prepare-only ]; then test_desktop; fi
}
profile_integration() {
  if [ "$backend_prepared" = false ] && { [ -z "$log_dir" ] || [ ! -e "$log_dir/backend.prepared" ]; }; then
    prepare_backend
  fi
  check_integration
}
profile_compat() {
  prepare_backend
  export KP_COMPAT_PASSWORD=keypaste-local-compat-fixture
  run bash scripts/make-compat-fixture.sh artifacts/compat/local.kdbx
  run bash scripts/verify-keepassxc-compat.sh artifacts/compat/local.kdbx
  run bash scripts/verify-keepassxc-writeback.sh artifacts/compat/local-writeback.kdbx
  run bash scripts/verify-keepassxc-history.sh artifacts/compat/local-history.kdbx
}

# Backend tests read workflows, scripts and the release definition, so those paths select backend too.
profiles_for_path() {
  case "$1" in
    src/Keypaste.App/*|tests/Keypaste.App.Tests/*|tests/Keypaste.MinimizeObserver/*) echo desktop ;;
    tests/Keypaste.Consistency.Tests/*|keypaste.app.slnx) echo desktop ;;
    src/Keypaste.Cli/Keypaste.Cli.csproj) echo scripts backend integration desktop ;;
    src/*|third_party/*) echo backend integration desktop ;;
    tests/Directory.Build.props) echo backend desktop ;;
    tests/*) echo backend ;;
    keypaste.slnx) echo backend integration ;;
    scripts/*) echo scripts backend integration ;;
    .github/*|release-targets.json) echo scripts backend ;;
    README.md|site/public/index.html|docs/PRODUCT.md) echo scripts integration ;;
    launch.md|docs/demo.md|docs/keepass-and-agents.md) echo integration ;;
    CHANGELOG.md|SECURITY.md|docs/RELEASE.md|docs/desktop.md|packaging/*|site/*) echo scripts ;;
    *.md|docs/*|LICENSE|.claude/*) echo quick ;;
    *) echo everything ;;
  esac
}

# VERIFY_CHANGED_PATHS replaces the working-tree reading for the fixtures.
changed_paths() {
  if [ "${VERIFY_CHANGED_PATHS+set}" = set ]; then
    printf '%s\n' "$VERIFY_CHANGED_PATHS"
    return
  fi
  git -c core.quotepath=off diff --name-only --no-renames HEAD --
  git -c core.quotepath=off ls-files --others --exclude-standard
}

changed_count=0
select_profiles() {
  local changes path mapped p var
  if [ "$everything" = true ]; then
    for p in "${heavy[@]}"; do printf -v "why_$p" '%s' '--all'; done
    echo 'verify: --all selects every profile'
    return
  fi
  if ! changes="$(changed_paths 2>&1)"; then
    for p in "${heavy[@]}"; do printf -v "why_$p" '%s' 'the working tree could not be read'; done
    echo "verify: git could not list changes, so everything runs: $changes"
    return
  fi
  while IFS= read -r path; do
    [ -n "$path" ] || continue
    changed_count=$((changed_count + 1))
    mapped="$(profiles_for_path "$path")"
    case "$mapped" in
      quick) continue ;;
      everything) mapped="${heavy[*]}"; path="$path, which no profile claims" ;;
    esac
    for p in $mapped; do
      var="why_$p"
      if [ -z "${!var:-}" ]; then printf -v "$var" '%s' "$path"; fi
      var="hits_$p"
      printf -v "$var" '%s' "$(( ${!var:-0} + 1 ))"
    done
  done <<< "$changes"
  echo "verify: $changed_count changed paths (git diff against HEAD, plus untracked files)"
}

planned=()
is_planned() {
  case " ${planned[*]:-} " in *" $1 "*) return 0 ;; *) return 1 ;; esac
}

plan_profiles() {
  local p var hits reached=false reason
  [ -n "$from" ] || reached=true
  for p in "${sequence[@]}"; do
    if [ "$p" = "$from" ]; then reached=true; fi
    var="why_$p"; reason="${!var:-}"
    var="hits_$p"; hits="${!var:-0}"
    if [ "$hits" -gt 1 ]; then reason="$reason and $((hits - 1)) more"; fi
    case "$p" in workflows|records) reason='always runs' ;; esac
    if [ -z "$reason" ]; then
      printf 'verify: skip %-12s no changed path maps to it\n' "$p"
    elif [ "$reached" = false ]; then
      printf 'verify: skip %-12s before --from %s\n' "$p" "$from"
    elif [ "$p" = records ] && [ -n "${why_backend:-}" ]; then
      printf 'verify: skip %-12s backend runs the same row checks\n' "$p"
    else
      printf 'verify: run  %-12s %s\n' "$p" "$reason"
      planned+=("$p")
    fi
  done
  printf 'verify: skip %-12s needs an installed KeePassXC; run it by name\n' compat
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
    integration) require_tools dotnet git jq; require_gnu_timeout ;;
    compat)
      require_tools dotnet git
      command -v "${KPXC_CLI:-keepassxc-cli}" >/dev/null || { echo 'set KPXC_CLI to an installed keepassxc-cli' >&2; exit 1; }
      ;;
    *) require_tools dotnet git ;;
  esac
}

execute_profile() {
  local p="$1" start=$SECONDS code
  if [ "$p" = integration ] && [ -e "$log_dir/backend.status" ] && [ ! -e "$log_dir/backend.prepared" ]; then
    echo blocked > "$log_dir/$p.status"
    echo "verify: not run $p: backend did not build"
    return
  fi
  echo "verify: start $p"
  set +e
  ( set -e; "profile_$p" ) > "$log_dir/$p.log" 2>&1
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
  local p status failed=() blocked=() first_code=0 resume start=$SECONDS
  select_profiles
  plan_profiles
  if [ "${#planned[@]}" -eq 0 ]; then echo 'verify: nothing to run'; return; fi
  if [ "$list" = true ]; then
    for p in "${planned[@]}"; do "profile_$p"; done
    return
  fi
  for p in "${planned[@]}"; do require_for "$p"; done
  log_dir="${VERIFY_LOG_DIR:-artifacts/verify}"
  mkdir -p "$log_dir"
  rm -f "$log_dir"/*.log "$log_dir"/*.status "$log_dir/backend.prepared"

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
  resume='bash scripts/verify.sh'
  if [ "$everything" = true ]; then resume="$resume --all"; fi
  printf '\nVerification failed: %s\n' "${failed[*]}"
  if [ "${#blocked[@]}" -gt 0 ]; then printf 'Not run: %s\n' "${blocked[*]}"; fi
  printf 'Fix, then resume with: %s --from %s\n' "$resume" "${failed[0]}"
  exit "$first_code"
}

if [ -z "$profile" ]; then
  run_selection
  exit 0
fi
if [ "$list" = false ]; then require_for "$profile"; fi
"profile_$profile"
if [ "$list" = false ]; then printf 'Verification passed: %s (%s)\n' "$profile" "$phase"; fi
