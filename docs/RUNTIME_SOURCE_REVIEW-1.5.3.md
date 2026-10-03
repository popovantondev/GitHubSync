# Runtime source-material review — 1.5.3

Technical inspection on 2026-10-03, not a legal compliance certificate or a reproducible-build claim. The application remains MIT; each runtime component retains its own license. Do not treat an application-source ZIP as the runtime sources.

## Evidence collected

- The official unmodified MinGit archive is pinned by SHA-256 in `runtime.lock.json`. Its 66-line inventory (64 unique package/version pairs) exactly matches `build-extra` commit `a762e4274c28611f8692fb01b894627326d04b32`, `versions/package-versions-2.53.0.3-MinGit.txt`.
- All 55 previously collected archives match the sizes/hashes in the original catalog. All 54 package source archives contain PKGBUILD and SRCINFO, and all 482 declared inputs are present. These include upstream source archives, build files, patches and three Git object repositories.
- Of those inputs, 404 ordinary files match the recipe SHA-256. The 75 recipe-SKIP entries are signature files (`.sig`/`.asc`); their signatures were not independently authenticated. Three VCS inputs are reviewed separately, not reported as ordinary file-hash matches.
- Complete Git object verification (`git fsck --full --no-reflogs`) passed for the included winpthreads, Cygwin/MSYS2 base and rebase repositories. Requested source commits resolve to `3fedac28018c447ccdd9519c9d556340dfa1c87e`, `c6680700f8d37effd928698fa818ffc4a927df42` and `4b9c18ba098855504dac71cdfcf24e892750e89d` respectively. All declared MSYS2 patches are present and hash-checked. The separate `msys2-runtime.commit` value is passed as version metadata by its recipe, not used as a missing checkout revision.
- Mingw64 is explicitly supported in the gettext/winpthreads recipes. SRCINFO generated for ucrt64 does not, by itself, indicate an incorrect source architecture.
- The complete pinned `build-extra` archive is additionally collected, retaining MinGit release/file-list/source-collection scripts and upstream LICENSE. SHA-256 and size are recorded in `runtime-sources.lock.json`; this is a retrieval digest, not a signature validation.
- GCM upstream source commit `5fa7116896c82164996a609accd1c5ad90fe730a` contains source, build project files, LICENSE and NOTICE. GCM is MIT, not a GPL component. Its original runtime LICENSE/NOTICE and supplementary dependency notices are retained. Exact MinGit GCM packaging provenance remains separately documented, rather than represented as an available package-source recipe.
- The supplied executables report Git build commit `f8165afd89b0c190677a093f20894f5fce12f97a` (matching the collected Git package recipe) and GCM `2.7.3+5fa7116896c82164996a609accd1c5ad90fe730a` (matching the collected upstream source). These runtime version reports are supporting provenance evidence, not bit-identical rebuild proof.

`tools/Test-RuntimeSourceInputs.ps1` reproduces the static lock/recipe-input/hash inspection with a provided source cache. It never sources or executes PKGBUILD. VCS object verification is separate. Local JSON reports are private generated artifacts, not user account data or part of a runtime binary.

## Remaining boundary

This evidence improves on archive availability and version matching alone. It does not prove bit-identical rebuilt binaries, independently authenticate every upstream signature, or validate every upstream package's legal interpretation. A final distribution decision must preserve notices and supply reviewed corresponding-source material with the binary at the same download location. No written source offer is made automatically. At the time of writing, independent packaging-provenance review and the final public-release gate are still open; no binary has been published.

Pinned upstream references: [inventory](https://github.com/git-for-windows/build-extra/blob/a762e4274c28611f8692fb01b894627326d04b32/versions/package-versions-2.53.0.3-MinGit.txt), [MinGit packaging](https://github.com/git-for-windows/build-extra/blob/a762e4274c28611f8692fb01b894627326d04b32/mingit/release.sh), [source collection](https://github.com/git-for-windows/build-extra/blob/a762e4274c28611f8692fb01b894627326d04b32/get-sources.sh), [GCM license](https://github.com/git-ecosystem/git-credential-manager/blob/v2.7.3/LICENSE).
