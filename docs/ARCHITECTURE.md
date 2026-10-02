# Architecture

- `src/SyncWpf.cs`: send/download direction, remote selection/review, private pending jobs, conditional update checks, tray lifecycle and cached icon animation.
- `Sync-Operations.ps1`: GET-only catalogs for readable repositories, published releases and pinned Code trees; optional noninteractive cached authentication; approved download validation.
- `src/SyncTransfer.cs`: streamed HTTP download, Range/If-Range handling, private partial metadata, path/consent guards, hashes and atomic replacement with backups. It is compiled by the existing PowerShell worker, not a new UI framework.
- `src/WindowsPathSafety.cs`: shared native reparse-tag inspection; Cloud placeholders are permitted, links/junctions/unknown tags are blocked. A metadata-only handle avoids hydrating files just to classify a path.
- `src/GitHubWrite.cs`: one-attempt JSON mutations for release publication, configured deadlines, no redirects, and sanitized error categories/request IDs. The legacy blob helper is regression-tested but no longer used by Code uploads.
- `src/GitCodeTransport.cs`: hidden bundled Git/GCM processes in a kill-on-close Windows job. An isolated temporary bare repository fetches the current branch snapshot (depth one, no checkout), streams raw blobs into its index, preserves the original tree/modes and creates one commit. A trusted pre-push hook checks the advertised remote head; a normal non-force push sends only the selected branch. Global/system Git config is not imported. Tokens never appear in arguments or temporary Git config. The exact generated directory is link-checked before cleanup; interrupted processes can leave a private cache under `%LOCALAPPDATA%/GitHubSync/code-transfers`.

Direction and content kind are independent. Legacy UploadMode remains code/release; new download directory/branch/prefix/release settings do not overwrite upload selections. Download plans bind source identities and local hashes; changing local files invalidates consent. Worker control files request a cooperative pause. Tray close does not terminate approved work; explicit Exit checkpoints downloads. Observers only perform GET requests and cannot publish or upload.

- `src/LauncherWpf.cs`: standard Windows-framed WPF window, shared orange theme, language switching, selection, pinned progress and hidden worker launch.
- `src/UploaderWpf.cs`: two modes, cancellation/background scan, review/publication dialogs and result links.
- `Release-UploadWatchdog.ps1`: PowerShell 5.1 worker, Git/GCM authentication in memory, repository/draft catalogs/creation, release upload and status snapshots.
- `Uploader-Operations.ps1`: read-only Code preview and fresh approval/account checks, native Code upload, and REST release publication. Only a GET-confirmed exact commit in the selected branch completes a Code upload. A lost push response is reconciled by GET, never by another push. Publication checks all assets and fresh draft state.
- `config.example.json`: neutral public configuration. Actual `config.json`/`ui-settings.json` belong to an extracted portable app, not the repository.

Mode/folder changes cancel the current scan by token/generation. Late results cannot replace a newer selection. File I/O runs off the WPF thread; control construction is bounded and the viewport virtualized. Upload stays unavailable until the complete scan is ready. Status/result actions remain outside the content scroll region.

Checks use fake account tokens only inside isolated test copies and mocked APIs. The source contains no live-account smoke runner. Production GitHub writes require the real user's explicit confirmation; tests must not bypass it to touch existing files.

Git pack progress reports object counts, not bytes of an individual source file or completed GitHub files. Preparation and commit confirmation have unknown duration. The configured transfer timeout applies to each native command; Git push is not byte-resumable. A fresh retry requires a new preview. Files above 100 MiB remain blocked before any write; Git LFS is not automatically configured.
