param([switch]$CheckOnly, [switch]$SignInOnly, [switch]$ListRepositories, [string]$ListDraftsFor='', [string]$CreateDraftRequest='', [string]$CreateProjectRequest='', [string]$UploaderRequest='', [string]$SyncRequest='', [string]$ProgressFile, [string]$Language)
if($SyncRequest) {
    $request=Get-Content -LiteralPath $SyncRequest -Raw -Encoding UTF8 | ConvertFrom-Json
    $entry=@{name='docs/инструкция.txt';length=12;digest=('a'*40);hashKind='git';url='https://api.github.com/repos/example/release-test/git/blobs/fixture';accept='application/vnd.github.raw+json';action='add';localHash='-'}
    $catalog=@{repository=$request.repository;contentKind=$request.contentKind;sourceId='fixture-source';branch='main';prefix='';releaseId=0;entries=@($entry);directory=$request.directory;etag='fixture-etag';fingerprint='fixture-fingerprint'}
    if($request.operation -eq 'preview'){$state='sync-planned'}else{$state='sync-catalog'}
    [IO.File]::WriteAllText($ProgressFile,(@{state=$state;direction='download';syncCatalog=$catalog} | ConvertTo-Json -Depth 10 -Compress),[Text.UTF8Encoding]::new($false));exit 0
}
if($UploaderRequest) {
    $request=Get-Content -LiteralPath $UploaderRequest -Raw -Encoding UTF8 | ConvertFrom-Json
    if($request.operation -eq 'preview') {
        $entries=@($request.paths | ForEach-Object { @{name=$_;target=$_;sha='fixture';action=$(if($_ -eq 'README.md'){'update'}else{'add'});length=20;mode='100644'} })
        $snapshot=@{state='project-planned';account='synthetic-account';plan=@{repository=$request.repository;branch='main';destination=$request.destination;baseCommit='fixture-base';entries=$entries}}
    } elseif($request.operation -eq 'upload') {
        if($CheckOnly -or $request.expectedAccount -ne 'synthetic-account' -or $request.baseCommit -ne 'fixture-base'){throw 'Code upload consent contract'}
        $snapshot=@{state='completed';mode='code';resultUrl="https://github.com/$($request.repository)/tree/main";confirmedFiles=@($request.paths);account='synthetic-account';filesCompleted=@($request.paths).Count;filesTotal=@($request.paths).Count}
    } elseif($request.operation -eq 'publish') {
        if($CheckOnly -or $request.expectedAccount -ne 'synthetic-account'){throw 'Publish consent contract'}
        $snapshot=@{state='completed';mode='release';published=$true;resultUrl="https://github.com/$($request.repository)/releases/tag/fixture";downloads=@(@{name='app.exe';localName='app.exe';url="https://github.com/$($request.repository)/releases/download/fixture/app.exe"});account='synthetic-account'}
    } else { $snapshot=@{state='catalog-ready';account='synthetic-account';choices=@(@{value='main';label='main'})} }
    [IO.File]::WriteAllText($ProgressFile,($snapshot | ConvertTo-Json -Depth 10 -Compress),[Text.UTF8Encoding]::new($false)); exit 0
}
if ($CreateProjectRequest) {
    $request=Get-Content -LiteralPath $CreateProjectRequest -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($request.private -ne $true -or $request.expectedAccount -ne 'synthetic-account') { throw 'Privacy/account contract failed' }
    [IO.File]::WriteAllText($ProgressFile, (@{state='project-created';createdRepository=('synthetic-account/'+$request.name);account='synthetic-account'} | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    exit 0
}
if ($CreateDraftRequest) {
    $request=Get-Content -LiteralPath $CreateDraftRequest -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($request.title -ne 'Тестовый черновик') { throw 'Unicode request lost' }
    [IO.File]::WriteAllText($ProgressFile, (@{state='draft-created';createdTag=$request.tag;account='synthetic-account'} | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    exit 0
}
if ($ListRepositories -or $ListDraftsFor) {
    $choices=@(if ($ListRepositories) { @{value='example/catalog-project';label='example/catalog-project'} } else { @{value='v-catalog-draft';label='Synthetic draft'} })
    [IO.File]::WriteAllText($ProgressFile, (@{state='catalog-ready';choices=$choices;account='synthetic-account'} | ConvertTo-Json -Depth 6 -Compress), [Text.UTF8Encoding]::new($false))
    exit 0
}
$state = if ($CheckOnly) { 'checked' } else { 'failed' }
$message = if ($Language -eq 'ru') { 'Искусственная ошибка.' } elseif ($Language -eq 'en') { 'Synthetic failure.' } else { 'Künstlicher Fehler.' }
if ($SignInOnly) { $state = 'checked'; $message = 'synthetic-account' }
Start-Sleep -Milliseconds 200
# More than a pipe buffer on each stream catches undrained-output deadlocks.
[Console]::Out.WriteLine(('x' * 100000))
[Console]::Error.WriteLine(('y' * 100000))
$snapshot = @{ state=$state; message=$message; totalBytes=0; fileBytes=0; confirmedFiles=@() } | ConvertTo-Json -Compress
[IO.File]::WriteAllText($ProgressFile, $snapshot, [Text.UTF8Encoding]::new($false))
if ($CheckOnly) { exit 0 } else { exit 1 }
