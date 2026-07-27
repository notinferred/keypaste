#!/usr/bin/env bash
# Proves that the shipped binaries produce an audit log whose hash chain detects tampering - and,
# just as importantly, that ordinary damage is not reported as tampering.
#
# The in-process tests build their files with the real writer too, but they never cross a process
# boundary: the log is written by keypaste-mcp and read back by keypaste, two separate programs that
# have to agree byte for byte about what a record is. That agreement is the whole mitigation for
# THREATS.md T-5, and this is the only place both halves are the shipped ones.
#
# NEGATIVE CONTROL: every "it verifies" below is paired with an edit that must break it, and every
# edit is followed by a restore that must verify again - so a verifier that simply always passed, or
# always failed, cannot get through this script. It also asserts the two things the chain cannot do,
# because a gate that only tested the claims would let the claims quietly grow.
# These checks must never be skipped or soft-passed.
set -euo pipefail

readonly MCP="${KEYPASTE_MCP_BIN:-artifacts/bin/Keypaste.Mcp/release/keypaste-mcp}"
readonly CLI="${KEYPASTE_BIN:-artifacts/bin/Keypaste.Cli/release/keypaste}"

die() {
  echo "::error::$*" >&2
  if [ -f "${AUDIT:-}" ]; then echo "--- audit log ---" >&2; cat "$AUDIT" >&2; fi
  exit 1
}

MCP_PATH="$MCP"
[ -x "$MCP_PATH" ] || MCP_PATH="${MCP}.exe"
[ -x "$MCP_PATH" ] || die "keypaste-mcp not found at $MCP (build first)"

CLI_PATH="$CLI"
[ -x "$CLI_PATH" ] || CLI_PATH="${CLI}.exe"
[ -x "$CLI_PATH" ] || die "keypaste not found at $CLI (build first)"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

readonly AUDIT="$WORK/audit.jsonl"
readonly GOOD="$WORK/audit.good"

# Runs the CLI and reports its exit code without tripping `set -e`.
status() {
  local rc=0
  "$@" >"$WORK/out.txt" 2>"$WORK/err.txt" || rc=$?
  echo "$rc"
}

expect() {
  local want="$1"; shift
  local what="$1"; shift
  local got
  got="$(status "$@")"
  [ "$got" = "$want" ] || {
    echo "--- stdout ---" >&2; cat "$WORK/out.txt" >&2
    echo "--- stderr ---" >&2; cat "$WORK/err.txt" >&2
    die "$what: expected exit $want, got $got"
  }
}

verify() { expect "$1" "$2" "$CLI_PATH" log verify --audit-log "$AUDIT"; }

restore() {
  cp "$GOOD" "$AUDIT"
  verify 0 "the restored log"
}

# Drives the real server over real pipes, asking for each entry named in "$@".
run_server() {
  {
    printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"ci-probe","version":"1.0.0"}}}'
    printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_entry_names","arguments":{}}}'

    local id=3
    local entry
    for entry in "$@"; do
      printf '{"jsonrpc":"2.0","id":%s,"method":"tools/call","params":{"name":"request_credential","arguments":{"entry":"env/dev/%s","field":"password","reason":"ci probe","ttl_seconds":60}}}\n' \
        "$id" "$entry"
      id=$((id + 1))
    done

    sleep 5
  } | "$MCP_PATH" --vault "$WORK/vault.kdbx" --audit-log "$AUDIT" --client-label ci-probe \
        >"$WORK/mcp-out.txt" 2>"$WORK/mcp-err.txt"
}

# ---------------------------------------------------------------------------
# A real log, written by the real server over real pipes.
# ---------------------------------------------------------------------------
run_server STRIPE_KEY DB_URL || die "keypaste-mcp exited non-zero"

[ -f "$AUDIT" ] || die "no audit log was written"
records="$(grep -c . "$AUDIT")"
[ "$records" = "3" ] || die "expected 3 audit records, got $records"

cp "$AUDIT" "$GOOD"

# ---------------------------------------------------------------------------
# 1. The reader agrees with the writer, across two separate programs.
# ---------------------------------------------------------------------------
expect 0 "listing a healthy log" "$CLI_PATH" log --audit-log "$AUDIT"
grep -q 'env/dev/STRIPE_KEY' "$WORK/out.txt" || die "the table does not name the entry that was asked for"
grep -q 'ci-probe' "$WORK/out.txt" || die "the table does not name the client"
grep -q '3 records' "$WORK/out.txt" || die "the table does not count the records"

verify 0 "a log nobody touched"
grep -q '3 records verified' "$WORK/out.txt" || die "verify did not count the records"

anchor="$("$CLI_PATH" log verify --audit-log "$AUDIT" | sed -n 's/^Latest: seq [0-9]*, hash //p')"
[ "${#anchor}" = "64" ] || die "verify did not print a 64-character anchor hash (got '${anchor}')"

# The filters narrow, and say that they narrowed.
expect 0 "--denied" "$CLI_PATH" log --audit-log "$AUDIT" --denied
grep -q 'refused calls only' "$WORK/out.txt" || die "--denied did not say the view was filtered"

expect 0 "--client with no match" "$CLI_PATH" log --audit-log "$AUDIT" --client no-such-client
grep -q 'No records matched' "$WORK/out.txt" || die "an empty filtered view printed nothing at all"

# ---------------------------------------------------------------------------
# 2. Tampering. Each edit must be caught; the restore after it must pass.
# ---------------------------------------------------------------------------

# A refusal edited into a grant - the same length, which is what an attacker would choose.
sed -i.bak 's/"decision":"denied"/"decision":"granted"/' "$AUDIT" && rm -f "$AUDIT.bak"
verify 5 "a denial edited into a grant"
grep -q 'THE CHAIN IS BROKEN' "$WORK/out.txt" || die "a broken chain was not announced"
grep -q 'own bytes have changed' "$WORK/out.txt" || die "the alteration was not named as one"

# The listing must still show the records, and must still fail.
expect 5 "listing an edited log" "$CLI_PATH" log --audit-log "$AUDIT"
grep -q 'env/dev/STRIPE_KEY' "$WORK/out.txt" || die "an edited log was hidden instead of flagged"
restore

# A record removed from the middle. The one after it is what notices.
sed -i.bak '2d' "$AUDIT" && rm -f "$AUDIT.bak"
verify 5 "a record removed from the middle"
grep -q 'does not follow the record before it' "$WORK/out.txt" || die "a removal was not named as one"
restore

# Something else writing into the log.
printf '%s\n' '{"tampered":true}' >>"$AUDIT"
verify 5 "a line something else wrote"
grep -q 'not a record keypaste wrote' "$WORK/out.txt" || die "a foreign line was not named as one"
restore

# A forged record claiming to predate the chain, spliced into the middle. It breaks no link -
# nothing before or after it changed - so the only thing standing between it and a rewritten history
# is that keypaste never writes a v1 record after a v2 one.
{
  head -n 1 "$GOOD"
  printf '%s\n' '{"v":1,"ts":"2026-07-26T14:10:00.000Z","seq":9,"pid":1,"client":{"label":"ci-probe"},"tool":"request_credential","args":{"entry":"env/prod/PAYROLL_DB"},"decision":"granted","method":"prompt"}'
  tail -n +2 "$GOOD"
} >"$AUDIT"
verify 5 "a forged record spliced into the middle"
grep -q 'keypaste never writes one there' "$WORK/out.txt" || die "an inserted unverifiable record was not named"

# And the table must not present it as a record the chain stands behind.
expect 5 "listing a log with a forged record" "$CLI_PATH" log --audit-log "$AUDIT"
grep -q 'env/prod/PAYROLL_DB' "$WORK/out.txt" || die "the forged record was hidden instead of marked"
grep -q 'does not vouch for this row' "$WORK/out.txt" || die "the forged record was not marked in the table"
restore

# The last record edited, and the file's final newline removed with it. Skipping an unterminated
# last line would make deleting one byte the way to edit the newest record freely.
# `$(...)` strips the trailing newlines, which is the whole point of this case.
printf '%s' "$(sed 's/"decision":"denied"/"decision":"granted"/' "$GOOD")" >"$AUDIT"
verify 5 "the last record edited, with the final newline removed"
grep -q 'own bytes have changed' "$WORK/out.txt" || die "an edit hid behind a missing final newline"
restore

# ---------------------------------------------------------------------------
# 3. What the chain cannot do, asserted so that it cannot quietly start claiming it can.
# ---------------------------------------------------------------------------

# Records deleted from the end leave a chain that is internally perfect. `verify` says so, and it is
# telling the truth about every record it can still see - THREATS.md T-5.
head -n 1 "$GOOD" >"$AUDIT"
verify 0 "a log truncated from the end"

# An anchor recorded earlier is the only thing that catches it.
expect 5 "an anchor whose record is gone" "$CLI_PATH" log verify --audit-log "$AUDIT" --expect "$anchor"
grep -q 'NOT IN THIS FILE' "$WORK/out.txt" || die "the missing anchor was not reported"

# And the anchor has to mean "that record is here", not "those characters are somewhere in the file".
# The entry argument is text the agent writes, so a hash planted there needs no file access at all.
run_server "$anchor" || die "keypaste-mcp exited non-zero"
grep -q "$anchor" "$AUDIT" || die "the planted anchor did not reach the log"
expect 5 "an anchor planted in an entry name" "$CLI_PATH" log verify --audit-log "$AUDIT" --expect "$anchor"
grep -q 'NOT IN THIS FILE' "$WORK/out.txt" || die "a planted hash was accepted as the anchored record"

restore
expect 0 "an anchor that is still there" "$CLI_PATH" log verify --audit-log "$AUDIT" --expect "$anchor"

# ---------------------------------------------------------------------------
# 4. Ordinary damage is not tampering. A verifier that reddens after a crash gets ignored, and then
#    the one alarm that mattered is the one nobody reads.
# ---------------------------------------------------------------------------
printf '{"v":2,"ts":"2026-07-26T14:03:11.4' >>"$AUDIT"
verify 0 "a write cut short by a crash"
grep -q 'interrupted write' "$WORK/out.txt" || die "an unfinished last line was not explained"

# ---------------------------------------------------------------------------
# 5. One appended byte must not stop the bridge working. Refusing to write when the file does not
#    end in a record made a blank line out of an editor a permanent denial of every credential
#    request - a cheaper lever for an attacker than the one the refusal was meant to close.
# ---------------------------------------------------------------------------
restore
before="$(grep -c . "$AUDIT")"
printf '\n' >>"$AUDIT"
run_server RECOVERY_KEY || die "keypaste-mcp refused to start over a blank line in the log"

after="$(grep -c . "$AUDIT")"
[ "$after" -gt "$before" ] || die "the server started but wrote nothing after a blank line"

# It recorded around the junk rather than starting a new chain, so the verifier must not report a
# truncation that never happened.
verify 0 "a log with a blank line in it"
"$CLI_PATH" log verify --audit-log "$AUDIT" >"$WORK/out.txt" 2>&1 || true
grep -q 'cut off' "$WORK/out.txt" && die "keypaste manufactured a truncation report against itself"

restore
echo "ok: the chain catches edits, removals, foreign lines, forged records and a rewritten last"
echo "ok: line; forgives an interrupted write and a blank line; and says plainly that it cannot"
echo "ok: see a truncation without an anchor that names a record, not a string in the file"
