#!/usr/bin/env bash
# Drives the three release decisions with fixtures, so none of them is first executed by a real tag.
#
# require-changelog-section.sh, require-tag-matches-source.sh and require-release-assets.sh were
# step bodies in release.yml's guard and publish job, behind ref_type == tag and behind publish's
# own gate. A decision only a tag can reach belongs in a script a fixture can drive. This
# is that fixture, and it runs on every push.
#
# The assets phase builds a whole fake release in a scratch directory - real files, real checksums -
# and breaks exactly one thing per case, so a refusal can only be about the thing that was broken.
# A case that fails for two reasons at once proves nothing about either.
#
# Usage:
#   verify-release-preflight.sh
#
# NEGATIVE CONTROL: two, one per direction that has a previous wrong answer on record.
#   - the changelog lookup weakened back to an unanchored match must ACCEPT 0.2.0 against a file
#     that only has a 0.2.0-rc.1 heading, a defect this gate has already caught;
#   - the asset count with its published floor deleted must ACCEPT a release that dropped a target,
#     which is the edit equality alone agrees with.
# If either weakened copy refuses, this gate fails.
set -euo pipefail

# The expected number is counted here rather than written down: a floor somebody raises by hand
# falls behind as cases are added, and a case that stops being driven then passes as a smaller suite.
readonly SELF="${BASH_SOURCE[0]}"
declared_cases() {
  local n
  n="$(grep -cE '^[[:space:]]*run_case ' "$SELF" || true)"
  case "$n" in "" | 0 | *[!0-9]*) echo "" ;; *) echo "$n" ;; esac
}

readonly CHANGELOG_CHECK="${KEYPASTE_CHANGELOG_CHECK:-scripts/require-changelog-section.sh}"
readonly VERSION_CHECK="${KEYPASTE_VERSION_CHECK:-scripts/require-tag-matches-source.sh}"
readonly ASSETS_CHECK="${KEYPASTE_ASSETS_CHECK:-scripts/require-release-assets.sh}"
readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-release-targets.json}"

die() { echo "::error::$*" >&2; exit 1; }

for f in "$CHANGELOG_CHECK" "$VERSION_CHECK" "$ASSETS_CHECK" "$DEFINITION"; do
  [ -f "$f" ] || die "nothing to check at $f"
done
command -v jq >/dev/null 2>&1 || die "no jq; this gate reads the definition and cannot run without it"

WORK="$(mktemp -d)"
trap 'chmod -R u+w "$WORK" 2>/dev/null || true; rm -rf "$WORK"' EXIT

CR="$(printf '\r')"

cases=0
accepted=0

# run_case <name> <expect-rc> <expect-in-output> -- <command...>
run_case() {
  local name="$1" want_rc="$2" want_text="$3" rc=0
  shift 4
  cases=$((cases + 1))
  "$@" >"$WORK/out.txt" 2>&1 || rc=$?
  [ "$rc" -eq 0 ] && accepted=$((accepted + 1))
  [ "$rc" -eq "$want_rc" ] || { sed -n '1,12p' "$WORK/out.txt" >&2; die "$name: exit $rc, expected $want_rc"; }
  grep -qF "$want_text" "$WORK/out.txt" \
    || { sed -n '1,12p' "$WORK/out.txt" >&2; die "$name: exited $rc but never said it"; }
  local verdict=refused
  [ "$rc" -eq 0 ] && verdict=accepted
  printf '  %-9s %-34s exit %s\n' "$verdict" "$name" "$rc"
}

echo "== a version has a section of its own, or it is not released"
CL="$WORK/CHANGELOG.md"
printf '# Changelog\n\n## Unreleased\n\n## 0.2.0-rc.1\n\n## 0.1.0\n' > "$CL"

run_case "changelog-present" 0 "has a section for 0.1.0" -- bash "$CHANGELOG_CHECK" 0.1.0 "$CL"
run_case "changelog-rc-present" 0 "has a section for 0.2.0-rc.1" -- bash "$CHANGELOG_CHECK" 0.2.0-rc.1 "$CL"
run_case "changelog-absent" 1 "has no" -- bash "$CHANGELOG_CHECK" 0.3.0 "$CL"
run_case "changelog-rc-is-not-the-release" 1 "has no" -- bash "$CHANGELOG_CHECK" 0.2.0 "$CL"
run_case "changelog-missing-file" 1 "no changelog at" -- bash "$CHANGELOG_CHECK" 0.1.0 "$WORK/absent.md"

echo "== a tag names the version the source declares, and may add a suffix"
run_case "version-exact" 0 "agrees with the source" -- env KEYPASTE_VERSION_PREFIX=0.1.0 bash "$VERSION_CHECK" v0.1.0
run_case "version-candidate" 0 "agrees with the source" -- env KEYPASTE_VERSION_PREFIX=0.1.0 bash "$VERSION_CHECK" v0.1.0-rc.1
run_case "version-disagrees" 1 "but the source declares 0.1.0" -- env KEYPASTE_VERSION_PREFIX=0.1.0 bash "$VERSION_CHECK" v0.2.0
run_case "version-unreadable" 1 "no project at" -- env KEYPASTE_VERSION_PROJECT="$WORK/none.csproj" bash "$VERSION_CHECK" v0.1.0
run_case "version-not-a-version" 1 "is not a version" -- env KEYPASTE_VERSION_PREFIX=0.1.0 bash "$VERSION_CHECK" vlatest

echo "== a staged directory is a whole release, or it is not published"
readonly V='9.9.9'

archives_for() {
  jq -r --arg v "$V" '.components.cli as $c | $c.targets[] | . as $t | $c.archive_pattern
    | split("{version}") | join($v) | split("{rid}") | join($t.rid) | split("{ext}") | join($t.archive)' "$1"     | tr -d "$CR"
}

stage() {
  local dir="$1" def="${2:-$DEFINITION}" a f
  rm -rf "$dir"; mkdir -p "$dir"
  while IFS= read -r a; do
    [ -n "$a" ] || continue
    printf 'archive %s\n' "$a" > "$dir/$a"
  done < <(archives_for "$def")
  printf 'source\n' > "$dir/keypaste-${V}-source.tar.gz"
  (
    cd "$dir"
    for f in keypaste-*; do sha256sum "$f" > "$f.sha256"; done
    sha256sum keypaste-* > SHA256SUMS
  )
  printf '{"assets":[]}
' > "$dir/keypaste-${V}-manifest.json"
  printf '{}
' > "$dir/keypaste-${V}-provenance.sigstore.jsonl"
}

GOOD="$WORK/good"; stage "$GOOD"
run_case "assets-complete" 0 "advertised assets, their checksums" -- bash "$ASSETS_CHECK" "$V" "$GOOD"

D="$WORK/stray"; stage "$D"; printf 'SECRET=1\n' > "$D/.env.production"
run_case "assets-stray-dotfile" 1 "is not a release asset" -- bash "$ASSETS_CHECK" "$V" "$D"

D="$WORK/short"; stage "$D"; rm -f "$D"/keypaste-"$V"-win-x64.zip*
run_case "assets-target-missing" 1 "advertised and missing" -- bash "$ASSETS_CHECK" "$V" "$D"

D="$WORK/corrupt"; stage "$D"; printf 'tampered\n' > "$D/keypaste-${V}-linux-x64.tar.gz"
run_case "assets-checksum-fails" 1 "does not match its checksum" -- bash "$ASSETS_CHECK" "$V" "$D"

D="$WORK/nosum"; stage "$D"; rm -f "$D/keypaste-${V}-linux-arm64.tar.gz.sha256"
run_case "assets-no-checksum" 1 "has no checksum beside it" -- bash "$ASSETS_CHECK" "$V" "$D"

D="$WORK/orphan"; stage "$D"; printf 'hash  ghost\n' > "$D/ghost.tar.gz.sha256"
run_case "assets-orphan-checksum" 1 "has no asset beside it" -- bash "$ASSETS_CHECK" "$V" "$D"

D="$WORK/nobundle"; stage "$D"; rm -f "$D/keypaste-${V}-provenance.sigstore.jsonl"
run_case "assets-no-attestation-bundle" 1 "can never be verified" -- bash "$ASSETS_CHECK" "$V" "$D"

D="$WORK/nomanifest"; stage "$D"; rm -f "$D/keypaste-${V}-manifest.json"
run_case "assets-no-manifest" 1 "can never be verified" -- bash "$ASSETS_CHECK" "$V" "$D"

D="$WORK/foreignbundle"; stage "$D"; printf '{}
' > "$D/keypaste-9.9.8-provenance.sigstore.jsonl"
run_case "assets-another-versions-bundle" 1 "is not a release asset" -- bash "$ASSETS_CHECK" "$V" "$D"

D="$WORK/empty"; rm -rf "$D"; mkdir -p "$D"
run_case "assets-empty-directory" 1 "advertised and missing" -- bash "$ASSETS_CHECK" "$V" "$D"

echo "== negative controls"
WEAK_CL="$WORK/weak-changelog.sh"
sed 's/grep -qxF/grep -qF/' "$CHANGELOG_CHECK" > "$WEAK_CL"
grep -q 'grep -qxF' "$WEAK_CL" && die "the weakened changelog check is still anchored"
run_case "weakened-takes-the-rc-heading" 0 "has a section for 0.2.0" -- bash "$WEAK_CL" 0.2.0 "$CL"

DROPPED="$WORK/dropped.json"
jq '.components.cli.targets |= map(select(.rid != "win-x64"))' "$DEFINITION" > "$DROPPED"
D="$WORK/dropped"; stage "$D" "$DROPPED"
run_case "a-dropped-target-is-refused" 1 "a release cannot shrink" -- env KEYPASTE_RELEASE_DEFINITION="$DROPPED" bash "$ASSETS_CHECK" "$V" "$D"

WEAK_ASSETS="$WORK/weak-assets.sh"
sed '/a release cannot shrink/d' "$ASSETS_CHECK" > "$WEAK_ASSETS"
grep -q 'a release cannot shrink' "$WEAK_ASSETS" && die "the weakened asset check still holds the floor"
run_case "weakened-takes-a-dropped-target" 0 "advertised assets, their checksums" -- env KEYPASTE_RELEASE_DEFINITION="$DROPPED" bash "$WEAK_ASSETS" "$V" "$D"

echo "== a desktop package is published signed, or it is not published"

# The definition as signing leaves it: the policy on, every package offered and signed but the attested
# AppImage, and the internal-and-unsigned label gone from their names. Every desktop case below is a
# mutation of this one, so the accepts describe the release that will actually happen rather than a
# shape invented here.
SIGNED="$WORK/signed.json"
jq '.components.app.signing.policy = "authenticode"
    | .components.app.targets |= map(if .packages then
        .packages |= map(if .unsigned_basis then . else (.internal = false | .signed = true
                         | .pattern = (.pattern | sub("-internal-unsigned"; ""))) end) else . end)' \
   "$DEFINITION" > "$SIGNED"

# Offered for publication while nothing signs them: the state release.yml must never reach.
OFFERED_UNSIGNED="$WORK/offered-unsigned.json"
jq '.components.app.signing.policy = "none"
    | .components.app.targets |= map(if .packages then
      .packages |= map(.internal = false
                       | .pattern = (.pattern | sub("-internal-unsigned"; ""))) else . end)' \
   "$DEFINITION" > "$OFFERED_UNSIGNED"

# Signing is on and one package still says it is not signed.
SIGNED_PARTLY="$WORK/signed-partly.json"
jq '.components.app.signing.policy = "authenticode"
    | .components.app.targets |= map(if .packages then
        .packages |= map(.internal = false | .signed = false
                         | .pattern = (.pattern | sub("-internal-unsigned"; ""))) else . end)' \
   "$DEFINITION" > "$SIGNED_PARTLY"

# Some offered, one held back: not publishable, and not the recorded not-yet state either.
HALF="$WORK/half.json"
jq '.components.app.targets |= map(if (.packages and .rid == "win-x64") then
        .packages |= map(.internal = true | .signed = false) else . end)' \
   "$SIGNED" > "$HALF"

# The desktop before signing, the one recorded not-yet state: the policy none and every package internal,
# unsigned and named so, until the step that lifts it.
NOT_YET="$WORK/not-yet.json"
jq '.components.app.signing = {policy: "none", macos: "developer-id", until: ["4.7c2"]}
    | .components.app.targets |= map(if .packages then
        .packages |= map(.internal = true | .signed = false | del(.unsigned_basis, .unsigned_disclosed_in)
                         | .pattern = (if (.pattern | test("-internal-unsigned")) then .pattern
                                       else (.pattern | sub("\\.(?<ext>[A-Za-z]+)$"; "-internal-unsigned.\(.ext)")) end)) else . end)' \
   "$DEFINITION" > "$NOT_YET"

packages_for() {
  jq -r --arg v "$V" '.components.app.targets[] | . as $t | (.packages // [])[]
    | select(.internal == false) | .pattern
    | split("{version}") | join($v) | split("{rid}") | join($t.rid)' "$1" | tr -d "$CR"
}

# The desktop packages join the directory the CLI already staged: one prefix, one upload.
stage_desktop() {
  local dir="$1" def="$2" a
  while IFS= read -r a; do
    [ -n "$a" ] || continue
    printf 'package %s\n' "$a" > "$dir/$a"
    (cd "$dir" && sha256sum "$a" > "$a.sha256")
  done < <(packages_for "$def")
  printf '{"assets":[]}\n' > "$dir/keypaste-app-${V}-manifest.json"
  printf '{}\n' > "$dir/keypaste-app-${V}-provenance.sigstore.jsonl"
}

BOTH="$WORK/both"; stage "$BOTH" "$SIGNED"; stage_desktop "$BOTH" "$SIGNED"
run_case "assets-desktop-complete" 0 "advertised assets, their checksums" \
  -- env KEYPASTE_RELEASE_DEFINITION="$SIGNED" bash "$ASSETS_CHECK" --component cli --component app "$V" "$BOTH"

D="$WORK/desktop-unsigned-policy"; stage "$D" "$OFFERED_UNSIGNED"; stage_desktop "$D" "$OFFERED_UNSIGNED"
run_case "assets-desktop-while-signing-is-none" 1 "while the app signing policy is none" \
  -- env KEYPASTE_RELEASE_DEFINITION="$OFFERED_UNSIGNED" bash "$ASSETS_CHECK" --component cli --component app "$V" "$D"

D="$WORK/desktop-recorded-unsigned"; stage "$D" "$SIGNED_PARTLY"; stage_desktop "$D" "$SIGNED_PARTLY"
run_case "assets-desktop-recorded-unsigned" 1 "is not recorded signed" \
  -- env KEYPASTE_RELEASE_DEFINITION="$SIGNED_PARTLY" bash "$ASSETS_CHECK" --component cli --component app "$V" "$D"

D="$WORK/desktop-missing"; stage "$D" "$SIGNED"; stage_desktop "$D" "$SIGNED"
# Derived, never written out: a package extension in this file would reach release.yml through
# the scripts it names, and verify-release-matrix.sh would then read that workflow as one which
# builds an installer and demand it sign one.
gone="$(packages_for "$SIGNED" | head -1)"
rm -f "$D/$gone" "$D/$gone.sha256"
run_case "assets-desktop-missing" 1 "is advertised and missing" \
  -- env KEYPASTE_RELEASE_DEFINITION="$SIGNED" bash "$ASSETS_CHECK" --component cli --component app "$V" "$D"

D="$WORK/desktop-stray"; stage "$D" "$SIGNED"; stage_desktop "$D" "$SIGNED"
# The name that package had before it was signed, which is exactly what a re-run would leave
# behind.
stale="$(jq -r --arg v "$V" '.components.app.targets[] | . as $t | (.packages // [])[] | .pattern
  | split("{version}") | join($v) | split("{rid}") | join($t.rid)' "$NOT_YET" | tr -d "$CR" | head -1)"
printf 'left over\n' > "$D/$stale"
run_case "assets-desktop-stray-package" 1 "is not a release asset" \
  -- env KEYPASTE_RELEASE_DEFINITION="$SIGNED" bash "$ASSETS_CHECK" --component cli --component app "$V" "$D"

# The union is the allowlist: asked about the cli alone, the same directory is full of strays. This
# is why one invocation names every component being published rather than one call per component.
D="$WORK/desktop-not-asked-for"; stage "$D" "$SIGNED"; stage_desktop "$D" "$SIGNED"
run_case "assets-desktop-not-asked-for" 1 "is not a release asset" \
  -- env KEYPASTE_RELEASE_DEFINITION="$SIGNED" bash "$ASSETS_CHECK" "$V" "$D"

# The one unsigned package that publishes is the linux-x64 AppImage on its attestation, as the Linux
# CLI does. The same basis claimed by the MSI or the DMG, or the AppImage without it, is refused.
unsign() { jq --arg rid "$2" "(.components.app.targets[] | select(.rid == \$rid) | .packages[0]) |= ($3)" "$SIGNED" > "$WORK/$1.json"; echo "$WORK/$1.json"; }
MSI_UNSIGNED="$(unsign msi-unsigned win-x64 '.signed = false')"
DMG_UNSIGNED="$(unsign dmg-unsigned osx-arm64 '.signed = false')"
MSI_ATTESTED="$(unsign msi-attested win-x64 '.signed = false | .unsigned_basis = "attestation"')"
APPIMAGE_BARE="$(unsign appimage-bare linux-x64 'del(.unsigned_basis)')"
APPIMAGE_UNATTESTED="$WORK/appimage-unattested.json"
jq 'del(.components.app.provenance.workflow)' "$SIGNED" > "$APPIMAGE_UNATTESTED"

[ "$(jq -r '.components.app.targets[] | select(.rid == "linux-x64") | .packages[0] | "\(.internal) \(.signed) \(.unsigned_basis)"' "$SIGNED" | tr -d "$CR")" = "false false attestation" ] \
  || die "the definition no longer offers the linux-x64 AppImage unsigned on its attestation, so these cases test nothing"

D="$WORK/attested-appimage"; stage "$D" "$SIGNED"; stage_desktop "$D" "$SIGNED"
run_case "assets-attested-appimage" 0 "advertised assets, their checksums" \
  -- env KEYPASTE_RELEASE_DEFINITION="$SIGNED" bash "$ASSETS_CHECK" --component cli --component app "$V" "$D"
D="$WORK/unsigned-msi"; stage "$D" "$MSI_UNSIGNED"; stage_desktop "$D" "$MSI_UNSIGNED"
run_case "assets-unsigned-msi" 1 "the msi package is offered for publication and is not recorded signed" \
  -- env KEYPASTE_RELEASE_DEFINITION="$MSI_UNSIGNED" bash "$ASSETS_CHECK" --component cli --component app "$V" "$D"
D="$WORK/unsigned-dmg"; stage "$D" "$DMG_UNSIGNED"; stage_desktop "$D" "$DMG_UNSIGNED"
run_case "assets-unsigned-dmg" 1 "the dmg package is offered for publication and is not recorded signed" \
  -- env KEYPASTE_RELEASE_DEFINITION="$DMG_UNSIGNED" bash "$ASSETS_CHECK" --component cli --component app "$V" "$D"
D="$WORK/attested-msi"; stage "$D" "$MSI_ATTESTED"; stage_desktop "$D" "$MSI_ATTESTED"
run_case "assets-msi-claims-attestation" 1 "which only the attested linux-x64 AppImage may" \
  -- env KEYPASTE_RELEASE_DEFINITION="$MSI_ATTESTED" bash "$ASSETS_CHECK" --component cli --component app "$V" "$D"
D="$WORK/appimage-bare"; stage "$D" "$APPIMAGE_BARE"; stage_desktop "$D" "$APPIMAGE_BARE"
run_case "assets-appimage-without-basis" 1 "the appimage package is offered for publication and is not recorded signed" \
  -- env KEYPASTE_RELEASE_DEFINITION="$APPIMAGE_BARE" bash "$ASSETS_CHECK" --component cli --component app "$V" "$D"

run_case "publishable-cli" 0 "cli is publishable" \
  -- bash "$ASSETS_CHECK" --component cli --publishable
run_case "publishable-app-is-not-yet" 2 "declared and not yet publishable" \
  -- env KEYPASTE_RELEASE_DEFINITION="$NOT_YET" bash "$ASSETS_CHECK" --component app --publishable
run_case "publishable-app-when-signed" 0 "app is publishable" \
  -- env KEYPASTE_RELEASE_DEFINITION="$SIGNED" bash "$ASSETS_CHECK" --component app --publishable
run_case "publishable-app-half-signed" 1 "neither publishable nor in the one recorded not-yet state" \
  -- env KEYPASTE_RELEASE_DEFINITION="$HALF" bash "$ASSETS_CHECK" --component app --publishable
run_case "publishable-app-msi-claims-attestation" 1 "1 of those unsigned" \
  -- env KEYPASTE_RELEASE_DEFINITION="$MSI_ATTESTED" bash "$ASSETS_CHECK" --component app --publishable
run_case "publishable-app-appimage-unattested" 1 "1 of those unsigned" \
  -- env KEYPASTE_RELEASE_DEFINITION="$APPIMAGE_UNATTESTED" bash "$ASSETS_CHECK" --component app --publishable

WEAK_SIGNED="$WORK/weak-signed.sh"
sed '/is not recorded signed/d' "$ASSETS_CHECK" > "$WEAK_SIGNED"
grep -q 'is not recorded signed' "$WEAK_SIGNED" && die "the weakened asset check still demands a signature"
D="$WORK/weak-unsigned"; stage "$D" "$SIGNED_PARTLY"; stage_desktop "$D" "$SIGNED_PARTLY"
run_case "weakened-publishes-an-unsigned-package" 0 "advertised assets, their checksums" \
  -- env KEYPASTE_RELEASE_DEFINITION="$SIGNED_PARTLY" bash "$WEAK_SIGNED" --component cli --component app "$V" "$D"

# The exception widened to any package must accept the unsigned MSI the repaired gate refuses.
WEAK_EXCEPTION="$WORK/weak-exception.sh"
sed 's/^attested_appimage() {$/attested_appimage() { return 0/' "$ASSETS_CHECK" > "$WEAK_EXCEPTION"
grep -q '^attested_appimage() { return 0$' "$WEAK_EXCEPTION" || die "the weakened asset check still narrows the exception"
D="$WORK/weak-exception"; stage "$D" "$MSI_ATTESTED"; stage_desktop "$D" "$MSI_ATTESTED"
run_case "weakened-publishes-an-attested-msi" 0 "advertised assets, their checksums" \
  -- env KEYPASTE_RELEASE_DEFINITION="$MSI_ATTESTED" bash "$WEAK_EXCEPTION" --component cli --component app "$V" "$D"

DECLARED="$(declared_cases)"
[ -n "$DECLARED" ] || die "no case lines found in $SELF; the count this gate checks itself against is derived from them"
[ "$cases" -eq "$DECLARED" ] || die "$cases cases ran, but $DECLARED are written in $SELF; a case is defined and not driven"
[ "$accepted" -eq 13 ] || die "$accepted cases were accepted, expected exactly 13"

echo "ok: $cases cases across three decisions a tag used to be the first thing to run."
echo "    A version with no section of its own, a tag naming a version the source does not"
echo "    declare, an unreadable version, a stray dotfile, a missing target, a corrupt archive,"
echo "    a checksum with no asset, an asset with no checksum and a release missing its manifest"
echo "    or attestation bundle all refuse. So does a desktop package offered for publication"
echo "    while nothing signs it, recorded unsigned, missing, left over under its old internal"
echo "    name, or staged for a component the call never named; an unsigned MSI or DMG refuses"
echo "    while the attested linux-x64 AppImage publishes, and no other package can claim its"
echo "    basis. All four weakened copies accept what the repaired ones refuse."
echo "not proved here: that release.yml reaches these on a tag, which only a tag shows; and that"
echo "    the bytes a real build stages are the bytes it published, which is R.0b."
