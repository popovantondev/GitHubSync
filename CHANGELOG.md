# Changelog

## 1.5.1

- Accept native OneDrive Cloud placeholders in download paths and Code sources without accepting symlinks, junctions or unknown reparse tags.
- Stream large Code blob requests with byte progress and the configured timeout instead of a fixed 120 seconds.
- Add safe, localised operation/HTTP-status diagnostics; retain no-force commits, read-only preflight and no blind mutation retries.
- Add real Cloud/junction metadata and artificial 61 MiB HTTP regression checks. No real-account Code write was performed.

## 1.5.0 — GitHubSync local candidate

- Send/download directions, readable public/private catalogs, selected Code files/folders and published release attachments.
- Streamed, verified downloads with cooperative pause, persisted partial files, safe byte resume and replacement backups.
- Conditional 15-minute update observer; transfers remain manually approved.
- Close-to-tray lifecycle, one running instance and shared two-arrow tray/window animation.
- Lazy row visuals keep mode switching responsive after a 6000-file list; deterministic software WPF rendering avoids GPU-driver failures in the simple interface.
- Backward-compatible settings, offline three-language tutorials and clean portable/source build.

## 1.4.1 — local release candidate

- Mode switching no longer recursively scans and creates the complete file list in the UI thread.
- Background scan, cancellation on mode/folder changes, bounded row-building batches and virtualized row containers.
- Shared checkbox/icon/menu/theme resources and one table update per bulk-selection action.
- Partial scans cannot overwrite selection or start uploads. Stale worker results do not repaint a new mode.
- Clean source/portable separation, pinned official runtime, source privacy audit, safe demo paths and three-language documentation.
- Sidebar app name and version are centered on separate lines; version text is two points smaller.

## 1.4.0

- Renamed GitHub Release Watchdog to GitHubUploader.
- Code uploads with relative paths, read-only change preview and one non-force commit.
- Release uploads with optional confirmed publication, later publication and page/asset links.
- Legacy settings retain draft-only behavior; separate Code/release selections.

Earlier development builds are not included in the public source or current portable package. No current-version real GitHub write acceptance is claimed by this history.
