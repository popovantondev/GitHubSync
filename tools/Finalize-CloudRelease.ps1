param([switch]$Publish, [switch]$DeleteReviewedRuns, [string]$ReleaseNotesPath)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$repository='popovantondev/GitHubSync'
$releaseId=402769880
$sourceCommit='efeb87af02dac8aca4f9b6ed1b7009f34447d22d'
$sourceTree='65b724d95507c6ea92efb4173e106fe9f33c69a3'
$noreply='334177155+popovantondev@users.noreply.github.com'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REPOSITORY -ne $repository){throw 'Only the owner-approved repository workflow may finalize this release'}
$root=Split-Path -Parent $PSScriptRoot
$output=Join-Path $root 'artifacts/release-finalization'
New-Item -ItemType Directory -Path $output -Force | Out-Null
function GhJson([string[]]$Arguments){
    $raw=& gh @Arguments
    if($LASTEXITCODE -ne 0){throw 'GitHub request failed; inspect remote state before any write retry'}
    $text=$raw -join "`n"
    if([string]::IsNullOrWhiteSpace($text)){return $null}
    return ($text | ConvertFrom-Json)
}
function ReadApi([string]$Path){GhJson -Arguments @('api',"repos/$repository/$Path")}
function PatchApi([string]$Path,$Payload){
    $request=Join-Path $output ('body-'+[guid]::NewGuid().ToString('N')+'.json')
    $requestJson=$Payload | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($request,$requestJson,[Text.UTF8Encoding]::new($false))
    GhJson -Arguments @('api','--method','PATCH',"repos/$repository/$Path",'--input',$request)
}
$expected=@(
    @{name='GitHubSync-1.5.3-win-x64.zip';bytes=44889968;sha256='461f8531784fc5cd69012a3656b5b63078bcd948bf65e350b6f1bcc0a2a25dd2'},
    @{name='GitHubSync-1.5.3-win-x64.zip.sha256';bytes=96;sha256='069bb7bc3c7a7366d9662e2cd4ddd236f3146988c4413c0c24d017b3d9322839'},
    @{name='GitHubSync-1.5.3-source.zip';bytes=1676489;sha256='64f91cb6b1ae6264d7814eb6ee86ecd232b34f263c34e9952b1ab839b80811f6'},
    @{name='GitHubSync-1.5.3-source.zip.sha256';bytes=95;sha256='551e39d01522c57cd887954d68f84237d84b724b9df3548a0401984f9a66cd41'},
    @{name='GitHubSync-1.5.3-runtime-sources.zip';bytes=708179939;sha256='42875a51d62192263e1095195678b52b4882ccf562f16584ba6f981713e5ad07'},
    @{name='GitHubSync-1.5.3-runtime-sources.zip.sha256';bytes=104;sha256='65f8dfc693de87e5d21a471eaa4b5f567ec086e8ef97f07754a1307840f87fd7'}
)
function AssertRelease($Release){
    if($Release.id -ne $releaseId -or $Release.tag_name -cne 'v1.5.3' -or $Release.target_commitish -cne $sourceCommit -or $Release.prerelease){throw 'Release identity changed'}
    if(@($Release.assets).Count -ne 6){throw 'Release must contain exactly the six independently reviewed files'}
    foreach($file in $expected){
        $match=@($Release.assets | Where-Object {$_.name -ceq $file.name})
        if($match.Count -ne 1 -or $match[0].size -ne $file.bytes -or $match[0].state -cne 'uploaded' -or $match[0].digest -cne ('sha256:'+$file.sha256)){throw ('Unconfirmed release attachment: '+$file.name)}
    }
}
function AssertSource{
    $repo=GhJson -Arguments @('api',"repos/$repository")
    if($repo.full_name -cne $repository -or -not $repo.private -or $repo.default_branch -cne 'main'){throw 'Repository state changed; keep visibility private during finalization'}
    $main=ReadApi 'git/ref/heads/main'
    if($main.object.sha -cne $sourceCommit){throw 'Default branch changed after independent review'}
    $commit=ReadApi "git/commits/$sourceCommit"
    if($commit.tree.sha -cne $sourceTree -or $commit.author.email -cne $noreply -or $commit.committer.email -cne $noreply -or @($commit.parents).Count -ne 1 -or $commit.parents[0].sha -cne '4b08eb15304d514359c720b7ecbd6069affaaea8'){throw 'Reviewed source/privacy identity changed'}
    $ci=ReadApi 'actions/runs/37187960608'
    if($ci.id -ne 37187960608 -or $ci.head_sha -cne $sourceCommit -or $ci.status -cne 'completed' -or $ci.conclusion -cne 'success' -or $ci.repository.full_name -cne $repository){throw 'Successful CI for this exact reviewed source commit not confirmed'}
    # Draft releases may have no tag yet. Reject any existing wrong tag before
    # publishing; GitHub will create an absent tag from target_commitish.
    $tags=@(ReadApi 'git/matching-refs/tags/v1.5.3' | Where-Object {$_.ref -ceq 'refs/tags/v1.5.3'})
    if($tags.Count -gt 1 -or ($tags.Count -eq 1 -and ($tags[0].object.type -cne 'commit' -or $tags[0].object.sha -cne $sourceCommit))){throw 'Existing release tag does not identify the reviewed source commit'}
}
AssertSource
$release=ReadApi "releases/$releaseId"
AssertRelease $release
Write-Host 'Six exact asset names, uploaded states, sizes and SHA-256 digests confirmed.'
if($DeleteReviewedRuns){
    # Never deletes unrelated or running jobs, releases, code, credentials or files.
    $approvedRuns=@{37165616164='ed317afacfcc0e5fd93cc70a184b4f85ecd64e28';37166388428='3106d761a57173a034b95f09dae8f54a830ec070';37167690608='6bdf0ceefc454aea3c07506043bc71f6eaba1fff'}
    foreach($runId in @(37165616164,37166388428,37167690608)){
        $run=ReadApi "actions/runs/$runId"
        if($run.id -ne $runId -or $run.status -cne 'completed' -or $run.repository.full_name -cne $repository -or $run.head_sha -cne $approvedRuns[$runId]){throw 'Unexpected Actions record; deletion refused'}
        $null=GhJson -Arguments @('api','--method','DELETE',"repos/$repository/actions/runs/$runId")
        Write-Host ('Deleted owner-approved service Actions record: '+$runId)
    }
    Write-Host 'Run deletion does not prove purging unreachable commit objects or GitHub caches.'
}
if(-not $Publish){Write-Host 'Read-only release verification complete; no release publication or visibility change requested.';return}
if(-not $ReleaseNotesPath){throw 'Explicit reviewed release notes file required'}
# PS5 Get-Content decorates strings with provider metadata; serializing that
# decorated string can traverse drive/provider objects. Read a plain UTF-8 string.
$notes=[IO.File]::ReadAllText($ReleaseNotesPath,[Text.Encoding]::UTF8)
if($notes.Length -lt 1000 -or $notes -notmatch '## Русский' -or $notes -notmatch '## Deutsch' -or $notes -notmatch '## English' -or $notes -notmatch 'unsigned'){throw 'Reviewed three-language release notes missing'}
# Recheck the branch and every asset immediately before the one publication write.
AssertSource
$release=ReadApi "releases/$releaseId"
AssertRelease $release
if(-not $release.draft){throw 'Release is already published; inspect instead of repeating the write'}
$published=PatchApi "releases/$releaseId" @{name='GitHubSync 1.5.3';body=$notes;draft=$false;prerelease=$false;make_latest='true'}
AssertRelease $published
if($published.draft){throw 'Publication not confirmed; read GitHub before any retry'}
$readback=ReadApi "releases/$releaseId"
AssertRelease $readback
if($readback.draft -or $readback.body -cne $notes){throw 'Published release readback differs'}
$tag=ReadApi 'git/ref/tags/v1.5.3'
if($tag.object.type -cne 'commit' -or $tag.object.sha -cne $sourceCommit){throw 'Release tag does not identify the reviewed source commit'}
$repo=GhJson -Arguments @('api',"repos/$repository")
if(-not $repo.private){throw 'Repository visibility changed concurrently'}
Write-Host ('PRIVATE-RELEASE-PUBLISHED: '+$readback.html_url)
Write-Host 'Visibility remains private. Public exposure and Pages setup require separate administration capability and privacy decision.'
