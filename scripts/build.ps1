$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & .\test.cmd --build-only
    if ($LASTEXITCODE -ne 0) { throw 'Test utility compilation failed.' }
    & .\dist\LinkStack.Tests.exe --create-icon
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
    & .\build.cmd
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
    Write-Host "Built: $root\dist\LinkStack.exe"
} finally { Pop-Location }
