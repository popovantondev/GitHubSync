$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText((Join-Path $root 'Release-UploadWatchdog.ps1'), [Text.Encoding]::UTF8)
$pattern = [regex]::Escape("Add-Type -TypeDefinition @'") + '(?<code>.*?)' + [regex]::Escape("'@ -ReferencedAssemblies")
$match = [regex]::Match($source, $pattern, [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $match.Success) { throw 'Could not locate the upload progress writer.' }

Add-Type -TypeDefinition $match.Groups['code'].Value -ReferencedAssemblies 'System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll'
$payload = [IO.Path]::GetTempFileName()
$progress = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($payload, ('x' * 4096), [Text.UTF8Encoding]::new($false))
    [IO.File]::Delete($progress)
    $content = [WatchdogUploadContent]::new($payload, $progress, 3, 1, 1024, [long]4911940239, [string[]]@('confirmed.zip'), 2)
    $null = $content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    $content.Dispose()

    $state = Get-Content -LiteralPath $progress -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($state.state -ne 'uploading' -or $state.file -ne [IO.Path]::GetFileName($payload)) { throw 'Progress phase or filename was not serialized.' }
    if ([long]$state.fileBytes -ne 4096 -or [long]$state.fileSent -ne 4096) { throw 'Per-file progress was not serialized correctly.' }
    if ([long]$state.completedBytes -ne 1024 -or [long]$state.totalBytes -ne 4911940239) { throw 'Overall 64-bit byte counters were not serialized correctly.' }
    if ($state.confirmedFiles[0] -ne 'confirmed.zip' -or $state.attempt -ne 2) { throw 'Confirmation list or retry attempt was not serialized correctly.' }
    Write-Host 'Progress serialization passed: current file, completion bytes, and >Int32 total.' -ForegroundColor Green
}
finally {
    foreach ($path in @($payload, $progress, $progress + '.tmp')) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}
