# Third-party runtime

The application license does not replace dependency licenses.

The portable builder downloads the **unmodified official MinGit** archive, verifies its SHA-256 and extracts it into a new folder. It never copies a previously used runtime or a user's credential store. The archive, versions, checksum and upstream URLs are recorded in [runtime.lock.json](runtime.lock.json).

- Git for Windows 2.53.0.windows.3: [binary release](https://github.com/git-for-windows/git/releases/tag/v2.53.0.windows.3), [Git source tag](https://github.com/git-for-windows/git/tree/v2.53.0.windows.3).
- Git Credential Manager 2.7.3: [source tag](https://github.com/git-ecosystem/git-credential-manager/tree/v2.7.3).
- [Git for Windows build/packaging recipes](https://github.com/git-for-windows/build-extra) and [SDK package source recipes](https://github.com/git-for-windows/MINGW-packages) describe the upstream distribution. The upstream release also provides source packages; these are distinct from this application's source archive.

Keep the entire extracted runtime, including `runtime/git/LICENSE.txt`, `runtime/git/etc/package-versions.txt`, GCM `LICENSE` and `NOTICE` under `runtime/git/mingw64/doc/git-credential-manager/`, and every upstream license/notice directory. The inventory identifies the actual bundled libraries; Git's GPL and other library licenses remain applicable.

Windows/.NET Framework and PowerShell are operating-system prerequisites, not redistributed by this project. No account data is shipped. GCM handles authentication on the recipient's computer.

## Before public binary distribution

This package is a **local candidate**, not a published binary release. Preserving notices and linking upstream is not, by itself, a claim that every GPL/LGPL corresponding-source obligation is satisfied. Before attaching the portable ZIP to a public release, the distributor must review the exact upstream inventory and arrange the required corresponding-source availability/offer for all applicable components. No written source offer is made on the owner's behalf by this preparation. See [the release checklist](docs/RELEASE_CHECKLIST.md).

### Verified source material, 2026-10-02

The bundled package inventory matches the pinned upstream MinGit inventory. The official [Git 2.53.0.3 source package and packaging recipes](https://github.com/git-for-windows/pacman-repo/releases/download/2026-04-14T17-06-35.858808200Z/mingw-w64-git-2.53.0.3-1.src.tar.gz) was retrieved and its SHA-256 checked against the GitHub release asset digest: `95dfd523521fa5c07c2c5e1eb0a59c478457768ebd2551ccccf3900a747bc61d`. The included `git-v2.53.0.windows.3.tar.gz` also matches its PKGBUILD SHA-256: `397188a9f7c374a93ccc1823b0aea5d424f68c463b2586794ff7d083b92298de`.

This verifies source material for Git itself, **not** the entire runtime. The remaining components and the mechanism for supplying required source alongside the public binary still need review. This is not a source offer or a certification of distribution compliance.

An exact-version availability probe covered all 64 unique package/version pairs in the 66-line upstream inventory. It found 54 distinct candidate source archives covering 63 package mappings, from the official Git for Windows package mirror or MSYS2 source repositories. MSYS2 now supplies many archives as `.src.tar.zst`; checking only `.src.tar.gz` incorrectly misses them. Availability alone does not verify correspondence, source completeness or licenses. The one unresolved packaged-source mapping is Git Credential Manager 2.7.3; its upstream source tag remains linked above.

The [libssh2 1.11.1-2 source package](https://repo.msys2.org/mingw/sources/mingw-w64-libssh2-1.11.1-2.src.tar.zst) was inspected without executing its recipe. Its PKGBUILD explicitly includes the `wincng` variant and `mingw64` architecture, with BSD-3-Clause licensing and the upstream source archive. Local retrieved archive SHA-256: `bb78f8354441190221ece18a96c09f631fe02648f5dd7d1e247ae8d00a9fea5f` (a recorded retrieval digest, not an independently authenticated signature).

### Local source-material candidate

[runtime-sources.lock.json](runtime-sources.lock.json) now records 55 original archives, retrieval hashes and mappings covering all 64 unique package/version pairs. The final entry is the upstream GCM source at commit `5fa7116896c82164996a609accd1c5ad90fe730a`, which the official `v2.7.3` tag resolves to; it is not represented as a MinGit packaging source archive. The 54 package archives were downloaded and their recipes and member lists read without execution. This includes embedded source tarballs, patches and, for some packages, Git object repositories. Recipe license declarations are preserved in the lock as declarations, not legal conclusions.

`tools/Build-RuntimeSourceArchive.ps1 -SourceDirectory <verified-source-cache>` prepares a separate `GitHubSync-1.5.1-runtime-sources.zip` and checksum. It refuses existing output and verifies every input against the lock. Do not commit the source cache or this large generated ZIP. The public-distribution gate remains open until source correspondence and GCM transitive dependency notices are reviewed and the required materials are made available with the binary. The existing portable/application-source candidates must be rebuilt after these preparation changes.

### Supplementary GCM notices

[third-party-notices/gcm](third-party-notices/gcm/README.md) preserves the original NuGet metadata/copyright statements and supplied LICENSE/THIRD-PARTY-NOTICES resources for 31 matched package versions, covering 43 distinct runtime DLLs. The package hashes and non-certificate PE-content comparisons are recorded in [its catalog](third-party-notices/gcm/packages.lock.json). MIT-expression packages also have the common MIT permission text. This covers the inspected GCM dependency notice gap; it does not remove the original runtime notices or close the remaining source-correspondence/public-availability gate. No NuGet binaries are added to the application.
