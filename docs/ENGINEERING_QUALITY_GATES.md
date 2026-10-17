# Meridian Engineering Quality Gates

Meridian aims to be turnkey, cohesive, lightweight, safe and production-grade. These are measurable release requirements, not claims of current completion.

## Blocking release gates

1. **Build integrity:** clean Windows 11 x64 and ARM64 Release builds; reproducible package inputs; installer and portable artifacts verified on clean machines.
2. **Correctness:** core tests, UI smoke tests, error-path tests and cancellation tests must pass. No ignored build failures or silent exception swallowing.
3. **Data safety:** operations must respect the Safety Contract. Preview is not permission to modify files. Recovery claims must be tested.
4. **Security:** untrusted content must not execute during preview; dangerous parsers require isolation, resource limits and adversarial tests. Audit junction/symlink races and archive traversal.
5. **Performance:** benchmark cold/warm startup, idle memory/CPU, directory enumeration at 1K/10K/100K entries, file-copy throughput and cancellation latency. Publish measurements with hardware and dataset.
6. **Accessibility:** keyboard-only navigation, focus management, screen reader names, high-contrast compatibility and scalable text.
7. **Experience:** consistent error messages, no dead-end dialogs, clear progress/cancel, predictable navigation, no surprise background network activity.
8. **Media:** verify video playback, pause/seek, codec failure reporting and resource disposal. Document supported formats by tested codec/container combinations.
9. **Distribution:** signed release artifacts when signing infrastructure is available, hash publication, upgrade/uninstall tests, no bundled unwanted software.

## Explicit non-goals for claims

Do not describe the application as tested, best-in-class, race-free, universal in codec support, or production-ready until evidence supports those statements. Passing CI alone is necessary but not sufficient.

## Status

The branch contains prototype viewer and player UI, Foresight and TimeVault foundations. Windows builds and hands-on validation have not yet been confirmed. The CI workflow is intended to exercise core tests and Windows builds; check its run results before claiming success.
