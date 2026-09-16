#!/usr/bin/env bash
# Accepts a Windows file only when signtool's Authenticode policy accepts it, its chain ends at the
# named root and it carries an RFC 3161 timestamp (docs/STEPS.md 3.6b).
#
# Usage:
#   verify-windows-signature.sh <root-thumbprint> <file>...
#   verify-windows-signature.sh --selftest
set -euo pipefail

die() { echo "::error::$*" >&2; exit 1; }

find_signtool() {
  local found
  found="$(find '/c/Program Files (x86)/Windows Kits/10/bin' -path '*/x64/signtool.exe' 2>/dev/null | sort -V | tail -n 1 || true)"
  [ -n "$found" ] || die "no x64 signtool.exe under the Windows 10 SDK"
  echo "$found"
}

# judge <file> <want-root> <signtool-status> <signtool-output> <signature-status> <root> <timestamper>
judge() {
  local file="$1" want="$2" code="$3" output="$4" status="$5" root="$6" stamper="$7"
  [ "$code" -eq 0 ] || { echo "refused: $file: signtool verify /pa exited $code"; return 1; }
  case "$output" in *"not timestamped"*) stamper="" ;; esac
  [ -n "$stamper" ] || { echo "refused: $file: carries no timestamp"; return 1; }
  [ "$status" = "Valid" ] || { echo "refused: $file: signature status is $status"; return 1; }
  [ "${root^^}" = "${want^^}" ] || { echo "refused: $file: chains to $root, not $want"; return 1; }
  echo "accepted: $file: chains to $root, timestamped by $stamper"
}

read_signature() {
  powershell -NoProfile -NonInteractive -Command "
    \$ErrorActionPreference = 'Stop'
    \$s = Get-AuthenticodeSignature -LiteralPath '$1'
    \$root = ''
    if (\$s.SignerCertificate) {
      \$chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
      \$chain.ChainPolicy.RevocationMode = 'NoCheck'
      [void]\$chain.Build(\$s.SignerCertificate)
      \$root = \$chain.ChainElements[\$chain.ChainElements.Count - 1].Certificate.Thumbprint
    }
    'Status=' + \$s.Status
    'Root=' + \$root
    'Stamper=' + \$(if (\$s.TimeStamperCertificate) { \$s.TimeStamperCertificate.Subject } else { '' })
  " | tr -d '\r'
}

field() { sed -n "s/^$1=//p" <<< "$2"; }

verify() {
  local want="$1" signtool file code output reading failed=0
  shift
  signtool="$(find_signtool)"
  for file in "$@"; do
    [ -f "$file" ] || die "no file at $file"
    set +e
    output="$(MSYS_NO_PATHCONV=1 "$signtool" verify /pa /v "$(cygpath -w "$file")" 2>&1)"
    code=$?
    set -e
    reading="$(read_signature "$(cygpath -w "$file")")"
    judge "$file" "$want" "$code" "$output" "$(field Status "$reading")" "$(field Root "$reading")" "$(field Stamper "$reading")" \
      || { failed=1; echo "$output" | sed -n '1,12p'; }
  done
  [ "$failed" -eq 0 ]
}

selftest() {
  local cases=0 failures=0
  check() {
    local name="$1" want="$2"
    shift 2
    cases=$((cases + 1))
    local out status=0
    out="$(judge "$@")" || status=$?
    case "$want:$status" in
      accept:0 | refuse:1) echo "  $name: $out" ;;
      *) echo "::error::$name expected $want and got: $out"; failures=$((failures + 1)) ;;
    esac
  }
  local ok='Successfully verified: a.exe
The signature is timestamped: Wed Sep 16 10:00:00 2026'
  check valid accept a.exe ABCD 0 "$ok" Valid abcd 'CN=Microsoft Public RSA Time Stamping Authority'
  check signtool-refuses refuse a.exe ABCD 1 'SignTool Error: A certificate chain processed, but terminated in a root certificate which is not trusted' UnknownError ABCD 'CN=TSA'
  check changed-byte refuse a.exe ABCD 1 'SignTool Error: WinVerifyTrust returned error: 0x80096010' HashMismatch ABCD 'CN=TSA'
  check untimestamped refuse a.exe ABCD 0 'Successfully verified: a.exe
File is not timestamped.' Valid ABCD ''
  check stamper-absent refuse a.exe ABCD 0 "$ok" Valid ABCD ''
  check other-root refuse a.exe ABCD 0 "$ok" Valid EF01 'CN=TSA'
  check status-not-valid refuse a.exe ABCD 0 "$ok" NotSigned ABCD 'CN=TSA'
  [ "$failures" -eq 0 ] || die "$failures of $cases verify-windows-signature.sh cases failed"
  echo "ok: $cases verify-windows-signature.sh cases"
}

if [ "${1:-}" = "--selftest" ]; then
  selftest
else
  [ $# -ge 2 ] || die "usage: verify-windows-signature.sh <root-thumbprint> <file>..."
  verify "$@"
fi
