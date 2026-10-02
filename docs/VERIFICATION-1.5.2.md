# Verification of GitHubSync 1.5.2

This appearance patch builds on the [1.5.1 acceptance](VERIFICATION-1.5.1.md); it does not claim new live uploads or a publicly published release.

The WPF review checks every size in the embedded static ICO and all twelve animation ICOs for transparent outer edges, a retained light interior and enlarged dark-ring bounds. Scrollbar tests check both the 12px theme value and the actual gap between the native WPF content presenter and scrollbar, table-header alignment and removal of extra spacing when the outer scrollbar hides. The existing mode/folder-switch and virtualized-list checks remain in the suite.

Windows PowerShell 5.1 checks passed: all 13 backend/package suites, 86 write-transport assertions, 78 baseline download assertions with the regression rerun under three actual OneDrive Cloud ancestors, and 949 WPF assertions. After switching tray loading to the native ICO's small-size frame, the UI suite passed again: the 6000-file scan recorded 210 heartbeats with maximum gap 390 ms; switching mode took 12 ms. Representative real WPF renders, including small-window outer/table scrolling, were visually inspected. Package checks/build results are recorded in the local release folder's ACCEPTANCE-STATUS.txt after completion.

Synthetic Deutsch/Русский/English renders at logical 100–200% are not physical Windows DPI tests. Native taskbar/tray screenshots at actual OS scales are not claimed; embedded static/animation assets and the actual WPF/NotifyIcon paths are tested. Another PC and another interactive login/2FA are not tested.

A read-only cached-authentication preflight confirmed access to the approved TEST repository during this work. It does not invalidate the screenshot's HTTP 401 or guarantee future write authorization. No credential-store change or repeated upload was performed to investigate that screenshot.
