# Imported by the existing worker. Authentication stays in memory; no Git checkout is modified.
function Import-UploaderSupport {
    if (-not ('WindowsPathSafety' -as [type])) {
        Add-Type -Path @((Join-Path $PSScriptRoot 'src\WindowsPathSafety.cs'),(Join-Path $PSScriptRoot 'src\GitHubWrite.cs'),(Join-Path $PSScriptRoot 'src\GitCodeTransport.cs'),(Join-Path $PSScriptRoot 'src\SyncTransfer.cs')) -ReferencedAssemblies @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll')
    }
}
function Assert-UploaderProject([string]$repository) {
    if ($repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $repository -eq 'OWNER/REPOSITORY') { throw (L 'Projekt auswählen.' 'Выберите проект.' 'Choose a project.') }
}
function Get-UploaderPath([string]$path, [bool]$allowEmpty = $false) {
    $path = $path.Replace('\','/').Trim('/')
    if ($allowEmpty -and $path -eq '') { return '' }
    if ([string]::IsNullOrWhiteSpace($path) -or $path -match '[:\x00-\x1f]' -or @($path.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' -or $_ -ieq '.git' -or $_ -ieq '.githubsync' }).Count) { throw (L 'Ungültiger relativer Pfad.' 'Недопустимый относительный путь.' 'Invalid relative path.') }
    return $path
}
function Get-ProjectLocalFile($request, [string]$relative) {
    $relative = Get-UploaderPath $relative
    $base = [IO.Path]::GetFullPath([string]$request.sourceDirectory)
    if ($base.Length -gt [IO.Path]::GetPathRoot($base).Length) { $base=$base.TrimEnd('\') }
    Import-UploaderSupport
    try { [WindowsPathSafety]::AssertNoLinks($base) } catch { throw (L 'Quellpfad enthält einen Link/Junction oder kann nicht sicher geprüft werden.' 'В исходном пути есть ссылка/junction либо его безопасность не удалось проверить.' 'Source path contains a link/junction or its safety could not be verified.') }
    $full = [IO.Path]::GetFullPath((Join-Path $base $relative.Replace('/','\')))
    if (-not $full.StartsWith($base.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Path outside source directory' }
    $probe = $full
    while ($probe -ne $base) {
        $item = Get-Item -LiteralPath $probe -ErrorAction Stop
        if ([WindowsPathSafety]::IsUnsafeReparsePoint($probe)) { throw (L 'Verknüpfungen werden nicht geladen.' 'Ссылки и junction-папки не загружаются.' 'Links and junction directories are not uploaded.') }
        $probe = Split-Path -Parent $probe
    }
    $file = Get-Item -LiteralPath $full
    if ($file.PSIsContainer) { throw 'Expected a file' }
    if ($file.Length -gt 100MB) { throw (L 'Datei größer als 100 MiB. Senden → Release-Anhänge verwenden.' 'Файл больше 100 MiB. Выберите «Отправить → Вложения релиза».' 'File exceeds 100 MiB. Use Send → Release attachments.') }
    $hash = [Security.Cryptography.SHA1]::Create()
    $inputStream=[IO.File]::Open($full,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        $length=$inputStream.Length
        if($length -gt 100MB){throw (L 'Datei größer als 100 MiB. Release-Anhänge verwenden.' 'Файл больше 100 MiB. Используйте «Вложения релиза».' 'File exceeds 100 MiB. Use Release attachments.')}
        $header=[Text.Encoding]::ASCII.GetBytes("blob $length`0")
        $null=$hash.TransformBlock($header,0,$header.Length,$header,0)
        $buffer=New-Object byte[] (256KB)
        while(($read=$inputStream.Read($buffer,0,$buffer.Length)) -gt 0){$null=$hash.TransformBlock($buffer,0,$read,$buffer,0)}
        $null=$hash.TransformFinalBlock($buffer,0,0)
        $sha=([BitConverter]::ToString($hash.Hash)).Replace('-','').ToLowerInvariant()
    } finally { $inputStream.Dispose(); $hash.Dispose() }
    return [pscustomobject]@{ name=$relative; path=$full; length=[long]$length; sha=$sha }
}
function Get-UploaderTree([string]$repository, [string]$sha, [string]$prefix = '') {
    # Read subtrees separately; never trust a truncated recursive response.
    $tree = Invoke-GitHubGet "https://api.github.com/repos/$repository/git/trees/$sha"
    if ($tree.truncated) { throw (L 'Dateiliste unvollständig.' 'GitHub вернул неполный список файлов.' 'GitHub returned an incomplete file list.') }
    foreach ($entry in $tree.tree) {
        $path=$prefix+[string]$entry.path
        if ($entry.type -eq 'tree') { Get-UploaderTree $repository $entry.sha ($path+'/') }
        else { [pscustomobject]@{ path=$path; sha=[string]$entry.sha; mode=[string]$entry.mode; type=[string]$entry.type } }
    }
}
function Get-ProjectPlan($request) {
    Assert-UploaderProject $request.repository
    $project = Invoke-GitHubGet "https://api.github.com/repos/$($request.repository)"
    if (-not $project.permissions.push -or $project.archived) { throw (L 'Kein Schreibzugriff.' 'Нет права записи в проект или он архивный.' 'No write access, or project is archived.') }
    $branch = [string]$request.branch
    if ([string]::IsNullOrWhiteSpace($branch)) { $branch=[string]$project.default_branch }
    if ([string]::IsNullOrWhiteSpace($branch)) { throw (L 'Projekt zuerst mit README initialisieren.' 'Сначала создайте в проекте README, чтобы появилась основная ветка.' 'Initialize the project with a README first.') }
    $ref=Invoke-GitHubGet "https://api.github.com/repos/$($request.repository)/git/ref/heads/$([uri]::EscapeDataString($branch))"
    if ($ref.ref -cne "refs/heads/$branch") { throw 'Unexpected branch reference' }
    $commit=Invoke-GitHubGet "https://api.github.com/repos/$($request.repository)/git/commits/$($ref.object.sha)"
    $remote=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach($entry in @(Get-UploaderTree $request.repository $commit.tree.sha)) { $remote[$entry.path]=$entry }
    $destination=Get-UploaderPath ([string]$request.destination) $true
    $entries=New-Object 'System.Collections.Generic.List[object]'
    $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach($name in $request.paths) {
        $local=Get-ProjectLocalFile $request ([string]$name)
        $target=if ($destination) { $destination+'/'+$local.name } else { $local.name }
        if (-not $seen.Add($target)) { throw (L 'Doppelte Zielpfade.' 'Повторяются пути назначения.' 'Duplicate destination paths.') }
        $parent=$target
        while ($parent.Contains('/')) { $parent=$parent.Substring(0,$parent.LastIndexOf('/')); if ($remote.ContainsKey($parent)) { throw (L 'Datei blockiert Zielordner.' 'Файл GitHub занимает место папки назначения.' 'A GitHub file blocks a destination folder.') } }
        if (@($remote.Keys | Where-Object { $_.StartsWith($target+'/',[StringComparison]::Ordinal) }).Count) { throw (L 'Ordner darf nicht durch Datei ersetzt werden.' 'Нельзя заменить папку файлом.' 'Cannot replace a folder with a file.') }
        $existing=$remote[$target]
        if (@($remote.Keys | Where-Object { $_ -ieq $target -and $_ -cne $target }).Count) { throw (L 'Zielpfad unterscheidet sich nur in Groß-/Kleinschreibung.' 'Путь назначения отличается от существующего только регистром букв. Уточните имя.' 'Destination differs from an existing path only by case. Review the name.') }
        if ($existing -and ($existing.type -ne 'blob' -or $existing.mode -notin @('100644','100755'))) { throw (L 'Symlink/Submodul nicht überschreiben.' 'Ссылки и подмодули не перезаписываются.' 'Symlinks and submodules are not overwritten.') }
        $action=if (-not $existing) { 'add' } elseif ($existing.sha -ceq $local.sha) { 'same' } else { 'update' }
        $entries.Add([pscustomobject]@{ name=$local.name; target=$target; sha=$local.sha; length=$local.length; action=$action; mode=$(if($existing){$existing.mode}else{'100644'}) })
    }
    if ($entries.Count -eq 0) { throw (L 'Dateien wählen.' 'Выберите файлы.' 'Select files.') }
    $url="https://github.com/$($request.repository)/tree/$([uri]::EscapeDataString($branch))"
    if($destination) { $url+='/'+(($destination.Split('/') | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/') }
    return [pscustomobject]@{ repository=[string]$request.repository; branch=$branch; destination=$destination; baseCommit=[string]$ref.object.sha; baseTree=[string]$commit.tree.sha; entries=@($entries.ToArray()); resultUrl=$url; private=[bool]$project.private }
}
function Invoke-UploaderTransport([string]$method, [string]$uri, $payload, [int]$seconds) {
    Import-UploaderSupport
    if ($uri.EndsWith('/git/blobs') -and $payload.encoding -eq 'base64') {
        return [GitHubWrite]::SendBlob($uri,[string]$payload.content,$script:token,$seconds,[string]$script:progressFile)
    }
    return [GitHubWrite]::SendJson($method.ToUpperInvariant(),$uri,($payload | ConvertTo-Json -Depth 15 -Compress),$script:token,$seconds)
}
function Invoke-UploaderWrite([string]$method, [string]$uri, $payload) {
    $stage = if ($uri.EndsWith('/git/blobs')) { L 'Dateiübertragung' 'Передача файла' 'File transfer' } elseif ($uri.EndsWith('/git/trees')) { L 'Dateibaum' 'Подготовка структуры файлов' 'Preparing file tree' } elseif ($uri.EndsWith('/git/commits')) { L 'Commit' 'Создание коммита' 'Creating commit' } elseif ($uri -match '/git/refs/') { L 'Branch aktualisieren' 'Обновление ветки' 'Updating branch' } else { L 'Release veröffentlichen' 'Публикация релиза' 'Publishing release' }
    $hours = 3
    if ($script:config -and $script:config.HttpTimeoutHours) { $hours=[Math]::Max(1,[Math]::Min(24,[int]$script:config.HttpTimeoutHours)) }
    try {
        $body=Invoke-UploaderTransport $method $uri $payload ($hours*3600)
        if ([string]::IsNullOrWhiteSpace($body)) { throw 'Empty GitHub response' }
        return $body | ConvertFrom-Json -ErrorAction Stop
    } catch {
        $errorCause=$_.Exception.GetBaseException()
        $category='uncertain'; $httpCode=0; $requestId=''
        if ($errorCause.GetType().Name -eq 'GitHubWriteException') { $category=$errorCause.Category; $httpCode=$errorCause.Status; $requestId=$errorCause.RequestId }
        $detail = switch ($category) {
            'authentication' { L 'Anmeldung abgelehnt. Erneut anmelden.' 'GitHub отклонил вход. Войдите снова.' 'GitHub rejected authentication. Sign in again.' }
            'permission' { L 'GitHub verweigert Schreibzugriff. Rechte und Branch-Regeln prüfen.' 'GitHub запретил запись. Проверьте права аккаунта и правила ветки.' 'GitHub denied the write. Check account permissions and branch rules.' }
            'sso' { L 'Organisation verlangt SSO-Freigabe.' 'Организация требует разрешить доступ через SSO.' 'The organization requires SSO authorization.' }
            'rate-limit' { L 'GitHub-API-Limit erreicht. Später erneut prüfen.' 'Достигнут лимит GitHub API. Повторите проверку позже.' 'GitHub API rate limit reached. Review again later.' }
            'not-found' { L 'Projekt oder Ziel nicht gefunden oder nicht zugänglich.' 'Проект или место назначения не найдено либо недоступно аккаунту.' 'Project or destination not found or inaccessible to this account.' }
            'conflict' { L 'GitHub meldet einen Konflikt. Erneut prüfen.' 'GitHub сообщил о конфликте. Проверьте изменения заново.' 'GitHub reported a conflict. Review again.' }
            'size' { L 'GitHub lehnt die Dateigröße ab. Release-Anhänge verwenden.' 'GitHub отклонил размер файла. Используйте «Вложения релиза».' 'GitHub rejected the file size. Use Release attachments.' }
            'validation' { L 'GitHub lehnt die Daten ab. Dateigröße, Branch-Regeln und Workflow-Rechte prüfen.' 'GitHub отклонил данные. Проверьте размер, правила ветки и права изменения workflow.' 'GitHub rejected the data. Check size, branch rules and workflow permissions.' }
            'timeout' { L 'Zeitlimit erreicht. Ergebnis nicht bestätigt; vor erneutem Senden GitHub prüfen.' 'Истекло время передачи. Результат не подтверждён; перед повтором проверьте GitHub.' 'Transfer timed out. Result unconfirmed; check GitHub before resending.' }
            'connection' { L 'Verbindung unterbrochen. Ergebnis nicht bestätigt; GitHub vor Wiederholung prüfen.' 'Соединение прервалось. Результат не подтверждён; перед повтором проверьте GitHub.' 'Connection interrupted. Result unconfirmed; check GitHub before retrying.' }
            default { L 'Ergebnis nicht bestätigt. Vor erneutem Senden GitHub prüfen; nicht blind wiederholen.' 'Результат не подтверждён. Перед повторной отправкой проверьте GitHub; не повторяйте вслепую.' 'Result unconfirmed. Check GitHub before resending; do not retry blindly.' }
        }
        $suffix=if ($httpCode) { " (HTTP $httpCode)" } else { '' }
        if ($requestId) { $suffix+=" [Request ID: $requestId]" }
        throw "${stage}: $detail$suffix"
    }
}
function Invoke-ProjectGitTransport($plan,$request) {
    Import-UploaderSupport
    $identity=Invoke-GitHubGet 'https://api.github.com/user'
    if($request.expectedAccount -and $identity.login -cne $request.expectedAccount) { throw (L 'Konto geändert. Erneut prüfen.' 'Аккаунт изменился. Выполните проверку заново.' 'Account changed. Review again.') }
    $files=New-Object 'System.Collections.Generic.List[GitCodeFile]'
    foreach($entry in @($plan.entries | Where-Object action -ne 'same')) {
        $local=Get-ProjectLocalFile $request $entry.name
        $file=New-Object GitCodeFile; $file.SourcePath=$local.path; $file.Name=$entry.name; $file.Target=$entry.target; $file.Sha=$entry.sha; $file.Mode=$entry.mode; $file.Length=$entry.length; $files.Add($file)
    }
    $hours=3
    if($script:config.HttpTimeoutHours){$hours=[Math]::Max(1,[Math]::Min(24,[int]$script:config.HttpTimeoutHours))}
    return [GitCodeTransport]::Upload((Find-Git),$plan.repository,$plan.branch,$plan.baseCommit,$plan.baseTree,$files.ToArray(),$identity.login,[long]$identity.id,($hours*3600),[string]$script:progressFile)
}
function Get-ProjectGitError($errorCause) {
    $category=if($errorCause.GetType().Name -eq 'GitCodeException'){$errorCause.Category}else{'preparation'}
    $detail=switch($category) {
        'branch-changed' { L 'Branch geändert. Erneut prüfen; nichts überschrieben.' 'Ветка изменилась. Проверьте изменения заново; чужие файлы не перезаписаны.' 'Branch changed. Review again; remote changes were not overwritten.' }
        'local-changed' { L 'Lokale Datei geändert. Erneut prüfen.' 'Локальный файл изменился. Проверьте изменения заново.' 'Local file changed. Review again.' }
        'authentication' { L 'GitHub-Anmeldung abgelehnt. Erneut anmelden.' 'GitHub отклонил вход. Войдите снова.' 'GitHub rejected authentication. Sign in again.' }
        'permission' { L 'GitHub verweigert den Push. Rechte und Branch-Regeln prüfen.' 'GitHub запретил отправку. Проверьте права аккаунта и правила ветки.' 'GitHub denied the push. Check account permissions and branch rules.' }
        'not-found' { L 'Projekt nicht gefunden oder nicht zugänglich.' 'Проект не найден либо недоступен аккаунту.' 'Project not found or inaccessible to this account.' }
        'size' { L 'Datei zu groß. Release-Anhänge verwenden.' 'GitHub отклонил размер файла. Используйте «Вложения релиза».' 'GitHub rejected the file size. Use Release attachments.' }
        'missing-runtime' { L 'Portable-Ordner vollständig einschließlich Git/GCM entpacken.' 'Распакуйте portable-папку целиком, включая Git и Git Credential Manager.' 'Extract the entire portable folder, including Git and Git Credential Manager.' }
        'process-control' { L 'Git konnte nicht sicher gestartet werden. Keine Übertragung gestartet.' 'Не удалось безопасно запустить Git. Передача не началась.' 'Git could not be started safely. No transfer started.' }
        default { L 'Git-Übertragung nicht bestätigt. Vor erneutem Senden Ergebnis prüfen.' 'Отправка через Git не подтверждена. Перед повтором проверьте результат на GitHub.' 'Git transfer unconfirmed. Check GitHub before resending.' }
    }
    if($errorCause.GetType().Name -eq 'GitCodeException' -and $errorCause.HttpStatus){return "$detail (HTTP $($errorCause.HttpStatus))"}
    return $detail
}
function Send-ProjectFiles($request) {
    $plan=Get-ProjectPlan $request
    if ($plan.baseCommit -cne [string]$request.baseCommit) { throw (L 'Branch geändert. Erneut prüfen.' 'Ветка изменилась. Проверьте изменения заново.' 'Branch changed. Review again.') }
    if($plan.entries.Count -ne @($request.entries).Count){throw (L 'Dateiauswahl geändert. Erneut prüfen.' 'Выбор файлов изменился после проверки. Проверьте заново.' 'File selection changed after review. Review again.')}
    foreach($entry in $plan.entries) {
        $expected=@($request.entries | Where-Object { $_.name -ceq $entry.name })
        if ($expected.Count -ne 1 -or $expected[0].sha -cne $entry.sha -or $expected[0].target -cne $entry.target -or $expected[0].action -cne $entry.action) { throw (L 'Dateien geändert. Erneut prüfen.' 'Файлы изменились после проверки. Проверьте заново.' 'Files changed after review. Review again.') }
    }
    $script:progressMode='code'; $script:resultUrl=$plan.resultUrl
    $script:progressFileCount=$plan.entries.Count
    $script:progressBytesTotal=[long](($plan.entries | Measure-Object length -Sum).Sum)
    $changes=@($plan.entries | Where-Object action -ne 'same')
    if ($changes.Count) {
        $script:progressCurrentFile=''; $script:progressCurrentSize=0
        Write-ProgressState 'preparing'
        $commit=''; $failure=''
        try { $gitResult=Invoke-ProjectGitTransport $plan $request; $commit=[string]$gitResult.Commit }
        catch { $cause=$_.Exception.GetBaseException(); if($cause.GetType().Name -eq 'GitCodeException'){$commit=[string]$cause.Commit}; $failure=Get-ProjectGitError $cause }
        # A lost push reply is reconciled by GET, never by another push. Only the
        # exact reviewed commit in the selected branch can complete this upload.
        if(-not $commit){throw $failure}
        try {$head=Invoke-GitHubGet "https://api.github.com/repos/$($plan.repository)/git/ref/heads/$([uri]::EscapeDataString($plan.branch))"}
        catch {if($failure){throw $failure};throw (L 'Commit-Rückprüfung nicht erreichbar. Ergebnis vor Wiederholung prüfen.' 'Не удалось проверить коммит. Перед повтором проверьте результат на GitHub.' 'Commit readback unavailable. Check GitHub before retrying.')}
        if($head.object.sha -cne $commit) { if($failure){throw $failure}; throw (L 'Commit in Branch nicht bestätigt. Ergebnis prüfen.' 'Коммит в выбранной ветке не подтверждён. Проверьте результат на GitHub.' 'Commit not confirmed in the selected branch. Check GitHub.') }
    }
    foreach($entry in $plan.entries) { $script:progressConfirmed.Add($entry.name) }
    $script:progressFilesDone=$plan.entries.Count; $script:progressBytesDone=$script:progressBytesTotal
    Write-ProgressState 'completed' '' 0 0 $(if($changes.Count){''}else{L 'Keine Änderungen.' 'Изменений нет. Новый коммит не создавался.' 'No changes. No new commit created.'})
}
function Complete-UploaderRelease($release) {
    if (-not $release.draft -or @($release.assets).Count -eq 0 -or @($release.assets | Where-Object state -ne 'uploaded').Count) { throw (L 'Nur vollständigen Entwurf veröffentlichen.' 'Опубликовать можно только черновик с полностью загруженными вложениями.' 'Only a draft with fully uploaded attachments can be published.') }
    Write-ProgressState 'publishing'
    $uri="$script:releasesUri/$($release.id)"
    $expected=@($release.assets | ForEach-Object { "$($_.id):$($_.size):$($_.state)" } | Sort-Object) -join '|'
    $current=Invoke-GitHubGet $uri
    if (-not $current.draft -or $current.id -ne $release.id -or ((@($current.assets | ForEach-Object { "$($_.id):$($_.size):$($_.state)" } | Sort-Object) -join '|') -cne $expected)) { throw (L 'Entwurf geändert. Erneut prüfen.' 'Черновик изменился. Проверьте заново.' 'Draft changed. Review again.') }
    try { $null=Invoke-UploaderWrite 'Patch' $uri @{draft=$false} }
    catch { $current=Invoke-GitHubGet $uri; if($current.draft -or $current.id -ne $release.id) { throw }; $release=$current }
    $published=Invoke-GitHubGet $uri
    if ($published.draft -or $published.id -ne $release.id -or ((@($published.assets | ForEach-Object { "$($_.id):$($_.size):$($_.state)" } | Sort-Object) -join '|') -cne $expected)) { throw (L 'Veröffentlichung nicht bestätigt.' 'Публикация не подтверждена. Проверьте GitHub перед повтором.' 'Publication not confirmed. Check GitHub before retrying.') }
    $script:published=$true; $script:progressReleaseUrl=[string]$published.html_url; $script:resultUrl=[string]$published.html_url
    if ($script:progressFileCount -eq 0) {
        $script:progressFileCount=@($published.assets).Count; $script:progressFilesDone=$script:progressFileCount
        $script:progressBytesTotal=[long](($published.assets | Measure-Object size -Sum).Sum); $script:progressBytesDone=$script:progressBytesTotal
        foreach($asset in $published.assets) { $script:progressConfirmed.Add($(if($asset.label){[string]$asset.label}else{[string]$asset.name})) }
    }
    $script:downloadLinks=@($published.assets | ForEach-Object { @{name=[string]$_.name;localName=$(if($_.label){[string]$_.label}else{[string]$_.name});url=[string]$_.browser_download_url} })
}
