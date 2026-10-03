param([Parameter(Mandatory=$true)][string]$SourceDirectory,[string]$ReportPath)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$SourceDirectory=[IO.Path]::GetFullPath($SourceDirectory)
if($SourceDirectory -match '["\r\n]'){throw 'Unsafe source directory'}
$catalog=Get-Content (Join-Path $root 'runtime-sources.lock.json') -Raw | ConvertFrom-Json
$tar=Join-Path $env:WINDIR 'System32\tar.exe'
$rows=@()
function Get-MemberHash([string]$Archive,[string]$Member,[string]$Algorithm){
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$tar
    $start.Arguments='-xOf "'+$Archive+'" "'+$Member+'"'
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $process=New-Object Diagnostics.Process; $process.StartInfo=$start
    $hash=[Security.Cryptography.HashAlgorithm]::Create($Algorithm)
    try{
        if(-not $process.Start()){throw 'Cannot start archive reader'}
        $errorTask=$process.StandardError.ReadToEndAsync()
        $value=$hash.ComputeHash($process.StandardOutput.BaseStream)
        $process.WaitForExit()
        if($process.ExitCode -ne 0){throw ('Archive reader failed for '+$Member)}
        return ([BitConverter]::ToString($value).Replace('-','').ToLowerInvariant())
    }finally{$hash.Dispose(); $process.Dispose()}
}
foreach($entry in $catalog.archives){
    if($entry.archive -notmatch '^[A-Za-z0-9_.~+-]+\.tar\.(gz|zst)$'){throw 'Unsafe archive name'}
    $path=Join-Path $SourceDirectory $entry.archive
    if((Get-Item -LiteralPath $path).Length -ne [long]$entry.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256){throw ('Lock mismatch: '+$entry.archive)}
    $members=@(& $tar -tf $path)
    if($LASTEXITCODE -ne 0){throw ('Cannot list: '+$entry.archive)}
    foreach($member in $members){if($member -match '["\r\n]|(^/|^[A-Za-z]:|(^|/)\.\.(/|$)|\\)'){throw 'Unsafe archive member'}}
    $infoMember=@($members | Where-Object {$_ -match '(^|/)\.SRCINFO$'})
    $recipeMember=@($members | Where-Object {$_ -match '(^|/)PKGBUILD$'})
    if($infoMember.Count -ne 1 -or $recipeMember.Count -ne 1){
        $rows+= [pscustomobject]@{archive=$entry.archive;status='manual-review';reason='No unique package recipe/SRCINFO; upstream source only';inputs=@()}
        continue
    }
    $info=@(& $tar -xOf $path $infoMember[0])
    if($LASTEXITCODE -ne 0){throw 'Cannot read SRCINFO'}
    $recipe=@(& $tar -xOf $path $recipeMember[0])
    if($LASTEXITCODE -ne 0){throw 'Cannot read recipe'}
    $prefix=$infoMember[0].Substring(0,$infoMember[0].LastIndexOf('/')+1)
    $sources=@($info | Where-Object {$_ -match '^\s*source(?:_[A-Za-z0-9_]+)?\s*='} | ForEach-Object {($_ -split '=',2)[1].Trim()})
    $checksums=@($info | Where-Object {$_ -match '^\s*sha256sums\s*='} | ForEach-Object {($_ -split '=',2)[1].Trim()})
    $algorithm='SHA256'
    if($checksums.Count -ne $sources.Count){
        $checksums=@($info | Where-Object {$_ -match '^\s*sha512sums\s*='} | ForEach-Object {($_ -split '=',2)[1].Trim()})
        $algorithm='SHA512'
    }
    $index=0
    $inputs=@()
    foreach($source in $sources){
        if($source.Contains('::')){$local=($source -split '::',2)[0]}else{$local=[IO.Path]::GetFileName(($source -split '#',2)[0])}
        if($local -notmatch '^[^/\\"\r\n]+$' -or $local -eq '..'){throw ('Unsafe source label: '+$entry.archive)}
        $member=$prefix+$local
        $present=($members -contains $member) -or ($members -contains ($member+'/'))
        $vcs=($source -match 'git\+|::git://')
        $checksumStatus='manual-review'
        if($present -and -not $vcs -and $checksums.Count -eq $sources.Count){
            if($checksums[$index] -eq 'SKIP'){$checksumStatus='recipe-skip'}else{
                $actual=Get-MemberHash $path $member $algorithm
                if($actual -ne $checksums[$index]){throw ('Recipe checksum mismatch: '+$entry.archive+' -> '+$member)}
                $checksumStatus='verified-'+$algorithm
            }
        }
        $inputs+=[pscustomobject]@{source=$source;member=$member;present=$present;vcs=$vcs;checksumStatus=$checksumStatus}
        $index++
    }
    $missing=@($inputs | Where-Object {-not $_.present})
    $rows+=[pscustomobject]@{archive=$entry.archive;status=$(if($missing.Count){'missing-input'}else{'inputs-present'});packages=$entry.packages;version=@($info | Where-Object {$_ -match '^\s*(pkgver|pkgrel)\s*='});architectures=@($recipe | Where-Object {$_ -match '^mingw_arch='});inputs=$inputs}
}
$report=[pscustomobject]@{schemaVersion=1;scope='Static archive/lock/recipe input presence only; no recipes executed, no binary equivalence or legal certification';rows=$rows}
$json=$report | ConvertTo-Json -Depth 10
if($ReportPath){
    if(Test-Path -LiteralPath $ReportPath){throw 'Report exists; overwrite refused'}
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath),$json,(New-Object Text.UTF8Encoding($false)))
    Write-Host ('Reviewed '+$rows.Count+' archives; '+@($rows | Where-Object {$_.status -eq 'inputs-present'}).Count+' complete input-presence lists; '+@($rows | Where-Object {$_.status -eq 'manual-review'}).Count+' manual review. Report: '+$ReportPath)
}else{$json}
if(@($rows | Where-Object {$_.status -eq 'missing-input'}).Count){throw 'Missing recipe inputs: review report'}
