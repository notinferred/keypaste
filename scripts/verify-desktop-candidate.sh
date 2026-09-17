#!/usr/bin/env bash
# Refuses a desktop installation candidate unless it is the package app.yml attested for its tag (4.7b).
#
# app.yml attests the MSI and AppImage it builds on a tag and uploads them with a .sha256 beside each,
# but publishes no Sigstore bundle, so `gh attestation verify` asks GitHub's attestation store and needs
# a token (D-0138). The .sha256 comes from the same job and proves only an intact transfer; the
# attestation is what binds the bytes to this repository's app workflow at that tag.
#
# What it refuses, before anything is installed:
#   - a file whose name is not the definition's package pattern for its version and RID;
#   - a file whose bytes differ from the .sha256 beside it;
#   - a file gh does not verify for this repository, app workflow and tag;
#   - a verifier that exits 0 without naming this file's digest.
#
# Usage:
#   verify-desktop-candidate.sh <package-file> <version> <rid>
#   verify-desktop-candidate.sh --selftest    # fake gh; no network, no credential
#
# Environment:
#   KEYPASTE_RELEASE_DEFINITION  the definition to read (default: release-targets.json)
#   GH_TOKEN                     read access to this repository's attestations
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
ROOT="$(cd "$(dirname "$SELF")/.." && pwd)"
readonly ROOT
readonly CR=$'\r'

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d "$CR"; }
definition() { printf '%s' "${KEYPASTE_RELEASE_DEFINITION:-$ROOT/release-targets.json}"; }

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum < "$1" | awk '{print $1}'
  else
    shasum -a 256 < "$1" | awk '{print $1}'
  fi
}

expected_name() {
  local version="$1" rid="$2" kind
  case "$rid" in
    win-x64) kind=msi ;;
    linux-x64) kind=appimage ;;
    *) die "no installation candidate is declared for $rid" ;;
  esac
  # shellcheck disable=SC2016 # jq expands these variables, not bash.
  jqr --arg rid "$rid" --arg kind "$kind" \
    '[.components.app.targets[] | select(.rid == $rid) | .packages[]? | select(.kind == $kind) | .pattern] | if length == 1 then .[0] else empty end' \
    "$(definition)" | sed -e "s/{version}/$version/g" -e "s/{rid}/$rid/g"
}

verify() {
  local file="$1" version="$2" rid="$3" name repo workflow recorded digest out
  command -v gh >/dev/null 2>&1 || die "no gh"
  command -v jq >/dev/null 2>&1 || die "no jq"
  [ -f "$file" ] || die "no candidate at $file"

  name="$(expected_name "$version" "$rid")"
  [ -n "$name" ] || die "$(definition) does not declare exactly one installation package for app/$rid"
  [ "$(basename "$file")" = "$name" ] || die "$(basename "$file") is not the declared candidate name $name"

  [ -f "$file.sha256" ] || die "no $name.sha256 beside the candidate"
  digest="$(sha256_of "$file")"
  recorded="$(awk 'NR == 1 {print $1}' "$file.sha256" | tr -d "$CR")"
  [ "$digest" = "$recorded" ] || die "$name does not match its .sha256"

  repo="$(jqr '.components.cli.provenance.repository // empty' "$(definition)")"
  workflow="$(jqr '.components.app.workflow // empty' "$(definition)")"
  [ -n "$repo" ] && [ -n "$workflow" ] || die "$(definition) names no provenance repository or app workflow"
  out="$(gh attestation verify "$file" \
    --repo "$repo" \
    --signer-workflow "$repo/$workflow" \
    --source-ref "refs/tags/v$version" \
    --deny-self-hosted-runners \
    --format json 2>/dev/null)" || die "$name is not attested by $repo's $workflow for v$version"
  # Compared as text: jq 1.6's -e exits 0 on empty input, which would believe a silent verifier (D-0106).
  [ "$(printf '%s' "$out" | jq -r --arg d "$digest" \
    'type == "array" and length > 0 and any(.[]; any(.verificationResult.statement.subject[]?; .digest.sha256 == $d))' \
    2>/dev/null)" = "true" ] || die "$name is not attested by $repo's $workflow for v$version"

  echo "ok: $name sha256 $digest was built by $repo's $workflow for v$version"
}

# ---------------------------------------------------------------------------
selftest() {
  local work v=9.9.9 cases=0 failures=0
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN
  mkdir -p "$work/bin"

  # The attestation store is a JSON file naming the identity and the digests it covers; the fake gh
  # verifies like the real one: every flag must match and the file's digest must be a subject.
  cat > "$work/bin/gh" <<'FAKE'
#!/usr/bin/env bash
set -uo pipefail
[ "$1 $2" = "attestation verify" ] || exit 64
file="$3"; shift 3
while [ $# -gt 0 ]; do
  case "$1" in
    --repo) repo="$2"; shift 2 ;;
    --signer-workflow) workflow="$2"; shift 2 ;;
    --source-ref) ref="$2"; shift 2 ;;
    *) shift ;;
  esac
done
digest="$(sha256sum < "$file" | awk '{print $1}')"
jq -e --arg r "$repo" --arg w "$workflow" --arg f "$ref" --arg d "$digest" \
  '.repo == $r and .workflow == $w and .ref == $f and (.subjects | index($d))' "$KEYPASTE_FAKE_STORE" >/dev/null || exit 1
case "${KEYPASTE_FAKE_GH:-}" in
  silent) exit 0 ;;
  other-digest) digest="0000000000000000000000000000000000000000000000000000000000000000" ;;
esac
printf '[{"verificationResult":{"statement":{"subject":[{"digest":{"sha256":"%s"}}]}}}]\n' "$digest"
FAKE
  chmod +x "$work/bin/gh"

  export KEYPASTE_RELEASE_DEFINITION="$ROOT/release-targets.json"
  export KEYPASTE_FAKE_STORE="$work/store.json"
  local msi appimage
  msi="$(expected_name "$v" win-x64)"
  appimage="$(expected_name "$v" linux-x64)"
  [ -n "$msi" ] && [ -n "$appimage" ] || die "the definition declares no MSI or AppImage candidate"

  # stage <file> [workflow] [ref] [repo]
  stage() {
    local name="$1" workflow="${2:-notinferred/keypaste/.github/workflows/app.yml}" ref="${3:-refs/tags/v$v}"
    local repo="${4:-notinferred/keypaste}"
    rm -rf "$work/dist"; mkdir -p "$work/dist"
    printf 'the bytes of %s\n' "$name" > "$work/dist/$name"
    sha256sum "$work/dist/$name" | awk -v n="$name" '{print $1 "  " n}' > "$work/dist/$name.sha256"
    jq -n --arg r "$repo" --arg w "$workflow" --arg f "$ref" --arg d "$(sha256_of "$work/dist/$name")" \
      '{repo: $r, workflow: $w, ref: $f, subjects: [$d]}' > "$KEYPASTE_FAKE_STORE"
  }

  expect() { # expect <accept|refuse> <name> <text> -- command...
    local verdict="$1" name="$2" text="$3" rc=0
    shift 4
    cases=$((cases + 1))
    PATH="$work/bin:$PATH" "$@" > "$work/out" 2>&1 || rc=$?
    if { [ "$verdict" = accept ] && [ "$rc" -ne 0 ]; } || { [ "$verdict" = refuse ] && [ "$rc" -eq 0 ]; } \
      || ! grep -qF -- "$text" "$work/out"; then
      echo "::error::$name: expected $verdict saying '$text', got exit $rc"
      sed 's/^/    /' "$work/out"
      failures=$((failures + 1))
      return
    fi
    printf '  %-7s %s\n' "$verdict" "$name"
  }

  [ "$(PATH="$work/bin:$PATH" command -v gh)" = "$work/bin/gh" ] || die "the fake gh is not first on PATH"

  stage "$msi"
  expect accept genuine-msi "was built by" -- bash "$SELF" "$work/dist/$msi" "$v" win-x64
  stage "$appimage"
  expect accept genuine-appimage "was built by" -- bash "$SELF" "$work/dist/$appimage" "$v" linux-x64

  stage "$msi"; printf 'the bytes Of %s\n' "$msi" > "$work/dist/$msi"
  expect refuse changed-byte "does not match its .sha256" -- bash "$SELF" "$work/dist/$msi" "$v" win-x64

  stage "$msi"; printf 'the bytes Of %s\n' "$msi" > "$work/dist/$msi"
  sha256sum "$work/dist/$msi" | awk -v n="$msi" '{print $1 "  " n}' > "$work/dist/$msi.sha256"
  expect refuse changed-byte-with-rewritten-sha256 "is not attested" -- bash "$SELF" "$work/dist/$msi" "$v" win-x64

  stage "$msi"; rm -f "$work/dist/$msi.sha256"
  expect refuse no-sha256 "no $msi.sha256" -- bash "$SELF" "$work/dist/$msi" "$v" win-x64

  stage "$msi"; mv "$work/dist/$msi" "$work/dist/renamed.msi"; mv "$work/dist/$msi.sha256" "$work/dist/renamed.msi.sha256"
  expect refuse undeclared-name "is not the declared candidate name" -- bash "$SELF" "$work/dist/renamed.msi" "$v" win-x64

  stage "$msi"
  expect refuse another-version "is not the declared candidate name" -- bash "$SELF" "$work/dist/$msi" 9.9.8 win-x64

  stage "$msi" notinferred/keypaste/.github/workflows/release.yml
  expect refuse release-workflow "is not attested" -- bash "$SELF" "$work/dist/$msi" "$v" win-x64

  stage "$appimage" notinferred/keypaste/.github/workflows/app.yml refs/tags/v9.9.8
  expect refuse another-tag "is not attested" -- bash "$SELF" "$work/dist/$appimage" "$v" linux-x64

  stage "$appimage" notinferred/other/.github/workflows/app.yml refs/tags/v$v notinferred/other
  expect refuse another-repository "is not attested" -- bash "$SELF" "$work/dist/$appimage" "$v" linux-x64

  stage "$msi"
  expect refuse verifier-says-nothing "is not attested" -- env KEYPASTE_FAKE_GH=silent bash "$SELF" "$work/dist/$msi" "$v" win-x64
  expect refuse verifier-names-another-digest "is not attested" -- env KEYPASTE_FAKE_GH=other-digest bash "$SELF" "$work/dist/$msi" "$v" win-x64

  # Negative control: without the digest check, a verifier that exits 0 about another digest is believed.
  sed 's/any(\.verificationResult\.statement\.subject\[\]?; \.digest\.sha256 == \$d)/true/' "$SELF" > "$work/weak.sh"
  cmp -s "$SELF" "$work/weak.sh" && die "the weakened copy still checks the verified digest"
  mkdir -p "$work/weak/scripts" && mv "$work/weak.sh" "$work/weak/scripts/verify-desktop-candidate.sh"
  expect accept weakened-believes-another-digest "was built by" -- env KEYPASTE_FAKE_GH=other-digest bash "$work/weak/scripts/verify-desktop-candidate.sh" "$work/dist/$msi" "$v" win-x64

  [ "$failures" -eq 0 ] || die "$failures of $cases desktop candidate cases failed"
  echo "ok: $cases cases. A genuine MSI and AppImage verify. A changed byte, with or without a rewritten .sha256,"
  echo "    a missing .sha256, an undeclared name or version, the release workflow, another tag or repository and"
  echo "    a verifier that proves nothing all refuse; the weakened copy does not."
}

case "${1:-}" in
  --selftest) [ $# -eq 1 ] || die "usage: verify-desktop-candidate.sh --selftest"; selftest ;;
  '' | -*) die "usage: verify-desktop-candidate.sh <package-file> <version> <rid> | --selftest" ;;
  *) [ $# -eq 3 ] || die "usage: verify-desktop-candidate.sh <package-file> <version> <rid>"; verify "$1" "$2" "$3" ;;
esac
