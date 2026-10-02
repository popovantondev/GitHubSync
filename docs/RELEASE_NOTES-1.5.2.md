# GitHubSync 1.5.2 — clearer icons and scrollbar spacing

The white outer icon tile is removed. The dark hexagonal ring and orange two-way arrows occupy more of the icon canvas; the light inner hexagon remains. All static ICO sizes and twelve animation frames are updated, including the window/taskbar, tray and executable resources. The tray loads the native ICO size for the system's small-icon slot rather than downscaling the window bitmap.

Visible vertical scrollbars now have a 12 logical-pixel content gutter in the main page, virtualized file list and scrollable review/link dialogs. Table headers match the row viewport. Hidden scrollbars do not reserve this extra space. Native scrolling, virtualization, keyboard behavior and pinned progress/actions remain intact.

This is an appearance-only patch on the accepted 1.5.1 transfer baseline. No user data, credentials or transfer settings are added. HTTP 401 means GitHub rejected authentication for that request; use the sign-in button before a fresh review. This patch does not silently retry uploads or weaken path/access checks.

Extract the complete new portable folder; do not overwrite a running installation. Existing releases/settings are retained separately. Windows may retain an old cached shell icon for an old executable path; exit the old tray process and start the new extracted executable. The ZIP contains the new embedded icon.

[Verification](VERIFICATION-1.5.2.md) · [Offline help](Guide-en.html)
