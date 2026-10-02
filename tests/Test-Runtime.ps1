$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Release-UploadWatchdog.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Worker syntax invalid.' }
foreach ($name in @('L','Find-Git','Get-GitHubToken')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }.GetNewClosure(), $true)
    . ([scriptblock]::Create($function.Extent.Text))
}
$savedPath = $env:PATH
$fixture = Join-Path $env:TEMP ('WatchdogRuntimeTest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $env:PATH = ''
    if ((Find-Git) -ne (Join-Path $root 'runtime\git\cmd\git.exe')) { throw 'Bundled Git not selected with empty PATH.' }
    $version = & (Find-Git) --version
    if ($LASTEXITCODE -ne 0 -or $version -notmatch '^git version') { throw 'Bundled Git cannot start.' }
    $manager = Join-Path $root 'runtime\git\mingw64\bin\git-credential-manager.exe'
    # GCM needs Git in its process PATH, exactly as configured by the worker.
    $env:PATH = (Join-Path $root 'runtime\git\cmd') + ';' + (Join-Path $root 'runtime\git\mingw64\bin')
    $managerVersion = & $manager --version
    if ($LASTEXITCODE -ne 0 -or $managerVersion -notmatch '^2\.') { throw 'Bundled GCM cannot start.' }
    $projectRoot = $root
    $env:PATH = ''
    $root = $fixture
    foreach ($Language in @('de','ru','en')) {
        $failed = $false
        try { Find-Git | Out-Null } catch {
            $failed = $true
            $expected = L 'Git fehlt.' 'Git не найден.' 'Git not found.'
            if (-not $_.Exception.Message.StartsWith($expected)) { throw 'Missing-runtime language mismatch.' }
        }
        if (-not $failed) { throw 'Missing runtime was silently accepted.' }
    }
    $fakeGit = Join-Path $fixture 'git.exe'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:exe "/out:$fakeGit" (Join-Path $PSScriptRoot 'FakeGit.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile protocol fixture.' }
    function Find-Git { return $fakeGit }
    $Language = 'ru'
    $value = Get-GitHubToken
    if ($value -ne 'synthetic-test-value') { throw 'Credential stdin protocol failed.' }
    $value = $null
    foreach ($license in @('LICENSE.txt','mingw64\doc\git-credential-manager\LICENSE','mingw64\doc\git-credential-manager\NOTICE')) {
        if (-not (Test-Path -LiteralPath (Join-Path $projectRoot ('runtime\git\' + $license)))) { throw 'Runtime license missing.' }
    }
    Write-Host 'Runtime checks passed: empty PATH, Git/GCM start, three-language missing-runtime errors, synthetic credential protocol, licenses. No authentication/network used.'
}
finally { $env:PATH = $savedPath }
