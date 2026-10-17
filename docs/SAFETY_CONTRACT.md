# Safety Contract — no regrettable surprises

**Normative design requirements.** This is the release standard for future Meridian operations, not a claim that the current preview already meets it. No software can guarantee every action is undoable.

## Safety tiers

| Tier | Examples | Minimum behavior |
| --- | --- | --- |
| Observe | Browse, inspect, search, preview cleanup | Read-only; no incidental changes; bound resource use |
| Additive | Create a new file, copy to a new destination | Never overwrite; stage and verify; preserve originals |
| Reversible change | Rename, move, Recycle Bin deletion | Explicit scope; durable operation record; verify recovery path before describing as reversible |
| Destructive/system | Permanent deletion, restore-point removal, servicing cleanup | Disabled by default; separate explicit opt-in, preflight and strong confirmation; no automatic retry |
| Security-sensitive | Vault creation, encryption, key rotation, vault deletion | Audited design, tested backup/recovery, explicit warnings and security review before general release |

## Mandatory invariants

1. **Never silently destroy, replace, truncate or encrypt existing data.** No-overwrite must be enforced at the actual filesystem operation, not merely by an earlier check.
2. **No hidden privilege escalation or background destructive maintenance.** Elevation, if ever required, must be narrowly scoped and user-initiated.
3. **Dry run first** for batch, recursive, system-changing and cleanup operations: show exact affected locations, counts, sizes, conflicts, exclusions, access requirements and realistic rollback limits.
4. **Default to keeping data.** Unknown, inaccessible, in-use, linked, remote, protected or changing targets must be skipped or fail closed, never treated as disposable.
5. **Use quarantine/Recycle Bin where available**, not permanent deletion by default. Confirm that recovery is actually supported for the target volume; do not claim that Recycle Bin guarantees recovery.
6. **Keep source data until destination integrity is established.** Use unique staging, no-overwrite publication and post-copy integrity checks; for cross-volume moves, only consider removing the source after verification.
7. **Durable operation journal** before enabling complex mutations: identifiers, timestamps, user-approved scope, per-item outcome, partial failures, and recovery instructions. Do not record file contents, passwords or vault keys.
8. **Honest undo.** Only label an action undoable when required artifacts remain and a tested inverse exists. Show an undo expiry or dependency, and invalidate undo if intervening changes make it unsafe.
9. **Bound every operation.** Cancellation, resource limits, timeout strategy where relevant, progress reporting, and crash-recovery behavior must be documented and tested. Cancellation does not imply rollback.
10. **Race-resistant filesystem access.** Resolve reparse points, junctions, hard links, path aliases, ADS, device paths and permissions at use time; use handle-based validation when feasible. Lexical checks alone do not meet this gate for destructive actions.
11. **Never assume restore points are backups.** Windows System Restore generally does not recover personal files; VSS snapshots and File History have separate availability and retention limits.
12. **Vault security before convenience.** Encryption must be authenticated, key management reviewed, recovery tested, and plaintext spill paths examined. Do not promise secure erase on SSDs, snapshots or backups.
13. **Accessibility is a safety control.** Consequence dialogs must work with keyboard, screen readers and high contrast; warnings cannot depend on color alone.
14. **No dark patterns.** No preselected destructive choices, confusing double negatives, misleading reclaimed-space totals, or pressure to delete data.

## Execution protocol for risky actions

**Discover → Classify → Preview → Revalidate → Confirm → Execute conservatively → Verify → Record → Offer recovery.**

Confirmation is not a replacement for verification. When the environment changes between preview and execution, stop and request a new preview. Errors must report what definitely happened, what may have happened, and what remains untouched.

## Evidence required to unlock features

- Automated tests for success, denial, collision, cancellation, interrupted writes, disk-full, locked files, permission changes, junctions, symlinks, alternate streams, path swaps and recovery failures.
- Windows 11 x64 and ARM64 CI plus actual-device/VM exercises, with logs and reproducible steps.
- Failure injection and restore drills proving recoverability claims, including cases where undo must be refused.
- Independent security review for encrypted vaults and any privileged servicing.
- A release report identifying verified guarantees, known limitations and intentionally disabled unsafe paths.

**Fail closed:** if these gates are unmet, keep the capability read-only, experimental and/or unavailable. A polished UI does not make an unsafe operation release-ready.
