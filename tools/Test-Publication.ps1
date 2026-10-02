param([string]$PackagePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$findings = [Collections.Generic.List[string]]::new()
if ($PackagePath) {
    $scanRoot = [IO.Path]::GetFullPath($PackagePath)
    $paths = @(Get-ChildItem -LiteralPath $scanRoot -File -Recurse | ForEach-Object { $_.FullName.Substring($scanRoot.TrimEnd('\').Length + 1).Replace('\','/') })
    $config = Get-Content -LiteralPath (Join-Path $scanRoot 'config.json') -Raw | ConvertFrom-Json
    $example = Get-Content -LiteralPath (Join-Path $root 'config.example.json') -Raw | ConvertFrom-Json
    if ($config.Repository -ne 'OWNER/REPOSITORY' -or $config.Files.Count -ne 0 -or $config.SourceDirectory -ne 'upload' -or ($config | ConvertTo-Json -Compress) -ne ($example | ConvertTo-Json -Compress)) { $findings.Add('Package configuration differs from clean example.') }
    foreach ($required in @('GitHubSync.exe','Release-UploadWatchdog.ps1','Uploader-Operations.ps1','Sync-Operations.ps1','src/SyncTransfer.cs','src/WindowsPathSafety.cs','src/GitHubWrite.cs','src/GitCodeTransport.cs','LICENSE','THIRD_PARTY.md','runtime-sources.lock.json','third-party-notices/gcm/MIT.txt','third-party-notices/gcm/README.md','third-party-notices/gcm/packages.lock.json','runtime/git/LICENSE.txt','runtime/git/mingw64/doc/git-credential-manager/LICENSE')) {
        if ($paths -notcontains $required) { $findings.Add("Missing package file: $required") }
    }
} else {
    $scanRoot = $root
    if (Test-Path -LiteralPath (Join-Path $root '.git')) {
        $listing = & git -C $root ls-files --cached --others --exclude-standard -z
        if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate Git publication set.' }
        $paths = @(($listing -join "`n").Split([char]0) | Where-Object { $_ })
    } else {
        $paths = @(Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\','/') } | Where-Object { $_ -notmatch '^(artifacts|runtime|\.git)/' })
    }
}
$secretPatterns = @('-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----','\bgh[pousr]_[A-Za-z0-9]{30,}\b','\bgithub_pat_[A-Za-z0-9_]{30,}\b','\b(?:AKIA|ASIA)[A-Z0-9]{16}\b','\bxox[baprs]-[A-Za-z0-9-]{20,}\b','\bsk-(?:proj-)?[A-Za-z0-9_-]{32,}\b','https?://[^\s/:]+:[^\s/@]+@')
foreach ($relative in $paths) {
    if ($relative -match '(^|/)(ui-settings\.json|[^/]*\.log|\.env(?:\..*)?|\.git-credentials|credentials\.json|id_rsa|id_ed25519)$' -or $relative -match '(Request|CreateDraft|CreateProject)[^/]*\.json$') { $findings.Add("Private/local file: $relative") }
    if (-not $PackagePath -and ($relative -match '^(artifacts|runtime|release|upload)/' -or $relative -match '\.(exe|zip|pfx|p12|key|pem)$' -or $relative -match '(^|/)config\.json$')) { $findings.Add("Generated/private source entry: $relative") }
    $file = Join-Path $scanRoot $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { $findings.Add("Missing indexed file: $relative"); continue }
    if ($relative -match '\.(cs|ps1|cmd|md|json|html|css|yml|yaml|txt|toml)$' -or [IO.Path]::GetExtension($relative) -eq '') {
        $content = [IO.File]::ReadAllText($file,[Text.Encoding]::UTF8)
        foreach ($pattern in $secretPatterns) { if ($content -match $pattern) { $findings.Add("Possible secret (value redacted): $relative"); break } }
        if ($relative -notmatch '^runtime/' -and $content -match '[A-Za-z]:\\Users\\(?!Public\\|Example\\|Demo\\)') { $findings.Add("Personal Windows path: $relative") }
    }
    if ($relative.EndsWith('.ps1')) {
        $bytes = [IO.File]::ReadAllBytes($file)
        if ($bytes.Length -lt 3 -or $bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191) { $findings.Add("Missing PS5 UTF8 BOM: $relative") }
        $tokens=$null; $errors=$null
        [Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$errors) | Out-Null
        if ($errors.Count) { $findings.Add("PowerShell syntax: $relative") }
    }
}
foreach ($lang in @('de','ru','en')) {
    if (-not (Test-Path -LiteralPath (Join-Path $scanRoot "docs\Guide-$lang.html"))) { $findings.Add("Missing HTML guide: $lang") }
}
foreach ($relative in @($paths | Where-Object { $_ -notmatch '^runtime/' -and $_ -match '\.(html|md)$' })) {
    $document = Join-Path $scanRoot $relative
    $content = [IO.File]::ReadAllText($document)
    $pattern = if ($relative.EndsWith('.html')) { '(?:href|src)="([^"]+)"' } else { '\]\(([^)\s]+)\)' }
    foreach ($match in [regex]::Matches($content,$pattern)) {
        $link = [Uri]::UnescapeDataString($match.Groups[1].Value.Split('#')[0])
        if ($link -and $link -notmatch '^[A-Za-z][A-Za-z0-9+.-]*:' -and -not (Test-Path -LiteralPath (Join-Path (Split-Path $document) $link))) { $findings.Add("Broken local documentation link: $relative -> $link") }
    }
}
if ($findings.Count) { throw ($findings | Select-Object -Unique | Out-String) }
Write-Host "Publication checks passed: $($paths.Count) files, clean config, no detected credential signatures/personal paths, PS5 syntax/BOM, offline help. This is not a guarantee of absence of all secrets."
