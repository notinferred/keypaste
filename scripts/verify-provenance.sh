#!/usr/bin/env bash
# Checks that a keypaste release was built by this repository's release workflow for its own tag, and
# that every published asset is the one it attested (3.8).
#
# release.yml attests every asset of a release and its manifest in one Sigstore bundle, and publishes
# the bundle and the manifest beside them. This script needs no GitHub login: with a bundle on disk,
# `gh attestation verify` checks the signature against Sigstore's public trust root (D-0138).
#
# What it refuses:
#   - a manifest the bundle does not attest for this repository, workflow and tag;
#   - a manifest that does not name exactly the assets a whole release holds;
#   - an asset whose bytes are not the ones the manifest records, or that the bundle does not attest;
#   - a verifier that exits 0 without saying which digest it verified, or names another one.
#
# What it does not prove: that the source is correct, that the build is reproducible (O-0012), or who
# may run the release workflow. A compromised workflow run attests whatever it built (THREATS T-21).
#
# Usage:
#   verify-provenance.sh <version> [download-dir]   # download anonymously from the release origin
#   verify-provenance.sh --dir <version> <dir>       # a directory already holding the release
#   verify-provenance.sh --selftest                  # fake gh and curl; no network, no credential
#
# Environment:
#   KEYPASTE_RELEASE_DEFINITION  the definition to read   (default: release-targets.json)
#   KEYPASTE_ORIGIN_OVERRIDE     fetch from here instead of the definition's origin (fixtures)
#   KEYPASTE_TRUSTED_ROOT        a `gh attestation trusted-root` file, for offline verification
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
readonly ROOT="$(cd "$(dirname "$SELF")/.." && pwd)"
readonly COMPLETION="$ROOT/scripts/release-completion.sh"
readonly CR=$'\r'

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d "$CR"; }

# Read from stdin: given a path containing a backslash, GNU sha256sum prefixes its digest with one.
sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum < "$1" | awk '{print $1}'
  else
    shasum -a 256 < "$1" | awk '{print $1}'
  fi
}

definition() { printf '%s' "${KEYPASTE_RELEASE_DEFINITION:-$ROOT/release-targets.json}"; }

# Prints nothing and returns 1 unless gh verified this exact file for this repository, workflow and tag.
attested() {
  local file="$1" bundle="$2" version="$3" repo workflow out digest
  repo="$(jqr '.components.cli.provenance.repository // empty' "$(definition)")"
  workflow="$(jqr '.components.cli.workflow // empty' "$(definition)")"
  [ -n "$repo" ] && [ -n "$workflow" ] || die "$(definition) names no provenance repository or release workflow"
  local args=(attestation verify "$file" --bundle "$bundle"
    --repo "$repo"
    --signer-workflow "$repo/$workflow"
    --source-ref "refs/tags/v$version"
    --deny-self-hosted-runners
    --format json)
  [ -z "${KEYPASTE_TRUSTED_ROOT:-}" ] || args+=(--custom-trusted-root "$KEYPASTE_TRUSTED_ROOT")
  out="$(gh "${args[@]}" 2>/dev/null)" || return 1
  digest="$(sha256_of "$file")"
  # Compared as text: jq 1.6's -e exits 0 on empty input, which would believe a silent verifier (D-0106).
  [ "$(printf '%s' "$out" | jq -r --arg d "$digest" \
    'type == "array" and length > 0 and any(.[]; any(.verificationResult.statement.subject[]?; .digest.sha256 == $d))' \
    2>/dev/null)" = "true" ]
}

verify_dir() {
  local version="$1" dir="$2" manifest bundle name want_sha want_bytes checked=0
  command -v gh >/dev/null 2>&1 || die "no gh; install GitHub CLI (no login is needed)"
  command -v jq >/dev/null 2>&1 || die "no jq"
  manifest="$(KEYPASTE_RELEASE_DEFINITION="$(definition)" bash "$COMPLETION" manifest-name "$version")"
  bundle="$(KEYPASTE_RELEASE_DEFINITION="$(definition)" bash "$COMPLETION" bundle-name "$version")"
  [ -f "$dir/$bundle" ] || die "$dir has no $bundle, so nothing attests this release"
  [ -f "$dir/$manifest" ] || die "$dir has no $manifest, so there is no list of what was attested"

  attested "$dir/$manifest" "$dir/$bundle" "$version" \
    || die "$manifest is not attested by $bundle for this repository's release workflow at v$version"
  echo "  attested  $manifest"

  [ "$(jqr '.version' "$dir/$manifest")" = "$version" ] || die "$manifest describes another version"
  diff <(KEYPASTE_RELEASE_DEFINITION="$(definition)" bash "$COMPLETION" names "$version" | sort) \
       <(jqr '.assets[].name' "$dir/$manifest" | sort) >/dev/null \
    || die "$manifest does not name exactly the assets a whole $version holds"

  while IFS=$'\t' read -r name want_sha want_bytes; do
    [ -n "$name" ] || continue
    [ -f "$dir/$name" ] || die "$dir has no $name"
    [ "$(sha256_of "$dir/$name")" = "$want_sha" ] || die "$name does not match the manifest"
    [ "$(wc -c < "$dir/$name" | tr -d '[:space:]')" = "$want_bytes" ] || die "$name is not the size the manifest records"
    attested "$dir/$name" "$dir/$bundle" "$version" \
      || die "$name is not attested by $bundle for this repository's release workflow at v$version"
    echo "  attested  $name"
    checked=$((checked + 1))
  done < <(jqr '.assets[] | [.name, .sha256, (.bytes|tostring)] | @tsv' "$dir/$manifest")

  [ "$checked" -gt 0 ] || die "the manifest named no assets, so nothing was checked"
  echo "ok: $manifest and all $checked assets of $version were built by $(jqr '.components.cli.provenance.repository' "$(definition)")'s release workflow for v$version"
}

download() {
  local version="$1" dir="$2" origin name
  command -v curl >/dev/null 2>&1 || die "no curl"
  origin="${KEYPASTE_ORIGIN_OVERRIDE:-$(jqr --arg v "$version" '.components.cli.origin | split("{version}") | join($v)' "$(definition)")}"
  mkdir -p "$dir"
  while IFS= read -r name; do
    [ -n "$name" ] || continue
    curl -fsSL --max-time 120 -o "$dir/$name" "$origin$name" || die "$origin$name could not be downloaded"
  done < <(
    KEYPASTE_RELEASE_DEFINITION="$(definition)" bash "$COMPLETION" names "$version"
    KEYPASTE_RELEASE_DEFINITION="$(definition)" bash "$COMPLETION" manifest-name "$version"
    KEYPASTE_RELEASE_DEFINITION="$(definition)" bash "$COMPLETION" bundle-name "$version"
  )
  echo "  downloaded $version from $origin without credentials"
}

# ---------------------------------------------------------------------------
selftest() {
  local work v=9.9.9 cases=0 failures=0
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN
  mkdir -p "$work/bin"

  # A bundle here is JSON naming the identity that signed it and the digests it covers. The fake gh
  # verifies like the real one: the identity must match every flag and the file's digest must be a subject.
  cat > "$work/bin/gh" <<'FAKE'
#!/usr/bin/env bash
set -uo pipefail
[ "$1 $2" = "attestation verify" ] || exit 64
file="$3"; shift 3
while [ $# -gt 0 ]; do
  case "$1" in
    --bundle) bundle="$2"; shift 2 ;;
    --repo) repo="$2"; shift 2 ;;
    --signer-workflow) workflow="$2"; shift 2 ;;
    --source-ref) ref="$2"; shift 2 ;;
    *) shift ;;
  esac
done
digest="$(sha256sum < "$file" | awk '{print $1}')"
jq -e --arg r "$repo" --arg w "$workflow" --arg f "$ref" --arg d "$digest" \
  '.repo == $r and .workflow == $w and .ref == $f and (.subjects | index($d))' "$bundle" >/dev/null || exit 1
case "${KEYPASTE_FAKE_GH:-}" in
  silent) exit 0 ;;
  other-digest) digest="0000000000000000000000000000000000000000000000000000000000000000" ;;
esac
printf '[{"verificationResult":{"statement":{"subject":[{"digest":{"sha256":"%s"}}]}}}]\n' "$digest"
FAKE
  cat > "$work/bin/curl" <<'FAKE'
#!/usr/bin/env bash
out=""; url=""
while [ $# -gt 0 ]; do case "$1" in -o) out="$2"; shift 2 ;; --max-time) shift 2 ;; -*) shift ;; *) url="$1"; shift ;; esac; done
[ -f "$KEYPASTE_SERVED/${url##*/}" ] || exit 22
cp "$KEYPASTE_SERVED/${url##*/}" "$out"
FAKE
  chmod +x "$work/bin/gh" "$work/bin/curl"

  export KEYPASTE_RELEASE_DEFINITION="$ROOT/release-targets.json"
  export KEYPASTE_ORIGIN_OVERRIDE="https://fixture.invalid/v$v/"
  export KEYPASTE_SERVED="$work/served"
  local subject="$SELF" names manifest bundle
  names="$(bash "$COMPLETION" names "$v")"
  manifest="$(bash "$COMPLETION" manifest-name "$v")"
  bundle="$(bash "$COMPLETION" bundle-name "$v")"

  # stage [repo] [workflow] [ref] [asset to leave unattested]
  stage() {
    local repo="${1:-notinferred/keypaste}" workflow="${2:-notinferred/keypaste/.github/workflows/release.yml}"
    local ref="${3:-refs/tags/v$v}" skip="${4:-}" n subjects
    rm -rf "$work/dist" "$work/served"; mkdir -p "$work/dist"
    while IFS= read -r n; do printf 'the bytes of %s\n' "$n" > "$work/dist/$n"; done <<< "$names"
    bash "$COMPLETION" record "$v" "v$v" deadbeef "$work/dist" > "$work/dist/$manifest"
    subjects="$(cd "$work/dist" && for n in $names "$manifest"; do [ "$n" = "$skip" ] || sha256sum "$n" | awk '{print $1}'; done | jq -R . | jq -s .)"
    jq -n --arg r "$repo" --arg w "$workflow" --arg f "$ref" --argjson s "$subjects" \
      '{repo: $r, workflow: $w, ref: $f, subjects: $s}' > "$work/dist/$bundle"
    cp -R "$work/dist" "$work/served"
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

  stage
  expect accept genuine-release-downloaded "all 11 assets" -- bash "$subject" "$v" "$work/get"
  expect accept genuine-staged-directory "all 11 assets" -- bash "$subject" --dir "$v" "$work/dist"
  mkdir -p "$work/back\slash" && cp -R "$work/dist" "$work/back\slash/"
  expect accept path-with-a-backslash "all 11 assets" -- bash "$subject" --dir "$v" "$work/back\slash/dist"

  stage; printf 'the bytes Of %s\n' "$(sed -n 1p <<< "$names")" > "$work/served/$(sed -n 1p <<< "$names")"
  expect refuse changed-byte "does not match the manifest" -- bash "$subject" "$v" "$work/get1"

  stage; first="$(sed -n 1p <<< "$names")"; printf 'replaced\n' > "$work/dist/$first"
  jq --arg n "$first" --arg s "$(sha256_of "$work/dist/$first")" --argjson b "$(wc -c < "$work/dist/$first")" \
    '(.assets[] | select(.name == $n)) |= (.sha256 = $s | .bytes = $b)' "$work/served/$manifest" > "$work/dist/$manifest"
  expect refuse manifest-rewritten-to-match "is not attested" -- bash "$subject" --dir "$v" "$work/dist"

  stage notinferred/other notinferred/other/.github/workflows/release.yml
  expect refuse wrong-repository "is not attested" -- bash "$subject" --dir "$v" "$work/dist"

  stage notinferred/keypaste notinferred/keypaste/.github/workflows/ci.yml
  expect refuse unrelated-workflow "is not attested" -- bash "$subject" --dir "$v" "$work/dist"

  stage notinferred/keypaste notinferred/keypaste/.github/workflows/release.yml refs/tags/v9.9.8
  expect refuse another-tag "is not attested" -- bash "$subject" --dir "$v" "$work/dist"

  stage "" "" "" "$(sed -n 2p <<< "$names")"
  expect refuse asset-left-out-of-the-bundle "is not attested" -- bash "$subject" --dir "$v" "$work/dist"

  stage; rm -f "$work/served/$bundle"
  expect refuse bundle-not-published "could not be downloaded" -- bash "$subject" "$v" "$work/get2"

  stage; jq '.assets |= .[1:]' "$work/dist/$manifest" > "$work/short" && mv "$work/short" "$work/dist/$manifest"
  jq --arg s "$(sha256_of "$work/dist/$manifest")" '.subjects += [$s]' "$work/dist/$bundle" > "$work/b" && mv "$work/b" "$work/dist/$bundle"
  expect refuse attested-short-manifest "does not name exactly" -- bash "$subject" --dir "$v" "$work/dist"

  stage
  expect refuse verifier-says-nothing "is not attested" -- env KEYPASTE_FAKE_GH=silent bash "$subject" --dir "$v" "$work/dist"
  expect refuse verifier-names-another-digest "is not attested" -- env KEYPASTE_FAKE_GH=other-digest bash "$subject" --dir "$v" "$work/dist"

  # Negative control: without the digest check, a verifier that exits 0 about another digest is believed.
  mkdir -p "$work/weak/scripts"
  cp "$COMPLETION" "$work/weak/scripts/"
  sed 's/any(\.verificationResult\.statement\.subject\[\]?; \.digest\.sha256 == \$d)/true/' "$SELF" > "$work/weak/scripts/verify-provenance.sh"
  cmp -s "$SELF" "$work/weak/scripts/verify-provenance.sh" && die "the weakened copy still checks the verified digest"
  expect accept weakened-believes-another-digest "all 11 assets" -- env KEYPASTE_FAKE_GH=other-digest bash "$work/weak/scripts/verify-provenance.sh" --dir "$v" "$work/dist"

  [ "$failures" -eq 0 ] || die "$failures of $cases provenance cases failed"
  echo "ok: $cases cases. A path with a backslash verifies. A changed byte, a rewritten manifest, another repository,"
  echo "    workflow or tag, an unattested or unpublished asset, a short manifest and a verifier that proves nothing"
  echo "    all refuse; the weakened copy does not."
}

case "${1:-}" in
  --selftest) [ $# -eq 1 ] || die "usage: verify-provenance.sh --selftest"; selftest ;;
  --dir) [ $# -eq 3 ] || die "usage: verify-provenance.sh --dir <version> <dir>"; verify_dir "$2" "$3" ;;
  '' | -*) die "usage: verify-provenance.sh <version> [download-dir] | --dir <version> <dir> | --selftest" ;;
  *)
    [ $# -le 2 ] || die "usage: verify-provenance.sh <version> [download-dir]"
    dir="${2:-$(mktemp -d)}"
    download "$1" "$dir"
    verify_dir "$1" "$dir"
    ;;
esac
