param([Parameter(Mandatory=$true)][string]$SourceDirectory,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$version=(Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid VERSION.'}
$catalogPath=Join-Path $root 'runtime-sources.lock.json'
$catalog=Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$sourceRoot=[IO.Path]::GetFullPath($SourceDirectory)
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root 'artifacts'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(-not(Test-Path -LiteralPath $OutputDirectory)){New-Item -ItemType Directory -Path $OutputDirectory | Out-Null}
$output=Join-Path $OutputDirectory "GitHubSync-$version-runtime-sources.zip"
if(Test-Path -LiteralPath $output){throw 'Runtime-source output exists; do not overwrite a reviewed artifact.'}
foreach($entry in $catalog.archives){
    if($entry.archive -notmatch '^[a-zA-Z0-9_.~+-]+\.tar\.(gz|zst)$'){throw 'Unsafe source archive name.'}
    $path=Join-Path $sourceRoot $entry.archive
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw ('Missing source archive: '+$entry.archive)}
    if((Get-Item -LiteralPath $path).Length -ne [long]$entry.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256){throw ('Source archive does not match lock: '+$entry.archive)}
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::Open($output,[IO.Compression.ZipArchiveMode]::Create)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$catalogPath,'runtime-sources.lock.json',[IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    foreach($entry in $catalog.archives){
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $sourceRoot $entry.archive),('upstream/'+$entry.archive),[IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    }
    $readme=$zip.CreateEntry('README.txt')
    $writer=New-Object IO.StreamWriter($readme.Open(),(New-Object Text.UTF8Encoding($false)))
    try {
        $writer.WriteLine('GitHubSync '+$version+' - third-party runtime source material')
        $writer.WriteLine('This is a separate source-material candidate, not the application source ZIP or an executable.')
        $writer.WriteLine('The lock records exact versions, upstream retrieval URLs and SHA-256 of each archived file.')
        $writer.WriteLine('Original source archives include upstream licenses, PKGBUILD recipes and patches; some contain Git repositories.')
        $writer.WriteLine('No packaging recipe was executed during collection. Extract only into a separate trusted build environment.')
        $writer.WriteLine('Supplementary GCM dependency notices are collected separately in third-party-notices/gcm and included in the portable package.')
        $writer.WriteLine('Those notices cover the inspected dependency inventory; they do not certify licensing compliance or complete source correspondence.')
        $writer.WriteLine('Complete runtime source correspondence and public availability remain review gates.')
        $writer.WriteLine('If distributing binaries, supply the reviewed required source material alongside them; do not substitute only this application''s source ZIP.')
        $writer.WriteLine('This preparation makes no written source offer on the owner''s behalf.')
    } finally {$writer.Dispose()}
} finally {$zip.Dispose()}
$sha=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($output+'.sha256') -Value ($sha+'  '+[IO.Path]::GetFileName($output)) -Encoding ASCII
Write-Host ('Runtime-source candidate: '+$output+' ('+$catalog.archives.Count+' original archives, all lock hashes verified).')
