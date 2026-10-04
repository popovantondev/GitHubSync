param([string]$Repository='popovantondev/GitHubSync')
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$root=Split-Path -Parent $PSScriptRoot
if($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REPOSITORY -ne $Repository){throw 'Only the owner-approved GitHub Actions repository may run this release helper'}
$version=(Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if($version -ne '1.5.3'){throw 'This release preparation is approved for 1.5.3 only'}
$output=Join-Path $root 'artifacts/cloud-release'
$cache=Join-Path $root 'artifacts/runtime-source-cache'
if(& git -C $root status --porcelain){throw 'Release checkout must be clean'}
$approved='ed317afacfcc0e5fd93cc70a184b4f85ecd64e28'
$changes=@(& git -C $root diff --name-only $approved HEAD)
if($LASTEXITCODE -ne 0 -or $changes.Count -ne 2 -or @($changes | Where-Object {$_ -notin @('tools/Prepare-CloudRelease.ps1','.github/workflows/prepare-release.yml')}).Count){throw 'Preparation must contain only the two reviewed cloud files beyond the approved application snapshot'}
$preparationTree=(& git -C $root rev-parse 'HEAD^{tree}').Trim()
function GhJson([string[]]$Arguments){$raw=& gh @Arguments;if($LASTEXITCODE -ne 0){throw 'GitHub operation failed; inspect remote state before retry'};return ($raw -join "`n" | ConvertFrom-Json)}
function GhPost([string]$Path,$Payload){
    $requestPath=Join-Path $output ('api-body-'+[guid]::NewGuid().ToString('N')+'.json')
    [IO.File]::WriteAllText($requestPath,($Payload | ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    GhJson -Arguments @('api','--method','POST',$Path,'--input',$requestPath)
}
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$catalog=Get-Content (Join-Path $root 'runtime-sources.lock.json') -Raw | ConvertFrom-Json
if($catalog.archives.Count -ne 56){throw 'Unexpected reviewed runtime source inventory'}
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
foreach($entry in $catalog.archives){
    if($entry.archive -notmatch '^[a-zA-Z0-9_.~+-]+\.tar\.(gz|zst)$'){throw 'Unsafe source archive name'}
    $uri=[Uri]$entry.url
    if($uri.Scheme -ne 'https' -or $uri.Host -notin @('github.com','codeload.github.com','repo.msys2.org')){throw 'Unexpected locked source host'}
    $path=Join-Path $cache $entry.archive
    Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $path -TimeoutSec 600
    if((Get-Item $path).Length -ne [long]$entry.bytes -or (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256){throw ('Locked source checksum mismatch: '+$entry.archive)}
    Write-Host ('Verified source: '+$entry.archive)
}
& (Join-Path $PSScriptRoot 'Build-RuntimeSourceArchive.ps1') -SourceDirectory $cache -OutputDirectory $output
& (Join-Path $PSScriptRoot 'Build-Portable.ps1') -OutputDirectory $output
# Record the actual cloud-built archive sizes, not the earlier local candidate.
$metadataPath=Join-Path $root 'public-release.json'
$metadata=Get-Content $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach($asset in $metadata.assets){$asset.size=(Get-Item (Join-Path $output $asset.name)).Length}
$metadataJson=($metadata | ConvertTo-Json -Depth 12).Replace("`r`n","`n")
[IO.File]::WriteAllText($metadataPath,$metadataJson,[Text.UTF8Encoding]::new($false))
if(@(& git -C $root diff --name-only | Where-Object {$_ -ne 'public-release.json'}).Count){throw 'Unexpected working tree changes during packaging'}
& git -C $root add public-release.json
if($LASTEXITCODE -ne 0){throw 'Cannot stage cloud artifact metadata'}
$tree=(& git -C $root write-tree).Trim()
if($LASTEXITCODE -ne 0){throw 'Cannot prepare reviewed release tree'}
# The connector's default author can disclose a primary email. The release
# snapshot deliberately uses noreply and the previously reviewed clean parent.
$parent='4b08eb15304d514359c720b7ecbd6069affaaea8'
$branch="release-$version-reviewed"
$refs=@(GhJson -Arguments @('api',"repos/$Repository/git/matching-refs/heads/$branch"))
if(@($refs | Where-Object {$_.ref -eq "refs/heads/$branch"}).Count){throw 'Release snapshot branch already exists; review before retry'}
$releases=@(GhJson -Arguments @('api',"repos/$Repository/releases?per_page=100"))
if(@($releases | Where-Object {$_.tag_name -eq "v$version"}).Count){throw 'Release already exists; do not overwrite it'}
$blob=GhPost "repos/$Repository/git/blobs" @{encoding='base64';content=[Convert]::ToBase64String([IO.File]::ReadAllBytes($metadataPath))}
if($blob.sha -ne (& git -C $root rev-parse ':public-release.json').Trim()){throw 'Remote metadata blob mismatch'}
$remoteTree=GhPost "repos/$Repository/git/trees" @{base_tree=$preparationTree;tree=@(@{path='public-release.json';mode='100644';type='blob';sha=$blob.sha})}
if($remoteTree.sha -ne $tree){throw 'Remote release tree differs from reviewed source index'}
$identity=@{name='Anton Popov';email='334177155+popovantondev@users.noreply.github.com';date=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')}
$safeCommit=GhPost "repos/$Repository/git/commits" @{tree=$tree;parents=@($parent);message='Prepare independently checked GitHubSync 1.5.3 cloud release';author=$identity;committer=$identity}
$commit=$safeCommit.sha
if($safeCommit.author.email -ne $identity.email -or $safeCommit.committer.email -ne $identity.email -or $safeCommit.tree.sha -ne $tree){throw 'Privacy-safe commit not confirmed'}
# Atomic create-ref fails if a concurrent writer created the branch. No force,
# replacement, or existing history rewrite is permitted in this helper.
$ref=GhPost "repos/$Repository/git/refs" @{ref="refs/heads/$branch";sha=$commit}
if($ref.object.sha -ne $commit){throw 'New release snapshot branch not confirmed'}
& gh auth setup-git
if($LASTEXITCODE -ne 0){throw 'Cannot configure ephemeral Actions Git helper'}
& git -C $root fetch --no-tags origin $commit
if($LASTEXITCODE -ne 0){throw 'Cannot read back release snapshot'}
& git -C $root checkout --detach $commit
if($LASTEXITCODE -ne 0){throw 'Cannot select privacy-safe release snapshot'}
if(& git -C $root status --porcelain){throw 'Release snapshot checkout is not clean'}
& (Join-Path $PSScriptRoot 'Build-SourceArchive.ps1') -OutputDirectory $output
$files=@()
foreach($kind in @('win-x64','source','runtime-sources')){
    $name="GitHubSync-$version-$kind.zip"
    foreach($suffix in @('','.sha256')){
        $file=Get-Item (Join-Path $output ($name+$suffix))
        $files+=@{name=$file.Name;bytes=$file.Length;sha256=(Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    $expected=(Get-Content (Join-Path $output ($name+'.sha256'))).Split(' ')[0]
    if($expected -ne (Get-FileHash (Join-Path $output $name) -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Companion checksum mismatch'}
}
# Standard ephemeral Actions token only; no user credential is copied or stored.
$notes="GitHubSync $version`n`nWindows x64 portable, MIT application source, bundled dependency notices and complete pinned runtime source archives. Verify the companion SHA-256 before extracting. EXE is unsigned. Extract the entire portable package and open GitHubSync.exe.`n`nThis cloud-built draft still requires independent review of all six attachments before public release. No real user data, settings or credentials are included."
$body=@{tag_name="v$version";target_commitish=$commit;name="GitHubSync $version";body=$notes;draft=$true;prerelease=$false}
$bodyPath=Join-Path $output 'draft-body.json'
[IO.File]::WriteAllText($bodyPath,($body | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$release=GhJson -Arguments @('api','--method','POST',"repos/$Repository/releases",'--input',$bodyPath)
foreach($file in $files){
    & gh release upload "v$version" (Join-Path $output $file.name) --repo $Repository
    if($LASTEXITCODE -ne 0){throw ('Attachment upload uncertain; read back before retry: '+$file.name)}
    $readback=GhJson -Arguments @('api',"repos/$Repository/releases/$($release.id)")
    $match=@($readback.assets | Where-Object {$_.name -ceq $file.name})
    if($match.Count -ne 1 -or $match[0].size -ne $file.bytes -or $match[0].state -ne 'uploaded' -or $match[0].digest -ne ('sha256:'+$file.sha256)){throw ('Attachment not confirmed: '+$file.name)}
    Write-Host ('Confirmed attachment: '+$file.name)
}
$manifest=@{status='draft-only-independent-review-required';repository=$Repository;version=$version;sourceCommit=$commit;sourceTree=$tree;releaseId=$release.id;files=$files}
[IO.File]::WriteAllText((Join-Path $output 'CLOUD-RELEASE-MANIFEST.json'),($manifest | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Host ('CLOUD-RELEASE-MANIFEST: '+($manifest | ConvertTo-Json -Depth 8 -Compress))
Write-Host ('All six attachments confirmed. Release remains draft; repository visibility and main are unchanged. Release ID '+$release.id)
