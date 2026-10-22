# Context Capsule local store — prototype

`ContextCapsuleStore` is a low-level store for the versioned JSON format. It supports asynchronous load/save and same-directory temporary file replacement. It does not integrate with the WinUI Capsule controls yet.

- A missing file loads as an empty list; corrupted, unsupported or oversized files report errors and are **not** automatically reset.
- Save uses exclusive temporary-file creation, flushes before rename, then moves into place. This lowers partial-write risk, but atomic rename/durability vary by filesystem and power loss.
- Stored path references can be sensitive; the file remains local and is not uploaded by Meridian.
- This implementation does not provide multi-process locking, backups, conflict resolution, migrations, or complete crash recovery. Do not use it as a recovery journal.

Test with `./scripts/test.ps1 -Configuration Release` on Windows. Additional clean-device, network-share, power-loss, and permission testing is required.
