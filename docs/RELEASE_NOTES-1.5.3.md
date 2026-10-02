# GitHubSync 1.5.3 — reliable Code transport

Code uploads now use bundled native Git instead of sending large base64 JSON blobs through the REST API. The reported 60.6 MiB archive produced an approximately 80.8 MiB request and received HTTP 502; the precise server-side cause is unknown. This version removes that large REST-write path, rather than retrying it or merely increasing its timeout.

The existing read-only preview and confirmation stay in place. Git prepares one commit in a private temporary bare repository, preserves unrelated files and existing modes, checks the remote branch head immediately before push, and uses no force or protection bypass. Only read-back confirmation of the exact commit completes the operation. A lost response triggers a read, not another push. No empty commit is made for unchanged files. Local files and existing Git checkouts are not modified.

Progress distinguishes actual Git object-pack counters from confirmed files. Preparation and commit confirmation have an unknown duration. Code's 100 MiB file limit remains; large downloadable packages still belong in release attachments. Native push does not offer byte resume or guarantee transfer speed on every network.

The tray menu no longer recreates its items every second. Icon animation pauses while the popup is open, preserving position and the selected item. The larger transparent icons and scrollbar gutters from 1.5.2 remain.

Owner-approved private TEST acceptance passed a 61 MiB artificial binary plus text/Unicode files: one confirmed commit, existing content preserved, no-op without a new commit, and download SHA-256 equality. The upload took about 36 minutes on the test connection; this fix does not promise faster internet or eliminate all possible GitHub/network errors.

Extract the entire new portable folder, including its Git/GCM runtime. Existing settings can be copied after closing the older app; never publish a used installation. Public release/push is not performed by preparing this local candidate.

[Verification](VERIFICATION-1.5.3.md) · [Offline help](Guide-en.html)
