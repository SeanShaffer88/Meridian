# Meridian Engineering Quality Gates

Meridian aims to be turnkey, cohesive, lightweight, safe and production-grade. These are measurable release requirements, not claims of current completion.

## Delivery workflow

Use the [High-throughput Engineering Pipeline](HIGH_THROUGHPUT_DEVELOPMENT.md) for batched feature delivery, independent workstreams, evidence tracking, and integration rules. Faster iteration does **not** waive any release gate below.

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

The branch contains integrated viewer, TimeVault discovery/listing and ZIP/TAR/TGZ workflows plus backend Foresight, Consequence Engine and Storage Reclaimer foundations. At the [dated baseline](STATUS.md), core tests, x64/ARM64 build/publish/installers and an x64 published startup smoke passed. Hands-on workflows, ARM64 execution, clean install, accessibility, performance and complete security validation remain open. Refer to [VERIFICATION_REPORT.md](VERIFICATION_REPORT.md) for exact evidence; this status does not waive the gates above.

## CI evidence rule

A workflow with GitHub status `pending` and an empty jobs list is **not** evidence of compilation or test success. Do not mark build, packaging, accessibility, playback, or safety gates as passed until a completed run with inspectable successful jobs and appropriate test artifacts is available. If GitHub Actions remains pending without jobs, inspect repository Actions settings, workflow approval requirements, runner availability and billing/usage status using authorized repository administration; do not infer a cause from the empty job list alone.
