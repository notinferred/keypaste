#!/usr/bin/env bash
# Decide which ci.yml jobs a change can possibly break, from the list of changed paths on stdin.
#
# One line per path in; three lines out, each `name=true|false`:
#
#   full_test  the three-OS test matrix. Anything that compiles, is executed by a gate, or shapes
#              the restore: src/, tests/, scripts/ (except the demo recording pipeline), the
#              vendored library, the build props, the solutions, the SDK pin, the lock files, the
#              editorconfig, and this workflow.
#   compat     the three-OS KeePassXC matrix. Only what can change the bytes keypaste writes or how
#              the fixture is built and asserted: the core, the CLI that generates the fixture, the
#              vendored library, the two compat scripts and the fixture script, the props, the SDK
#              pin, the lock files, and this workflow.
#   aot        the NativeAOT publish. What the trimmer sees: src/, the vendored library, the props,
#              the SDK pin, the lock files, the trim baseline and its script, and this workflow.
#
# Nothing here decides whether the gate job runs; it always does when the workflow does. A change
# that matches none of the three - the five pinned pages and the demo fixture they run, for instance - gets the gate and a
# Linux-only build with the demo gate (the `pages` job in ci.yml), which is what those pages need.
#
# The rule is allow-to-run: a path is matched by what it is, and a new directory nobody listed here
# runs everything, because `full_test` also fires on any path outside the documents. Fail closed.
#
# Exercised locally with hand-written path lists before it was trusted (DECISIONS.md D-0078); an
# empty input means "nothing changed that we can see" and runs everything for the same reason.
set -euo pipefail

full_test=false; compat=false; aot=false; any=false

while IFS= read -r p; do
  [ -n "$p" ] || continue
  any=true
  case "$p" in
    # --- documents and files that no JOB in ci.yml reads: never a reason to run anything ------------
    # This bucket is deliberately WIDER than ci.yml's paths-ignore, and the two are not drifting.
    # paths-ignore decides whether the workflow starts at all and stays narrow so a path nobody
    # thought about still runs everything; this decides which jobs run once it has started. A path
    # here but not there costs the scope and gate jobs and nothing more. Adding one there instead is
    # a second list to keep in step, which is the bug D-0080 had to fix between paths-ignore and
    # scripts/verify-claims.sh. The documents here are gated - docs.yml runs verify-claims.sh on them.
    docs/PRODUCT.md|CLAUDE.md|docs/STEPS.md|DECISIONS.md|CHANGELOG.md|THREATS.md|SECURITY.md|\
    THIRD_PARTY_NOTICES.md|CONTRIBUTING.md|LICENSE|docs/mcp-setup.md|docs/replace-dotenv.md|\
    docs/desktop.md|site/README.md|site/public/thanks/*|.gitignore|.gitattributes|\
    .github/dependabot.yml|.github/workflows/app.yml|.github/workflows/release.yml|\
    .github/workflows/install.yml|.github/workflows/dco.yml|.github/workflows/docs.yml|\
    .claude/*|docs/policy.md|docs/approvals.md|\
    scripts/demo/README.md|scripts/demo/build-demo-binaries.sh|scripts/demo/demo-env.sh|\
    scripts/demo/demo.bashrc|scripts/demo/demo.tmux.conf|scripts/demo/install-recording-tools.sh|\
    scripts/demo/make-demo-fixture.sh|scripts/demo/record-demo.sh|scripts/demo/render-demo-gif.sh|\
    site/src/*|site/package.json|site/package-lock.json|site/wrangler.jsonc|site/schema.sql)
      ;;
    # --- the five pinned pages: the demo gate, on one OS, is all they need -----------------------
    README.md|launch.md|docs/demo.md|docs/keepass-and-agents.md|site/public/index.html|scripts/demo/deploy.sh)
      ;;
    # --- what can change the bytes keypaste writes, or how that is checked -----------------------
    src/Keypaste.Core/*|src/Keypaste.Cli/*|third_party/*|scripts/make-compat-fixture.sh|\
    scripts/verify-keepassxc-compat.sh|scripts/verify-keepassxc-writeback.sh|\
    Directory.Build.props|Directory.Packages.props|global.json|NuGet.config|*/packages.lock.json|\
    .github/workflows/ci.yml)
      compat=true; aot=true; full_test=true ;;
    # --- what the trimmer sees but the vault writer does not ------------------------------------
    src/*|scripts/verify-aot-trim.sh|scripts/aot-trim-baseline.txt)
      aot=true; full_test=true ;;
    # --- everything else that is code or a gate: the test matrix ---------------------------------
    *)
      full_test=true ;;
  esac
done

if [ "$any" = false ]; then full_test=true; compat=true; aot=true; fi

echo "full_test=$full_test"
echo "compat=$compat"
echo "aot=$aot"
