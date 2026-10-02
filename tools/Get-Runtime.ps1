param([string]$Destination)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $root 'runtime.lock.json') -Raw | ConvertFrom-Json
if (-not $Destination) { $Destination = Join-Path $root 'runtime\git' }
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Runtime destination exists; existing installations or credentials are never overwritten.' }
$cache = Join-Path $root 'artifacts\dependencies'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache $lock.archiveName
if (-not (Test-Path -LiteralPath $archive)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $download = Join-Path $cache ('download-' + [guid]::NewGuid().ToString('N') + '.zip')
    Invoke-WebRequest -UseBasicParsing -Uri $lock.url -OutFile $download
    if ((Get-FileHash -LiteralPath $download).Hash.ToLowerInvariant() -ne $lock.sha256) { throw 'Upstream runtime checksum mismatch. Nothing extracted.' }
    Move-Item -LiteralPath $download -Destination $archive
}
if ((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne $lock.sha256) { throw 'Cached runtime checksum mismatch. Nothing extracted.' }
Expand-Archive -LiteralPath $archive -DestinationPath $Destination
foreach ($relative in @('cmd\git.exe','mingw64\bin\git-credential-manager.exe','LICENSE.txt','mingw64\doc\git-credential-manager\LICENSE','mingw64\doc\git-credential-manager\NOTICE','etc\package-versions.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Destination $relative))) { throw "Missing upstream component: $relative" }
}
Write-Host "Verified official runtime extracted: $Destination"
