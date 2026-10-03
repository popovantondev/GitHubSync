param([switch]$IncludeUncommitted,[string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid VERSION.' }
& (Join-Path $PSScriptRoot 'Test-Publication.ps1')
if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) { throw 'Initialize and review the source index first.' }
$status = & git -C $root status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git status.' }
if ($status -and -not $IncludeUncommitted) { throw 'Working tree is not committed. Explicit -IncludeUncommitted creates a candidate from reviewed tracked files only.' }
$listing = & git -C $root ls-files -z
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source index.' }
$paths = @(($listing -join "`n").Split([char]0) | Where-Object { $_ })
if ($paths.Count -eq 0) { throw 'Source index is empty.' }
$output = if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root 'artifacts'}
if(-not(Test-Path -LiteralPath $output)){New-Item -ItemType Directory -Path $output | Out-Null}
$stage = Join-Path ([IO.Path]::GetTempPath()) ("GitHubSync-$version-Source-" + [guid]::NewGuid().ToString('N'))
$archive = Join-Path $output "GitHubSync-$version-source.zip"
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $archive)) { throw 'Source output already exists; never overwrite reviewed artifacts.' }
foreach ($relative in $paths) {
    $target = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archive + '.sha256') -Value "$hash  $([IO.Path]::GetFileName($archive))" -Encoding ASCII
Write-Host "Source candidate: $archive (no .git, dependencies, credentials or generated builds)"
