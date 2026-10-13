param([string]$Version = '1.0.2')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a numeric version, e.g. 1.0.2.' }
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) ('LinkStack-package-' + [Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $work 'LinkStack'
New-Item -ItemType Directory -Path $bundle | Out-Null
try {
    foreach ($name in 'LinkStack.exe','LinkStack.ico','WinDivert.dll','WinDivert64.sys') {
        $source = Join-Path (Join-Path $root 'dist') $name
        if (!(Test-Path $source)) { throw "Missing $name. Run build.ps1 and fetch-windivert.ps1 first." }
        Copy-Item $source $bundle
    }
    foreach ($name in 'README.md','README.pl.md','LICENSE','CHANGELOG.md') { Copy-Item (Join-Path $root $name) $bundle }
    Copy-Item (Join-Path $root 'third_party') $bundle -Recurse
    Copy-Item (Join-Path $root 'third_party\WinDivert-LICENSE.txt') (Join-Path $bundle 'WinDivert-LICENSE.txt')
    $zip = Join-Path $artifacts "LinkStack-$Version-windows-x64.zip"
    Compress-Archive -Path $bundle -DestinationPath $zip -Force
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($zip))" | Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Packaged: $zip"
} finally { Remove-Item -LiteralPath $work -Recurse -Force }
