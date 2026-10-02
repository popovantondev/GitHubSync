$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$source=[IO.File]::ReadAllText((Join-Path $root 'Release-UploadWatchdog.ps1'))
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Worker syntax'}
$mockToken=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-GitHubToken'},$true)
$mockGet=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-GitHubGet'},$true)
$mockGetBody=@'
function Invoke-GitHubGet($uri) {
    if($uri -eq 'https://api.github.com/user'){return @{login='synthetic-account'}}
    $release=[pscustomobject]@{id=1;tag_name='v-test';draft=(-not $script:fakePublished);html_url='https://github.com/example/test/releases/tag/v-test';assets=@([pscustomobject]@{id=4;name='test.txt';label='test.txt';size=5;state=$(if($script:fakePartial){'new'}else{'uploaded'});browser_download_url='https://github.com/example/test/releases/download/v-test/test.txt'})}
    if($uri -match 'page=1'){return @($release)}
    if($uri -match '/releases/1$'){return $release}
    throw 'Unexpected GET in isolated workflow'
}
'@
$mockRest=@'
function Invoke-UploaderTransport {
    param($Method,$Uri,$Payload,$Seconds)
    if($Method -ne 'Patch' -or $Uri -notmatch '/releases/1$' -or $Payload.draft -ne $false){throw 'Unexpected write'}
    $script:fakePublished=$true
    return '{"draft":false}'
}
'@
$taskFixture=Join-Path ([IO.Path]::GetTempPath()) ('UploaderWorkflow-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskFixture,(Join-Path $taskFixture 'upload') | Out-Null
[IO.File]::WriteAllText((Join-Path $taskFixture 'upload\test.txt'),'hello',[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $taskFixture 'Uploader-Operations.ps1'),([IO.File]::ReadAllText((Join-Path $root 'Uploader-Operations.ps1'))+"`r`n"+$mockRest),[Text.UTF8Encoding]::new($true))
$patched=$source.Replace($mockToken.Extent.Text,"function Get-GitHubToken { return 'synthetic-no-real-credential' }").Replace($mockGet.Extent.Text,$mockGetBody).Replace("'Local\GitHubReleaseWatchdog.Upload'","'Local\UploaderWorkflow-$([guid]::NewGuid().ToString('N'))'")
$lines=$patched -split "`r?`n"
$patched=($lines[0..1] -join "`r`n")+"`r`n"+$mockRest+"`r`n"+($lines[2..($lines.Length-1)] -join "`r`n")
$worker=Join-Path $taskFixture 'Release-UploadWatchdog.ps1'
[IO.File]::WriteAllText($worker,$patched,[Text.UTF8Encoding]::new($true))
$powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
foreach($publish in @($false,$true)) {
    $config=@{Repository='example/test';ReleaseTag='v-test';Files=@('test.txt');SourceDirectory='upload';MaxAttempts=1;RetryBaseSeconds=1;PollSeconds=5;HttpTimeoutHours=1;PublishAfterUpload=$publish;ExpectedAccount='synthetic-account'}
    [IO.File]::WriteAllText((Join-Path $taskFixture 'config.json'),($config | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    $state=Join-Path $taskFixture 'state.json'
    & $powershell -NoProfile -ExecutionPolicy Bypass -File $worker -ProgressFile $state -Language en | Out-Null
    $snapshot=Get-Content -LiteralPath $state -Raw | ConvertFrom-Json
    if($LASTEXITCODE -ne 0 -or $snapshot.state -ne 'completed' -or $snapshot.published -ne $publish -or $snapshot.filesCompleted -ne 1){throw "Combined release workflow failed, publish=$publish"}
    & $powershell -NoProfile -ExecutionPolicy Bypass -File $worker -CheckOnly -ProgressFile $state -Language en | Out-Null
    $snapshot=Get-Content -LiteralPath $state -Raw | ConvertFrom-Json
    if($LASTEXITCODE -ne 0 -or $snapshot.state -ne 'checked' -or $snapshot.published){throw 'Check published a release'}
}
# Exercise real dispatch guards in the isolated copy, before any authentication.
$request=Join-Path $taskFixture 'request.json'
foreach($operation in @('upload','publish')) {
    [IO.File]::WriteAllText($request,(@{operation=$operation;repository='example/test'} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    & $powershell -NoProfile -ExecutionPolicy Bypass -File $worker -CheckOnly -UploaderRequest $request -ProgressFile $state -Language en | Out-Null
    $snapshot=Get-Content -LiteralPath $state -Raw | ConvertFrom-Json
    if($LASTEXITCODE -ne 1 -or $snapshot.message -notmatch 'Check mode cannot change'){throw 'Uploader read-only guard failed'}
}
& $powershell -NoProfile -ExecutionPolicy Bypass -File $worker -ListRepositories -UploaderRequest $request -ProgressFile $state -Language en | Out-Null
if($LASTEXITCODE -ne 1){throw 'Mixed operation guard failed'}
Write-Host 'Actual worker workflow passed: draft-only, upload+publish, check never publishes, read-only and mixed-mode guards. All API/auth replaced in isolated temporary copy; no real network or writes.'
