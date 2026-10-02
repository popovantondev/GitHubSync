# Verification of GitHubSync 1.5.3

This candidate changes Code upload transport and tray stability, while retaining WPF, the bundled runtime, three languages and existing release/download behavior. Live acceptance is restricted to artificial files in the owner's existing private TEST repository; no real Downloads files or production repository writes are permitted.

## Offline checks

The native Git fixture sends a real local 61 MiB incompressible binary, nested Unicode/apostrophe paths, CRLF text and an empty file to an isolated bare repository. It checks one direct-parent commit, raw blob hashes, preserved executable mode and unrelated file/symlink entries, no-op without a push, a stale branch, a remote rewind immediately before push, protected/access rejection, a lost push reply, invalid paths and the 100 MiB guard. Hidden subprocess deadline/job termination is tested separately. No network or credentials are used by this suite.

The worker mocks check read-only preview, one native write, commit readback after a lost reply, access/conflict failures and no empty commit. Progress serialization preserves only whitelisted Git fields on failure. WPF checks distinguish Git object counts from confirmed files and stop activity on failure. Tray checks repeatedly apply failed snapshots, run real timers with an isolated open native context menu, and verify stable geometry, selection, item identity, frozen icons and deferred language/action updates.

Windows PowerShell 5.1 completed all 16 suites (15 non-UI plus WPF), including 32 native Git assertions, 86 legacy REST transport assertions and the existing 78-assertion download baseline/Cloud rerun. WPF passed 1179 assertions, twelve language/density renders and fixture renders. The last full run recorded a maximum 141 ms heartbeat gap during the 6000-file scan and a 6 ms mode switch. Native EXE/tray icon checks and source publication/privacy/BOM/link checks passed. These are test observations, not performance guarantees.

Physical Windows DPI switching, a second computer and a new real browser login/2FA are not claimed. Synthetic 100–200% WPF renders do not replace those checks. The native large-file fixture now lives in local Temp rather than the OneDrive source tree; the dedicated Cloud suite still checks three actual OneDrive ancestors and rejects a real junction escape.

## Approved live acceptance

On 2026-10-02 the production native Git transport/worker functions uploaded a 61 MiB incompressible synthetic binary, TXT and nested Unicode TXT into a unique owned folder in the approved private TEST repository. The selected branch confirmed one direct-parent commit, `c6227607829687bb68137338c5cbab9b46db8522`, based on `ca9d068486684fafa32b2db91dc419b43d29b120`. All three Git blob hashes matched. All four pre-existing file entries kept their SHA/mode. A second unchanged request created no commit or push. Code download then reproduced all source SHA-256 hashes, including the large binary: `464a8966fdb29a3246d10a3ceac12520031346d175be514d7e3a0b57e3871155`.

Upload took 2174 seconds (about 36 minutes 14 seconds) on this connection, without HTTP 502. This verifies the large native transport path, not universal speed or freedom from future server/network errors. Completed synthetic native fixtures were moved recoverably out of the OneDrive source tree; the live transfer was not restarted. The exact cause of the earlier REST 502 remains unknown. No real user Downloads files, production repository, release publication or repository visibility were changed by this acceptance.

Successful transport behavior was exercised live; final progress/error-message refinements are covered by offline native/worker/WPF tests. Protected branches, remote rewinds and lost replies were simulated against isolated local remotes, not imposed on the user's real repository.

## Packaging

Portable/source archives must pass the publication guard independently, retain upstream licenses/notices and contain neutral configuration only. User settings, cached sign-in, test data and runtime-generated logs are excluded from clean packages. Older archives are retained rather than overwritten.
