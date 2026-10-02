# Download operations never call a GitHub mutation endpoint.
function Invoke-SyncGet([string]$uri, [string]$etag = '') {
    if ($uri -notmatch '^https://api\.github\.com/repos/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:/|$)') { throw 'Invalid GitHub API resource' }
    $web = [Net.HttpWebRequest]::Create($uri)
    $web.Method = 'GET'; $web.UserAgent = 'GitHubSync/1.5.2'; $web.Accept = 'application/vnd.github+json'
    $web.Headers['X-GitHub-Api-Version'] = '2022-11-28'; $web.Timeout = 30000; $web.AllowAutoRedirect = $false
    if ($script:token) { $web.Headers['Authorization'] = 'Bearer ' + $script:token }
    if ($etag) { $web.Headers['If-None-Match'] = $etag }
    try { $response = $web.GetResponse() }
    catch [Net.WebException] {
        $failed = $_.Exception.Response
        if ($failed) {
            $code = [int]$failed.StatusCode
            $script:syncRetryAfter = [string]$failed.Headers['Retry-After']
            $script:syncRateReset = [string]$failed.Headers['X-RateLimit-Reset']
            $failed.Close()
            if ($code -eq 304) { return @{notModified=$true;etag=$etag} }
            # Expired cached credentials must not block public, anonymous reads.
            if ($code -eq 401 -and $script:token) { $script:token=''; return (Invoke-SyncGet $uri $etag) }
            if ($code -eq 404 -and $uri.EndsWith('/releases/latest')) {
                $null=Invoke-SyncGet ($uri.Substring(0,$uri.Length-16))
                throw (L 'Noch kein stabiler veröffentlichter Release. Andere Version wählen oder im Modus Senden einen Release erstellen.' 'Стабильных опубликованных релизов пока нет. Выберите другую версию или создайте релиз в режиме «Отправить».' 'No stable published release yet. Choose another version or create a release in Send mode.')
            }
            if ($code -in @(401,404)) { throw (L 'Projekt nicht gefunden oder Anmeldung erforderlich. GitHub-Link und Konto prüfen.' 'Проект не найден или нужен вход. Проверьте ссылку и аккаунт GitHub.' 'Project not found or sign-in required. Check the GitHub link and account.') }
            if ($code -in @(403,429)) { throw (L 'Zugriff/API-Limit. Prüfung später wiederholen.' 'Нет доступа или достигнут лимит API. Повторите проверку позже.' 'Access denied or API rate limit reached. Check again later.') }
            throw "GitHub HTTP $code"
        }
        throw (L 'GitHub nicht erreichbar.' 'Нет соединения с GitHub.' 'Cannot reach GitHub.')
    }
    try {
        $reader = New-Object IO.StreamReader($response.GetResponseStream(),[Text.Encoding]::UTF8)
        try { $value = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        return @{data=$value;etag=[string]$response.Headers['ETag'];notModified=$false}
    } finally { $response.Close() }
}
function Get-SyncTree([string]$repository, [string]$sha, [string]$prefix = '') {
    $tree = (Invoke-SyncGet "https://api.github.com/repos/$repository/git/trees/$sha").data
    if ($tree.truncated) { throw (L 'Dateiliste unvollständig.' 'GitHub вернул неполный список файлов.' 'GitHub returned an incomplete file list.') }
    foreach ($entry in $tree.tree) {
        $path=$prefix+[string]$entry.path
        if ($entry.type -eq 'tree') { Get-SyncTree $repository $entry.sha ($path+'/') }
        elseif ($entry.type -eq 'blob' -and $entry.mode -in @('100644','100755')) {
            @{name=$path;length=[long]$entry.size;digest=[string]$entry.sha;hashKind='git';accept='application/vnd.github.raw+json';url="https://api.github.com/repos/$repository/git/blobs/$($entry.sha)"}
        }
        else { $script:syncUnsupported++ }
    }
}
function Get-SyncCatalog($request) {
    Assert-UploaderProject $request.repository
    $base="https://api.github.com/repos/$($request.repository)"
    $entries=New-Object 'System.Collections.Generic.List[object]'
    $script:syncUnsupported=0
    $etag=if($request.operation -eq 'updates'){[string]$request.etag}else{''}
    if ($request.contentKind -eq 'code') {
        $branch=[string]$request.branch
        if (-not $branch) { $branch=[string](Invoke-SyncGet $base).data.default_branch }
        if (-not $branch) { throw (L 'Projekt enthält noch keine Dateien.' 'В проекте пока нет файлов и веток.' 'Project has no files or branches yet.') }
        $ref=Invoke-SyncGet "$base/git/ref/heads/$([uri]::EscapeDataString($branch))" $etag
        if ($ref.notModified) { return @{notModified=$true;sourceId=[string]$request.sourceId;etag=$etag} }
        $commit=(Invoke-SyncGet "$base/git/commits/$($ref.data.object.sha)").data
        $prefix=Get-UploaderPath ([string]$request.prefix) $true
        foreach ($entry in @(Get-SyncTree $request.repository $commit.tree.sha)) {
            if (-not $prefix -or $entry.name.StartsWith($prefix+'/',[StringComparison]::Ordinal)) {
                if ($prefix) { $entry.name=$entry.name.Substring($prefix.Length+1) }
                $entries.Add($entry)
            }
        }
        $result=@{repository=$request.repository;contentKind='code';sourceId=[string]$ref.data.object.sha;branch=$branch;prefix=$prefix;releaseId=[long]0;resultUrl="$([string]$base -replace 'api.github.com/repos','github.com')/tree/$($ref.data.object.sha)";etag=$ref.etag}
    } elseif ($request.contentKind -eq 'release') {
        $endpoint=if ([long]$request.releaseId -gt 0) { "$base/releases/$($request.releaseId)" } else { "$base/releases/latest" }
        $data=Invoke-SyncGet $endpoint $etag
        if ($data.notModified) { return @{notModified=$true;sourceId=[string]$request.sourceId;etag=$etag} }
        $release=$data.data
        if ($release.draft) { throw (L 'Download nur veröffentlichter Releases.' 'Для скачивания выберите опубликованный релиз.' 'Choose a published release for downloading.') }
        foreach ($asset in $release.assets) {
            if ($asset.state -ne 'uploaded') { continue }
            $digest=if([string]$asset.digest -match '^sha256:([0-9a-fA-F]{64})$'){$Matches[1].ToLowerInvariant()}else{''}
            $entries.Add(@{name=[string]$asset.name;length=[long]$asset.size;digest=$digest;hashKind='sha256';revision=[string]$asset.updated_at;accept='application/octet-stream';url="$base/releases/assets/$($asset.id)"})
        }
        $result=@{repository=$request.repository;contentKind='release';sourceId=[string]$release.id;branch='';prefix='';releaseId=[long]$release.id;tag=[string]$release.tag_name;resultUrl=[string]$release.html_url;etag=$data.etag}
    } else { throw 'Invalid content kind' }
    $result.entries=@($entries.ToArray()); $result.unsupported=$script:syncUnsupported
    $identity=@($result.entries | Sort-Object name | ForEach-Object { "$($_.name)|$($_.url)|$($_.digest)|$($_.length)|$($_.revision)" }) -join "`n"
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $result.fingerprint=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity)))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() }
    return $result
}
function Import-SyncTransfer {
    Add-Type -AssemblyName System.Web.Extensions
    if (-not ('SyncTransfer' -as [type])) {
        Import-UploaderSupport
    }
}
function ConvertTo-SyncPlan($value) {
    Import-SyncTransfer
    $serializer=New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $serializer.MaxJsonLength=32MB
    return $serializer.Deserialize(($value | ConvertTo-Json -Depth 15 -Compress),[SyncPlan])
}
function Invoke-SyncOperation($request) {
    $script:progressDirection='download'; $script:progressMode=[string]$request.contentKind
    $script:token=''
    # Read-only background checks never display a browser or credential prompt.
    try { $script:token=Get-GitHubToken $false } catch { }
    if ($request.operation -eq 'projects') {
        if (-not $script:token) { throw (L 'Anmelden oder öffentlichen GitHub-Link einfügen.' 'Войдите или вставьте ссылку на публичный проект GitHub.' 'Sign in or paste a public GitHub project link.') }
        $script:catalogChoices=@(Get-PagedGitHubItems 'https://api.github.com/user/repos?sort=updated&affiliation=owner,collaborator,organization_member' | ForEach-Object { @{value=[string]$_.full_name;label=[string]$_.full_name} })
        Write-ProgressState 'sync-choices'; return
    }
    Assert-UploaderProject $request.repository
    if ($request.operation -in @('branches','releases')) {
        $kind=[string]$request.operation
        $items=@(Get-PagedGitHubItems "https://api.github.com/repos/$($request.repository)/$kind")
        $script:catalogChoices=@(if($kind -eq 'branches') { $items | ForEach-Object { @{value=[string]$_.name;label=[string]$_.name} } } else { $items | Where-Object { -not $_.draft } | ForEach-Object { @{value=[string]$_.id;label=([string]$_.tag_name+' · '+[string]$_.name);tag=[string]$_.tag_name} } })
        Write-ProgressState 'sync-choices'; return
    }
    if ($request.operation -in @('catalog','updates')) {
        $script:syncCatalog=Get-SyncCatalog $request
        Write-ProgressState 'sync-catalog'; return
    }
    if ($request.operation -eq 'preview') {
        $catalog=Get-SyncCatalog $request
        if ($catalog.sourceId -cne $request.sourceId) { throw (L 'Quelle geändert. Liste aktualisieren.' 'Источник изменился. Обновите список файлов.' 'Source changed. Refresh the file list.') }
        $catalog.entries=@($catalog.entries | Where-Object { $request.paths -ccontains $_.name })
        if ($catalog.entries.Count -ne @($request.paths).Count -or $catalog.entries.Count -eq 0) { throw 'Selection changed; refresh file list.' }
        $catalog.directory=[string]$request.directory
        $plan=ConvertTo-SyncPlan $catalog
        [SyncTransfer]::Preview($plan)
        $script:syncCatalog=$plan
        Write-ProgressState 'sync-planned'; return
    }
    if ($request.operation -eq 'download') {
        if ($CheckOnly) { throw 'Check mode cannot download' }
        $plan=ConvertTo-SyncPlan $request.plan
        if ($plan.repository -cne $request.repository -or $plan.contentKind -cne $request.contentKind) { throw 'Download plan mismatch' }
        # Validate approved resources, including renamed/replaced release assets, before local writes.
        if ($plan.contentKind -eq 'code') {
            $commit=(Invoke-SyncGet "https://api.github.com/repos/$($plan.repository)/git/commits/$($plan.sourceId)").data
            $remote=@(Get-SyncTree $plan.repository $commit.tree.sha)
            foreach($entry in $plan.entries) {
                $path=if($plan.prefix){$plan.prefix+'/'+$entry.name}else{$entry.name}
                $match=@($remote | Where-Object { $_.name -ceq $path -and $_.url -ceq $entry.url -and $_.digest -ceq $entry.digest -and $_.length -eq $entry.length })
                if($match.Count -ne 1) { throw 'Approved Code resource changed' }
            }
        } else {
            $request.releaseId=$plan.releaseId
            $remote=Get-SyncCatalog $request
            foreach($entry in $plan.entries) {
                $match=@($remote.entries | Where-Object { $_.name -ceq $entry.name -and $_.url -ceq $entry.url -and $_.digest -ceq $entry.digest -and $_.length -eq $entry.length -and $_.revision -ceq $entry.revision })
                if($match.Count -ne 1) { throw 'Approved release asset changed; refresh file list.' }
            }
        }
        $null=[SyncTransfer]::Run($plan,$script:token,$ProgressFile,[string]$request.controlFile)
        return
    }
    throw 'Invalid sync operation'
}
