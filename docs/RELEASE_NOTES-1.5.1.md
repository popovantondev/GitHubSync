# GitHubSync 1.5.1 — transfer fixes

- Downloads to OneDrive now distinguish Cloud placeholders from links/junctions by the native Windows reparse tag. Both Code and release downloads retain path-containment, replacement-consent, checksum and resume safeguards. Unknown reparse types and genuine links/junctions remain blocked.
- The same Cloud-aware policy applies to the recursive Code source list and worker validation; OneDrive files are no longer silently excluded as links.
- Code blob uploads stream the base64 JSON request in bounded chunks with actual byte-progress snapshots. They use the configured HTTP timeout (default three hours), rather than a hard-coded two-minute deadline. This does not change the 100 MiB preflight rule or introduce mutation retries.
- GitHub write errors identify the operation, HTTP status and safe request ID. Authentication, permissions, SSO, rate limiting, size rejection, conflicts and timeouts have Deutsch/Русский/English guidance. Server bodies, tokens and payloads are not printed or saved.
- Existing one-commit/no-force/confirmed-branch behavior and draft-only release uploads are preserved. Download path errors no longer display the PowerShell method-invocation wrapper in the operating system's language.

The two reported download failures were reproduced against real OneDrive Cloud metadata and repaired. The old Code error discarded its HTTP response, so its original cause cannot be established from the screenshot/log. Owner-approved live acceptance in the existing private TEST repository subsequently passed Code upload/download and release upload/download with artificial data, replacement/stale-preview checks and draft/publication safeguards. This does not establish the cause of the old failure or prove a large live upload; see the verification notes for limits.

Extract the complete new portable folder. The previous installation and its settings remain intact. To keep local preferences, close the old app and copy only `config.json` and `ui-settings.json` locally; never put those used files in a distribution archive or source commit. Cached GitHub sign-in remains in Windows/Git Credential Manager.

[Verification](VERIFICATION-1.5.1.md) · [Offline help](Guide-en.html)
