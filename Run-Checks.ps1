param([switch]$SkipUi)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'runtime\git\cmd\git.exe'))) { & (Join-Path $root 'tools\Get-Runtime.ps1') }
$tests = @('Test-Uploader.ps1','Test-ReleaseWorkflow.ps1','Test-Assets.ps1','Test-Draft.ps1','Test-Catalog.ps1','Test-Runtime.ps1','Test-ProgressSerialization.ps1','Test-CreateProject.ps1','Test-CreateDraft.ps1','Test-Package.ps1','Test-Sync.ps1','Test-TransferFixes.ps1','Test-Publication.ps1','Test-Icons.ps1')
if (-not $SkipUi) { $tests += 'Test-Ui.ps1' }
foreach ($test in $tests) {
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root ('tests\' + $test))
    if ($LASTEXITCODE -ne 0) { throw "Check failed: $test" }
}
& (Join-Path $root 'tools\Test-Publication.ps1')
Write-Host 'All selected checks passed. No real GitHub writes or authentication performed.'
