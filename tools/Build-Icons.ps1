$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output=Join-Path $root 'artifacts\icon-build'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$references=@('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll') | ForEach-Object {"/reference:$_"}
$references+=@('System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll') | ForEach-Object {"/reference:$(Join-Path $framework $_)"}
& $compiler /nologo /codepage:65001 /target:exe "/out:$output\GenerateIcons.exe" $references (Join-Path $PSScriptRoot 'GenerateIcons.cs')
if($LASTEXITCODE -ne 0){throw 'Icon compiler failed'}
& (Join-Path $output 'GenerateIcons.exe') (Join-Path $root 'assets\sync.svg') (Join-Path $root 'assets')
if($LASTEXITCODE -ne 0){throw 'Icon generation failed'}
