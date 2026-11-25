#!/usr/bin/env bash
# Refuses a version that has no section of its own in the changelog.
#
# Every release carries a real changelog section, and this is its
# gate. It lived in release.yml's guard under `if: ref_type == 'tag'`, so nothing but a tag ran it -
# a decision no fixture can drive. It is here so a fixture can drive it.
#
# THE MATCH IS WHOLE-LINE, and that is the whole point of the file. `grep -qF "## 0.2.0"` is
# satisfied by a `## 0.2.0-rc.1` heading, so a release could be cut against a candidate's notes -
# found by this repository's own release gate, red first. `grep -qxF` refuses that and still finds the real heading.
#
# Usage:
#   require-changelog-section.sh <version> [changelog]
set -euo pipefail

die() { echo "::error::$*" >&2; exit 1; }

VERSION="${1:-}"
CHANGELOG="${2:-CHANGELOG.md}"

[ -n "$VERSION" ] || die "usage: require-changelog-section.sh <version> [changelog]"
[ -f "$CHANGELOG" ] || die "no changelog at $CHANGELOG"

grep -qxF -- "## $VERSION" "$CHANGELOG" \
  || die "$CHANGELOG has no '## $VERSION' section; a release says what changed or it is not a release"

echo "  ok  $CHANGELOG has a section for $VERSION"
