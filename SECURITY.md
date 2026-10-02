# Security and privacy

Never attach tokens, passwords, SMS codes, private keys, personal configuration, unredacted logs or private repository/file names to public issues. Use artificial files and screenshots where possible. Revoke an exposed credential before relying on deletion; Git history may retain it.

For a vulnerability, use GitHub private vulnerability reporting in the Security tab if the owner enables it. If unavailable, first request a private contact channel in an issue without sensitive details. Do not publish credentials or exploit data in that request.

The app uses Git Credential Manager for official browser authentication. App settings, status snapshots and temporary operation requests do not store the token. Settings do contain local file paths, selected names and account identifiers. GCM and Windows may keep authorized credentials in their own protected credential store; this is separate from the source/portable package.

Uploads are explicit. Code preview is read-only; one commit, no deletion/force or branch-protection bypass. Release publication is confirmed separately or as part of confirmed upload, and only after complete asset verification. Unknown write outcomes are checked before retry, not blindly repeated. Files named like credentials are not initially selected, but filename heuristics do not replace content review.

Network requests use HTTPS; the app contacts GitHub and the browser/GCM sign-in services. There is no added telemetry service. Git/runtime binaries are external dependencies with independent vulnerabilities and licenses. This audit is a bounded local check, not a guarantee covering every PC, file, dependency or network condition.
