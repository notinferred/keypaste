#!/usr/bin/env bash
# Installs apt packages on a hosted Linux runner without letting a stalled mirror hold the job to its limit.
#
# Each request gives up after 30 s and apt retries it; each update attempt stops after two minutes and is
# tried three times, so a mirror that never answers fails the step in minutes instead of at the job's limit.
#
# Usage: apt-install.sh [apt-get install options] package...
set -euo pipefail

[ $# -gt 0 ] || { echo '::error::usage: apt-install.sh [options] package...' >&2; exit 1; }

readonly -a APT_OPTIONS=(-o Acquire::Retries=3 -o Acquire::http::Timeout=30 -o Acquire::https::Timeout=30)

for attempt in 1 2 3; do
  if sudo timeout --kill-after=10s 2m apt-get "${APT_OPTIONS[@]}" update; then
    break
  fi
  if [ "$attempt" -eq 3 ]; then
    echo '::error::apt-get update failed three times' >&2
    exit 1
  fi
  echo "apt-get update attempt $attempt failed; retrying" >&2
  sleep 5
done

sudo timeout --kill-after=10s 10m apt-get "${APT_OPTIONS[@]}" install -y "$@"
