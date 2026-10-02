$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Release-UploadWatchdog.ps1'),[ref]$tokens,[ref]$errors)
foreach ($name in @('L','Find-RemoteAsset','Confirm-RemoteAsset')) {
    $node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name}.GetNewClosure(),$true)
    . ([scriptblock]::Create($node.Extent.Text))
}
$Language='en'; $script:uploadedAssetIds=@{}; $script:localDigests=@{}
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('WatchdogAssetFixture-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$path=Join-Path $fixture 'notes ; original.html'
[IO.File]::WriteAllText($path,'Synthetic HTML content',[Text.UTF8Encoding]::new($false))
$local=@{Name='notes ; original.html';Path=$path;Length=(Get-Item -LiteralPath $path).Length}
$script:localAssets=@($local)
$hash='sha256:'+(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
$remote=@{id=55;name='notes.original.html';state='uploaded';size=$local.Length;digest=$hash}
$release=@{assets=@($remote)}
if (-not (Confirm-RemoteAsset $release $local)) { throw 'Renamed legacy asset not confirmed using SHA256' }
$remote.digest='sha256:wrong'
if (Confirm-RemoteAsset $release $local) { throw 'Equal size with wrong content accepted' }
$remote.digest=$hash; $remote.label=$local.Name
if (-not (Confirm-RemoteAsset $release $local)) { throw 'Original label ignored' }
$remote.digest='sha256:wrong'; $failed=$false
try { Confirm-RemoteAsset $release $local | Out-Null } catch { $failed=$true }
if(-not $failed){throw 'Exact-label equal-size content conflict accepted'}
$remote.digest=$hash
$remote.Remove('label'); $script:uploadedAssetIds[$local.Name]=55
if (-not (Confirm-RemoteAsset $release $local)) { throw 'Upload response ID ignored' }
$script:uploadedAssetIds.Clear(); $release.assets=@($remote,@{id=56;name='duplicate.html';state='uploaded';size=$local.Length;digest=$hash})
$failed=$false
try { Confirm-RemoteAsset $release $local | Out-Null } catch { $failed=$true }
if (-not $failed) { throw 'Ambiguous content match accepted' }
Write-Host 'Asset checks passed: original label, response ID, legacy renamed SHA256, wrong digest, ambiguous assets. No network.'
