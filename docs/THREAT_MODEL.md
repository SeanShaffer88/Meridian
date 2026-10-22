# Threat Model (initial)

## Assets and trust boundaries
Meridian accesses files using the current user's Windows permissions. Names, metadata, file contents, archive entries, and paths may be malformed or attacker-controlled. File launch delegates to the Windows shell and system file associations.

## Current mitigations
- No requested administrator elevation.
- No telemetry or network features implemented.
- Copy refuses to overwrite an existing destination.
- Copy stages content in the destination directory before final rename.
- Reparse points are rejected instead of followed by the copy engine.
- Directory enumeration catches common per-item access/race failures.

## Known risks and gaps
- Copy is not a transactional filesystem operation; crashes or disk errors may leave staging files.
- UI exposes cancellation during individual copy and batch execution, but not a general pause/resume operation queue. The initial batch preflight is not cancellable from its UI button on `main`.
- File launching may invoke arbitrary handlers configured by the user or system.
- Archive parsing and richer viewers are not implemented on `main` (some code is proposed in open PR #37). There is a read-only copy Operation Simulator preview; scripting/plugins are not implemented.
- Race conditions remain possible between path checks and use.
- No formal security review has been completed.

## Release requirements
Document threat-driven tests, review dependencies, test junctions/symlinks and path edge cases, and verify untrusted content is not executed merely by previewing it.
