$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('WatchdogUiReview-' + [guid]::NewGuid().ToString('N'))
$output = Join-Path $root 'artifacts\tests\ui'
New-Item -ItemType Directory -Path $fixture, (Join-Path $fixture 'upload'), (Join-Path $fixture 'empty'), $output -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $fixture 'one'), (Join-Path $fixture 'many') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $fixture 'code\docs'), (Join-Path $fixture 'code\.git') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $fixture 'large\nested') -Force | Out-Null
1..6000 | ForEach-Object { [IO.File]::WriteAllText((Join-Path $fixture ('large\nested\file-{0:00000}.txt' -f $_)), 'artificial stress fixture') }
[IO.File]::WriteAllText((Join-Path $fixture 'code\README.md'),'synthetic project')
[IO.File]::WriteAllText((Join-Path $fixture 'code\docs\инструкция.txt'),'synthetic Unicode')
[IO.File]::WriteAllBytes((Join-Path $fixture 'code\app.bin'),[byte[]](0,1,2,255))
[IO.File]::WriteAllText((Join-Path $fixture 'code\.env'),'synthetic-only-not-a-real-secret')
[IO.File]::WriteAllText((Join-Path $fixture 'code\.git\config'),'excluded synthetic file')
[IO.File]::WriteAllText((Join-Path $fixture 'one\one-file.txt'), 'Synthetic content')
1..30 | ForEach-Object { [IO.File]::WriteAllText((Join-Path $fixture ('many\file-{0:00}-long-layout-test-name.txt' -f $_)), 'Synthetic content') }
$names = @('app.exe','README-with-a-long-filename-for-layout-review.md','changelog.txt')
foreach ($name in $names) { [IO.File]::WriteAllText((Join-Path (Join-Path $fixture 'upload') $name), 'Synthetic test content', [Text.UTF8Encoding]::new($false)) }
$config = Get-Content -LiteralPath (Join-Path $root 'config.example.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$config.PSObject.Properties.Remove('UploadMode')
$config.PSObject.Properties.Remove('PublishAfterUpload')
$config.Repository = 'example/release-test'
$config.Files = $names
$config.SourceDirectory = 'upload'
[IO.File]::WriteAllText((Join-Path $fixture 'config.json'), ($config | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $fixture 'Release-UploadWatchdog.ps1'), [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WorkerFixture.ps1'), [Text.Encoding]::UTF8), [Text.UTF8Encoding]::new($true))
[IO.File]::WriteAllText((Join-Path $fixture 'Sync-Operations.ps1'),'# Synthetic fixture marker',[Text.UTF8Encoding]::new($true))
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runner = Join-Path $fixture 'UiReview.exe'
$launcher = Join-Path $output 'GitHubSync.exe'
& $compiler /nologo /target:exe /codepage:65001 "/out:$runner" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/reference:$framework\System.Xaml.dll" "/reference:$framework\WPF\WindowsBase.dll" "/reference:$framework\WPF\PresentationCore.dll" "/reference:$framework\WPF\PresentationFramework.dll" (Join-Path $PSScriptRoot 'UiReview.cs')
if ($LASTEXITCODE -ne 0) { throw 'Could not compile the isolated UI review.' }
$references = @('/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll', "/reference:$framework\System.Xaml.dll", "/reference:$framework\WPF\WindowsBase.dll", "/reference:$framework\WPF\PresentationCore.dll", "/reference:$framework\WPF\PresentationFramework.dll")
$frames=1..12 | ForEach-Object { "/resource:$(Join-Path $root ('assets\sync-frame-'+$_+'.ico')),Sync.Frame$_" }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$launcher" "/win32icon:$(Join-Path $root 'assets\sync.ico')" "/resource:$(Join-Path $root 'assets\sync.png'),Watchdog.Icon" "/resource:$(Join-Path $root 'assets\sync.ico'),Watchdog.IconIco" "/win32manifest:$(Join-Path $root 'app.manifest')" $frames $references (Join-Path $root 'src\LauncherWpf.cs') (Join-Path $root 'src\UploaderWpf.cs') (Join-Path $root 'src\SyncWpf.cs') (Join-Path $root 'src\WindowsPathSafety.cs')
if ($LASTEXITCODE -ne 0) { throw 'Could not compile the updated launcher for UI review.' }
& $runner $launcher $output (Join-Path $fixture 'empty')
if ($LASTEXITCODE -ne 0) { throw 'UI review failed.' }
Write-Host "UI images: $output"
Write-Host "Isolated synthetic fixture: $fixture"
