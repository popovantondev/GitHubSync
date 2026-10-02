$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('GitHubSyncNativeGit-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /codepage:65001 /target:exe "/out:$fixture\GitCodeReview.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $root 'src\WindowsPathSafety.cs') (Join-Path $root 'src\GitCodeTransport.cs') (Join-Path $PSScriptRoot 'GitCodeReview.cs')
if($LASTEXITCODE -ne 0){throw 'Native Code transport compilation failed'}
& (Join-Path $fixture 'GitCodeReview.exe') (Join-Path $root 'runtime\git\cmd\git.exe') (Join-Path $fixture "Unicode ' paths")
if($LASTEXITCODE -ne 0){throw 'Native Code transport tests failed'}
