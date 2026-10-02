$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Release-UploadWatchdog.ps1'),[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Worker syntax invalid' }
foreach ($name in @('L','New-GitHubProject','Get-ApiHeaders')) {
    $node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name}.GetNewClosure(),$true)
    . ([scriptblock]::Create($node.Extent.Text))
}
$Language='en'; $script:existing=@(); $script:posts=0; $script:failPost=$false
function Get-PagedGitHubItems { $script:existing }
function Invoke-RestMethod {
    param($Method,$Uri,$Headers,$Body,$ContentType,$TimeoutSec)
    if ($Method -ne 'Post' -or $Uri -ne 'https://api.github.com/user/repos') { throw 'Wrong endpoint' }
    $script:posts++
    $script:payload=[Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json
    if ($script:payload.auto_init -ne $true -or $script:payload.private -isnot [bool]) { throw 'Missing initial README/explicit privacy' }
    if ($script:failPost) { throw 'Synthetic uncertain POST' }
    @{id=42;full_name=('owner/'+$script:payload.name);private=$script:payload.private}
}
foreach ($privacy in @($true,$false)) {
    $result=New-GitHubProject 'owner' 'New-App' $privacy
    if ($result.full_name -ne 'owner/New-App' -or $script:payload.private -ne $privacy) { throw 'Project identity/privacy mismatch' }
}
$script:existing=@(@{name='New-App';owner=@{login='owner'}})
$before=$script:posts; $failed=$false
try { New-GitHubProject 'owner' 'new-app' $false | Out-Null } catch { $failed=$true }
if (-not $failed -or $script:posts -ne $before) { throw 'Existing project changed/duplicated' }
$script:existing=@()
foreach ($name in @('','bad name','https://github.com/owner/repo','../bad','repo.git')) {
    $failed=$false
    try { New-GitHubProject 'owner' $name $true | Out-Null } catch { $failed=$true }
    if (-not $failed -or $script:posts -ne $before) { throw 'Invalid name reached POST' }
}
$script:failPost=$true
foreach ($Language in @('de','ru','en')) {
    $before=$script:posts; $failed=$false
    try { New-GitHubProject 'owner' 'New-App' $true | Out-Null } catch { $failed=$true; if ($_.Exception.Message -match 'Synthetic') { throw 'Raw error leaked' } }
    if (-not $failed -or $script:posts -ne $before+1) { throw 'Uncertain creation retried' }
}
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('WatchdogProjectGuard-'+[guid]::NewGuid().ToString('N')+'.json')
$guardRoot=$fixture+'.files'; New-Item -ItemType Directory -Path $guardRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Release-UploadWatchdog.ps1'),(Join-Path $root 'Uploader-Operations.ps1') -Destination $guardRoot
& (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy Bypass -File (Join-Path $guardRoot 'Release-UploadWatchdog.ps1') -CheckOnly -CreateProjectRequest nonexistent.json -ProgressFile $fixture -Language en | Out-Null
if ($LASTEXITCODE -ne 1 -or (Get-Content -LiteralPath $fixture -Raw | ConvertFrom-Json).message -notmatch 'other modes') { throw 'Read-only/project guard failed' }
Write-Host 'Project creation checks passed: private/public payload, initial README, existing-name conflict, invalid names, no POST retry, actual read-only guard. No network/authentication.'
