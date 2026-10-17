# File-operation policy

The current move-safety increment restricts Move rather than offering an unverified copy/delete workflow. See [status](STATUS.md) and [verification evidence](VERIFICATION_REPORT.md) for the tested baseline and subsequent checks.

## Move and rename

`Services/FileOperations.cs` invokes Windows `MoveFileExW` with flags set to zero. Neither `MOVEFILE_COPY_ALLOWED` nor `MOVEFILE_REPLACE_EXISTING` is enabled. The OS rename therefore cannot intentionally fall back to copying/deleting across volumes or replace an existing destination. This follows the [Windows API contract](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw).

Windows resolves whether a rename is possible. Drive-letter equality is not a volume identity test: a substituted drive can refer to the same underlying filesystem, while mount points and aliases can obscure actual volume boundaries. Existing reparse-point ancestors remain rejected. Unsupported cross-volume moves fail with an explanatory message and no application-created destination copy or source deletion. Use Copy to preserve the original; Meridian does not automatically follow it with source deletion.

Move still performs preflight existence/collision/self-descendant checks. The native operation enforces no replacement at execution time; preflight alone is not a lock. Windows failures become I/O or access-denied errors. Long paths receive extended Windows path syntax where needed. Public move/rename require Windows; cross-platform core compilation does not imply these operations work on another OS.

Case-only rename uses a unique temporary sibling and attempts rollback if its second rename fails. If rollback is blocked, a temporary `.meridian-rename-*.tmp` item can remain; there is no general transaction or durable receipt. Path checks still do not provide handle-based identity guarantees against concurrent replacement. This increment does not resolve every move/rename safety gate.

## Copy and batch behavior

Copy remains separate: it stages data in the destination folder, checks cancellation between bounded I/O operations and publishes without replacing a final item. It attempts cleanup on failure and preserves source files. Post-copy checksum verification and a snapshot of changing directory contents are not implemented; a staged tree is not a consistent filesystem snapshot. Process interruption, disk failure or denied cleanup can leave staging data.

Drop Shelf batch copy is sequential, performs advisory preflight and execution-time checks, and retains earlier successful results after a later failure or cancellation. Its displayed result is not a persistent journal or rollback mechanism. New-folder creation, Recycle Bin requests and shell opening are separate UI paths with their own checks; do not infer all move guards apply to them.

## Evidence and future gates

- Four core regressions exercise same-volume drive aliases for files/directories, locked-source failure without destination output, and long Unicode rename/move. The locked-source case fails against the removed fallback implementation.
- The local Release suite passed 343 tests, zero failures/skips. Local x64/ARM64 builds passed with zero warnings/errors; ARM64 build is not ARM64 runtime evidence.
- Separate disposable C: to E: checks refused both a file and a directory with Windows `ERROR_NOT_SAME_DEVICE` (17), source contents intact and destination empty. These are local integration observations, not automated coverage on every volume/provider.

Before re-enabling cross-volume move, implement verified destination integrity, source stability/identity, a durable journal, explicit scope/confirmation and truthful partial outcomes; test disk-full, interruption, denied deletion, changed/added source files, aliases and recovery. Do not infer those guarantees from copying and comparing metadata. See [Safety Contract](SAFETY_CONTRACT.md), [threat model](THREAT_MODEL.md) and [release gate](RELEASE_GATE.md).
