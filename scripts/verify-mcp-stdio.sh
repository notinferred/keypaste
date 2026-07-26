#!/usr/bin/env bash
# Proves the shipped keypaste-mcp binary really speaks MCP over real pipes, and that nothing but
# protocol ever reaches stdout.
#
# The in-process tests drive the server through StreamServerTransport, so they never exercise
# StdioServerTransport or Main - and "an MCP client spawns it with redirected stdio and no
# terminal" is the premise of the entire feature. This is the only place that premise is tested.
# It is the same gap scripts/verify-run-injection.sh exists to close for `keypaste run`.
#
# NEGATIVE CONTROL: a stray line on stdout, a missing tool, a granted credential, or a missing
# audit line each fail this script. These checks must never be skipped or soft-passed.
set -euo pipefail

readonly BIN="${KEYPASTE_BIN:-artifacts/bin/Keypaste.Mcp/release/keypaste-mcp}"

die() {
  echo "::error::$*" >&2
  if [ -f "${OUT:-}" ]; then echo "--- stdout ---" >&2; cat "$OUT" >&2; fi
  if [ -f "${ERR:-}" ]; then echo "--- stderr ---" >&2; cat "$ERR" >&2; fi
  exit 1
}

command -v jq >/dev/null 2>&1 || die "jq is required and was not found; this gate must never be skipped"

BIN_PATH="$BIN"
[ -x "$BIN_PATH" ] || BIN_PATH="${BIN}.exe"
[ -x "$BIN_PATH" ] || die "keypaste-mcp not found at $BIN (build first)"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

readonly AUDIT="$WORK/audit.jsonl"
readonly OUT="$WORK/stdout.txt"
readonly ERR="$WORK/stderr.txt"

# Newline-delimited JSON-RPC, which is what the stdio transport speaks.
{
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
  printf '%s\n' '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_entry_names","arguments":{}}}'
  printf '%s\n' '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"request_credential","arguments":{"entry":"env/dev/STRIPE_KEY","field":"password","reason":"ci probe","ttl_seconds":60}}}'

  # Hold the pipe open. Closing stdin is how an MCP client says goodbye, and the server takes it
  # at its word: without this it shuts down before it has answered anything, and the gate sees an
  # empty stdout that looks like a protocol failure rather than a race in the harness.
  sleep 5
} | "$BIN_PATH" --vault "$WORK/vault.kdbx" --audit-log "$AUDIT" --client-label ci-probe \
      >"$OUT" 2>"$ERR" || die "keypaste-mcp exited non-zero"

[ -s "$OUT" ] || die "keypaste-mcp wrote nothing to stdout"

# 1. Everything on stdout is protocol. One stray println corrupts the stream for a real client,
#    and the failure looks like the client is broken rather than like keypaste is.
while IFS= read -r line; do
  [ -z "$line" ] && continue
  printf '%s' "$line" | jq -e . >/dev/null 2>&1 \
    || die "a line on stdout is not JSON: $line"
done <"$OUT"

# 2. Exactly two tools, with the names the documentation promises.
tools="$(jq -c 'select(.id == 2) | .result.tools' <"$OUT" | head -n 1)"
[ -n "$tools" ] || die "no tools/list response"

count="$(printf '%s' "$tools" | jq 'length')"
[ "$count" = "2" ] || die "expected exactly 2 tools, got $count"

for want in list_entry_names request_credential; do
  printf '%s' "$tools" | jq -e --arg n "$want" 'any(.[]; .name == $n)' >/dev/null \
    || die "tool $want is missing"
done

# 3. The hints the SDK would otherwise leave unset, and which the spec reads as hostile.
printf '%s' "$tools" | jq -e 'all(.[]; .annotations.destructiveHint == false)' >/dev/null \
  || die "a tool is missing destructiveHint=false"
printf '%s' "$tools" | jq -e 'all(.[]; .annotations.openWorldHint == false)' >/dev/null \
  || die "a tool is missing openWorldHint=false"

# 4. Both calls refuse. A granted credential in this version is the one unacceptable outcome.
for id in 3 4; do
  jq -e --argjson id "$id" 'select(.id == $id) | .result.isError == true' <"$OUT" >/dev/null \
    || die "call $id did not return isError=true"
done

# 5. Every call left a line, because an unlogged access is a law 3.3 violation whether or not it
#    was granted.
[ -f "$AUDIT" ] || die "no audit log was written"
lines="$(grep -c . "$AUDIT")"
[ "$lines" = "2" ] || die "expected 2 audit lines, got $lines"

grep -q '"tool":"list_entry_names"' "$AUDIT" || die "the listing call was not audited"
grep -q '"tool":"request_credential"' "$AUDIT" || die "the credential call was not audited"
grep -q '"decision":"denied"' "$AUDIT" || die "no denial was recorded"
grep -q '"label":"ci-probe"' "$AUDIT" || die "the operator-supplied client label was not recorded"

# 6. The audit log is the user's own record and never leaves the machine, so it is allowed to name
#    an entry - but it must never carry a field value. Nothing here had one; assert the shape.
grep -q '"password"' "$AUDIT" || die "the requested field name was not recorded"

echo "ok: keypaste-mcp speaks MCP over stdio, exposes two tools, denies both calls, and audits them"
