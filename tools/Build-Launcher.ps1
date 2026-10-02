$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build-Icons.ps1')
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid VERSION.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework compiler required; nothing is installed automatically.' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$output = Join-Path $root 'artifacts\build'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$exe = Join-Path $output 'GitHubSync.exe'
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll') | ForEach-Object { "/reference:$_" }
$references += @('System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { "/reference:$(Join-Path $framework $_)" }
$frames=1..12 | ForEach-Object { "/resource:$(Join-Path $root ('assets\sync-frame-'+$_+'.ico')),Sync.Frame$_" }
& $compiler /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ "/out:$exe" "/win32icon:$(Join-Path $root 'assets\sync.ico')" "/resource:$(Join-Path $root 'assets\sync.png'),Watchdog.Icon" "/resource:$(Join-Path $root 'assets\sync.ico'),Watchdog.IconIco" "/win32manifest:$(Join-Path $root 'app.manifest')" $frames $references (Join-Path $root 'src\LauncherWpf.cs') (Join-Path $root 'src\UploaderWpf.cs') (Join-Path $root 'src\SyncWpf.cs') (Join-Path $root 'src\WindowsPathSafety.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
$info = (Get-Item -LiteralPath $exe).VersionInfo
if ($info.FileVersion -ne "$version.0" -or $info.ProductName -ne 'GitHubSync') { throw 'VERSION/source metadata mismatch.' }
Write-Host "Built GitHubSync ${version}: $exe"
