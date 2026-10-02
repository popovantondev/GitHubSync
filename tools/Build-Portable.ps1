$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid VERSION.' }
$output = Join-Path $root 'artifacts'
$stage = Join-Path $output "GitHubSync-$version-Portable"
$archive = Join-Path $output "GitHubSync-$version-win-x64.zip"
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $archive)) { throw 'Version already built. Move existing output aside before rebuilding; settings are never removed automatically.' }
& (Join-Path $PSScriptRoot 'Build-Launcher.ps1')
foreach ($part in @('','docs','assets','upload','src')) { New-Item -ItemType Directory -Path (Join-Path $stage $part) -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $root 'artifacts\build\GitHubSync.exe') -Destination (Join-Path $stage 'GitHubSync.exe')
Copy-Item -LiteralPath (Join-Path $root 'config.example.json') -Destination (Join-Path $stage 'config.json')
# Always use a freshly verified upstream archive, never a used credential folder.
& (Join-Path $PSScriptRoot 'Get-Runtime.ps1') -Destination (Join-Path $stage 'runtime\git')
foreach ($name in @('VERSION','Start-GitHubSync.cmd','Start-UploadWatchdog.cmd','Check-Configuration.cmd','Release-UploadWatchdog.ps1','Uploader-Operations.ps1','Sync-Operations.ps1','README.md','LICENSE','THIRD_PARTY.md','runtime.lock.json','runtime-sources.lock.json','CONTRIBUTING.md','SECURITY.md','CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $stage $name)
}
foreach ($name in @('index.html','README.de.md','README.ru.md','ARCHITECTURE.md','DESIGN.md',"RELEASE_NOTES-$version.md",'RELEASE_CHECKLIST.md',"VERIFICATION-$version.md",'VERIFICATION-1.5.1.md','Guide-de.html','Guide-ru.html','Guide-en.html','guide.css','Ui-de.png','Ui-ru.png','Ui-en.png','Ui-de-code.png','Ui-ru-code.png','Ui-en-code.png','Ui-de-progress.png','Ui-ru-progress.png','Ui-en-progress.png','Ui-de-download.png','Ui-ru-download.png','Ui-en-download.png')) {
    Copy-Item -LiteralPath (Join-Path $root ('docs\' + $name)) -Destination (Join-Path $stage ('docs\' + $name))
}
Copy-Item -LiteralPath (Join-Path $root 'assets\sync.ico'),(Join-Path $root 'assets\sync.png') -Destination (Join-Path $stage 'assets')
Copy-Item -LiteralPath (Join-Path $root 'third-party-notices') -Destination (Join-Path $stage 'third-party-notices') -Recurse
foreach ($name in @('SyncTransfer.cs','WindowsPathSafety.cs','GitHubWrite.cs')) { Copy-Item -LiteralPath (Join-Path $root ('src\'+$name)) -Destination (Join-Path $stage ('src\'+$name)) }
& (Join-Path $PSScriptRoot 'Test-Publication.ps1') -PackagePath $stage
Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archive + '.sha256') -Value "$hash  $([IO.Path]::GetFileName($archive))" -Encoding ASCII
Write-Host "Portable app: $stage"
Write-Host "Release archive: $archive"
