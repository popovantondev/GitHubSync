$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('GitHubSyncNativeGit-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /codepage:65001 /target:exe "/out:$fixture\GitCodeReview.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $root 'src\WindowsPathSafety.cs') (Join-Path $root 'src\GitCodeTransport.cs') (Join-Path $PSScriptRoot 'GitCodeReview.cs')
if($LASTEXITCODE -ne 0){throw 'Native Code transport compilation failed'}
& (Join-Path $fixture 'GitCodeReview.exe') (Join-Path $root 'runtime\git\cmd\git.exe') (Join-Path $fixture "Unicode ' paths")
if($LASTEXITCODE -ne 0){throw 'Native Code transport tests failed'}
& $compiler /nologo /codepage:65001 /target:winexe "/out:$fixture\GitCodeNoConsoleReview.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $root 'src\WindowsPathSafety.cs') (Join-Path $root 'src\GitCodeTransport.cs') (Join-Path $PSScriptRoot 'GitCodeNoConsoleReview.cs')
if($LASTEXITCODE -ne 0){throw 'No-console transport compilation failed'}
$result=Join-Path $fixture 'no-console-result.txt'
$arguments=@((Join-Path $root 'runtime\git\cmd\git.exe'),$fixture,$result) | ForEach-Object {'"'+$_+'"'}
$process=Start-Process -FilePath (Join-Path $fixture 'GitCodeNoConsoleReview.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(30000)){$process.Kill();throw 'No-console transport test timed out'}
if($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result)){throw 'No-console transport test failed'}
$message=Get-Content -LiteralPath $result -Raw
if($message -notlike 'PASS:*'){throw $message}
Write-Host $message
