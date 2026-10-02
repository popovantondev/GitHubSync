$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('GitHubSyncTest-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /codepage:65001 /target:exe "/out:$fixture\SyncDownloadReview.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll (Join-Path $root 'src\SyncTransfer.cs') (Join-Path $root 'src\WindowsPathSafety.cs') (Join-Path $PSScriptRoot 'SyncDownloadReview.cs')
if($LASTEXITCODE -ne 0){throw 'Download test compiler failed'}
& (Join-Path $fixture 'SyncDownloadReview.exe') $fixture
if($LASTEXITCODE -ne 0){throw 'Download tests failed'}
$Language='ru'; function L($de,$ru,$en){$ru}
. (Join-Path $root 'Uploader-Operations.ps1')
. (Join-Path $root 'Sync-Operations.ps1')
$script:gets=0
function Invoke-SyncGet($uri,$etag='') {
    $script:gets++
    if($etag -eq 'unchanged'){return @{notModified=$true;etag=$etag}}
    if($uri -match '/git/ref/'){return @{data=@{object=@{sha='commit1'}};etag='head1'}}
    if($uri -match '/git/commits/'){return @{data=@{tree=@{sha='tree1'}}}}
    if($uri -match '/git/trees/tree1$'){return @{data=@{truncated=$false;tree=@(@{path='README.md';type='blob';mode='100644';sha='blob1';size=12},@{path='sub';type='tree';sha='tree2'},@{path='module';type='commit';mode='160000'})}}}
    if($uri -match '/git/trees/tree2$'){return @{data=@{truncated=$false;tree=@(@{path='данные.bin';type='blob';mode='100644';sha='blob2';size=4})}}}
    if($uri -match '/releases/(latest|42)$'){return @{data=@{id=42;tag_name='v-test';draft=$false;html_url='https://github.com/example/artificial/releases/tag/v-test';assets=@(@{id=51;name='GitHub-renamed.bin';size=4;state='uploaded';digest=('sha256:'+('a'*64));updated_at='fixture'})};etag='release1'}}
    if($uri -match '/repos/example/artificial$'){return @{data=@{default_branch='main';permissions=@{push=$false}}}}
    throw "Unexpected GET $uri"
}
function Assert($value,$message){if(-not $value){throw $message}}
$request=[pscustomobject]@{operation='catalog';repository='example/artificial';contentKind='code';branch='';prefix='';releaseId=0;sourceId='';etag=''}
$catalog=Get-SyncCatalog $request
Assert ($catalog.entries.Count -eq 2 -and $catalog.branch -eq 'main' -and $catalog.unsupported -eq 1) 'Readable repositories do not require push; nested files and unsupported submodules'
Assert ($catalog.entries[1].name -ceq 'sub/данные.bin' -and $catalog.entries[1].accept -eq 'application/vnd.github.raw+json') 'Unicode paths and raw blob media type'
$request.prefix='sub';$catalog=Get-SyncCatalog $request;Assert ($catalog.entries.Count -eq 1 -and $catalog.entries[0].name -ceq 'данные.bin') 'Selected root relative paths'
$request.operation='updates';$request.etag='unchanged';$request.branch='main';$before=$script:gets;$catalog=Get-SyncCatalog $request;Assert ($catalog.notModified -and $script:gets -eq $before+1) 'Conditional check does not traverse unchanged tree'
$request.operation='catalog';$request.etag='';$request.contentKind='release';$catalog=Get-SyncCatalog $request;Assert ($catalog.releaseId -eq 42 -and $catalog.entries[0].digest.Length -eq 64 -and $catalog.entries[0].name -eq 'GitHub-renamed.bin') 'Published release names, IDs, digest and latest stable selection'
$plan=ConvertTo-SyncPlan (@{repository='example/artificial';directory=$fixture;contentKind='release';sourceId='42';tag=$catalog.tag;entries=$catalog.entries})
Assert ($plan.entries.Count -eq 1 -and $plan.entries[0].length -eq 4 -and $plan.tag -eq 'v-test') 'PowerShell to C# complete request serialization, including human-readable release tag'
$deep=[SyncTransfer]::SafePath($fixture,(('x'*110)+'/'+('y'*110)+'/данные.bin'))
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($deep)) | Out-Null
[IO.File]::WriteAllBytes($deep,[byte[]](0,1,255))
Assert (([SyncTransfer]::Fingerprint($deep)).Length -eq 64) 'Long-path support also works inside the actual Windows PowerShell 5.1 host'
Write-Host 'Catalog checks passed: read-only/public access, default branch, recursive Unicode, prefix, submodules, conditional GET, release IDs/digests, PS5/C# serialization. No network/auth.'
