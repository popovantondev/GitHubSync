$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path $root ('artifacts\tests\transfer-fixes-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture,(Join-Path $fixture 'source\nested'),(Join-Path $fixture 'outside') | Out-Null
$Language='ru'; function L($de,$ru,$en) { if($Language -eq 'de'){$de}elseif($Language -eq 'en'){$en}else{$ru} }
. (Join-Path $root 'Uploader-Operations.ps1')
Import-UploaderSupport
function Assert($condition,$message) { if(-not $condition){throw $message} }
foreach($variant in 0..15) { Assert ([WindowsPathSafety]::IsCloudTag(([uint32]0x9000001AL -bor ([uint32]$variant -shl 12)))) 'All Windows Cloud variants supported' }
foreach($tag in @([uint32]0xA0000003L,[uint32]0xA000000CL,[uint32]0x80000021L,[uint32]0x9001001AL,[uint32]0x12345678)) { Assert (-not [WindowsPathSafety]::IsCloudTag($tag)) 'Links/junctions/unknown tags not allowed as Cloud' }
# Read-only native check against source ancestry, which may be under OneDrive.
[WindowsPathSafety]::AssertNoLinks($root)
$probe=$root; $cloudCount=0
while($probe) { if([WindowsPathSafety]::IsCloudTag([WindowsPathSafety]::ReparseTag($probe))){$cloudCount++};$probe=[IO.Path]::GetDirectoryName($probe) }
[IO.File]::WriteAllBytes((Join-Path $fixture 'source\nested\данные.bin'),[byte[]](0,1,2,255))
$request=@{sourceDirectory=(Join-Path $fixture 'source')}
$file=Get-ProjectLocalFile $request 'nested/данные.bin'
Assert ($file.length -eq 4 -and $file.sha.Length -eq 40) 'Upload accepts ordinary and Cloud ancestors'
[IO.File]::WriteAllText((Join-Path $fixture 'outside\secret.txt'),'artificial non-secret boundary fixture')
$junction=Join-Path $fixture 'source\junction'
New-Item -ItemType Junction -Path $junction -Target (Join-Path $fixture 'outside') | Out-Null
Assert ([WindowsPathSafety]::ReparseTag($junction) -eq [uint32]0xA0000003L) 'Actual NTFS junction recognised'
$failed=$false;try {Get-ProjectLocalFile $request 'junction/secret.txt' | Out-Null}catch{$failed=$true};Assert $failed 'Upload still rejects junction escape'
$failed=$false;try {[SyncTransfer]::SafePath((Join-Path $fixture 'source'),'junction/secret.txt') | Out-Null}catch{$failed=$true};Assert $failed 'Download still rejects junction escape'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /codepage:65001 /target:exe "/out:$fixture\WriteTransportReview.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll (Join-Path $root 'src\GitHubWrite.cs') (Join-Path $PSScriptRoot 'WriteTransportReview.cs')
if($LASTEXITCODE -ne 0){throw 'Write transport compiler failed'}
& (Join-Path $fixture 'WriteTransportReview.exe') (Join-Path $fixture 'http')
if($LASTEXITCODE -ne 0){throw 'Write transport review failed'}
& $compiler /nologo /codepage:65001 /target:exe "/out:$fixture\CloudDownloadReview.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll (Join-Path $root 'src\SyncTransfer.cs') (Join-Path $root 'src\WindowsPathSafety.cs') (Join-Path $PSScriptRoot 'SyncDownloadReview.cs')
if($LASTEXITCODE -ne 0){throw 'Cloud download compiler failed'}
& (Join-Path $fixture 'CloudDownloadReview.exe') (Join-Path $fixture 'cloud-download')
if($LASTEXITCODE -ne 0){throw 'Cloud download review failed'}
# Exercise the actual PS5 error wrapper without invoking a real HTTP mutation.
function Invoke-UploaderTransport($method,$uri,$payload,$seconds) {
    $script:actualDeadline=$seconds
    if($script:syntheticCategory){throw [GitHubWriteException]::new($script:syntheticCategory,403,'ABCD:1234')}
    return '{"sha":"acknowledged"}'
}
$script:config=@{HttpTimeoutHours=7};$script:syntheticCategory=''
$result=Invoke-UploaderWrite 'Post' 'https://api.github.com/repos/example/artificial/git/blobs' @{}
Assert ($result.sha -eq 'acknowledged' -and $script:actualDeadline -eq 25200) 'Code uses configured timeout, not 120 seconds'
foreach($lang in @('de','ru','en')) {
    $Language=$lang;$script:syntheticCategory='permission';$message=''
    try {Invoke-UploaderWrite 'Post' 'https://api.github.com/repos/example/artificial/git/blobs' @{} | Out-Null}catch{$message=$_.Exception.Message}
    Assert ($message.Contains('HTTP 403') -and $message.Contains('ABCD:1234') -and $message.StartsWith((L 'Dateiübertragung' 'Передача файла' 'File transfer'))) 'Localised stage/status without German method wrapper'
}
Write-Host "Transfer regressions passed: Cloud tag family, $cloudCount actual Cloud ancestors, OneDrive upload/download, actual junction escape rejected, large Code transport, three-language diagnostics. Artificial data; no real GitHub writes."
