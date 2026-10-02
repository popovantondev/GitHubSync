[CmdletBinding()]
param([switch]$CheckOnly, [switch]$SignInOnly, [switch]$ListRepositories, [string]$ListDraftsFor = '', [string]$CreateDraftRequest = '', [string]$CreateProjectRequest = '', [string]$UploaderRequest = '', [string]$SyncRequest = '', [string]$ProgressFile = $env:WATCHDOG_PROGRESS_FILE, [ValidateSet('de','ru','en')][string]$Language = 'de')

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$configPath = Join-Path $root 'config.json'
$uploadRoot = Join-Path $root 'upload'
$http = $null
$token = $null
$mutex = $null
$mutexOwned = $false
$script:progressFile = $ProgressFile
$script:progressFileCount = 0
$script:progressFilesDone = 0
$script:progressBytesTotal = [long]0
$script:progressBytesDone = [long]0
$script:progressConfirmed = New-Object 'System.Collections.Generic.List[string]'
$script:progressCurrentFile = ''
$script:progressCurrentSize = [long]0
$script:progressAttempt = 0
$script:progressRetrySeconds = 0
$script:catalogChoices = @()
$script:catalogAccount = ''
$script:createdTag = ''
$script:createdRepository = ''
$script:uploadedAssetIds = @{}
$script:localDigests = @{}
$script:progressReleaseUrl = ''
$script:progressMode = 'release'
$script:resultUrl = ''
$script:published = $false
$script:downloadLinks = @()
$script:projectPlan = $null
$script:releaseAssets = @()
. (Join-Path $root 'Uploader-Operations.ps1')
$script:progressDirection = 'upload'
$script:syncCatalog = $null
if (Test-Path -LiteralPath (Join-Path $root 'Sync-Operations.ps1')) { . (Join-Path $root 'Sync-Operations.ps1') }

function L([string]$de, [string]$ru, [string]$en) {
    if ($Language -eq 'ru') { return $ru }
    if ($Language -eq 'en') { return $en }
    return $de
}

function Get-WorkerErrorMessage($exception) {
    $probe=$exception
    while ($probe) {
        if ($probe.Message -match 'Path contains a link/junction') { return (L 'Pfad enthält einen Link/Junction oder einen nicht unterstützten Reparse-Punkt. Einen normalen Ordner wählen; OneDrive-Cloudordner sind erlaubt.' 'В пути есть ссылка/junction или неподдерживаемая служебная ссылка. Выберите обычную папку; облачные папки OneDrive разрешены.' 'Path contains a link/junction or an unsupported reparse point. Choose a regular folder; OneDrive cloud folders are supported.') }
        if ($probe.Message -match 'Cannot inspect (path safety|reparse)|Reparse tag is unavailable') { return (L 'Pfad konnte nicht sicher geprüft werden. Ordnerzugriff und OneDrive-Verfügbarkeit prüfen.' 'Не удалось безопасно проверить путь. Проверьте доступ к папке и доступность OneDrive.' 'Cannot verify path safety. Check folder access and OneDrive availability.') }
        $probe=$probe.InnerException
    }
    $message=$exception.GetBaseException().Message
    if ($message -eq 'Choose an existing download folder.') { return (L 'Vorhandenen Download-Ordner wählen.' 'Выберите существующую папку для скачивания.' 'Choose an existing download folder.') }
    return $message
}
function Find-Git {
    $bundled = Join-Path $root 'runtime\git\cmd\git.exe'
    if (Test-Path -LiteralPath $bundled -PathType Leaf) { return $bundled }
    $systemGit = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($systemGit) { return $systemGit.Source }
    throw (L 'Git fehlt. Den vollständigen Portable-Ordner einschließlich runtime entpacken.' 'Git не найден. Распакуйте portable-папку целиком, включая runtime.' 'Git not found. Extract the complete portable folder, including runtime.')
}

function Write-ProgressState([string]$state, [string]$file = '', [long]$fileSize = 0, [long]$fileSent = 0, [string]$message = '') {
    if ([string]::IsNullOrWhiteSpace($script:progressFile)) { return }
    try {
        $snapshot = @{
            state = $state; file = $file; fileBytes = $fileSize; fileSent = $fileSent
            filesTotal = $script:progressFileCount; filesCompleted = $script:progressFilesDone
            totalBytes = $script:progressBytesTotal; completedBytes = $script:progressBytesDone
            confirmedFiles = @($script:progressConfirmed.ToArray()); attempt = $script:progressAttempt
            retryAfterSeconds = $script:progressRetrySeconds
            releaseUrl = $script:progressReleaseUrl
            mode = $script:progressMode; resultUrl = $script:resultUrl; published = $script:published
            direction = $script:progressDirection; syncCatalog = $script:syncCatalog
            retryAfter = $script:syncRetryAfter; rateReset = $script:syncRateReset
            downloads = @($script:downloadLinks); plan = $script:projectPlan; releaseAssets = @($script:releaseAssets)
            message = $message; updated = [DateTime]::UtcNow.ToString('o')
            choices = @($script:catalogChoices); account = $script:catalogAccount; createdTag = $script:createdTag; createdRepository = $script:createdRepository
        } | ConvertTo-Json -Depth 15 -Compress
        if($state -eq 'failed' -and [IO.File]::Exists($script:progressFile)) {
            try {
                $previous=Get-Content -LiteralPath $script:progressFile -Raw -Encoding UTF8 | ConvertFrom-Json
                if($previous.transferKind -eq 'git') {
                    $record=$snapshot | ConvertFrom-Json
                    foreach($key in @('transferKind','gitStage','gitProgressKnown','gitObjectsPercent','gitObjectsDone','gitObjectsTotal','gitPackBytes')) {if($previous.PSObject.Properties[$key]){$record | Add-Member -NotePropertyName $key -NotePropertyValue $previous.$key -Force}}
                    $snapshot=$record | ConvertTo-Json -Depth 15 -Compress
                }
            }catch{}
        }
        $temp = $script:progressFile + '.tmp'
        [System.IO.File]::WriteAllText($temp, $snapshot, (New-Object System.Text.UTF8Encoding($false)))
        if ([System.IO.File]::Exists($script:progressFile)) {
            try { [System.IO.File]::Replace($temp, $script:progressFile, $null) }
            catch { [System.IO.File]::Delete($script:progressFile); [System.IO.File]::Move($temp, $script:progressFile) }
        } else { [System.IO.File]::Move($temp, $script:progressFile) }
    } catch { }
}

if ($CheckOnly) { Write-ProgressState 'checking' } else { Write-ProgressState 'preparing' }

function Confirm-ProgressFile($local) {
    if (-not $script:progressConfirmed.Contains($local.Name)) {
        $script:progressConfirmed.Add($local.Name)
        $script:progressFilesDone++
        $script:progressBytesDone += [long]$local.Length
    }
}

function Write-Heading([string]$text) {
    Write-Host ''
    Write-Host $text -ForegroundColor Cyan
    Write-Host ('-' * [Math]::Min(72, $text.Length)) -ForegroundColor DarkGray
}

function Get-GitHubToken([bool]$interactive = $true) {
    $git = Find-Git
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $git
    $start.EnvironmentVariables['PATH'] = (Split-Path $git) + ';' + (Join-Path (Split-Path (Split-Path $git)) 'mingw64\bin') + ';' + $env:PATH
    $start.Arguments = '-c credential.gitHubAuthModes=browser credential fill'
    if (-not $interactive) { $start.Arguments = '-c credential.interactive=false ' + $start.Arguments; $start.EnvironmentVariables['GIT_TERMINAL_PROMPT']='0'; $start.EnvironmentVariables['GCM_INTERACTIVE']='0' }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $start
    if (-not $proc.Start()) { throw (L 'Git Credential Manager konnte nicht gestartet werden.' 'Не удалось запустить Git Credential Manager.' 'Could not start Git Credential Manager.') }
    $proc.StandardInput.WriteLine('protocol=https')
    $proc.StandardInput.WriteLine('host=github.com')
    $proc.StandardInput.WriteLine()
    $proc.StandardInput.Close()
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    $limit = if($interactive){300000}else{30000}
    if(-not $proc.WaitForExit($limit)) { try{$proc.Kill()}catch{}; $proc.Dispose(); throw (L 'Anmeldung dauert zu lange. Erneut versuchen.' 'Время ожидания входа истекло. Повторите попытку.' 'Sign-in timed out. Try again.') }
    $outText = $outTask.GetAwaiter().GetResult()
    $errText = $errTask.GetAwaiter().GetResult()
    $code = $proc.ExitCode
    $proc.Dispose()
    if ($code -ne 0) { throw (L "Git Credential Manager wurde mit Code $code beendet. Anmeldung prüfen." "Git Credential Manager завершился с кодом $code. Проверьте вход в GitHub." "Git Credential Manager exited with code $code. Check GitHub sign-in.") }
    $entry = $outText -split "\r?\n" | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
    $outText = $null
    if (-not $entry) { throw (L 'Keine GitHub-Anmeldung gefunden. Mit Git Credential Manager anmelden.' 'Не найден вход в GitHub. Выполните вход через Git Credential Manager.' 'No GitHub sign-in found. Sign in with Git Credential Manager.') }
    return $entry.Substring(9)
}

function Get-ApiHeaders {
    $headers = @{
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    if ($script:token) { $headers.Authorization = "Bearer $script:token" }
    return $headers
}

function Invoke-GitHubGet([string]$uri) {
    try { Invoke-RestMethod -Method Get -Uri $uri -Headers (Get-ApiHeaders) -TimeoutSec 30 }
    catch {
        $response = $_.Exception.Response
        if ($response) {
            $code = [int]$response.StatusCode
            switch ($code) {
                401 { throw (L 'GitHub-Anmeldung ungültig. Erneut anmelden.' 'GitHub не принял авторизацию. Выполните вход повторно.' 'GitHub rejected authentication. Sign in again.') }
                403 { throw (L 'GitHub verweigert Zugriff. Berechtigungen, SSO oder API-Limit prüfen.' 'GitHub запретил доступ. Проверьте права, SSO или лимит API.' 'GitHub denied access. Check permissions, SSO or API rate limit.') }
                404 { throw (L 'Repository oder Release nicht gefunden oder für dieses Konto nicht zugänglich.' 'Репозиторий или релиз не найден либо недоступен этому аккаунту.' 'Repository or release not found or inaccessible to this account.') }
                default { throw (L "GitHub-Fehler HTTP $code." "Ошибка GitHub HTTP $code." "GitHub error HTTP $code.") }
            }
        }
        throw (L 'GitHub nicht erreichbar. Internetverbindung prüfen.' 'Нет соединения с GitHub. Проверьте интернет.' 'Cannot reach GitHub. Check your internet connection.')
    }
}

function Get-Release {
    if ($script:releaseId) {
        $result = Invoke-GitHubGet "$script:releasesUri/$script:releaseId"
        $script:progressReleaseUrl = [string]$result.html_url
        return $result
    }
    # Drafts are listed for authorized writers; the tag endpoint is published-only.
    for ($page = 1; ; $page++) {
        # Windows PowerShell 5.1 may emit a REST JSON array as one pipeline object.
        # Flatten batches explicitly before inspecting draft/id/tag on one release.
        $items = New-Object 'System.Collections.Generic.List[object]'
        foreach ($batch in @(Invoke-GitHubGet "$script:releasesUri`?per_page=100&page=$page")) {
            foreach ($item in $batch) { $items.Add($item) }
        }
        foreach ($item in $items) {
            if ($item.tag_name -ceq $script:config.ReleaseTag) {
                $script:releaseId = $item.id
                $script:progressReleaseUrl = [string]$item.html_url
                return $item
            }
        }
        if ($items.Count -lt 100) { break }
    }
    throw ((L 'Release-Tag nicht gefunden.' 'Тег релиза не найден.' 'Release tag not found.') + ' ' + (L "Gesucht: $($script:config.ReleaseTag). Auf GitHub unter Releases einen Draft mit genau diesem Tag speichern und Schreibzugriff prüfen. Watchdog erstellt keine Releases." "Искомый тег: $($script:config.ReleaseTag). На GitHub в Releases сохраните черновик с точно таким тегом и проверьте права записи. Watchdog не создаёт релизы." "Requested tag: $($script:config.ReleaseTag). Save a draft with exactly this tag in GitHub Releases and check write access. Watchdog does not create releases."))
}

function Get-PagedGitHubItems([string]$baseUri) {
    $separator = if ($baseUri.Contains('?')) { '&' } else { '?' }
    for ($page = 1; ; $page++) {
        $items = New-Object 'System.Collections.Generic.List[object]'
        foreach ($batch in @(Invoke-GitHubGet "$baseUri${separator}per_page=100&page=$page")) {
            foreach ($item in $batch) { $items.Add($item) }
        }
        foreach ($item in $items) { $item }
        if ($items.Count -lt 100) { break }
    }
}

function Get-LocalAssets {
    $items = @()
    foreach ($name in $script:config.Files) {
        if ([string]::IsNullOrWhiteSpace([string]$name)) { throw (L 'Files enthält einen leeren Namen.' 'Files содержит пустое имя.' 'Files contains an empty name.') }
        if ([System.IO.Path]::GetFileName([string]$name) -ne [string]$name -or $name -match '[\\/]' -or $name -eq '.' -or $name -eq '..') {
            throw (L "Nur Dateinamen ohne Pfade erlaubt: $name" "Разрешены только имена файлов без путей: $name" "Only file names without paths are allowed: $name")
        }
        $path = Join-Path $script:uploadRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw (L "Datei nicht gefunden: $path" "Не найден файл: $path" "File not found: $path") }
        $items += [pscustomobject]@{ Name = [string]$name; Path = $path; Length = [long](Get-Item -LiteralPath $path).Length }
    }
    if ($items.Count -eq 0) { throw (L 'Keine Dateien ausgewählt.' 'В Files нет файлов для загрузки.' 'No files selected.') }
    $duplicates = @($items | Group-Object Name | Where-Object Count -gt 1)
    if ($duplicates.Count -gt 0) { throw (L 'Doppelte Dateinamen. Duplikate entfernen.' 'В Files повторяются имена. Удали дубликаты из config.json.' 'Duplicate file names. Remove duplicates.') }
    return $items
}

function Show-RemoteStatus($release, $localAssets) {
    $remoteAssets = @($release.assets)
    $uploadedCount = 0
    foreach ($local in $localAssets) {
        $remote = Find-RemoteAsset $release $local.Name
        if ($remote -and $remote.state -eq 'uploaded' -and [long]$remote.size -eq $local.Length) { $uploadedCount++ }
    }
    Write-Host ((L "GitHub: {0}/{1} Dateien bestätigt; Release {2}." "GitHub: {0}/{1} файлов подтверждено; release {2}." "GitHub: {0}/{1} files confirmed; release {2}.") -f $uploadedCount, $localAssets.Count, $release.tag_name) -ForegroundColor Cyan
    foreach ($local in $localAssets) {
        $remote = Find-RemoteAsset $release $local.Name
        if ($remote -and $remote.state -eq 'uploaded' -and [long]$remote.size -eq $local.Length) {
            Write-Host ((L "  OK {0} ({1:N0} Bytes)" "  OK {0} ({1:N0} байт)" "  OK {0} ({1:N0} bytes)") -f $local.Name, $local.Length) -ForegroundColor Green
        } elseif ($remote) {
            Write-Host ((L "  !! {0}: Datei vorhanden, Größe/Status stimmen nicht überein." "  !! {0}: asset с таким именем уже есть, размер/статус не совпадает." "  !! {0}: asset exists, size/status do not match.") -f $local.Name) -ForegroundColor Red
        } else {
            Write-Host ((L "  .. {0}: wartet auf Upload" "  .. {0}: ожидает загрузки" "  .. {0}: waiting for upload") -f $local.Name) -ForegroundColor DarkGray
        }
    }
}

function Get-TotalBytesSent {
    [long]$sum = 0
    foreach ($adapter in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
        try { $sum += [long]$adapter.GetIPv4Statistics().BytesSent } catch { }
    }
    return $sum
}

function Find-RemoteAsset($release, [string]$name) {
    if ($script:uploadedAssetIds.ContainsKey($name)) {
        return $release.assets | Where-Object { $_.id -eq $script:uploadedAssetIds[$name] } | Select-Object -First 1
    }
    $exact=@($release.assets | Where-Object { $_.name -ceq $name -or $_.label -ceq $name })
    if ($exact.Count -eq 1) { return $exact[0] }
    if ($exact.Count -gt 1) { throw (L 'Konflikt: mehrdeutige Dateizuordnung.' 'Конфликт: несколько файлов подходят по имени.' 'Conflict: ambiguous filename mapping.') }
    # Recover older uploads renamed by GitHub, only with a unique content match.
    $local=@($script:localAssets | Where-Object Name -CEQ $name)
    if ($local.Count -ne 1 -or -not (Test-Path -LiteralPath $local[0].Path -PathType Leaf)) { return $null }
    $candidates=@($release.assets | Where-Object { $_.state -eq 'uploaded' -and [long]$_.size -eq [long]$local[0].Length -and $_.digest -like 'sha256:*' })
    if ($candidates.Count -eq 0) { return $null }
    if (-not $script:localDigests.ContainsKey($name)) { $script:localDigests[$name]='sha256:'+(Get-FileHash -LiteralPath $local[0].Path -Algorithm SHA256).Hash.ToLowerInvariant() }
    $matches=@($candidates | Where-Object { $_.digest -eq $script:localDigests[$name] })
    if ($matches.Count -gt 1) { throw (L 'Konflikt: mehrere Assets haben denselben Inhalt.' 'Конфликт: несколько файлов GitHub имеют одинаковое содержимое.' 'Conflict: several GitHub assets have identical content.') }
    if ($matches.Count -eq 1) {
        if (@($script:localAssets | Where-Object { $_.Name -cne $name -and $_.Length -eq $local[0].Length -and (Test-Path -LiteralPath $_.Path) -and ('sha256:'+(Get-FileHash -LiteralPath $_.Path -Algorithm SHA256).Hash.ToLowerInvariant()) -eq $script:localDigests[$name] }).Count -gt 0) { throw (L 'Konflikt: gleiche lokale Inhalte, Zuordnung unklar.' 'Конфликт: одинаковое содержимое локальных файлов; соответствие неясно.' 'Conflict: identical local contents, mapping unclear.') }
        return $matches[0]
    }
    return $null
}

function Confirm-RemoteAsset($release, $local) {
    $remote = Find-RemoteAsset $release $local.Name
    if (-not $remote) { return $false }
    if ($remote.state -ne 'uploaded' -or [long]$remote.size -ne $local.Length) {
        throw (L "Konflikt: $($local.Name) vorhanden, Größe/Status stimmen nicht überein. Nicht überschrieben." "Конфликт: GitHub уже содержит $($local.Name), но статус/размер не совпадает. Файл не перезаписан." "Conflict: $($local.Name) exists, size/status do not match. Not overwritten.")
    }
    if ($remote.digest -like 'sha256:*') {
        $actual='sha256:'+(Get-FileHash -LiteralPath $local.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ine $remote.digest) { throw (L 'Konflikt: Dateiinhalte stimmen nicht überein. Nicht überschrieben.' 'Конфликт: содержимое файла на GitHub отличается. Файл не перезаписан; релиз не опубликован.' 'Conflict: GitHub file content differs. Not overwritten; release not published.') }
        $script:localDigests[$local.Name]=$actual
    }
    return $true
}

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

public sealed class WatchdogUploadContent : HttpContent {
    private readonly string path;
    private readonly long length;
    private readonly string progressPath;
    private readonly string fileName;
    private readonly int filesTotal;
    private readonly int filesCompleted;
    private readonly long bytesCompleted;
    private readonly string[] confirmedFiles;
    private readonly int attempt;
    public WatchdogUploadContent(string filePath, string statusPath, int total, int completed, long doneBefore, long allBytes)
        : this(filePath, statusPath, total, completed, doneBefore, allBytes, new string[0], 1) { }
    public WatchdogUploadContent(string filePath, string statusPath, int total, int completed, long doneBefore, long allBytes, string[] confirmed, int currentAttempt) {
        path = filePath;
        length = new FileInfo(filePath).Length;
        progressPath = statusPath;
        fileName = Path.GetFileName(filePath);
        filesTotal = total;
        filesCompleted = completed;
        bytesCompleted = doneBefore;
        totalBytes = allBytes;
        confirmedFiles = confirmed;
        attempt = currentAttempt;
    }
    private void Publish(long done) {
        if (String.IsNullOrEmpty(progressPath)) return;
        try {
            var data = new System.Collections.Generic.Dictionary<string, object> {
                {"state", "uploading"}, {"file", fileName}, {"fileBytes", length}, {"fileSent", done},
                {"filesTotal", filesTotal}, {"filesCompleted", filesCompleted}, {"totalBytes", totalBytes},
                {"completedBytes", bytesCompleted}, {"message", ""}, {"updated", DateTime.UtcNow.ToString("o")},
                {"confirmedFiles", confirmedFiles}, {"attempt", attempt}, {"retryAfterSeconds", 0}
            };
            var text = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(data);
            var temp = progressPath + ".tmp";
            File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
            if (File.Exists(progressPath)) {
                try { File.Replace(temp, progressPath, null); }
                catch { File.Delete(progressPath); File.Move(temp, progressPath); }
            } else File.Move(temp, progressPath);
        } catch { }
    }
    private readonly long totalBytes;
    protected override async Task SerializeToStreamAsync(Stream target, TransportContext context) {
        byte[] buffer = new byte[1024 * 1024];
        long done = 0;
        long next = 0;
        using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, true)) {
            int read;
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0) {
                await target.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                done += read;
                if (done >= next || done == length) {
                    int percent = length > 0 ? (int)(100 * done / length) : 100;
                    Console.Write("\r  {0,3}%  {1:N0}/{2:N0} MiB", percent, done / 1048576.0, length / 1048576.0);
                    Publish(done);
                    next = done + Math.Max(1048576, length / 100);
                }
            }
        }
        Console.WriteLine();
    }
    protected override bool TryComputeLength(out long value) {
        value = length;
        return true;
    }
}
'@ -ReferencedAssemblies 'System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll' -WarningAction SilentlyContinue

function New-GitHubProject([string]$login, [string]$name, [bool]$isPrivate) {
    if ($login -notmatch '^[A-Za-z0-9-]+$' -or $name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$' -or $name.EndsWith('.git',[StringComparison]::OrdinalIgnoreCase)) { throw (L 'Projektname: Buchstaben, Zahlen, Punkt, - oder _ (1–100), keine URL.' 'Имя проекта: латинские буквы, цифры, точка, - или _ (1–100), без ссылки.' 'Project name: letters, digits, dot, - or _ (1–100), not a URL.') }
    foreach ($existing in @(Get-PagedGitHubItems 'https://api.github.com/user/repos?affiliation=owner')) {
        if ([string]$existing.owner.login -ieq $login -and [string]$existing.name -ieq $name) { throw (L 'Projektname bereits vergeben. Bestehendes Projekt auswählen oder anderen Namen verwenden.' 'Такой проект уже есть. Выберите его из списка или задайте другое имя.' 'Project already exists. Select it from the list or use another name.') }
    }
    $body=@{name=$name;private=$isPrivate;auto_init=$true} | ConvertTo-Json -Compress
    try { $project=Invoke-RestMethod -Method Post -Uri 'https://api.github.com/user/repos' -Headers (Get-ApiHeaders) -Body ([Text.Encoding]::UTF8.GetBytes($body)) -ContentType 'application/json; charset=utf-8' -TimeoutSec 60 }
    catch { throw (L 'Erstellung nicht bestätigt. Projektliste vor erneutem Erstellen aktualisieren; Zugriff/Internet/Name prüfen.' 'Создание не подтверждено. Сначала обновите список проектов: запрос мог выполниться. Проверьте права, интернет и имя.' 'Creation unconfirmed. Refresh projects before creating again: the request may have succeeded. Check access, connection and name.') }
    if (-not $project.id -or [string]$project.full_name -ine "$login/$name" -or $project.private -ne $isPrivate) { throw (L 'Projekt oder Sichtbarkeit nicht bestätigt. GitHub prüfen.' 'Проект или доступ не подтверждены. Проверьте GitHub.' 'Project or visibility not confirmed. Check GitHub.') }
    return $project
}

function New-DraftRelease([string]$repositoryName, [string]$tag, [string]$title) {
    if ($repositoryName -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $repositoryName -eq 'OWNER/REPOSITORY') { throw (L 'Repository auswählen.' 'Выберите проект GitHub.' 'Choose a GitHub project.') }
    if ($tag -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$' -or $tag.Contains('..') -or $tag.EndsWith('.') -or $tag.EndsWith('.lock')) { throw (L 'Versionskennung: Buchstaben, Zahlen, Punkt, - oder _; z.B. v1.0.0.' 'Метка версии: буквы, цифры, точка, - или _; например v1.0.0.' 'Version tag: letters, digits, dot, - or _; e.g. v1.0.0.') }
    if ([string]::IsNullOrWhiteSpace($title) -or $title.Length -gt 120) { throw (L 'Titel: 1–120 Zeichen.' 'Название: 1–120 символов.' 'Title: 1–120 characters.') }
    $uri = "https://api.github.com/repos/$repositoryName/releases"
    foreach ($existing in @(Get-PagedGitHubItems $uri)) {
        if ([string]$existing.tag_name -ceq $tag) {
            if ($existing.draft -eq $true) { return $existing }
            throw (L 'Diese Version ist bereits veröffentlicht. Eine neue Versionskennung verwenden.' 'Эта версия уже опубликована. Укажите другую метку версии.' 'This version is already published. Use a different version tag.')
        }
    }
    $body = @{ tag_name=$tag; name=$title; draft=$true; prerelease=$false; generate_release_notes=$false } | ConvertTo-Json -Compress
    try {
        $release = Invoke-RestMethod -Method Post -Uri $uri -Headers (Get-ApiHeaders) -Body ([Text.Encoding]::UTF8.GetBytes($body)) -ContentType 'application/json; charset=utf-8' -TimeoutSec 60
    } catch {
        # A timed-out POST may have succeeded remotely: never retry automatically.
        throw (L 'Entwurf nicht bestätigt. Entwurfliste aktualisieren, bevor erneut erstellt wird. Zugriff/Internet und Versionskennung prüfen.' 'Создание не подтверждено. Сначала обновите список черновиков: запрос мог выполниться. Проверьте доступ, интернет и метку версии.' 'Creation was not confirmed. Refresh drafts before creating again: the request may have succeeded. Check access, connection and version tag.')
    }
    if (-not $release.id -or $release.draft -ne $true -or [string]$release.tag_name -cne $tag) { throw (L 'GitHub hat den Entwurf nicht bestätigt. Liste aktualisieren.' 'GitHub не подтвердил черновик. Обновите список.' 'GitHub did not confirm the draft. Refresh the list.') }
    return $release
}

function Get-CatalogChoices([string]$repositoryName) {
    if ([string]::IsNullOrWhiteSpace($repositoryName)) {
        Get-PagedGitHubItems 'https://api.github.com/user/repos?sort=updated&affiliation=owner,collaborator,organization_member' |
            Where-Object { $_.permissions.push -and -not $_.archived } |
            ForEach-Object { @{ value=[string]$_.full_name; label=[string]$_.full_name } }
    } else {
        if ($repositoryName -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw (L 'Repository auswählen.' 'Выберите проект GitHub.' 'Choose a GitHub project.') }
        Get-PagedGitHubItems "https://api.github.com/repos/$repositoryName/releases" |
            Where-Object { $_.draft } |
            ForEach-Object { @{ value=[string]$_.tag_name; label=([string]$_.name + ' · ' + [string]$_.tag_name) } }
    }
}

try {
    $exclusive=@($SignInOnly,$ListRepositories,(-not [string]::IsNullOrWhiteSpace($ListDraftsFor)),(-not [string]::IsNullOrWhiteSpace($CreateDraftRequest)),(-not [string]::IsNullOrWhiteSpace($CreateProjectRequest)),(-not [string]::IsNullOrWhiteSpace($UploaderRequest)),(-not [string]::IsNullOrWhiteSpace($SyncRequest))) | Where-Object { $_ }
    if (@($exclusive).Count -gt 1) { throw (L 'Modi nicht kombinieren.' 'Нельзя совмещать разные операции.' 'Cannot combine operations.') }
    if ($SyncRequest) {
        $request=Get-Content -LiteralPath $SyncRequest -Raw -Encoding UTF8 | ConvertFrom-Json
        Invoke-SyncOperation $request
        exit 0
    }
    if ($UploaderRequest) {
        $request=Get-Content -LiteralPath $UploaderRequest -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($request.operation -notin @('preview','upload','branches','publish')) { throw 'Invalid uploader operation' }
        if ($CheckOnly -and $request.operation -in @('upload','publish')) { throw (L 'Prüfen darf nichts schreiben.' 'Режим проверки не может изменять GitHub.' 'Check mode cannot change GitHub.') }
        Assert-UploaderProject $request.repository
        $script:token=Get-GitHubToken
        $account=Invoke-GitHubGet 'https://api.github.com/user'; $script:catalogAccount=[string]$account.login
        if ($request.operation -in @('upload','publish') -and ([string]::IsNullOrWhiteSpace($request.expectedAccount) -or $request.expectedAccount -ine $account.login)) { throw (L 'Konto geändert. Erneut prüfen.' 'Аккаунт изменился. Проверьте заново.' 'Account changed. Review again.') }
        if ($request.operation -eq 'branches') {
            $script:catalogChoices=@(Get-PagedGitHubItems "https://api.github.com/repos/$($request.repository)/branches" | ForEach-Object { @{value=$_.name;label=$_.name} })
            Write-ProgressState 'catalog-ready'; exit 0
        }
        $script:mutex=New-Object System.Threading.Mutex($false,'Local\GitHubReleaseWatchdog.Upload'); $script:mutexOwned=$script:mutex.WaitOne(0)
        if(-not $script:mutexOwned) { throw (L 'Upload läuft bereits.' 'Другая загрузка уже выполняется.' 'Another upload is already running.') }
        if ($request.operation -eq 'preview') { $script:progressMode='code'; $script:projectPlan=Get-ProjectPlan $request; Write-ProgressState 'project-planned'; exit 0 }
        if ($request.operation -eq 'upload') { $script:config=@{HttpTimeoutHours=$(if($request.timeoutHours){$request.timeoutHours}else{3})}; Send-ProjectFiles $request; exit 0 }
        $script:config=@{Repository=$request.repository;ReleaseTag=$request.tag}; $script:releasesUri="https://api.github.com/repos/$($request.repository)/releases"
        $release=Get-Release
        $current=@($release.assets | ForEach-Object { "$($_.id):$($_.size):$($_.state)" } | Sort-Object)
        if ($release.id -ne $request.releaseId -or ($current -join '|') -cne (@($request.assetSignatures | Sort-Object) -join '|')) { throw (L 'Entwurf geändert. Erneut prüfen.' 'Состав черновика изменился. Проверьте его заново.' 'Draft attachments changed. Check again.') }
        Complete-UploaderRelease $release
        Write-ProgressState 'completed'; exit 0
    }
    if (-not [string]::IsNullOrWhiteSpace($CreateProjectRequest)) {
        if ($CheckOnly -or $SignInOnly -or $ListRepositories -or $ListDraftsFor -or $CreateDraftRequest) { throw (L 'Erstellen kann nicht mit anderen Modi kombiniert werden.' 'Создание нельзя совмещать с другими режимами.' 'Creation cannot be combined with other modes.') }
        Write-ProgressState 'checking'
        $request=Get-Content -LiteralPath $CreateProjectRequest -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($request.private -isnot [bool]) { throw (L 'Sichtbarkeit auswählen.' 'Выберите доступ к проекту.' 'Choose project visibility.') }
        $script:token=Get-GitHubToken
        $account=Invoke-GitHubGet 'https://api.github.com/user'
        $script:catalogAccount=[string]$account.login
        if ([string]::IsNullOrWhiteSpace([string]$request.expectedAccount) -or [string]$request.expectedAccount -ine $script:catalogAccount) { throw (L 'Konto geändert. Projektliste erneut öffnen.' 'Аккаунт изменился. Снова откройте список проектов.' 'Account changed. Reopen the project list.') }
        $project=New-GitHubProject $script:catalogAccount ([string]$request.name) $request.private
        $script:createdRepository=[string]$project.full_name
        Write-ProgressState 'project-created'
        exit 0
    }
    if (-not [string]::IsNullOrWhiteSpace($CreateDraftRequest)) {
        if ($CheckOnly -or $SignInOnly -or $ListRepositories -or -not [string]::IsNullOrWhiteSpace($ListDraftsFor)) { throw (L 'Erstellen kann nicht mit einem Prüfmodus kombiniert werden.' 'Создание нельзя совмещать с режимом проверки.' 'Creation cannot be combined with a read-only mode.') }
        Write-ProgressState 'checking'
        $request = Get-Content -LiteralPath $CreateDraftRequest -Raw -Encoding UTF8 | ConvertFrom-Json
        $script:token = Get-GitHubToken
        $account = Invoke-GitHubGet 'https://api.github.com/user'
        $script:catalogAccount = [string]$account.login
        $created = New-DraftRelease ([string]$request.repository) ([string]$request.tag) ([string]$request.title)
        $script:createdTag = [string]$created.tag_name
        Write-ProgressState 'draft-created'
        exit 0
    }
    if ($ListRepositories -or -not [string]::IsNullOrWhiteSpace($ListDraftsFor)) {
        Write-ProgressState 'checking'
        $script:token = Get-GitHubToken
        $account = Invoke-GitHubGet 'https://api.github.com/user'
        $script:catalogAccount = [string]$account.login
        if ($ListRepositories) {
            $script:catalogChoices = @(Get-CatalogChoices '')
        } else {
            $script:catalogChoices = @(Get-CatalogChoices $ListDraftsFor)
        }
        Write-ProgressState 'catalog-ready'
        exit 0
    }
    if ($SignInOnly) {
        Write-ProgressState 'checking'
        $script:token = Get-GitHubToken
        $account = Invoke-GitHubGet 'https://api.github.com/user'
        if (-not $account.login) { throw (L 'Konto konnte nicht bestätigt werden.' 'Не удалось подтвердить аккаунт.' 'Could not confirm account.') }
        Write-ProgressState 'checked' '' 0 0 $account.login
        exit 0
    }
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) { throw (L 'config.json fehlt. Vollständiges Archiv entpacken.' 'Не найден config.json. Распакуй архив целиком.' 'config.json missing. Extract the whole archive.') }
    $script:config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($script:config.UploadMode -eq 'code') { throw (L 'Projektdateien über GitHubSync.exe prüfen und laden.' 'Откройте GitHubSync.exe: файлы проекта загружаются только после просмотра изменений.' 'Use GitHubSync.exe to review and upload project files.') }
    if (-not [string]::IsNullOrWhiteSpace([string]$script:config.SourceDirectory)) {
        $candidate = [string]$script:config.SourceDirectory
        if (-not [System.IO.Path]::IsPathRooted($candidate)) { $candidate = Join-Path $root $candidate }
        $uploadRoot = [System.IO.Path]::GetFullPath($candidate)
    }
    if ($script:config.Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $script:config.Repository -eq 'OWNER/REPOSITORY') {
        throw (L 'Repository als Eigentümer/Name eingeben.' 'Укажи Repository в config.json в формате владелец/репозиторий.' 'Enter repository as owner/name.')
    }
    if ([string]::IsNullOrWhiteSpace([string]$script:config.ReleaseTag)) {
        throw (L 'Tag eines vorhandenen Draft-Releases eingeben.' 'Укажи ReleaseTag из существующего черновика релиза.' 'Enter tag of an existing draft release.')
    }
    foreach ($pair in @(@('MaxAttempts',1,10),@('RetryBaseSeconds',1,300),@('PollSeconds',5,300),@('HttpTimeoutHours',1,24))) {
        $value = [int]$script:config.($pair[0])
        if ($value -lt $pair[1] -or $value -gt $pair[2]) { throw (L "$($pair[0]) außerhalb des erlaubten Bereichs." "$($pair[0]) выходит за разрешённые пределы." "$($pair[0]) is outside the allowed range.") }
    }
    if (-not (Test-Path -LiteralPath $uploadRoot -PathType Container)) { throw (L "Dateiordner nicht gefunden: $uploadRoot" "Не найдена папка с файлами: $uploadRoot" "Source folder not found: $uploadRoot") }
    $script:localAssets = @(Get-LocalAssets)
    $script:progressFileCount = $script:localAssets.Count
    $script:progressBytesTotal = [long](($script:localAssets | Measure-Object -Property Length -Sum).Sum)
    Write-ProgressState 'checking'
    $repoParts = $script:config.Repository.Split('/')
    $script:releasesUri = "https://api.github.com/repos/$($repoParts[0])/$($repoParts[1])/releases"
    $script:releaseId = $null

    $script:mutex = New-Object System.Threading.Mutex($false, 'Local\GitHubReleaseWatchdog.Upload')
    $script:mutexOwned = $script:mutex.WaitOne(0)
    if (-not $script:mutexOwned) { throw (L 'Watchdog läuft bereits. Zweiter Start gestoppt.' 'Вотчдог уже запущен в другом окне. Второй запуск остановлен.' 'Watchdog already running. Second start stopped.') }

    $script:token = Get-GitHubToken
    # Get credentials while the CMD wrapper's OEM codepage is still active.
    $account=Invoke-GitHubGet 'https://api.github.com/user'; $script:catalogAccount=[string]$account.login
    if (-not $CheckOnly -and $script:config.PublishAfterUpload -eq $true -and ([string]::IsNullOrWhiteSpace([string]$script:config.ExpectedAccount) -or $script:config.ExpectedAccount -ine $account.login)) { throw (L 'Konto geändert. Erneut anmelden und bestätigen.' 'Аккаунт изменился. Выполните вход и подтвердите публикацию заново.' 'Account changed. Sign in and confirm publication again.') }
    # Switch to UTF-8 only after GCM has finished; this avoids PS 5.1/GCM's
    # "credential missing protocol field" stdin parsing failure.
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Console]::OutputEncoding
    Write-Host 'GitHubSync' -ForegroundColor White
    Write-Host ("Repository: {0}    Tag: {1}" -f $script:config.Repository, $script:config.ReleaseTag) -ForegroundColor Gray
    Write-Host ((L "Dateien in Warteschlange: {0}. Upload nacheinander." "Файлов в очереди: {0}. Параллельные загрузки отключены." "Files queued: {0}. Sequential upload only.") -f $script:localAssets.Count) -ForegroundColor Gray
    $headers = Get-ApiHeaders
    $release = Get-Release
    if (-not $release.draft) { throw (L 'Release bereits veröffentlicht. Nur Draft-Upload erlaubt.' 'Этот release уже опубликован. Для безопасности загружать разрешено только в черновик.' 'Release already published. Only draft upload allowed.') }
    Show-RemoteStatus $release $script:localAssets

    if ($CheckOnly) {
        foreach ($local in $script:localAssets) {
            if (Confirm-RemoteAsset $release $local) { Confirm-ProgressFile $local }
        }
        $script:releaseAssets=@($release.assets | ForEach-Object { @{id=$_.id;size=$_.size;state=$_.state;name=$_.name} })
        $script:releaseId=$release.id
        $script:projectPlan=@{releaseId=$release.id;tag=$release.tag_name;repository=$script:config.Repository}
        Write-ProgressState 'checked'
        Write-Host (L 'Prüfung abgeschlossen. Keine Dateien hochgeladen.' 'Проверка завершена. Файлы не загружались.' 'Check completed. No files uploaded.') -ForegroundColor Green
        exit 0
    }
    Write-ProgressState 'preparing'

    $waitPid = $script:config.WaitForProcessId
    if ($null -ne $waitPid -and [int]$waitPid -gt 0) {
        $waiting = Get-Process -Id ([int]$waitPid) -ErrorAction SilentlyContinue
        $waitingStart = if ($waiting) { $waiting.StartTime } else { $null }
        while ($waiting -and $waitingStart -and $waiting.StartTime -eq $waitingStart) {
            Write-ProgressState 'waiting' '' 0 0 ((L "Warte auf Prozess PID $waitPid" "Ожидание процесса PID $waitPid" "Waiting for process PID $waitPid"))
            Write-Host ((L "Warte auf PID {0}; kein paralleler Upload." "Жду завершения заданного процесса PID {0}; не запускаю параллельную отправку." "Waiting for PID {0}; no parallel upload.") -f $waitPid) -ForegroundColor Yellow
            Start-Sleep -Seconds ([int]$script:config.PollSeconds)
            $waiting = Get-Process -Id ([int]$waitPid) -ErrorAction SilentlyContinue
        }
    }

    $lastBytes = Get-TotalBytesSent
    $lastTime = Get-Date
    $http = [System.Net.Http.HttpClient]::new()
    $http.Timeout = [TimeSpan]::FromHours([int]$script:config.HttpTimeoutHours)
    $null = $http.DefaultRequestHeaders.TryAddWithoutValidation('Authorization', $headers.Authorization)
    $null = $http.DefaultRequestHeaders.TryAddWithoutValidation('Accept', 'application/vnd.github+json')

    foreach ($local in $script:localAssets) {
        $script:progressCurrentFile = $local.Name
        $script:progressCurrentSize = [long]$local.Length
        $release = Get-Release
        if (-not $release.draft) { throw (L 'Draft wurde während der Arbeit veröffentlicht. Upload gestoppt.' 'Черновик релиза был опубликован во время работы. Загрузка остановлена.' 'Draft published during operation. Upload stopped.') }
        if (Confirm-RemoteAsset $release $local) {
            Confirm-ProgressFile $local
            Write-ProgressState 'uploading' $local.Name ([long]$local.Length) 0
            continue
        }

        $collision = @($release.assets | Where-Object name -eq $local.Name)
        if ($collision.Count -gt 0) { throw (L "Datei $($local.Name) vorhanden. Keine automatische Ersetzung." "На GitHub уже есть asset с именем $($local.Name). Автоматически удалять или заменять его небезопасно." "Asset $($local.Name) exists. No automatic replacement.") }

        $succeeded = $false
        for ($attempt = 1; $attempt -le [int]$script:config.MaxAttempts; $attempt++) {
            $script:progressAttempt = $attempt
            $script:progressRetrySeconds = 0
            Write-ProgressState 'uploading' $local.Name ([long]$local.Length) 0
            $content = [WatchdogUploadContent]::new($local.Path, $script:progressFile, $script:progressFileCount, $script:progressFilesDone, $script:progressBytesDone, $script:progressBytesTotal, $script:progressConfirmed.ToArray(), $attempt)
            $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/octet-stream')
            $response = $null
            try {
                $uploadUri = "https://uploads.github.com/repos/$($script:config.Repository)/releases/$($release.id)/assets?name=$([uri]::EscapeDataString($local.Name))&label=$([uri]::EscapeDataString($local.Name))"
                Write-Host ''
                Write-Host ((L "Upload {0}, Versuch {1}/{2}" "Загружаю {0}, попытка {1}/{2}" "Uploading {0}, attempt {1}/{2}") -f $local.Name, $attempt, $script:config.MaxAttempts) -ForegroundColor White
                $response = $http.PostAsync($uploadUri, $content).GetAwaiter().GetResult()
                [Console]::WriteLine()
                if (-not $response.IsSuccessStatusCode) {
                    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    throw (L "GitHub antwortete mit HTTP $([int]$response.StatusCode)." "GitHub вернул HTTP $([int]$response.StatusCode): $body" "GitHub returned HTTP $([int]$response.StatusCode).")
                }
                $remote = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                if ($remote.state -ne 'uploaded' -or [long]$remote.size -ne $local.Length) { throw (L 'GitHub hat Größe und Status nicht bestätigt.' 'GitHub не подтвердил ожидаемый статус и размер файла.' 'GitHub did not confirm file size and status.') }
                if (-not $remote.id) { throw (L 'Asset-ID fehlt.' 'GitHub не вернул ID файла.' 'GitHub did not return an asset ID.') }
                $script:uploadedAssetIds[$local.Name]=$remote.id
                Write-Host ((L "Fertig: {0} ({1:N0} Bytes)" "Готово: {0} ({1:N0} байт)" "Done: {0} ({1:N0} bytes)") -f $local.Name, $remote.size) -ForegroundColor Green
                $succeeded = $true
                break
            } catch {
                $failure = $_
                [Console]::WriteLine()
                try {
                    $check = Get-Release
                    if (Confirm-RemoteAsset $check $local) {
                        Write-Host (L 'GitHub hat Datei bestätigt; kein doppelter Upload.' 'GitHub принял файл, хотя ответ загрузчика потерялся; дубль не отправляю.' 'GitHub confirmed file; no duplicate upload.') -ForegroundColor Yellow
                        $succeeded = $true
                        break
                    }
                } catch {
                    if ($_.Exception.Message -match '^(Konflikt|Конфликт|Conflict):') { throw }
                }
                if ($attempt -ge [int]$script:config.MaxAttempts) { throw $failure }
                $delay = [Math]::Min(300, [int]$script:config.RetryBaseSeconds * [Math]::Pow(2, $attempt - 1))
                $script:progressRetrySeconds = [int]$delay
                Write-ProgressState 'retrying' $local.Name ([long]$local.Length) 0
                Write-Host ((L "Fehler: {0}" "Сбой: {0}" "Failure: {0}") -f $failure.Exception.Message) -ForegroundColor Yellow
                Write-Host ((L "Neuer Versuch in {0} s; Datei wird von Anfang an gesendet." "Повторю через {0} сек.; файл будет передан с начала." "Retry in {0} s; file transfer starts from the beginning.") -f $delay) -ForegroundColor Yellow
                Start-Sleep -Seconds $delay
            } finally {
                if ($response) { $response.Dispose() }
                if ($content) { $content.Dispose() }
            }
        }
        if (-not $succeeded) { throw (L "Upload fehlgeschlagen: $($local.Name)." "Не удалось загрузить $($local.Name)." "Could not upload $($local.Name).") }
        Confirm-ProgressFile $local
        Write-ProgressState 'uploading' $local.Name ([long]$local.Length) 0
        $now = Get-Date
        $bytesNow = Get-TotalBytesSent
        $elapsed = [Math]::Max([double]1, [double]($now - $lastTime).TotalSeconds)
        $delta = [Math]::Max([long]0, [long]($bytesNow - $lastBytes))
        Write-Host ((L "Gesamter PC-Traffic: {0:N1} MiB, etwa {1:N2} MiB/s." "Общий трафик ПК за интервал: {0:N1} MiB, примерно {1:N2} MiB/s." "Total PC traffic: {0:N1} MiB, approximately {1:N2} MiB/s.") -f ($delta / 1MB), (($delta / 1MB) / $elapsed)) -ForegroundColor DarkCyan
        $lastBytes = $bytesNow
        $lastTime = $now
    }

    $final = Get-Release
    if (-not $final.draft) { throw (L 'Release ist kein Draft mehr. Abschlussprüfung gestoppt.' 'Release больше не является черновиком; финальный отчёт остановлен.' 'Release no longer a draft. Final check stopped.') }
    Show-RemoteStatus $final $script:localAssets
    $allConfirmed = $true
    foreach ($local in $script:localAssets) {
        if (-not (Confirm-RemoteAsset $final $local)) { $allConfirmed = $false }
    }
    if (-not $allConfirmed) { throw (L 'Nicht alle Dateien von GitHub bestätigt.' 'Не все файлы подтверждены GitHub.' 'Not all files confirmed by GitHub.') }
    $script:releaseAssets=@($final.assets | ForEach-Object { @{id=$_.id;size=$_.size;state=$_.state;name=$_.name} })
    $script:projectPlan=@{releaseId=$final.id;tag=$final.tag_name;repository=$script:config.Repository}
    if ($script:config.PublishAfterUpload -eq $true) { Complete-UploaderRelease $final }
    Write-ProgressState 'completed' '' 0 0
    Write-Host $(if ($script:published) { L 'Dateien bestätigt. Release veröffentlicht.' 'Файлы подтверждены. Релиз опубликован.' 'Files confirmed. Release published.' } else { L 'Dateien bestätigt. Entwurf noch nicht veröffentlicht.' 'Файлы подтверждены. Черновик ещё не опубликован.' 'Files confirmed. Draft not yet published.' }) -ForegroundColor Green
} catch {
    $failureMessage=Get-WorkerErrorMessage $_.Exception
    Write-ProgressState 'failed' $script:progressCurrentFile $script:progressCurrentSize 0 $failureMessage
    try {
        @(
            "Time: $(Get-Date -Format o)"
            "Error: $failureMessage"
        ) | Set-Content -LiteralPath (Join-Path $root 'GitHubSync-error.log') -Encoding UTF8
    } catch { }
    Write-Host ((L "FEHLER: {0}" "ОШИБКА: {0}" "ERROR: {0}") -f $failureMessage) -ForegroundColor Red
    exit 1
} finally {
    if ($http) { $http.Dispose() }
    $script:token = $null
    $token = $null
    if ($mutex -and $mutexOwned) { try { $mutex.ReleaseMutex() } catch { } }
    if ($mutex) { $mutex.Dispose() }
    [GC]::Collect()
}
