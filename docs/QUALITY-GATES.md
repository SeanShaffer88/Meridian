# Meridian engineering quality gates

**Product identity:** Meridian — *Beyond File Management.*

This document defines evidence required before calling Meridian production-ready. A passing CI workflow proves only that the checked code builds and its automated tests pass; it does **not** prove real-device compatibility, accessibility, or safety under all filesystem races.

## Merge gate (every code change)

- Core tests pass on the pull request's head commit.
- Windows 11 x64 and ARM64 CI build jobs pass on the same head commit.
- Destructive or mutating workflows have explicit user intent, no silent overwrite, and tests covering failure and cancellation where relevant.
- UI changes are reviewed for keyboard access, readable errors, and narrow-window behavior.
- README and in-app claims match features actually merged to main.

## Release gate (requires hands-on Windows validation)

- Launch and install/uninstall on clean Windows 11 x64 and ARM64 devices or representative virtual machines.
- Exercise two-pane navigation, keyboard shortcuts, tabs, bookmarks, shelf and batch operations with a screen reader and keyboard only.
- Test NTFS edge cases: junctions/symlinks, access-denied paths, locked files, case-only names, long paths, empty directories, removable drives, network shares, low disk space, and interruption during transfer.
- Confirm copies never overwrite unrelated targets and originals survive failed/cancelled copies; inspect and clean leftover `.meridian-*.partial` staging items deliberately.
- Profile startup, large-directory enumeration, UI responsiveness, memory use, and cancellation latency on actual Windows hardware; record repeatable measurements and hardware.
- Verify installer signing, update strategy, dependencies, crash reporting policy, and privacy/offline behavior before distributing as stable.

## Engineering principles

1. **Local-first:** File contents do not leave the computer without a separately designed, opt-in feature.
2. **Preview is advisory:** Revalidate when executing; no promise of a transaction or rollback.
3. **Safe by default:** Refuse collisions and ambiguous destinations. Report partial success plainly.
4. **Cancellation is cooperative:** Never advertise instant or atomic cancellation.
5. **Test the boundary:** Core services should be independently testable without the Windows UI.
6. **Honest evidence:** Distinguish source review, automated CI, emulation, and hands-on Windows testing.
7. **Innovate deliberately:** Prefer coherent workflows (Drop Shelf, Folder DNA, Operation Simulator) over feature count.

## Known limitations

- Batch copies are sequential, and a cancelled/failed batch can leave earlier completed items at the destination.
- Preview cannot reserve paths or eliminate filesystem time-of-check/time-of-use races.
- Reparse-point checks currently cover the source entry, recursive children during copy, and the destination directory entry; ancestor traversal and adversarial concurrent replacement require further hardening.
- Drop Shelf is in-memory and is not persisted between launches.
- Automated Windows builds are not equivalent to verified installation or runtime testing.
