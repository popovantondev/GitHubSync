# Build and verify

Windows x64 with Windows PowerShell 5.1 and .NET Framework 4.8-compatible WPF/compiler. No new framework, NuGet service, administrator elevation or system Git installation required. Never use personal account files for tests.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Get-Runtime.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Run-Checks.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Build-Portable.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Build-SourceArchive.ps1
```

`Get-Runtime` is needed once on a clean checkout. It downloads the official archive pinned in `runtime.lock.json`, checks SHA-256 before extracting, and never overwrites an existing runtime. Downloads stay in ignored `artifacts/dependencies`. Windows PowerShell scripts retain UTF-8 BOM. `Run-Checks -SkipUi` runs backend tests without modal WPF reviews; the full local suite also produces demonstration renders under `artifacts/tests/ui`. No real GitHub writes/authentication in either suite. CI runs backend checks/build, not real acceptance.

Build output is `artifacts/build/GitHubSync.exe`; use the complete portable folder, not this EXE alone. The portable builder creates `artifacts/GitHubSync-<version>-Portable`, ZIP and `.sha256`. It refuses to overwrite an existing portable folder because it may contain settings. Source archives must use `git ls-files`, not the whole project folder.

## Structure

```text
GitHubSync/
  src/                 WPF interface and launcher
  assets/              approved app icon only
  docs/                three-language guides, safe demo images, design/architecture
  tests/               artificial files and mocked GitHub API/auth only
  tools/               build, pinned runtime and publication audit
  .github/             read-only CI and issue forms
  artifacts/           ignored app, ZIP, checksums, test renders and download cache
  runtime/             ignored upstream dependency for local tests
  config.example.json  factory settings, never personal config.json
```

The worker PS1 files and `src/SyncTransfer.cs` remain beside the packaged EXE for compatibility. `WatchdogWindow` and the existing worker entrypoint filename are intentionally retained. Do not rename the worker without updating launcher/tests. `tools/Build-Icons.ps1` builds static and animation resources deterministically from `assets/sync.svg`. Change `VERSION`, assembly metadata, manifest and guide/release notes together. Never include user config/logs, live smoke scripts, previous builds or development conversation notes.

## Before publication

Run `tools/Test-Publication.ps1`, inspect `git diff --cached`, and scan the index with Gitleaks or another secret scanner. `.gitignore` alone is not a security guarantee. Review screenshots, vendor licenses and source availability. A locally built ZIP is not a published GitHub release. Publishing/changing repository visibility requires a separate explicit owner action. Do not add a workflow that uploads releases automatically.
