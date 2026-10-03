param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts\documentation-site-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Output exists; overwrite refused.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'assets') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'assets\sync.png') -Destination (Join-Path $OutputDirectory 'assets\sync.png')
Copy-Item -LiteralPath (Join-Path $root 'docs\guide.css') -Destination $OutputDirectory
Get-ChildItem -LiteralPath (Join-Path $root 'docs') -File -Filter 'Ui-*.png' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $OutputDirectory }
foreach ($name in @('index.html','Guide-de.html','Guide-ru.html','Guide-en.html')) {
    $text = [IO.File]::ReadAllText((Join-Path $root ('docs\' + $name)))
    $text = $text.Replace('../assets/', 'assets/').Replace('href="../README.md"', 'href="https://github.com/popovantondev/GitHubSync#readme"')
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $name), $text, (New-Object Text.UTF8Encoding($false)))
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory '.nojekyll'), '')
Write-Output ('Documentation site prepared locally: ' + $OutputDirectory)
Write-Output 'No upload, repository creation or deployment performed.'
