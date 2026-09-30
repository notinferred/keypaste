#!/usr/bin/env bash
#
# make-compat-fixture.sh
#
# Builds the KDBX fixture that scripts/verify-keepassxc-compat.sh checks against a real
# KeePassXC (docs/PRODUCT.md law 4.6).
#
# This drives the SHIPPED keypaste binary. Until Stage 0.3 the fixture came from a throwaway
# project that called Keypaste.Core directly; driving the CLI instead means the permanent gate
# now also covers argument parsing, group-path splitting, the non-echoing prompt's
# redirected-stdin path, init's confirm-twice loop, and add's open-modify-save cycle — see
# DECISIONS.md D-0012.
#
# The binary is invoked DIRECTLY rather than through `dotnet run`, which would put the SDK's
# stdin forwarding between the pipe and the process under test.
#
# Usage:  scripts/make-compat-fixture.sh <output.kdbx>
# Env:    KP_COMPAT_PASSWORD  master password for the fixture   (required)
#         KEYPASTE_BIN        path to the keypaste binary       (default: the Release build)
#
# The values written here are duplicated in verify-keepassxc-compat.sh on purpose. That
# duplication is the change detector: expectations generated from the writer under test would
# agree with it forever and assert nothing.

set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='make-compat-fixture: '

db=${1:-}
[ -n "$db" ] || die "usage: make-compat-fixture.sh <output.kdbx>"
pw=${KP_COMPAT_PASSWORD:-}
[ -n "$pw" ] || die "KP_COMPAT_PASSWORD is not set"
kp=$(keypaste_bin)

mkdir -p "$(dirname "$db")"
rm -f "$db"

# One line of stdin per prompt, in a fixed order. init confirms the master password even when
# piped, so that the gate exercises the same code path a human takes; add takes the master
# password then the entry password.
"$kp" --version >/dev/null || die "keypaste is not runnable"

printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$db"

printf '%s\n%s\n' "$pw" 'ascii-only-P@ssw0rd' | "$kp" add compat/ascii --vault "$db" \
  --username 'compat-user' \
  --url 'https://example.invalid/keypaste' \
  --notes 'first notes line
second line: , ; = " '"'"' punctuation'

printf '%s\n%s\n' "$pw" 'pässwörd-ünïcode' | "$kp" add compat/unicode --vault "$db" \
  --username 'ünïcode-user' \
  --url 'https://example.invalid/ünïcode' \
  --notes 'café — 日本語 — 🔑'

printf '%s\n%s\n' "$pw" 'deep-pass' | "$kp" add compat/nested/deep --vault "$db" \
  --username 'deep-user' \
  --url 'https://example.invalid/deep' \
  --notes 'entry in a nested group'

printf 'fixture written to %s\n' "$db"
