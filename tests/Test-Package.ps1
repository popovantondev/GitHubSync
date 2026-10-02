$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$main = Join-Path $root 'Release-UploadWatchdog.ps1'
$source = [IO.File]::ReadAllText($main, [Text.Encoding]::UTF8)
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile($main, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw ($errors | ForEach-Object { $_.Message } | Out-String) }
$credentialIndex = $source.IndexOf('$script:token = Get-GitHubToken', [StringComparison]::Ordinal)
$utf8Index = $source.IndexOf('[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)', [StringComparison]::Ordinal)
if ($credentialIndex -lt 0 -or $utf8Index -lt $credentialIndex) { throw 'GCM must run before switching the console to UTF-8.' }
$config = Get-Content -LiteralPath (Join-Path $root 'config.example.json') -Raw | ConvertFrom-Json
foreach ($property in @('Repository','ReleaseTag','Files','MaxAttempts','RetryBaseSeconds','PollSeconds','HttpTimeoutHours')) {
    if ($null -eq $config.$property) { throw "Missing config property: $property" }
}
if ($config.Repository -ne 'OWNER/REPOSITORY' -or $config.Files.Count -ne 0 -or $config.SourceDirectory -ne 'upload') { throw 'Example configuration must not contain user data.' }
$largeCounter = [Math]::Max([long]0, [long]4911940239)
if ($largeCounter -ne 4911940239L) { throw '64-bit network counter test failed.' }
foreach ($file in @('Start-UploadWatchdog.cmd','Check-Configuration.cmd','README.md','LICENSE')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) { throw "Missing package file: $file" }
}
Write-Host 'Package checks passed: PowerShell syntax, JSON config, large Int64 counter, launcher/docs present.' -ForegroundColor Green
