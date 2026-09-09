#!/usr/bin/env bash
# Holds scripts/publish-release.sh to refusing every destination it cannot positively verify.
#
# Nothing in this repository can execute a step out of a workflow, which is why F.4a's defect
# survived: the fail-open line lived in release.yml, and the only thing that ever ran it was a tag
# against a public bucket. So the decision moved into a script, and this drives that script against
# a fake `aws` on PATH - the technique verify-install.sh uses to shadow curl - through every answer
# a listing can give. No credential, no network, and no real bucket is involved.
#
# What it holds, per V-F.4a:
#   - exactly one scenario reaches an upload, and it is the one where a listing succeeded, parsed,
#     named the prefix that was asked about, and said zero keys;
#   - every other scenario makes zero write calls, counted from an allowlist of read verbs rather
#     than a denylist of write ones, and leaves the pre-existing objects byte-identical;
#   - the pre-fix one-liner, run through the same harness against the same fake, uploads on two of
#     those scenarios. That pair is the whole argument: same fake, one difference.
#
# Usage:
#   verify-release-destination.sh                  the fake-aws scenarios (no aws needed)
#   verify-release-destination.sh --with-real-aws  those, plus three failures asked of the real one
#
# --with-real-aws is a mode the caller selects, not a check that skips itself. Both workflows pass
# it, and a test asserts they still do.
#
# What it cannot hold, said here rather than left to be assumed: the success payloads are the shape
# S3 documents and Cloudflare's R2 compatibility page describes, not bytes captured from the real
# bucket - this machine has no aws CLI and this gate is not given a credential. If R2 answers in
# some other shape, the positive control below turns that into a refused release rather than an
# overwrite, which is the direction law 3.7 asks for; only a real dispatch or tag can observe it.
#
# NEGATIVE CONTROL: phase G writes F.4a's defect back out verbatim - `aws s3 ls ... 2>/dev/null ||
# true` and an unguarded `cp` - and requires it to reach an upload on the denied-quiet and
# always-empty scenarios. If the old code refuses, this gate fails: a fixture that cannot reproduce
# the fail-open condition proves nothing about the fix (D-0043).
#
# The real-awscli phase must never be skipped or soft-passed: it dies where it cannot run, and the
# closing lines say how many of its cases were reached rather than implying a number.
set -euo pipefail

readonly PUBLISHER="${KEYPASTE_PUBLISHER:-scripts/publish-release.sh}"
readonly BUCKET='keypaste-fixture-dl'
readonly PROBE='v0.1.0/'
readonly VERSION='0.1.1'
readonly TARGET="v${VERSION}/"
readonly SEEDED=2

die() { echo "::error::$*" >&2; exit 1; }

MODE="${1:-}"
case "$MODE" in
  ''|--with-real-aws) ;;
  *) die "usage: verify-release-destination.sh [--with-real-aws]" ;;
esac

[ -f "$PUBLISHER" ] || die "no publisher to check: $PUBLISHER"
command -v jq >/dev/null 2>&1 || die "no jq; this gate reads listings and cannot run without it"

WORK="$(mktemp -d)"
trap 'chmod -R u+w "$WORK" 2>/dev/null || true; rm -rf "$WORK"' EXIT

readonly SHIM="$WORK/shim"
readonly STATE="$WORK/state"
readonly DIST="$WORK/dist"
readonly CALLS="$WORK/calls.log"
readonly OUT="$WORK/out.txt"
mkdir -p "$SHIM" "$DIST"

# The fake reads these three out of its environment. Exported once here rather than assigned in
# every subshell: `export X="$X"` on a readonly name is an error bash prints and then works around,
# and under errexit it ends the subshell before the thing being tested ever runs.
export STATE BUCKET CALLS

# ---- 0. the release a fake bucket is asked to accept, and the objects already on it.

for rid in linux-x64 linux-arm64 osx-arm64; do
  echo "archive $rid" > "$DIST/keypaste-${VERSION}-${rid}.tar.gz"
done
echo 'archive win-x64' > "$DIST/keypaste-${VERSION}-win-x64.zip"
echo 'source' > "$DIST/keypaste-${VERSION}-source.tar.gz"
for f in "$DIST"/keypaste-*; do echo "hash  $(basename "$f")" > "$f.sha256"; done
: > "$DIST/SHA256SUMS"
readonly DIST_FILES="$(find "$DIST" -type f | wc -l | tr -d '[:space:]')"
[ "$DIST_FILES" -ge 5 ] || die "the fixture built no release to upload"

seed_state() {
  rm -rf "$STATE"
  mkdir -p "$STATE/$PROBE"
  local i
  for i in $(seq 1 "$SEEDED"); do
    printf 'published byte %s\n' "$i" > "$STATE/${PROBE}asset-$i.tar.gz"
  done
}

occupy_target() {
  mkdir -p "$STATE/$TARGET"
  printf 'a partial upload nobody may overwrite\n' > "$STATE/${TARGET}asset-1.tar.gz"
  printf 'and its checksum\n' > "$STATE/${TARGET}asset-1.tar.gz.sha256"
}

snapshot() { (cd "$STATE" && find . -type f | sort | while IFS= read -r f; do
  printf '%s  %s\n' "$(sha256sum < "$f" | cut -d' ' -f1)" "$f"; done); }

# ---- 1. the fake. One binary, behaviour chosen by $SCENARIO, every invocation recorded.

cat > "$SHIM/aws" <<'FAKE'
#!/usr/bin/env bash
# A stand-in for the AWS CLI. It records what it was asked before it answers anything, so "zero
# write calls" is a fact about a file rather than an inference from an exit code.
set -uo pipefail
printf '%s\n' "$*" >> "$CALLS"

if [ "${1:-}" = '--version' ]; then echo 'aws-cli/2.0.0-fixture'; exit 0; fi

verb="${1:-} ${2:-}"

prefix=''; dest=''; src=''; prev=''
for a in "$@"; do
  case "$prev" in --prefix) prefix="$a" ;; esac
  case "$a" in s3://*) dest="$a" ;; esac
  prev="$a"
done
src="${3:-}"

# The shape S3 documents and Cloudflare's R2 compatibility page describes, built from what the
# fake bucket actually holds - so a listing and an upload cannot disagree with each other.
listing_of() {
  local want="$1" dir="$STATE/$1"
  local keys='[]'
  if [ -d "$dir" ]; then
    keys="$(cd "$STATE" && find "$want" -type f | sort | jq -R -s 'split("\n") | map(select(length > 0)) |
      map({Key: ., LastModified: "2026-09-05T12:00:00+00:00", ETag: "\"0\"", Size: 1, StorageClass: "STANDARD"})')"
  fi
  jq -n --arg bucket "$BUCKET" --arg prefix "$want" --argjson keys "$keys" \
    '{IsTruncated: false, Contents: $keys, Name: $bucket, Prefix: $prefix, MaxKeys: 1000, KeyCount: ($keys | length)}
     | if (.Contents | length) == 0 then del(.Contents) else . end'
}

fail() { [ -z "${1:-}" ] || echo "$1" >&2; exit 255; }

case "$verb" in
  's3api list-objects-v2')
    case "$SCENARIO" in
      honest)            listing_of "$prefix" ;;
      always-empty)      listing_of 'v0.0.0-nothing-here/' | jq --arg p "$prefix" '.Prefix = $p' ;;
      denied)            fail 'An error occurred (AccessDenied) when calling the ListObjectsV2 operation: Access Denied' ;;
      denied-quiet)      fail '' ;;
      throttled)         fail 'An error occurred (SlowDown) when calling the ListObjectsV2 operation: Please reduce your request rate' ;;
      unavailable)       fail 'Could not connect to the endpoint URL: "https://fixture.r2.cloudflarestorage.com/"' ;;
      empty-stdout)      exit 0 ;;
      # The probe answers honestly and only the destination does not, which is the one shape the
      # positive control cannot catch for the publisher: it has already been satisfied by then.
      target-empty)      if [ "$prefix" = "${KEYPASTE_R2_PROBE_PREFIX:-}" ]; then listing_of "$prefix"; else exit 0; fi ;;
      target-truncated)  if [ "$prefix" = "${KEYPASTE_R2_PROBE_PREFIX:-}" ]; then listing_of "$prefix"; else printf '%s' '{"Contents":['; fi ;;
      truncated-json)    printf '%s' '{"Contents":[' ;;
      root-array)        echo '[]' ;;
      root-null)         echo 'null' ;;
      root-string)       echo '"0"' ;;
      truncated-no-keys) jq -n --arg b "$BUCKET" --arg p "$prefix" '{IsTruncated: true, Name: $b, Prefix: $p}' ;;
      keyless-entry)     jq -n --arg b "$BUCKET" --arg p "$prefix" '{IsTruncated: false, Contents: [{Size: 1}], Name: $b, Prefix: $p}' ;;
      wrong-prefix)      listing_of "$prefix" | jq '.Prefix = "v9.9.9/"' ;;
      wrong-bucket)      listing_of "$prefix" | jq '.Name = "somebody-elses-bucket"' ;;
      *) echo "fixture aws: no such scenario: $SCENARIO" >&2; exit 64 ;;
    esac
    ;;
  's3 ls')
    # Only the pre-fix one-liner asks this. Real awscli exits 1 with no output for a prefix that
    # matches nothing, which is the ambiguity F.4a is about, so the fake reproduces it.
    case "$SCENARIO" in
      honest|always-empty)
        target="${2#s3://}"; target="${target#*/}"
        if [ -d "$STATE/$target" ] && [ "$SCENARIO" = honest ]; then
          (cd "$STATE" && find "$target" -type f | sort | sed 's/^/2026-09-05 12:00:00          1 /')
        else
          exit 1
        fi
        ;;
      denied)       fail 'An error occurred (AccessDenied) when calling the ListObjectsV2 operation: Access Denied' ;;
      denied-quiet) fail '' ;;
      throttled)    fail 'An error occurred (SlowDown) when calling the ListObjectsV2 operation' ;;
      unavailable)  fail 'Could not connect to the endpoint URL' ;;
      *)            exit 1 ;;
    esac
    ;;
  's3 cp')
    # Deliberately succeeds. A failure to refuse must show up as a write, not as an unrelated crash.
    path="${dest#s3://}"; path="${path#*/}"
    mkdir -p "$STATE/$path"
    cp -r "$src"/. "$STATE/$path"
    echo "fixture: copied $src to $dest"
    ;;
  *) echo "fixture aws: unexpected invocation: $*" >&2; exit 64 ;;
esac
FAKE
chmod +x "$SHIM/aws"

# ---- 2. one harness. Every scenario is judged on four things, not on an exit code alone.

cases_run=0
writes_seen=0

# A read verb is named; anything else counts as a write. A denylist would have to enumerate every
# verb that can put bytes on a bucket, and the day one is missed is the day this stops being a
# gate (the argument verify-publisher-metadata.sh makes about copyright lines).
writes_in_log() {
  local n=0 line
  [ -s "$CALLS" ] || { echo 0; return; }
  while IFS= read -r line; do
    case "$line" in
      's3api list-objects-v2 '*|'s3 ls '*|'--version'*) ;;
      *) n=$((n + 1)) ;;
    esac
  done < "$CALLS"
  echo "$n"
}

# run_case <name> <scenario> <publisher> <mode> <expect-rc> <expect-writes> <expect-in-output>
run_case() {
  local name="$1" scenario="$2" publisher="$3" mode="$4" want_rc="$5" want_writes="$6" want_says="$7"
  local rc=0 before after writes

  : > "$CALLS"
  before="$(snapshot)"

  set +e
  (
    export PATH="$SHIM:$PATH"
    export SCENARIO="$scenario"
    export R2_BUCKET="$BUCKET" R2_ACCOUNT_ID='fixture-account'
    export AWS_ACCESS_KEY_ID='fixture-key' AWS_SECRET_ACCESS_KEY='fixture-secret'
    export KEYPASTE_R2_PROBE_PREFIX="$PROBE"
    [ "$(command -v aws)" = "$SHIM/aws" ] || { echo "the shim is not in front of PATH" >&2; exit 90; }
    if [ "$mode" = '--check' ]; then bash "$publisher" --check "$VERSION"
    else bash "$publisher" --publish "$VERSION" "$DIST"; fi
  ) >"$OUT" 2>&1
  rc=$?
  set -e

  after="$(snapshot)"
  writes="$(writes_in_log)"

  [ "$rc" -ne 90 ] || { sed -n '1,20p' "$OUT" >&2; die "$name: the fake was not the aws on PATH"; }
  [ "$rc" = "$want_rc" ] || { sed -n '1,30p' "$OUT" >&2; die "$name: expected exit $want_rc, got $rc"; }
  [ "$writes" = "$want_writes" ] \
    || { cat "$CALLS" >&2; die "$name: expected $want_writes write call(s), counted $writes"; }
  grep -qF -- "$want_says" "$OUT" \
    || { sed -n '1,30p' "$OUT" >&2; die "$name: nothing in the output said '$want_says'"; }
  [ -s "$CALLS" ] || die "$name: the publisher never called aws at all, so nothing was exercised"

  if [ "$want_writes" -eq 0 ]; then
    [ "$before" = "$after" ] || die "$name: refused and still changed the bucket"
  fi

  cases_run=$((cases_run + 1))
  writes_seen=$((writes_seen + writes))
  printf '  ok  %-22s exit %s, %s write call(s)\n' "$name" "$rc" "$writes"
}

echo '--- A. the one destination that may be published to'
seed_state
run_case 'empty/publish' honest "$PUBLISHER" --publish 0 1 'published to v0.1.1/'
# The upload is the other half of the anti-vacuity guard: a publisher hard-wired to refuse would
# satisfy every assertion below.
[ -d "$STATE/$TARGET" ] || die 'the verified-empty destination did not reach an upload'
uploaded="$(find "$STATE/$TARGET" -type f | wc -l | tr -d '[:space:]')"
[ "$uploaded" = "$DIST_FILES" ] || die "uploaded $uploaded of $DIST_FILES files"
[ "$(find "$STATE/$PROBE" -type f | wc -l | tr -d '[:space:]')" = "$SEEDED" ] \
  || die 'publishing one version disturbed another'
echo "  ok  the upload put $uploaded file(s) under $TARGET and left $PROBE alone"

seed_state
run_case 'empty/check' honest "$PUBLISHER" --check 0 0 'the destination is free'

echo '--- B. a version somebody has already published'
seed_state; occupy_target
run_case 'occupied/publish' honest "$PUBLISHER" --publish 1 0 'immutable'
seed_state; occupy_target
run_case 'occupied/check' honest "$PUBLISHER" --check 1 0 'immutable'

echo '--- C. the check itself could not be made'
for scenario in denied denied-quiet throttled unavailable; do
  seed_state
  run_case "$scenario" "$scenario" "$PUBLISHER" --publish 1 0 'refusing without a verified answer'
done

echo '--- D. an answer that is not a listing'
for scenario in empty-stdout truncated-json root-array root-null root-string truncated-no-keys keyless-entry; do
  seed_state
  run_case "$scenario" "$scenario" "$PUBLISHER" --publish 1 0 'is not a listing this can act on'
done
for scenario in wrong-prefix wrong-bucket; do
  seed_state
  run_case "$scenario" "$scenario" "$PUBLISHER" --publish 1 0 'is not a listing this can act on'
done

echo '--- D2. the probe answers and the destination alone does not'
# Phase D asks every listing the same bad question, so the positive control refuses first and the
# destination is never judged. That is how a jq exiting 0 for empty input stayed invisible until
# release run 34302850825 on ubuntu-22.04, where an empty answer about the destination alone
# reached `aws s3 cp` over a published version. Reproduced by hand, then made a case.
for scenario in target-empty target-truncated; do
  seed_state
  run_case "$scenario" "$scenario" "$PUBLISHER" --publish 1 0 'is not a listing this can act on'
done

echo '--- E. the impostor: everything looks empty, including what is not'
# The scenario the pre-fix code could not tell from success, and the only one a checker reading
# exit codes alone still cannot: every listing succeeds and every listing says nothing is there.
seed_state
run_case 'always-empty' always-empty "$PUBLISHER" --publish 1 0 'the positive control found nothing'

echo '--- F. the version string itself'
seed_state
for bad in '' 'v0.1.1' '0.1' '../0.1.1' '0.1.1/../..'; do
  : > "$CALLS"
  rc=0
  (
    export PATH="$SHIM:$PATH" SCENARIO=honest
    export R2_BUCKET="$BUCKET" R2_ACCOUNT_ID='fixture-account'
    export AWS_ACCESS_KEY_ID='k' AWS_SECRET_ACCESS_KEY='s'
    bash "$PUBLISHER" --publish "$bad" "$DIST"
  ) >"$OUT" 2>&1 || rc=$?
  [ "$rc" -ne 0 ] || die "version '$bad' was accepted as a destination"
  [ "$(writes_in_log)" = 0 ] || die "version '$bad' reached a write"
  cases_run=$((cases_run + 1))
done
echo "  ok  five version strings that name no release were refused before any call"

: > "$CALLS"
rc=0
(
  export PATH="$SHIM:$PATH" SCENARIO=honest
  export R2_BUCKET="$BUCKET" R2_ACCOUNT_ID='fixture-account'
  export AWS_ACCESS_KEY_ID='k' AWS_SECRET_ACCESS_KEY='s'
  bash "$PUBLISHER"
) >"$OUT" 2>&1 || rc=$?
[ "$rc" -ne 0 ] || die 'the publisher ran with no mode given'
grep -qF 'usage:' "$OUT" || die 'no mode given did not print a usage line'
cases_run=$((cases_run + 1))
echo '  ok  no mode means no upload'

echo '--- G. NEGATIVE CONTROL: what the code being replaced did with the same answers'
cat > "$WORK/old-publisher.sh" <<'OLD'
#!/usr/bin/env bash
# F.4a's defect, verbatim from release.yml's "Upload to R2" step before this repair, kept here so
# the gate can prove it is caught. Not a copy of the fix: a copy of what the fix replaced.
set -euo pipefail
mode="$1"; version="$2"; dir="${3:-}"
endpoint="https://${R2_ACCOUNT_ID}.r2.cloudflarestorage.com"
prefix="s3://${R2_BUCKET}/v${version}/"
existing="$(aws s3 ls "$prefix" --endpoint-url "$endpoint" 2>/dev/null || true)"
if [ -n "$existing" ]; then
  echo "::error::v${version}/ already exists on the bucket and a release is immutable"
  echo "$existing"
  exit 1
fi
[ "$mode" = '--publish' ] || { echo 'ok: the destination is free. Nothing was written.'; exit 0; }
aws s3 cp "$dir" "s3://${R2_BUCKET}/v${version}/" --endpoint-url "$endpoint" --recursive --no-progress
echo "published to v${version}/:"
OLD

old_writes=0
for scenario in denied-quiet always-empty; do
  seed_state; occupy_target
  before="$(snapshot)"
  : > "$CALLS"
  rc=0
  (
    export PATH="$SHIM:$PATH" SCENARIO="$scenario"
    export R2_BUCKET="$BUCKET" R2_ACCOUNT_ID='fixture-account'
    export AWS_ACCESS_KEY_ID='k' AWS_SECRET_ACCESS_KEY='s'
    bash "$WORK/old-publisher.sh" --publish "$VERSION" "$DIST"
  ) >"$OUT" 2>&1 || rc=$?
  w="$(writes_in_log)"
  [ "$rc" -eq 0 ] && [ "$w" -ge 1 ] \
    || { sed -n '1,20p' "$OUT" >&2; die "the pre-fix code refused '$scenario' (exit $rc, $w writes); this fixture is not reproducing the fail-open condition it exists to catch"; }
  [ "$before" != "$(snapshot)" ] || die "the pre-fix code wrote nothing on '$scenario'"
  old_writes=$((old_writes + w))
  printf '  ok  %-22s the pre-fix one-liner uploaded over a published version\n' "$scenario"
done
[ "$old_writes" -ge 2 ] || die "the negative control recorded $old_writes writes, expected at least 2"

echo '--- H. what the real AWS CLI does with the failures this rests on'
real_aws_phases=0
if [ "$MODE" = '--with-real-aws' ]; then
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
      die 'the real-awscli phase cannot run under MSYS: it rewrites the endpoint URL argument and the observation would be about the shell, not the CLI. Run it on Linux or macOS.' ;;
  esac
  command -v aws >/dev/null 2>&1 \
    || die 'the real-awscli phase was asked for and there is no aws CLI here; it is never skipped'
  aws --version 2>&1 | sed -n '1p'
  seed_state
  # No credential that reaches anything: a bogus endpoint on a closed local port, a host that does
  # not resolve, and an argument the CLI itself rejects. What is asserted is that none of the three
  # can be mistaken for a count, and that the publisher refuses each.
  i=0
  for ep in 'https://127.0.0.1:1' 'https://keypaste-fixture-does-not-resolve.invalid'; do
    rc=0
    (
      export R2_BUCKET="$BUCKET" KEYPASTE_R2_ENDPOINT="$ep"
      export AWS_ACCESS_KEY_ID='fixture-key' AWS_SECRET_ACCESS_KEY='fixture-secret'
      export AWS_DEFAULT_REGION=auto AWS_EC2_METADATA_DISABLED=true
      export AWS_RETRY_MODE=standard AWS_MAX_ATTEMPTS=1
      bash "$PUBLISHER" --check "$VERSION"
    ) >"$OUT" 2>&1 || rc=$?
    [ "$rc" -ne 0 ] || die "the publisher accepted a destination behind $ep"
    grep -qF 'refusing without a verified answer' "$OUT" \
      || { sed -n '1,20p' "$OUT" >&2; die "$ep was refused for some other reason than an unverified listing"; }
    i=$((i + 1))
  done
  [ "$i" -eq 2 ] || die 'the real-awscli phase ran none of its cases'
  real_aws_phases=2
  echo "  ok  the real AWS CLI failed both unreachable endpoints and the publisher refused both"
else
  echo '  --  not run: pass --with-real-aws on a machine that has one (both workflows do)'
fi

# ---- 3. two guards, in opposite directions.

[ "$cases_run" -ge 23 ] || die "only $cases_run cases ran; the scenario table has lost rows"
[ "$writes_seen" -eq 1 ] \
  || die "$writes_seen write call(s) across the fixed publisher's cases, expected exactly 1"

cat <<EOF
ok: $cases_run cases. One verified-empty destination reached an upload and put $DIST_FILES files
    there; every other answer - occupied, denied, denied without a word, throttled, unreachable,
    eleven malformed shapes, two of which answered only the destination badly and left the
    positive control satisfied, an all-empty impostor and six unusable version strings - made zero
    write calls and left the $SEEDED published objects byte-identical. The pre-fix one-liner, same
    fake and same harness, uploaded over a published version on two of them.
not proved here: that R2 answers a listing in the shape this fake returns, which needs a real
    dispatch or tag; that an interrupted upload leaves a recoverable version, which is R.0b; and
    the window between the check and the first byte, which the workflow's serialised concurrency
    narrows and does not close. Real-awscli cases run: $real_aws_phases.
EOF
