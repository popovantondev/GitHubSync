# Release checklist

Preparation does not grant permission to publish or change repository visibility.

## Local checks

- [x] Run `Run-Checks.ps1` in Windows PowerShell 5.1. Final committed application source `b4f24a7` passed all 16 suites on 2026-10-02. Offline tests use mocks/local repositories, not real network/authentication.
- [x] Review the source publication set; run `tools/Test-Publication.ps1` and a redacted credential-signature/private-path scan. No findings in the reviewed set; this is not a guarantee of absence of every secret.
- [x] Review DE/RU/EN demonstration renders with artificial data and neutral paths. Three-language synthetic 100–200% renders passed; these are not physical OS-DPI tests.
- [x] Build and independently audit a clean portable folder: 505 files, neutral example configuration, required runtime/notices/help included. Never upload a used installation.
- [x] Verify both release ZIP SHA-256 files; independently extract the portable into a path containing spaces and check packaged native compilation, icon resources and offline help links.
- [ ] Accept a fresh end-user GUI launch/sign-in/help on another Windows PC. Existing fixture/packaged checks do not prove this scenario.
- [x] Make the application source ZIP from reviewed tracked source without `.git`, runtime, user settings or generated builds.
- [x] Preserve notices and accept the bounded technical source-input/provenance review, including independent GCM packaging review. See [runtime review](RUNTIME_SOURCE_REVIEW-1.5.3.md). Publish the complete runtime-source ZIP/checksum alongside the binary; source availability is required, not a legal certificate.

## Owner acceptance before a public release

- [x] Owner selected MIT; existing application license retained. Dependency licenses remain separate.
- [x] Owner authorized the full GitHub release after source completeness review. Target: public `popovantondev/GitHubSync`; MIT. Creation/publication is separately confirmed by API read-back, not implied by this checklist.
- [x] Test real writes only in the approved existing private TEST repository, using artificial files. Earlier release/draft/publication safeguards passed on 2026-10-02. Version 1.5.3 additionally passed a 61 MiB native Code upload, one confirmed commit, preservation of existing paths, no-op and hash-matched download; see [verification](VERIFICATION-1.5.3.md). No real user uploads were used.
- Excluded by the owner's acceptance scope: a second Windows PC, repeat real browser login/2FA and actual OS DPI changes. These are **unverified**, not passed. Synthetic 100–200% renders do not substitute for these tests.
- [x] Re-run checks against committed application source and independently audited release ZIP; review [verification notes](VERIFICATION-1.5.3.md). Evidence and archive hashes are recorded in the private release-ready folder's `ACCEPTANCE-STATUS.txt`.
- [ ] After any subsequent source/documentation change, review the exact final diff, rebuild affected archives without overwriting the prior candidate, and recheck publication contents/hashes. The earlier ZIPs remain snapshots of `b4f24a7`, not of later checklist edits.
- [x] Owner authorized preparing and publishing the full release on 2026-10-03 after closing source review. Do not commit runtime/builds or publish credentials.
- [x] Documentation distinguishes application source, runtime source material and portable binary. The EXE is unsigned; no signing, antivirus or absolute secret-absence guarantee is claimed.

CI only checks/builds. It does not publish releases, authenticate a user, deploy a documentation site or create repositories.
