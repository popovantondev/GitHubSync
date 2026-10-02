# Changelog

## 1.5.3 — native Git Code upload and stable tray menu

- Replace Code's large base64 REST mutations with a single native Git push using the bundled runtime and existing cached sign-in. Stream local hashing instead of loading whole files into memory.
- Prepare a temporary bare snapshot, preserve other files/modes, build one commit, and check the receive-pack advertised head with a trusted pre-push hook. No force, protection bypass, deletion or blind push retry. Read back the exact commit before reporting success, including after a lost push response.
- Display real Git object-pack progress separately from confirmed files. Unknown preparation/confirmation duration stays indeterminate; object progress is not a per-file byte counter.
- Contain hidden Git/GCM subprocesses in a kill-on-close Windows job, isolate Git configuration, sanitize process errors and clean only the verified generated cache directory.
- Keep permanent tray menu items instead of clearing/recreating them on progress updates. Apply real text/state changes only while the menu is closed.
- Freeze tray/window icon animation while the tray menu is open, then resume after closing. Preserve the selected action and popup position.
- Add isolated native Git, worker/progress and tray-menu regression checks for large binary/Unicode files, modes, no-op, remote rewind, protection/access denial, lost reply, process deadlines and menu stability.

## 1.5.2

- Remove the white outer icon tile, enlarge the hexagonal symbol, and retain transparent surroundings in every static/animated ICO size. Window, executable, taskbar and tray share the updated resources.
- Add a 12 logical-pixel gutter before visible vertical scrollbars in the page, file list and review/link dialogs. Hidden scrollbars do not reduce content width; table headers follow the row viewport after list replacement.
- Transfer/authentication behavior is unchanged. An HTTP 401 still requires a fresh authorized sign-in; cosmetic changes do not retry writes.

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

Earlier development builds are not included in the public source or current portable package. Version-specific verification notes distinguish offline checks from owner-approved real-account acceptance.
