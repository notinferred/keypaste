param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Checks)
$ErrorActionPreference = 'Stop'
if ($env:OS -eq 'Windows_NT') {
    # Windows' system bash.exe launches WSL; this entry point needs Git Bash.
    $gitCommand = (Get-Command git.exe).Source
    $gitRoot = Split-Path (Split-Path $gitCommand -Parent) -Parent
    $bash = Join-Path $gitRoot 'bin/bash.exe'
    if (-not (Test-Path -LiteralPath $bash)) {
        throw 'Git Bash was not found beside git.exe. Run bash scripts/verify.sh from Git Bash.'
    }
} else {
    $bash = (Get-Command bash).Source
}
& $bash (Join-Path $PSScriptRoot 'verify.sh') @Checks
exit $LASTEXITCODE
