$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$url = 'https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip'
$expected = '63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15'
$work = Join-Path ([IO.Path]::GetTempPath()) ('LinkStack-WinDivert-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $archive = Join-Path $work 'WinDivert.zip'
    Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'WinDivert archive hash mismatch.' }
    Expand-Archive -Path $archive -DestinationPath $work
    $upstream = Join-Path $work 'WinDivert-2.2.2-A'
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    Copy-Item (Join-Path $upstream 'x64\WinDivert.dll') $dist -Force
    Copy-Item (Join-Path $upstream 'x64\WinDivert64.sys') $dist -Force
    Copy-Item (Join-Path $upstream 'LICENSE') (Join-Path $dist 'WinDivert-LICENSE.txt') -Force
    Write-Host 'Verified official WinDivert 2.2.2-A x64 runtime.'
} finally { Remove-Item -LiteralPath $work -Recurse -Force }
