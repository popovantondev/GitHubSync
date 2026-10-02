$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Release-UploadWatchdog.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Worker syntax invalid' }
foreach ($name in @('L','Get-Release','Invoke-GitHubGet','Get-ApiHeaders')) {
    $node = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }.GetNewClosure(), $true)
    . ([scriptblock]::Create($node.Extent.Text))
}
$script:releasesUri = 'https://api.github.com/repos/example/test/releases'
$script:config = @{ ReleaseTag = 'v-test' }
$script:releaseId = $null
$script:requests = New-Object 'System.Collections.Generic.List[string]'
$script:boxedRestArray = $false
function Invoke-RestMethod {
    param($Method,$Uri,$Headers,$TimeoutSec)
    $script:requests.Add($Uri)
    if ($Uri -match 'page=1$') {
        $data = @(1..100 | ForEach-Object { @{id=$_;tag_name="other-$_";draft=$false} })
        if ($script:boxedRestArray) { return ,$data }
        return $data
    }
    if ($Uri -match 'page=2$') { return @{id=101;tag_name='v-test';draft=$true;html_url='https://github.com/example/test/releases/tag/untagged-fixture'} }
    if ($Uri -match '/101$') { return @{id=101;tag_name='v-test';draft=$true;html_url='https://github.com/example/test/releases/tag/untagged-fixture'} }
    throw 'Unexpected mock URL'
}
$draft = Get-Release
if ($script:progressReleaseUrl -ne $draft.html_url) { throw 'Exact API draft URL not preserved for the UI' }
if (-not $draft.draft -or $draft.id -ne 101 -or $script:requests.Count -ne 2) { throw 'Paginated draft lookup failed' }
$null = Get-Release
if ($script:requests[2] -notmatch '/101$') { throw 'Release ID was not used for refresh' }
$script:boxedRestArray = $true
$script:releaseId = $null
$script:requests.Clear()
$draft = Get-Release
if ($draft -is [array] -or $draft.id -ne 101 -or -not $draft.draft -or $script:requests.Count -ne 2) { throw 'Boxed PS5 REST array was not flattened into individual releases' }
$null = Get-Release
if ($script:requests[2] -notmatch '/101$') { throw 'Boxed array produced an invalid release ID refresh' }
$script:releaseId = $null
function Invoke-RestMethod { return @() }
foreach ($Language in @('de','ru','en')) {
    $failed = $false
    try { Get-Release | Out-Null } catch {
        $failed = $true
        $expected = L 'Release-Tag nicht gefunden.' 'Тег релиза не найден.' 'Release tag not found.'
        if (-not $_.Exception.Message.StartsWith($expected)) { throw 'Missing-draft message language mismatch' }
    }
    if (-not $failed) { throw 'Missing draft accepted' }
}
Write-Host 'Draft checks passed: pagination, draft selection, ID refresh, three-language missing tag. Mock only, no network.'
