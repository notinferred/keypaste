#!/usr/bin/env bash
# Drives release-completion.sh over V-R.0b's three refusals, against a fake origin.
#
# Same shape as verify-release-destination.sh: the real script, a fake fetcher on PATH, no
# credential and no network. The three cases are the ones R.0b names, and each fails for a reason
# the other two do not reach:
#
#   - an interrupted upload      one asset is not being served at all
#   - a corrupted public asset   the bytes served are not the bytes recorded
#   - an omitted target          the release never contained the asset, so the record is short
#
# The last is checked at BOTH ends, because they are different mistakes: a staged directory missing
# a target gets no record written, and a record missing a target is refused even when every URL it
# does name serves perfectly. A check that only did the first would be satisfied by a hand-edited
# record; one that only did the second would write a record for a release that was never whole.
#
# And the thing none of them may do: change the advertised version. That is asserted by comparing
# release-targets.json before and after, because "it does not write to it" is exactly the kind of
# claim that stays true right up until somebody adds a convenience.
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
readonly ROOT="$(cd "$(dirname "$SELF")/.." && pwd)"
readonly SUBJECT="${KEYPASTE_COMPLETION:-$ROOT/scripts/release-completion.sh}"
readonly DEFINITION="$ROOT/release-targets.json"
readonly VERSION="0.2.0"
readonly CR=$'\r'

cases=0
failures=0

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d "$CR"; }

command -v jq >/dev/null 2>&1 || die "no jq; this gate reads the definition and cannot run without it"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# ---------------------------------------------------------------------------
# A fake origin: a directory of files, and a `curl` that serves it and nothing else.
# ---------------------------------------------------------------------------
mkdir -p "$WORK/fakebin" "$WORK/served" "$WORK/dist"
cat > "$WORK/fakebin/curl" <<'CURL'
#!/usr/bin/env bash
# Serves $KEYPASTE_SERVED only. Anything not in it is a 404, which is what an interrupted upload
# looks like from outside. Records every fetch so the suite can count them.
set -uo pipefail
out=""; url=""
while [ $# -gt 0 ]; do
  case "$1" in
    -o) out="$2"; shift 2 ;;
    -*) shift ;;
    *)  url="$1"; shift ;;
  esac
done
name="${url##*/}"
echo "$name" >> "$KEYPASTE_FETCHLOG"
[ -f "$KEYPASTE_SERVED/$name" ] || exit 22
if [ -n "$out" ]; then cp "$KEYPASTE_SERVED/$name" "$out"; else cat "$KEYPASTE_SERVED/$name"; fi
CURL
chmod +x "$WORK/fakebin/curl"

export KEYPASTE_SERVED="$WORK/served"
export KEYPASTE_FETCHLOG="$WORK/fetches"
export KEYPASTE_ORIGIN_OVERRIDE="https://fixture.invalid/v${VERSION}/"
export KEYPASTE_RELEASE_DEFINITION="$DEFINITION"
# The retry exists for an origin catching up, and there is no origin here - so the loop is
# exercised at full count and no wall-clock cost.
export KEYPASTE_VERIFY_SLEEP=0

# A whole staged release, with content that differs per asset so a hash is a real answer.
asset_names() {
  local archives
  archives="$(jqr --arg v "$VERSION" '
    .components.cli as $c
    | $c.targets[] | . as $t
    | $c.archive_pattern
    | split("{version}") | join($v)
    | split("{rid}")     | join($t.rid)
    | split("{ext}")     | join($t.archive)
  ' "$DEFINITION")"
  {
    printf '%s\n' "$archives"
    printf '%s\n' "$archives" | sed 's/$/.sha256/'
    printf 'keypaste-%s-source.tar.gz\n'        "$VERSION"
    printf 'keypaste-%s-source.tar.gz.sha256\n' "$VERSION"
    printf 'SHA256SUMS\n'
  } | sed '/^$/d'
}

stage_whole() {
  rm -rf "$WORK/dist"; mkdir -p "$WORK/dist"
  local n
  while IFS= read -r n; do
    [ -n "$n" ] || continue
    printf 'the bytes of %s for %s\n' "$n" "$VERSION" > "$WORK/dist/$n"
  done < <(asset_names)
}

serve_from_dist() { rm -rf "$WORK/served"; mkdir -p "$WORK/served"; cp "$WORK/dist"/* "$WORK/served/"; }

run_subject() { PATH="$WORK/fakebin:$PATH" bash "$SUBJECT" "$@"; }

expect_refusal() { # $1 name, $2 want-in-output, then the command
  local name="$1" want="$2"; shift 2
  local out rc=0
  cases=$((cases + 1))
  out="$("$@" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ]; then
    echo "::error::fixture '$name' was accepted and must not be"
    printf '%s\n' "$out" | sed 's/^/::error::    /'
    failures=$((failures + 1)); return
  fi
  if ! printf '%s' "$out" | grep -qF -- "$want"; then
    echo "::error::fixture '$name' refused for the wrong reason; wanted '$want'"
    printf '%s\n' "$out" | sed 's/^/::error::    /'
    failures=$((failures + 1)); return
  fi
  echo "  refused: $name -- $want"
}

expect_pass() { # $1 name, then the command
  local name="$1"; shift
  local out rc=0
  cases=$((cases + 1))
  out="$("$@" 2>&1)" || rc=$?
  if [ "$rc" -ne 0 ]; then
    echo "::error::positive control '$name' failed, so the refusals below prove nothing"
    printf '%s\n' "$out" | sed 's/^/::error::    /'
    failures=$((failures + 1)); return
  fi
  echo "  ok: $name"
}

definition_fingerprint() { sha256sum "$DEFINITION" 2>/dev/null | awk '{print $1}'; }
before="$(definition_fingerprint)"

# ---------------------------------------------------------------------------
echo "== positive control: a whole release, served exactly as recorded"
stage_whole
run_subject record "$VERSION" "v$VERSION" "deadbeef" "$WORK/dist" > "$WORK/record.json"
serve_from_dist
expect_pass "whole-release-verifies" run_subject verify "$VERSION" "$WORK/record.json"

echo "== fixtures: a published release that is not whole"

# 1. Interrupted upload. Everything recorded, one asset never landed.
: > "$KEYPASTE_FETCHLOG"
serve_from_dist
missing="$(asset_names | head -1)"
rm -f "$WORK/served/$missing"
expect_refusal "interrupted-upload" "is not being served at" \
  run_subject verify "$VERSION" "$WORK/record.json"

# 2. A public asset whose bytes are not the recorded ones - and the SAME LENGTH, so only the hash
#    can catch it. A shorter corruption is also caught by the byte count, which would leave the hash
#    comparison untested and this fixture passing with it deleted. Verified below rather than
#    asserted in a comment.
serve_from_dist
corrupt="$(asset_names | head -1)"
was="$(wc -c < "$WORK/served/$corrupt" | tr -d '[:space:]')"
content="$(cat "$WORK/served/$corrupt")"
printf '%s\n' "${content/the bytes/THE BYTES}" > "$WORK/served/$corrupt"
now="$(wc -c < "$WORK/served/$corrupt" | tr -d '[:space:]')"
[ "$was" = "$now" ] \
  || die "the corruption changed the length ($was -> $now), so this fixture would pass without the hash check"
expect_refusal "corrupted-public-asset" "and the record says" \
  run_subject verify "$VERSION" "$WORK/record.json"

# 3a. Omitted target, at the staging end: no record is written at all.
stage_whole
dropped="$(asset_names | head -1)"
rm -f "$WORK/dist/$dropped"
expect_refusal "omitted-target-gets-no-record" "is not a whole release and gets no completion record" \
  run_subject record "$VERSION" "v$VERSION" "deadbeef" "$WORK/dist"

# 3b. Omitted target, at the verifying end: a short record whose every URL serves perfectly.
stage_whole
serve_from_dist
jq '.assets |= .[1:]' "$WORK/record.json" > "$WORK/short.json"
expect_refusal "omitted-target-in-the-record" "a record of an incomplete release" \
  run_subject verify "$VERSION" "$WORK/short.json"

# 4. None of the above may move the advertised version.
cases=$((cases + 1))
after="$(definition_fingerprint)"
if [ "$before" != "$after" ]; then
  echo "::error::release-targets.json changed while verifying a release; the advertised version is not this script's to move"
  failures=$((failures + 1))
else
  echo "  ok: the advertised version was not touched by any of the above"
fi

echo
[ "$failures" -eq 0 ] || die "$failures of $cases cases failed"
echo "$cases cases: a release is complete only if the public bytes say so, and saying so moves nothing"
