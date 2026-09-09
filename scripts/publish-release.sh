#!/usr/bin/env bash
# Decides whether a release destination is free, and is the only thing that uploads to it.
#
# F.4a's defect was one line of release.yml:
#
#   existing="$(aws s3 ls "$prefix" --endpoint-url "$endpoint" 2>/dev/null || true)"
#   if [ -n "$existing" ]; then ... exit 1; fi     # else fall through to `aws s3 cp --recursive`
#
# Three suppressions on one line: the diagnostic was discarded, the exit code was discarded, and
# *emptiness* was the only signal. So AccessDenied, an unreachable endpoint, a throttle and a
# genuinely absent prefix were one answer, and all but the last reached a recursive upload over a
# published, immutable version on a bucket the world can read (docs/PRODUCT.md laws 3.7 and 4.7).
#
# Two things follow, and the second is the one that matters.
#
# The question changed. `aws s3 ls` reports the same exit code for "matched nothing" and "failed",
# which is what `|| true` was reached for; asking it more carefully cannot fix that. This asks
# s3api list-objects-v2 instead, whose success is an API response, and reads the body rather than
# its emptiness - so "verified empty" is a parsed answer about the prefix that was asked about.
#
# But a refusal that rests only on a nonzero exit still trusts the read path to *fail loudly*,
# which is the assumption that failed the first time. So the check is a pair, and both halves must
# hold: a prefix known to be occupied must come back with at least one key, and only then is a
# target prefix of zero keys believed. A denied token, a wrong endpoint, a token with no
# ListBucket, a throttle awscli swallows and an SDK that starts answering `{}` all fail the first
# half - none of them can produce a positive answer about bytes that are really there.
# scripts/verify-install.sh's rule, applied to a listing: a run that fails on its own proves
# nothing; only the pair isolates the one thing being asserted.
#
# The upload lives here rather than in the workflow for the same reason the check does. V-F.4a
# requires that only a verified-empty destination *reaches upload* and that every other case makes
# zero write calls - a claim no test can make about a step body, and one the gate measures by
# driving this file against a fake `aws` and counting what it invoked.
#
# Usage:
#   publish-release.sh --check   <version>
#   publish-release.sh --publish <version> <dir>
#
# There is no default mode: a mis-wired step must not be able to upload by accident.
#
# Env:
#   R2_BUCKET                  destination bucket (required)
#   R2_ACCOUNT_ID              account, for the default endpoint (required)
#   AWS_ACCESS_KEY_ID          R2 credentials (required)
#   AWS_SECRET_ACCESS_KEY      R2 credentials (required)
#   KEYPASTE_R2_ENDPOINT       override the endpoint; used by the gate's real-awscli phase
#   KEYPASTE_R2_PROBE_PREFIX   the positive control's prefix (default v0.1.0/)
#
# It never deletes, never overwrites deliberately, and takes no --force: a bypass cannot be added
# without a diff that says so (D-0092).
#
# NEGATIVE CONTROL: scripts/verify-release-destination.sh runs the pre-fix one-liner against the
# same fake and requires it to upload where this refuses. If it does not, that gate fails: a
# fixture that cannot reproduce the fail-open condition proves nothing about the fix (D-0043).
set -euo pipefail

readonly PROBE_PREFIX="${KEYPASTE_R2_PROBE_PREFIX:-v0.1.0/}"

# A tag's suffix may carry dots and dashes; nothing else may. An empty version is the case this
# exists for: `env: VERSION: ${{ ... }}` is *set but empty* when the output is, so `set -u` never
# sees it, the prefix becomes `v/`, no published key starts with `v/`, the listing is honestly
# empty - and a release lands at a path nothing documents.
readonly VERSION_SHAPE='^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$'

die() { echo "::error::$*" >&2; exit 1; }

# Paging on a machine with no terminal hangs awscli v2. Set through the environment rather than
# with --no-cli-pager, which awscli v1 does not know and would fail the release over.
export AWS_PAGER=''

mode="${1:-}"
case "$mode" in
  --check|--publish) ;;
  *) die "usage: publish-release.sh --check <version> | --publish <version> <dir>" ;;
esac

version="${2:-}"
[ -n "$version" ] || die "no version given; refusing to name a destination from an empty string"
[[ "$version" =~ $VERSION_SHAPE ]] \
  || die "'$version' is not a version this pipeline publishes; refusing to derive a prefix from it"

bucket="${R2_BUCKET:-}"
[ -n "$bucket" ] || die "R2_BUCKET is not set"

endpoint="${KEYPASTE_R2_ENDPOINT:-}"
if [ -z "$endpoint" ]; then
  [ -n "${R2_ACCOUNT_ID:-}" ] || die "R2_ACCOUNT_ID is not set and no KEYPASTE_R2_ENDPOINT was given"
  endpoint="https://${R2_ACCOUNT_ID}.r2.cloudflarestorage.com"
fi

for v in AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY; do
  [ -n "${!v:-}" ] || die "credential $v is not set"
done

command -v aws >/dev/null 2>&1 || die "no aws CLI on this machine; a destination cannot be checked"
command -v jq  >/dev/null 2>&1 || die "no jq on this machine; a listing cannot be read"

target_prefix="v${version}/"
[ "$target_prefix" != "$PROBE_PREFIX" ] \
  || die "the positive control's prefix is the destination being published to; they cannot be the same"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# ---- 1. one listing, and one reading of it.

listing() {
  aws s3api list-objects-v2 \
    --bucket "$bucket" --prefix "$1" --endpoint-url "$endpoint" --output json
}

# stdin: what claims to be a ListObjectsV2 response. stdout: the number of keys. Nonzero for
# anything that is not positively a listing of the prefix that was asked about.
#
# `jq -e` is doing more work than it looks: it exits nonzero for error(), for a null or false last
# output, and for no output at all - so empty stdout, the single most dangerous input, is refused
# by the same code path as malformed JSON rather than by a special case somebody can delete. The
# number 0 is truthy in jq, so the legitimate empty answer is the one thing that passes.
#
# Name and Prefix are asserted only when the answer carries them. S3 specifies both and R2 says it
# is compatible, but this repository has not observed R2's reply, and an assertion on a field that
# turns out to be absent would refuse every release. What actually carries that claim is the
# positive control below: an implementation ignoring prefixes would answer the probe and the target
# identically, and those two answers are required to differ.
count_keys() {
  jq -e --arg want "$1" --arg bucket "$bucket" '
    if type != "object" then error("the answer is not a JSON object")
    elif has("Name")   and .Name   != $bucket then error("the answer names bucket \(.Name)")
    elif has("Prefix") and .Prefix != $want   then error("the answer is about prefix \(.Prefix)")
    elif ((.IsTruncated // false) == true) and (((.Contents // []) | length) == 0)
      then error("the answer says it is truncated and carries no keys")
    elif (.Contents // null) == null then 0
    elif (.Contents | type) != "array" then error("Contents is not an array")
    elif any(.Contents[]; (.Key | type) != "string") then error("an entry carries no Key")
    else (.Contents | length) end'
}

# Dies rather than returning a sentinel, so every caller must propagate: `die` inside a command
# substitution only ends the subshell (verify-publisher-metadata.sh:82).
keys_at() {
  local prefix="$1" rc=0 keys
  listing "$prefix" >"$WORK/out.json" 2>"$WORK/err.txt" || rc=$?
  if [ "$rc" -ne 0 ]; then
    sed -n '1,10p' "$WORK/err.txt" >&2
    die "listing $prefix failed (aws exited $rc); refusing without a verified answer"
  fi

  keys="$(count_keys "$prefix" <"$WORK/out.json")" \
    || die "the answer for $prefix is not a listing this can act on; refusing"

  # The count has to be a number here, not wherever it is next used. jq 1.6 exits 0 for empty
  # input, so on ubuntu-22.04 - the runner the publish job actually uses - an empty answer about
  # the destination came back as an empty string with a success status, sailed past the positive
  # control, failed `[ "$occupied" -ne 0 ]` with "integer expression expected", and was read as a
  # free destination. Trusting an exit code to say "no answer" is the assumption F.4a was about.
  case "$keys" in
    ''|*[!0-9]*) die "the answer for $prefix is not a listing this can act on; refusing" ;;
  esac

  printf '%s\n' "$keys"
}

# ---- 2. the positive control. Something known to be there has to be found.

probe="$(keys_at "$PROBE_PREFIX")" || exit 1
[ "$probe" -ge 1 ] || die "the positive control found nothing at $PROBE_PREFIX, so this credential and endpoint cannot be shown to see published objects at all; refusing rather than believing an empty answer about $target_prefix"
echo "the positive control sees $probe object(s) at $PROBE_PREFIX, so an empty answer means something"

# ---- 3. the destination itself.

occupied="$(keys_at "$target_prefix")" || exit 1
if [ "$occupied" -ne 0 ]; then
  echo "::error::${target_prefix} already holds $occupied object(s) and a published version is immutable"
  echo "::error::re-releasing means a new version number (docs/PRODUCT.md law 4.7)"
  listing "$target_prefix" 2>/dev/null | jq -r '.Contents[]?.Key' | sed -n '1,20p' || true
  exit 1
fi
echo "verified: ${target_prefix} holds no objects"

[ "$mode" = "--publish" ] || { echo "ok: the destination is free. Nothing was written."; exit 0; }

# ---- 4. only now, and only in --publish.

dir="${3:-}"
[ -n "$dir" ] || die "--publish needs the directory to upload"
[ -d "$dir" ] || die "not a directory: $dir"
find "$dir" -type f -print -quit | grep -q . || die "$dir holds no files to upload"

aws s3 cp "$dir" "s3://${bucket}/${target_prefix}" \
  --endpoint-url "$endpoint" --recursive --no-progress

# Informational, exactly as the step it replaces was. Turning it into a check needs a claim about
# R2's list-after-write consistency this repository has not established, and a false failure here
# would burn a version that had in fact been published; R.0b owns the completion record.
echo "published to ${target_prefix}:"
listing "$target_prefix" 2>/dev/null | jq -r '.Contents[]?.Key' || true
