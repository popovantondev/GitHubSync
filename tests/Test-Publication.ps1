$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('UploaderPublicationTest-' + [guid]::NewGuid().ToString('N'))
$gitExe = Join-Path $root 'runtime\git\cmd\git.exe'
if (-not (Test-Path -LiteralPath $gitExe)) { throw 'Bundled Git required for isolated audit tests.' }
$listing = & $gitExe -C $root ls-files --cached --others --exclude-standard -z
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source files.' }
$paths = @(($listing -join "`n").Split([char]0) | Where-Object { $_ })
foreach ($relative in $paths) {
    $target = Join-Path $fixture $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
& $gitExe -C $fixture init -q
& $gitExe -C $fixture add -- .
if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize fixture index.' }
$savedPath = $env:PATH
try {
    $env:PATH = (Split-Path $gitExe) + ';' + $savedPath
    function Assert-Audit([bool]$success, [string]$expected = '') {
        $savedPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $result = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'tools\Test-Publication.ps1') 2>&1
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = $savedPreference
        if (($exitCode -eq 0) -ne $success -or ($expected -and ($result | Out-String) -notmatch [regex]::Escape($expected))) { throw 'Publication guard did not produce the expected result; fixture output not printed.' }
    }
    Assert-Audit $true
    # Synthetic credentials, never a real user's data; ignored files must stay out.
    $privateFile = Join-Path $fixture 'config.json'
    [IO.File]::WriteAllText($privateFile,'{"source":"' + 'C:\\' + 'Users\\' + 'FixtureAccount\\upload"}')
    Assert-Audit $true
    & $gitExe -C $fixture add -f -- config.json
    Assert-Audit $false 'Generated/private source entry: config.json'
    & $gitExe -C $fixture rm --cached -q -- config.json
    Assert-Audit $true
    $candidate = Join-Path $fixture 'candidate.txt'
    [IO.File]::WriteAllText($candidate,('ghp_' + ('A' * 36)))
    Assert-Audit $false 'Possible secret (value redacted): candidate.txt'
    [IO.File]::WriteAllText($candidate,('C:\' + 'Users\' + 'FixtureAccount\upload'))
    Assert-Audit $false 'Personal Windows path: candidate.txt'
    [IO.File]::WriteAllText($candidate,'synthetic safe text')
    $scriptFile = Join-Path $fixture 'bom-test.ps1'
    [IO.File]::WriteAllText($scriptFile,'Write-Output "fixture"',[Text.UTF8Encoding]::new($false))
    Assert-Audit $false 'Missing PS5 UTF8 BOM: bom-test.ps1'
    [IO.File]::WriteAllText($scriptFile,'Write-Output "fixture"',[Text.UTF8Encoding]::new($true))
    $guide = Join-Path $fixture 'docs\Guide-en.html'
    $original = [IO.File]::ReadAllText($guide)
    [IO.File]::WriteAllText($guide,$original + '<img src="missing-fixture.png">')
    Assert-Audit $false 'Broken local documentation link:'
    [IO.File]::WriteAllText($guide,$original)
    Assert-Audit $true
    Write-Host 'Publication guards passed: clean index, ignored data, forced staging, synthetic token/path, PS5 BOM and broken documentation links. No real credentials/network.'
} finally { $env:PATH = $savedPath }
