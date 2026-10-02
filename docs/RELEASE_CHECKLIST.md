# Release checklist

Preparation does not grant permission to publish or change repository visibility.

## Local checks

- [x] Run `Run-Checks.ps1` in Windows PowerShell 5.1. No real network/auth in tests. Latest candidate run: 2026-10-02, exit 0; final committed-source rerun remains required below.
- [ ] Review `git diff --cached`; run `tools/Test-Publication.ps1` and a redacted secret scan against the publication set.
- [ ] Check DE/RU/EN screenshots for private paths, names and unintended secrets.
- [ ] Build one clean portable folder with `tools/Build-Portable.ps1`; audit it separately. Never upload a used installation.
- [ ] Verify ZIP SHA-256; test extraction/start/help from a path containing spaces.
- [ ] Make source ZIP with `tools/Build-SourceArchive.ps1`, using only reviewed tracked files. `-IncludeUncommitted` is explicit for an uncommitted local candidate.
- [ ] Preserve application and upstream licenses/notices. Review GPL/LGPL corresponding-source availability for all bundled runtime components before public binary redistribution.

## Owner acceptance before a public release

- [ ] Decide application licensing explicitly. This preparation retains the existing MIT license; it does not copy the more restrictive HotspotControl license.
- [ ] Confirm the target repository/visibility and add its issue/release links. No target repository is created here.
- [x] Test real writes only in the approved existing private TEST repository, using artificial files. Code upload/download, draft-only, separate publication, upload-and-publish and intentional conflict with no publication passed on 2026-10-02. Large live transfers remain unverified; see verification notes.
- Excluded by the owner's acceptance scope: a second Windows PC, repeat real browser login/2FA and actual OS DPI changes. These are **unverified**, not passed. Synthetic 100–200% renders do not substitute for these tests.
- [ ] Re-run checks against the final committed source and final release ZIP, review [verification notes](VERIFICATION-1.5.1.md), then upload the portable ZIP and checksum as release attachments. Do not commit runtime/builds or publish credentials.
- [ ] Keep source and binary links distinct; do not advertise unsigned EXEs as signed or claim virus/secret absence guarantees.

CI only checks/builds. It does not publish releases, authenticate a user, deploy a documentation site or create repositories.
