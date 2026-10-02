$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$Language='ru'
function L($de,$ru,$en) { if($Language -eq 'de'){$de}elseif($Language -eq 'en'){$en}else{$ru} }
. (Join-Path $root 'Uploader-Operations.ps1')
$taskFixture=Join-Path ([IO.Path]::GetTempPath()) ('UploaderTest-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskFixture,(Join-Path $taskFixture 'sub') | Out-Null
[IO.File]::WriteAllBytes((Join-Path $taskFixture 'sub\данные.bin'),[byte[]](0,1,2,255))
[IO.File]::WriteAllText((Join-Path $taskFixture 'README.md'),'new content',[Text.UTF8Encoding]::new($false))
$request=[pscustomobject]@{repository='example/test';sourceDirectory=$taskFixture;paths=@('README.md','sub/данные.bin');destination='';branch=''}
$script:head='old'; $script:writes=New-Object 'System.Collections.Generic.List[object]'; $script:conflict=$false; $script:protect=$false; $script:uncertain=$false
$script:states=New-Object 'System.Collections.Generic.List[string]'
function Write-ProgressState($state,$file,$size,$sent,$message) { $script:states.Add($state) }
function Reset-Progress {
    $script:progressConfirmed=New-Object 'System.Collections.Generic.List[string]'; $script:progressFilesDone=0; $script:progressFileCount=0; $script:progressBytesDone=[long]0; $script:progressBytesTotal=[long]0
    $script:head='old'; $script:writes.Clear(); $script:states.Clear(); $script:conflict=$false; $script:protect=$false; $script:uncertain=$false
}
function Invoke-GitHubGet($uri) {
    if($uri -match '/git/ref/') { return @{ref='refs/heads/main';object=@{sha=$script:head}} }
    if($uri -match '/git/commits/') { return @{sha='old';tree=@{sha='base-tree'}} }
    if($uri -match '/git/trees/base-tree$') { return @{truncated=$false;tree=@(@{path='README.md';mode='100755';type='blob';sha=$script:readmeSha},@{path='keep.txt';mode='100644';type='blob';sha='keep'})} }
    if($uri -match '/releases/1$') { return $script:release }
    if($uri -match '/repos/example/test$') { return @{default_branch='main';private=$true;archived=$false;permissions=@{push=$true}} }
    throw "Unexpected GET $uri"
}
function Invoke-UploaderWrite($method,$uri,$body) {
    $script:writes.Add(@{method=$method;uri=$uri;body=$body})
    if($uri -match '/git/blobs$') {
        $bytes=[Convert]::FromBase64String($body.content); $header=[Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0"); $hash=[Security.Cryptography.SHA1]::Create()
        try { $null=$hash.TransformBlock($header,0,$header.Length,$header,0); $null=$hash.TransformFinalBlock($bytes,0,$bytes.Length); return @{sha=([BitConverter]::ToString($hash.Hash)).Replace('-','').ToLowerInvariant()} } finally {$hash.Dispose()}
    }
    if($uri -match '/git/trees$') { if($body.base_tree -ne 'base-tree' -or @($body.tree | Where-Object { -not $_.sha }).Count) { throw 'Deletion or missing base_tree' }; return @{sha='new-tree'} }
    if($uri -match '/git/commits$') { if($body.parents.Count -ne 1 -or $body.parents[0] -ne 'old') { throw 'Wrong parent' }; if($script:conflict){$script:head='someone-else'}; return @{sha='new-commit'} }
    if($uri -match '/git/refs/') { if($body.force -ne $false) {throw 'Force update'}; if($script:protect){throw 'Protected branch'}; $script:head=$body.sha; if($script:uncertain){throw 'Response lost'}; return @{object=@{sha=$body.sha}} }
    if($uri -match '/releases/1$') { if($body.draft -ne $false) {throw 'Publication payload'}; $script:release.draft=$false; if($script:uncertain){throw 'Response lost'}; return $script:release }
    throw "Unexpected write $uri"
}
function Assert($condition,$message) { if(-not $condition){throw $message} }
Reset-Progress
$script:readmeSha='older-content'
$plan=Get-ProjectPlan $request
Assert ($plan.branch -eq 'main' -and $plan.entries.Count -eq 2) 'Default branch/nested selection'
Assert ($plan.entries[0].action -eq 'update' -and $plan.entries[0].mode -eq '100755') 'Update/executable mode'
Assert ($plan.entries[1].action -eq 'add' -and $plan.entries[1].target -ceq 'sub/данные.bin') 'Unicode/binary/nested add'
$request | Add-Member baseCommit $plan.baseCommit; $request | Add-Member entries $plan.entries
Send-ProjectFiles $request
Assert ($script:head -eq 'new-commit' -and $script:states[-1] -eq 'completed') 'Confirmed commit'
Assert ($script:progressConfirmed.Count -eq 2) 'Files only confirmed after branch update'
Assert (@($script:writes | Where-Object uri -match '/git/commits$').Count -eq 1) 'Single commit'
Assert (@($script:writes | Where-Object uri -match '/git/trees$')[0].body.base_tree -eq 'base-tree') 'Preserve unrelated files'
Reset-Progress; $script:conflict=$true
$failed=$false; try {Send-ProjectFiles $request}catch{$failed=$true}
Assert ($failed -and @($script:writes | Where-Object uri -match '/git/refs/').Count -eq 0 -and $script:progressConfirmed.Count -eq 0) 'Concurrent head stops before update'
Reset-Progress; $script:protect=$true
$failed=$false; try {Send-ProjectFiles $request}catch{$failed=$true}
Assert ($failed -and $script:head -eq 'old' -and $script:progressConfirmed.Count -eq 0) 'Protected branch leaves no confirmed files'
Reset-Progress; $script:uncertain=$true
Send-ProjectFiles $request
Assert ($script:states[-1] -eq 'completed' -and @($script:writes | Where-Object uri -match '/git/refs/').Count -eq 1) 'Uncertain response checked without repeated write'
Reset-Progress
$script:head='changed'; $failed=$false; try {Send-ProjectFiles $request}catch{$failed=$true}
Assert ($failed -and $script:writes.Count -eq 0) 'Changed head after preview causes no writes'
Reset-Progress
[IO.File]::WriteAllText((Join-Path $taskFixture 'README.md'),'changed again'); $failed=$false; try {Send-ProjectFiles $request}catch{$failed=$true}
Assert ($failed -and $script:writes.Count -eq 0) 'Changed local file causes no writes'
$sameRequest=[pscustomobject]@{repository='example/test';sourceDirectory=$taskFixture;paths=@('README.md');destination='';branch=''}
$script:readmeSha=(Get-ProjectLocalFile $sameRequest 'README.md').sha; $samePlan=Get-ProjectPlan $sameRequest
$sameRequest | Add-Member baseCommit $samePlan.baseCommit; $sameRequest | Add-Member entries $samePlan.entries
Reset-Progress; Send-ProjectFiles $sameRequest
Assert ($script:writes.Count -eq 0 -and $script:states[-1] -eq 'completed') 'No-op creates no commit'
foreach($bad in @('../README.md','.git/config','sub/../../README.md','C:/secret')) { $failed=$false; try{Get-ProjectLocalFile $request $bad | Out-Null}catch{$failed=$true}; Assert $failed "Invalid path $bad" }
$stream=[IO.File]::Create((Join-Path $taskFixture 'too-large.bin')); $stream.SetLength(100MB+1); $stream.Dispose()
$failed=$false; try{Get-ProjectLocalFile $request 'too-large.bin' | Out-Null}catch{$failed=$true}; Assert $failed 'Reject oversized file before writing'
$script:releasesUri='https://api.github.com/repos/example/test/releases'
function New-TestRelease { return [pscustomobject]@{id=1;draft=$true;html_url='https://github.com/example/test/releases/tag/1';assets=@([pscustomobject]@{id=4;name='app.exe';label='app.exe';state='uploaded';size=4;browser_download_url='https://github.com/example/test/releases/download/1/app.exe'})} }
Reset-Progress; $script:published=$false; $script:release=New-TestRelease
Complete-UploaderRelease $script:release
Assert ($script:published -and $script:downloadLinks.Count -eq 1 -and $script:writes.Count -eq 1) 'Publish confirmed, download link returned'
Reset-Progress; $script:published=$false; $script:uncertain=$true; $script:release=New-TestRelease
Complete-UploaderRelease $script:release
Assert ($script:published -and $script:writes.Count -eq 1) 'Uncertain publish verified without retry'
foreach($kind in @('published','partial','empty')) {
    Reset-Progress; $script:release=New-TestRelease
    if($kind -eq 'published'){$script:release.draft=$false}; if($kind -eq 'partial'){$script:release.assets[0].state='new'}; if($kind -eq 'empty'){$script:release.assets=@()}
    $failed=$false; try{Complete-UploaderRelease $script:release}catch{$failed=$true}; Assert ($failed -and $script:writes.Count -eq 0) "Unsafe publication $kind"
}
Write-Host 'Uploader mocks passed: nested Unicode/binary, preview, safe tree, one commit, no-op, conflicts, protected branch, changed files, path/size guards, publication/uncertain response. No network or auth.'
