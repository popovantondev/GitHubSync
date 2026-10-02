$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Release-UploadWatchdog.ps1'),[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Worker syntax invalid' }
foreach ($name in @('L','New-DraftRelease','Get-ApiHeaders')) {
    $node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name}.GetNewClosure(),$true)
    . ([scriptblock]::Create($node.Extent.Text))
}
$Language='en'; $script:existing=@(); $script:posts=0; $script:failPost=$false
function Get-PagedGitHubItems { $script:existing }
function Invoke-RestMethod {
    param($Method,$Uri,$Headers,$Body,$ContentType,$TimeoutSec)
    if ($Method -ne 'Post' -or $Uri -ne 'https://api.github.com/repos/owner/repo/releases') { throw 'Wrong endpoint or method' }
    $script:posts++
    $script:payload=[Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json
    if (-not $script:payload.draft -or $script:payload.generate_release_notes -or $script:payload.prerelease) { throw 'Publication/notes/prerelease were not disabled' }
    if ($script:failPost) { throw 'Synthetic timeout after POST' }
    @{id=42;draft=$true;tag_name=$script:payload.tag_name}
}
$draft=New-DraftRelease 'owner/repo' 'v-test' ([string][char]0x0422 + 'est')
if ($draft.id -ne 42 -or $script:posts -ne 1 -or $script:payload.name[0] -ne [char]0x0422) { throw 'Draft or UTF8 payload failed' }
$script:existing=@(@{id=50;draft=$true;tag_name='v-test'})
if ((New-DraftRelease 'owner/repo' 'v-test' 'Different title').id -ne 50 -or $script:posts -ne 1) { throw 'Existing draft was changed or duplicated' }
$script:existing=@(@{id=50;draft=$false;tag_name='v-test'})
$failed=$false
try { New-DraftRelease 'owner/repo' 'v-test' 'Title' | Out-Null } catch { $failed=$true }
if (-not $failed -or $script:posts -ne 1) { throw 'Published release was accepted or mutated' }
$script:existing=@()
foreach ($tag in @('','bad tag','../tag','v..1','v.lock','v.')) {
    $failed=$false
    try { New-DraftRelease 'owner/repo' $tag 'Title' | Out-Null } catch { $failed=$true }
    if (-not $failed -or $script:posts -ne 1) { throw "Invalid tag accepted: $tag" }
}
$script:failPost=$true
foreach ($Language in @('de','ru','en')) {
    $before=$script:posts; $failed=$false
    try { New-DraftRelease 'owner/repo' 'v-new' 'Title' | Out-Null } catch { $failed=$true; if ($_.Exception.Message -match 'Synthetic timeout') { throw 'Raw error leaked' } }
    if (-not $failed -or $script:posts -ne $before+1) { throw 'Uncertain POST was retried automatically' }
}
Write-Host 'Draft creation checks passed: draft-only POST, UTF8, reuse, published conflict, tag validation, localized uncertain-result failure with no retry. Mock only.'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('WatchdogReadOnlyGuard-'+[guid]::NewGuid().ToString('N')+'.json')
$guardRoot=$fixture+'.files'; New-Item -ItemType Directory -Path $guardRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Release-UploadWatchdog.ps1'),(Join-Path $root 'Uploader-Operations.ps1') -Destination $guardRoot
$powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $guardRoot 'Release-UploadWatchdog.ps1') -CheckOnly -CreateDraftRequest 'nonexistent-request.json' -ProgressFile $fixture -Language en | Out-Null
if ($LASTEXITCODE -ne 1) { throw 'Combined read-only/create modes must fail' }
$state=Get-Content -LiteralPath $fixture -Raw -Encoding UTF8 | ConvertFrom-Json
if ($state.state -ne 'failed' -or $state.message -notmatch 'read-only mode') { throw 'Read-only guard must reject before request loading/authentication' }
Write-Host 'Actual worker read-only/create guard passed without authentication or network.'
