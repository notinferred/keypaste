#!/usr/bin/env bash
# Replaces an MSI's cabinet stream with a cabinet header and nothing else, so the package still opens and
# still costs and sequences, and the file installation inside the transaction is what fails (F.14, D-0213).
#
# This is how an interrupted upgrade is produced on a runner: no power cut, no killed process - five other
# ways were measured and none interrupted anything (D-0210, D-0212). Only ever run against a copy.
#
# Usage: break-msi-cabinet.sh <msi-copy>
set -euo pipefail

die() { echo "::error::$*" >&2; exit 1; }

[ $# -eq 1 ] || die 'usage: break-msi-cabinet.sh <msi-copy>'
msi="$1"
[ -f "$msi" ] || die "no package at $msi"
case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) ;; *) die 'an MSI can only be edited on Windows' ;; esac

broken="$msi.cab"
printf 'MSCF' > "$broken"
head -c 4096 /dev/zero >> "$broken"
trap 'rm -f "$broken"' EXIT

# msiViewModifyUpdate is 2, and msiOpenDatabaseModeTransact is 1.
powershell -NoProfile -NonInteractive -Command "\$ErrorActionPreference = 'Stop'
  \$i = New-Object -ComObject WindowsInstaller.Installer
  \$db = \$i.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', \$null, \$i, @('$(cygpath -w "$msi")', 1))
  \$media = \$db.GetType().InvokeMember('OpenView', 'InvokeMethod', \$null, \$db, @('SELECT \`Cabinet\` FROM \`Media\`'))
  [void]\$media.GetType().InvokeMember('Execute', 'InvokeMethod', \$null, \$media, \$null)
  \$row = \$media.GetType().InvokeMember('Fetch', 'InvokeMethod', \$null, \$media, \$null)
  if (\$row -eq \$null) { throw 'the package declares no media' }
  \$cab = (\$row.GetType().InvokeMember('StringData', 'GetProperty', \$null, \$row, 1)).TrimStart('#')
  \$view = \$db.GetType().InvokeMember('OpenView', 'InvokeMethod', \$null, \$db, @(\"SELECT \`Name\`,\`Data\` FROM \`_Streams\` WHERE \`Name\` = '\$cab'\"))
  [void]\$view.GetType().InvokeMember('Execute', 'InvokeMethod', \$null, \$view, \$null)
  \$rec = \$view.GetType().InvokeMember('Fetch', 'InvokeMethod', \$null, \$view, \$null)
  if (\$rec -eq \$null) { throw \"the package holds no stream named \$cab\" }
  \$rec.GetType().InvokeMember('SetStream', 'InvokeMethod', \$null, \$rec, @(2, '$(cygpath -w "$broken")'))
  [void]\$view.GetType().InvokeMember('Modify', 'InvokeMethod', \$null, \$view, @(2, \$rec))
  \$db.GetType().InvokeMember('Commit', 'InvokeMethod', \$null, \$db, \$null)
  \"replaced \$cab with a cabinet header and 4096 zero bytes\"" | tr -d '\r'
