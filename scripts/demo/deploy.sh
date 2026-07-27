#!/usr/bin/env bash
#
# deploy.sh
#
# The deploy in docs/demo.md. It builds nothing, opens no socket, and is safe to run any number
# of times. It exists so the demo has a command that fails for exactly one reason — a missing
# credential — and succeeds the moment that credential is present.
#
# It deliberately does NOT mention keypaste. A fixture that tells the agent where to look has
# rigged the demo: the point being demonstrated is that the model reaches for the vault on its
# own, and a script that names the tool would be doing the reaching for it.
#
# It never prints the key. That is the demo's closing argument rather than a detail — the one
# program in the story with a legitimate need for the value still does not put it on screen.
#
# Usage:  ./deploy.sh
# Env:    STRIPE_KEY  the credential; without it this refuses
set -euo pipefail

readonly SERVICE='billing-service'
readonly TARGET='staging'

# The shortest key worth showing any of. Eight leading and four trailing characters of a
# 40-character key is under a third of it; the same mask over a 12-character key is all of it.
readonly MINIMUM_TO_MASK=16

# Paced only when a person is watching, so the recording reads at human speed and CI pays nothing.
beat() { [ -t 1 ] && sleep 0.4; return 0; }

if [ -z "${STRIPE_KEY:-}" ]; then
  echo 'deploy: STRIPE_KEY is not set.' >&2
  echo 'deploy: the billing service will not deploy without it.' >&2
  echo 'deploy: nothing was deployed.' >&2
  exit 1
fi

# Bash parameter expansion, never cut/sed/awk. Those would make the credential an argument to
# another process, which puts it in that process's argv and therefore in `ps` — the exact leak
# this script is meant to demonstrate the absence of.
length=${#STRIPE_KEY}

if [ "$length" -lt "$MINIMUM_TO_MASK" ]; then
  masked="(set, ${length} characters, too short to show any of it)"
else
  masked="${STRIPE_KEY:0:8}...${STRIPE_KEY: -4} (${length} characters)"
fi

echo "deploy: building ${SERVICE}"
beat
echo "deploy: STRIPE_KEY is set: ${masked}"
beat
echo "deploy: deployed ${SERVICE} to ${TARGET}"
beat
echo 'deploy: this is a demo. Nothing was built and nothing left this machine.'
