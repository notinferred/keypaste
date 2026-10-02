#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

usage() {
  cat <<'USAGE'
Usage: bash scripts/verify.sh [--all | --since <ref>] [--from <profile>] [--list]
       bash scripts/verify.sh [--all | --since <ref>] --plan
       bash scripts/verify.sh <profile> [--list] [--prepare-only|--test-only]
       bash scripts/verify.sh backend|desktop [--prepare-only|--test-only] --project <dir>... [--filter-class <class>]

With no profile, runs what the working tree changed: git diff against HEAD plus untracked
files, mapped to lanes and the lanes to profiles. --since <ref> reads the commits since <ref>
instead (git diff <ref>...HEAD). workflows always runs, a path the map does not know
runs everything, and every skipped profile is logged with its reason. --all (or all) runs
everything. Hosted CI remains the full gate.

--plan prints the CI selection as GITHUB_OUTPUT lines (lanes, backend_os, backend_tests,
desktop_tests, filter) and runs nothing. With --since, no changed path or an unreadable range
selects everything. VERIFY_CHANGED_PATHS, newline-separated, replaces the reading from git.

--from <profile> resumes at that profile after a failure, in the order
workflows, scripts, backend, integration, desktop. A failed run names every failed
profile, prints their held output and the command that resumes. scripts and workflows run
beside the dotnet profiles; each profile's output is held in artifacts/verify/<profile>.log.

workflows    Workflow syntax, expressions and embedded shell checks (pinned actionlint via Docker).
scripts      Offline release/diagnostic fixtures and shell syntax, run in parallel; no build or
             credentials. On Windows they run in a Linux container, where they take a tenth of
             the time; VERIFY_SCRIPTS_NATIVE=1 keeps them in Git Bash.
backend      Locked restore, format, Release build and backend tests.
integration  Prepare the backend and exercise real CLI/MCP processes and the demo pages.
desktop      The desktop solution AND the separate CLI/desktop consistency project.
compat       Prepare the backend and the app driver, and verify creation, write-back, history, recovery, organization,
             keyfiles, custom fields, project tags and every workflow on vaults KeePassXC made, through the CLI, the app and
             the site's Worker on loopback, and
             the first run's offer of the databases KeePassXC last opened, against installed KeePassXC.
             Never selected automatically; run it by name.

--list prints the selection and commands without executing them. Backend, desktop and
integration accept --prepare-only and --test-only for CI or a build already prepared by this
command. --project narrows backend or desktop to those test projects and the helpers they start,
and --filter-class runs one test class in each.
Use Git Bash on Windows. A full run needs dotnet, git, jq, GNU timeout and running Docker.
macOS can supply GNU timeout as gtimeout from coreutils. compat also needs keepassxc-cli
(or KPXC_CLI) with KeePassXC's app beside it or on PATH (or KPXC_APP), and Node 22 with npm and
curl for the site's Worker. Native AOT, packaging, other operating systems and live install checks stay in CI.
USAGE
}

bad_usage() { echo "$1" >&2; usage >&2; exit 2; }

sequence=(workflows scripts backend integration desktop)
heavy=(scripts backend integration desktop)
docker_lane=(workflows scripts)
# Both solutions build Keypaste.Core into one artifacts/ tree, so dotnet work never overlaps.
dotnet_lane=(backend integration desktop)

profile=''
phase=all
list=false
plan=false
everything=false
from=''
since=''
projects=()
filter_class=''
timeout_command=timeout
if command -v gtimeout >/dev/null 2>&1; then timeout_command=gtimeout; fi
while [ "$#" -gt 0 ]; do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --list) list=true ;;
    --plan) plan=true ;;
    --all) everything=true ;;
    --from)
      [ -z "$from" ] || bad_usage 'choose one --from profile'
      [ "$#" -ge 2 ] || bad_usage '--from needs a profile'
      from="$2"; shift
      ;;
    --since)
      [ -z "$since" ] || bad_usage 'choose one --since ref'
      [ -n "${2:-}" ] || bad_usage '--since needs a ref'
      since="$2"; shift
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
    all|backend|desktop|scripts|workflows|integration|compat)
      [ -z "$profile" ] || bad_usage 'choose one profile'
      profile="$1"
      ;;
    *) bad_usage "unknown argument: $1" ;;
  esac
  shift
done
if [ "$profile" = all ]; then profile=''; everything=true; fi
case "$phase:$profile" in
  all:*|*:backend|*:desktop|*:integration) ;;
  *) bad_usage 'phase selection is only supported for backend, desktop and integration' ;;
esac
if [ -n "$profile" ] && { [ "$everything" = true ] || [ -n "$from" ] || [ -n "$since" ] || [ "$plan" = true ]; }; then
  bad_usage '--all, --since, --from and --plan select among profiles; they cannot accompany a named one'
fi
if [ "$everything" = true ] && [ -n "$since" ]; then bad_usage 'choose --all or --since'; fi
if [ "$plan" = true ] && { [ "$list" = true ] || [ -n "$from" ]; }; then
  bad_usage '--plan prints the selection; it takes no --list or --from'
fi
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
  run dotnet format "$1" --no-restore --verify-no-changes --exclude third_party/
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

# dotnet format checks only the project it is given, so every project the build compiles is named.
prepare_projects() {
  local dir helper project targets=()
  for dir in "${projects[@]}"; do
    targets+=("$dir")
    for helper in $(runtime_helpers "$dir"); do targets+=("$helper"); done
  done
  read_project_graph
  for project in "${targets[@]}"; do run dotnet restore "$(project_file "$project")" --locked-mode; done
  for project in $(dependencies_closure "${targets[@]}"); do
    case "$project" in third_party/*) continue ;; esac
    run dotnet format "$(project_file "$project")" --no-restore --verify-no-changes --exclude third_party/
  done
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
prepare_desktop() {
  prepare keypaste.app.slnx
  prepare tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj
  prepare src/Keypaste.Mcp/Keypaste.Mcp.csproj
}
test_backend() {
  run_tests dotnet test keypaste.slnx --no-build -c Release
}
test_desktop() {
  run_tests dotnet test keypaste.app.slnx --no-build -c Release -- --long-running 120
  run_tests dotnet test tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj --no-build -c Release
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
  'scripts/verify-ci-scope.sh'
  'scripts/verify-lane-cache.sh'
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
    scripts/verify.sh scripts/verify-ci-scope.sh scripts/verify-clipboard-markers.sh \
    scripts/verify-desktop-candidate.sh scripts/exercise-desktop-install.sh scripts/exercise-desktop-upgrade.sh \
    scripts/build-windows-installer.sh scripts/build-macos-app.sh scripts/build-macos-dmg.sh scripts/install-keepassxc-windows.sh scripts/fetch-pinned-asset.sh scripts/apt-install.sh \
    scripts/build-upgrade-candidates.sh scripts/break-msi-cabinet.sh scripts/lib/*.sh
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
profile_desktop() {
  if [ "$phase" != test-only ]; then
    if [ "${#projects[@]}" -gt 0 ]; then prepare_projects; else prepare_desktop; fi
  fi
  if [ "$phase" != prepare-only ]; then
    if [ "${#projects[@]}" -gt 0 ]; then test_projects; else test_desktop; fi
  fi
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

# A lane is what hosted CI runs as one job; a local run maps lanes to profiles. Tests and scripts
# read workflows, documents and the release definition, so those paths select the lanes that read them.
all_lanes=(core cli mcp rules pages integration compat aot scripts desktop appcompat markers package)

path_lanes() {
  case "$1" in
    global.json|NuGet.config|.editorconfig|.gitattributes|Directory.*.props|*/Directory.*.props) echo everything ;;
    packages.lock.json|*/packages.lock.json|keypaste.slnx|keypaste.app.slnx|scripts/verify.sh) echo everything ;;
    src/*/*.csproj|tests/*/*.csproj|third_party/KeePassLib/*.csproj) echo scripts ;;
    src/*|tests/*|third_party/KeePassLib/*) echo unclaimed ;;
    third_party/eff-large-wordlist/*) echo core ;;
    .github/workflows/ci.yml) echo core cli mcp rules pages integration compat aot scripts ;;
    .github/workflows/app.yml) echo rules scripts desktop-all appcompat markers package ;;
    .github/*) echo rules scripts ;;
    scripts/lib/kpxc.sh) echo rules scripts compat aot appcompat ;;
    scripts/lib/*) echo rules scripts pages integration compat aot appcompat desktop-all ;;
    scripts/verify-keepassxc-workflows.sh|scripts/verify-keepassxc-first-run.sh) echo rules scripts appcompat ;;
    scripts/install-keepassxc-windows.sh) echo rules scripts compat appcompat ;;
    scripts/fetch-pinned-asset.sh) echo rules scripts compat appcompat package ;;
    scripts/make-compat-fixture.sh|scripts/verify-keepassxc-compat.sh|scripts/verify-keepassxc-writeback.sh) echo rules scripts compat aot ;;
    scripts/verify-keepassxc-xml-attach.sh|scripts/verify-keepassxc-keyfile.sh) echo rules scripts compat aot ;;
    scripts/verify-keepassxc-recyclebin.sh) echo core rules scripts compat ;;
    scripts/verify-keepassxc-*.sh) echo rules scripts compat ;;
    scripts/verify-run-injection.sh|scripts/verify-run-signals.sh|scripts/verify-mcp-stdio.sh) echo rules scripts integration aot ;;
    scripts/verify-approval-e2e.sh|scripts/verify-log-chain.sh) echo rules scripts integration aot ;;
    scripts/verify-demo.sh|scripts/demo/deploy.sh) echo rules scripts pages integration aot ;;
    scripts/verify-mcp-run.sh|scripts/verify-policy-e2e.sh) echo rules scripts integration ;;
    scripts/verify-aot-trim.sh) echo rules scripts aot ;;
    scripts/verify.ps1) echo core rules scripts ;;
    scripts/aot-trim-baseline.txt) echo aot ;;
    scripts/verify-session-authority.sh|scripts/verify-lock-boundary.sh|scripts/verify-current-state.sh) echo rules scripts desktop-all ;;
    scripts/verify-session-lifecycle.sh|scripts/verify-desktop-approval.sh|scripts/verify-agent-activity.sh) echo rules scripts desktop-all ;;
    scripts/verify-connect-client.sh|scripts/verify-run-session.sh|scripts/verify-held-saves.sh) echo rules scripts desktop-all ;;
    scripts/verify-clipboard-markers.sh) echo rules scripts markers ;;
    scripts/build-*|scripts/break-msi-cabinet.sh|scripts/exercise-desktop-*|scripts/drive-desktop-*) echo rules scripts package ;;
    scripts/publish-desktop-bridge.sh|scripts/rehearse-windows-signing.sh) echo rules scripts package ;;
    scripts/sign-windows.sh|scripts/verify-windows-*.sh|scripts/verify-desktop-candidate.sh) echo rules scripts package ;;
    scripts/verify-linux-appimage.sh|scripts/verify-publisher-metadata.sh) echo rules scripts package ;;
    packaging/*|release-targets.json) echo scripts package ;;
    LICENSE) echo package ;;
    scripts/*) echo rules scripts ;;
    README.md) echo pages scripts ;;
    # The home page's install blocks, floors and disclosures are checked by the scripts lane; its transcripts
    # are checked whenever the pages lane runs: for the CLI, README and the demo pages, and in every full run (D-0403).
    site/public/index.html) echo scripts ;;
    launch.md|docs/demo.md|docs/keepass-and-agents.md) echo pages ;;
    CHANGELOG.md|SECURITY.md|docs/RELEASE.md|docs/desktop.md) echo scripts ;;
    THIRD_PARTY_NOTICES.md) echo core package ;;
    site/test/share-vector.json) echo core scripts ;;
    # The fields gate runs the share Worker from site/wrangler.jsonc; a deploy-config edit waits for a full run (D-0403).
    site/src/*|site/package.json|site/package-lock.json|site/public/s/share-crypto.js) echo scripts compat ;;
    site/*) echo scripts ;;
    *.md|docs/*|.claude/*|assets/*|third_party/lucide/*) echo none ;;
    *) echo unclaimed ;;
  esac
}

# What a project selects when it or something it depends on changes; the test projects it reaches select their own.
project_lanes() {
  case "$1" in
    tests/Keypaste.Core.Tests) echo core ;;
    tests/Keypaste.Cli.Tests) echo cli ;;
    tests/Keypaste.Mcp.Tests) echo mcp ;;
    tests/Keypaste.App.Tests) echo desktop-app ;;
    tests/Keypaste.Consistency.Tests) echo desktop-consistency ;;
    src/Keypaste.Cli) echo integration aot compat appcompat desktop-all ;;
    src/Keypaste.Mcp) echo integration aot compat desktop-all package ;;
    src/Keypaste.App) echo desktop-all appcompat markers package ;;
    tests/Keypaste.AppDriver) echo appcompat desktop-all ;;
    tests/Keypaste.FakeMcpClient) echo desktop-all ;;
    # No test project builds these two, so only a whole solution's format and -warnaserror build checks them.
    tests/Keypaste.MinimizeObserver) echo markers desktop-all ;;
    tests/Keypaste.VaultRestorer) echo compat core cli mcp ;;
  esac
}

normalize_path() {
  local part parts kept=() joined=''
  IFS=/ read -r -a parts <<< "$1"
  for part in "${parts[@]}"; do
    case "$part" in
      ''|.) ;;
      ..) if [ "${#kept[@]}" -gt 0 ]; then unset "kept[$((${#kept[@]} - 1))]"; fi ;;
      *) kept+=("$part") ;;
    esac
  done
  for part in ${kept[@]+"${kept[@]}"}; do joined="$joined/$part"; done
  echo "${joined#/}"
}

# Edges are read from the project files, so a new reference or linked file needs no edit here.
# VERIFY_SCOPE_NO_LINKS=1 ignores linked files; only verify-ci-scope.sh's negative control sets it.
project_edges=''
include_edges=''
read_project_graph() {
  local csproj dir kind include project helper
  for csproj in src/*/*.csproj tests/*/*.csproj third_party/KeePassLib/*.csproj; do
    [ -f "$csproj" ] || continue
    dir="${csproj%/*}"
    while read -r kind include; do
      include="$(normalize_path "$dir/${include//\\//}")"
      case "$kind" in
        ProjectReference) project_edges="$project_edges$dir ${include%/*}"$'\n' ;;
        *)
          case "$include" in "$dir"/*) continue ;; esac
          if [ "${VERIFY_SCOPE_NO_LINKS:-}" != 1 ]; then include_edges="$include_edges$dir $include"$'\n'; fi
          ;;
      esac
    done < <(sed -nE 's/.*<([A-Za-z]+)[[:space:]][^>]*Include="([^"]*)".*/\1 \2/p' "$csproj")
  done
  for project in tests/*; do
    for helper in $(runtime_helpers "$project"); do project_edges="$project_edges$project $helper"$'\n'; done
  done
}

owning_project() {
  local dir="$1" candidate
  while [ "$dir" != "${dir%/*}" ]; do
    dir="${dir%/*}"
    case "$dir" in src/?*|tests/?*|third_party/KeePassLib|third_party/KeePassLib/*) ;; *) return 0 ;; esac
    for candidate in "$dir"/*.csproj; do
      if [ -f "$candidate" ]; then echo "$dir"; return 0; fi
    done
  done
}

linking_projects() {
  local project pattern
  while read -r project pattern; do
    [ -n "$pattern" ] || continue
    # shellcheck disable=SC2254
    case "$1" in $pattern) echo "$project" ;; esac
  done <<< "$include_edges"
}

dependents_closure() {
  local found=" $* " grew=true dependent dependency
  [ "$#" -gt 0 ] || return 0
  while [ "$grew" = true ]; do
    grew=false
    while read -r dependent dependency; do
      [ -n "$dependency" ] || continue
      case "$found" in *" $dependency "*) ;; *) continue ;; esac
      case "$found" in *" $dependent "*) continue ;; esac
      found="$found$dependent "
      grew=true
    done <<< "$project_edges"
  done
  echo "$found"
}

dependencies_closure() {
  local found=" $* " grew=true dependent dependency
  while [ "$grew" = true ]; do
    grew=false
    while read -r dependent dependency; do
      [ -n "$dependency" ] || continue
      case "$found" in *" $dependent "*) ;; *) continue ;; esac
      case "$found" in *" $dependency "*) continue ;; esac
      found="$found$dependency "
      grew=true
    done <<< "$project_edges"
  done
  echo "$found"
}

lanes_for_path() {
  local fixed token project found=''
  fixed="$(path_lanes "$1")"
  if [ "$fixed" = everything ]; then echo everything; return; fi
  # shellcheck disable=SC2046
  for project in $(dependents_closure $(owning_project "$1") $(linking_projects "$1")); do
    found="$found $(project_lanes "$project")"
  done
  for token in $fixed; do
    case "$token" in unclaimed|none) ;; *) found="$found $token" ;; esac
  done
  if [ -n "${found// /}" ]; then
    echo "$found"
  elif [ "$fixed" != none ]; then
    echo everything
  fi
}

profiles_for_lanes() {
  local lane
  for lane in "$@"; do
    case "$lane" in
      core|cli|mcp|rules) echo backend ;;
      integration|pages) echo integration ;;
      desktop-*) echo desktop ;;
      scripts) echo scripts ;;
    esac
  done
}

# VERIFY_CHANGED_PATHS replaces the reading from git for the fixtures.
changed_paths() {
  if [ "${VERIFY_CHANGED_PATHS+set}" = set ]; then
    printf '%s\n' "$VERIFY_CHANGED_PATHS"
    return
  fi
  if [ -n "$since" ]; then
    git -c core.quotepath=off diff --name-only --no-renames "$since...HEAD" --
    return
  fi
  git -c core.quotepath=off diff --name-only --no-renames HEAD --
  git -c core.quotepath=off ls-files --others --exclude-standard
}

selected=''
select_everything() {
  local p
  selected=everything
  for p in "${heavy[@]}"; do printf -v "why_$p" '%s' "$1"; done
}

changed_count=0
select_profiles() {
  local changes path mapped p var counted source='git diff against HEAD, plus untracked files'
  if [ -n "$since" ]; then source="git diff $since...HEAD"; fi
  if [ "$everything" = true ]; then
    select_everything '--all'
    echo 'verify: --all selects every profile'
    return
  fi
  if ! changes="$(changed_paths)"; then
    select_everything 'the changes could not be read'
    echo 'verify: git could not list changes, so everything runs' >&2
    return
  fi
  read_project_graph
  while IFS= read -r path; do
    [ -n "$path" ] || continue
    changed_count=$((changed_count + 1))
    mapped="$(lanes_for_path "$path")"
    if [ "$mapped" = everything ]; then
      selected=everything
      mapped="${heavy[*]}"; path="$path, which selects everything"
    else
      if [ "$selected" != everything ]; then selected="$selected $mapped"; fi
      # shellcheck disable=SC2086
      mapped="$(profiles_for_lanes $mapped)"
    fi
    counted=' '
    for p in $mapped; do
      case "$counted" in *" $p "*) continue ;; esac
      counted="$counted$p "
      var="why_$p"
      if [ -z "${!var:-}" ]; then printf -v "$var" '%s' "$path"; fi
      var="hits_$p"
      printf -v "$var" '%s' "$(( ${!var:-0} + 1 ))"
    done
  done <<< "$changes"
  if [ "$changed_count" = 0 ] && { [ -n "$since" ] || [ "$plan" = true ]; }; then
    select_everything 'no changed path was found'
    echo "verify: no changed paths ($source), so everything runs"
    return
  fi
  echo "verify: $changed_count changed paths ($source)"
}

has_lane() {
  case " $selected " in *" everything "*|*" $1 "*) return 0 ;; *) return 1 ;; esac
}

# The runners each CI job marks a pass on, and whether an earlier trusted run already passed it there
# on inputs this commit shares: VERIFY_SATISFIED holds runner:mark pairs from lane-cache.sh (D-0404,
# D-0411). A mark is its lane's name, or the lane and the job for a lane app.yml splits into jobs.
lane_runners() {
  case "$1" in
    core|cli|mcp|integration|compat) echo ubuntu-24.04 windows-2025 macos-15 ;;
    rules|pages|scripts|desktop) echo ubuntu-24.04 ;;
    aot) echo ubuntu-22.04 ;;
    appcompat.workflows) echo ubuntu-24.04 windows-2025 ;;
    appcompat.firstrun) echo ubuntu-24.04 windows-2025 macos-15 ;;
    markers) echo ubuntu-24.04 macos-15 ;;
    package) { jq -r '.components.app.targets[].runner' release-targets.json 2>/dev/null || true; } | tr -d '\r' | tr '\n' ' ' ;;
  esac
}
lane_marks() {
  case "$1" in
    appcompat) echo appcompat.workflows appcompat.firstrun ;;
    *) echo "$1" ;;
  esac
}
is_satisfied() {
  case " ${VERIFY_SATISFIED:-} " in *" $1:$2 "*) return 0 ;; *) return 1 ;; esac
}
still_needed() {
  local mark runner runners
  for mark in $(lane_marks "$1"); do
    runners="$(lane_runners "$mark")"
    [ -n "$runners" ] || return 0
    for runner in $runners; do is_satisfied "$runner" "$mark" || return 0; done
  done
  return 1
}

lane_in() {
  case " $2 " in *" $1 "*) return 0 ;; *) return 1 ;; esac
}

# One entry per runner the test job still needs, with that runner's own lanes and test projects.
print_test_matrix() {
  local runner lane lanes backend filter entries=''
  for runner in ubuntu-24.04 windows-2025 macos-15; do
    lanes=''
    for lane in core cli mcp rules pages integration compat; do
      has_lane "$lane" || continue
      case " $(lane_runners "$lane") " in *" $runner "*) ;; *) continue ;; esac
      is_satisfied "$runner" "$lane" && continue
      lanes="$lanes $lane"
    done
    [ -n "$lanes" ] || continue
    backend='' filter=''
    if lane_in core "$lanes" && lane_in cli "$lanes" && lane_in mcp "$lanes"; then
      backend=all
    else
      if lane_in core "$lanes" || lane_in rules "$lanes"; then backend=tests/Keypaste.Core.Tests; fi
      if lane_in cli "$lanes"; then backend="$backend tests/Keypaste.Cli.Tests"; fi
      if lane_in mcp "$lanes"; then backend="$backend tests/Keypaste.Mcp.Tests"; fi
      backend="${backend# }"
      if [ "$backend" = tests/Keypaste.Core.Tests ] && ! lane_in core "$lanes"; then filter=Keypaste.Core.Tests.WorkflowRulesTests; fi
    fi
    entries="$entries,{\"os\":\"$runner\",\"lanes\":\"$lanes \",\"backend_tests\":\"$backend\",\"filter\":\"$filter\"}"
  done
  printf 'test_matrix=[%s]\n' "${entries#,}"
}

print_plan() {
  local lane chosen=' ' os='[]' backend='' desktop='' filter=''
  for lane in "${all_lanes[@]}"; do
    if [ "$lane" = desktop ]; then
      has_lane desktop-all || has_lane desktop-app || has_lane desktop-consistency || continue
    else
      has_lane "$lane" || continue
    fi
    still_needed "$lane" || continue
    chosen="$chosen$lane "
  done
  if has_lane core || has_lane cli || has_lane mcp || has_lane integration || has_lane compat; then
    os='["ubuntu-24.04","windows-2025","macos-15"]'
  elif has_lane rules || has_lane pages; then
    os='["ubuntu-24.04"]'
  fi
  if has_lane core && has_lane cli && has_lane mcp; then
    backend=all
  else
    if has_lane core || has_lane rules; then backend=tests/Keypaste.Core.Tests; fi
    if has_lane cli; then backend="$backend tests/Keypaste.Cli.Tests"; fi
    if has_lane mcp; then backend="$backend tests/Keypaste.Mcp.Tests"; fi
    backend="${backend# }"
    if [ "$backend" = tests/Keypaste.Core.Tests ] && ! has_lane core; then filter=Keypaste.Core.Tests.WorkflowRulesTests; fi
  fi
  if has_lane desktop-all; then
    desktop=all
  else
    if has_lane desktop-app; then desktop=tests/Keypaste.App.Tests; fi
    if has_lane desktop-consistency; then desktop="$desktop tests/Keypaste.Consistency.Tests"; fi
    desktop="${desktop# }"
  fi
  lane_in desktop "$chosen" || desktop=''
  printf 'lanes=%s\nbackend_os=%s\nbackend_tests=%s\ndesktop_tests=%s\nfilter=%s\n' "$chosen" "$os" "$backend" "$desktop" "$filter"
  print_test_matrix
  print_app_matrix
}

# The runners each app.yml job still needs. A package list jq could not read is "all", so a missing
# tool never skips a release target.
print_app_matrix() {
  local job mark runner runners list
  for job in compat:appcompat.workflows firstrun:appcompat.firstrun markers:markers package:package; do
    mark="${job#*:}"
    list=''
    if has_lane "${mark%%.*}"; then
      runners="$(lane_runners "$mark")"
      if [ -z "${runners// /}" ]; then
        printf 'app_%s_os=all\n' "${job%%:*}"
        continue
      fi
      for runner in $runners; do
        is_satisfied "$runner" "$mark" || list="$list,\"$runner\""
      done
    fi
    printf 'app_%s_os=[%s]\n' "${job%%:*}" "${list#,}"
  done
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
    case "$p" in workflows) reason='always runs' ;; esac
    if [ -z "$reason" ]; then
      printf 'verify: skip %-12s no changed path maps to it\n' "$p"
    elif [ "$reached" = false ]; then
      printf 'verify: skip %-12s before --from %s\n' "$p" "$from"
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
  if [ "$plan" = true ]; then
    select_profiles > /dev/null
    print_plan
    return
  fi
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
  if [ -n "$since" ]; then resume="$resume --since $since"; fi
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
