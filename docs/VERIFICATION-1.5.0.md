# Verification of GitHubSync 1.5.0

Verification uses artificial files, isolated configuration and mocked authentication/GitHub endpoints. No existing user uploads are used and no production release is published.

## Automated checks

Local run on 2026-10-01: all 12 backend/package suites passed. The synthetic download runner passed 78 assertions. Actual WPF testing passed 483 assertions, including localized exit cancellation, passive change notifications, atomic resume from another project/mode, tray close/reopen and animated icon frames. The final 6000-file stress fixture switched mode in 6 ms; the largest observed UI timer gap was 301 ms in that run. No real GitHub request, authentication, write or publication was performed in these suites.

A clean portable archive was extracted into a separate temporary directory: WPF first-start initialization/render, empty repository/selection, product/version/icon, both directions, sign-in controls, all worker files, bundled Git and offline HTML help were checked. The packaged download core compiles in Windows PowerShell 5.1. ZIP SHA-256 and factory configuration are checked separately; a used test directory is never re-zipped for distribution.

- Original Code upload, release verification/publication, repository/draft creation, read-only guards, account binding, credential protocol, Int64 progress and publication-audit regression suites.
- Download core: synthetic HTTP full response, 206 byte resume, pause checkpoint, 200 restart fallback, changed ETag/wrong Content-Range/416 refusal, corrupt-partial recovery, hash mismatch/retry, private metadata, CDN authorization stripping, expired-token public fallback, unsafe redirects, Unicode/binary/empty files, replacement backups, local changes after consent, path/case/file-folder guards and paths longer than 260 characters. The long-path bridge also ran inside Windows PowerShell 5.1.
- Read-only catalogs: default branch, recursive/prefix paths, read-without-push permission, unsupported submodules, ETag no-change checks, release names/IDs/digests and PowerShell-to-C# serialization.
- Actual WPF tests and demonstration renders in Deutsch/Русский/English at 100%, 125%, 150%, 200%; large-list and mode-switch responsiveness are measured, not inferred from screenshots.

## Limits

Rendered DPI scales are not physical Windows display-setting changes. Live SMS/Google/private-account login, protected-branch server enforcement, real internet interruption and a second computer require separate acceptance. Existing safeguards are tested with mocks. No real GitHub write is authorized by running the automated suite. Source/portable audits detect common secret patterns, not every possible sensitive value. Third-party corresponding-source obligations remain a distributor review before public binary distribution.
