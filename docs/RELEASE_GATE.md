# Release Gate

Do not publish a stable release until every applicable item has evidence. CI cross-publishing is not a substitute for native ARM64 testing; installer build success is not an installation or uninstallation test.

See [verification status](VERIFICATION_REPORT.md), [threat model](THREAT_MODEL.md), and [performance measurement plan](PERFORMANCE.md).

## Current evidence versus gate completion

At the [dated baseline](STATUS.md), core tests, x64/ARM64 build/publish/installer jobs, resource packaging and an x64 startup smoke passed. These establish automated increments only. Composite checklist items below remain open when they include untested failure paths, clean-machine behavior or real-device checks. See [VERIFICATION_REPORT.md](VERIFICATION_REPORT.md) for the exact commit/run and remaining gaps; do not reuse an older passing run as evidence for a changed application.

## Stable-release checklist

- [ ] Windows 11 x64 and ARM64 builds succeed from a clean checkout with documented SDK versions.
- [ ] Core tests pass and relevant failure paths have tests.
- [ ] App launches on a clean Windows 11 VM without developer tools installed.
- [ ] Navigation and file operations pass edge-case testing.
- [ ] No silent overwrite; failures report affected paths and recovery options.
- [ ] Reparse point, junction, UNC, long-path, Unicode, and permission behavior tested.
- [ ] Accessibility, keyboard navigation, scaling, and high-contrast checks completed.
- [ ] Startup, large-directory navigation, idle CPU/memory, and copy performance measured.
- [ ] Third-party dependencies, licenses, vulnerabilities, and update policy reviewed.
- [ ] Privacy review confirms no telemetry or network access unless documented.
- [ ] Version, changelog, source archive, release notes, and SHA-256 checksum verified.
- [ ] Verification report contains real evidence; no tests or benchmarks are claimed without records.

## Mandatory data-safety release blockers

The [Safety Contract](SAFETY_CONTRACT.md) applies to every mutating capability. The following items must have concrete test evidence, not just design documents:

- [ ] Risk-tier classification and read-only defaults for every operation.
- [ ] Preview, revalidation, explicit confirmation, and safe abort on changed scope for destructive actions.
- [ ] No-overwrite enforcement at execution; destination integrity verification before any source removal.
- [ ] Operation receipts, truthful partial-failure reporting and crash-recovery guidance.
- [ ] Undo offered only when a tested recovery path remains available; no unconditional undo promises.
- [ ] Reparse/junction/alias/path-swap and privilege-boundary adversarial testing.
- [ ] Restore-point removal, Windows servicing cleanup and vault encryption disabled until their specialized security gates pass.
- [ ] Disk-full, cancellation, interruption and inaccessible-path failure injection.
- [ ] Accessible safety prompts and no preselected destructive options.
- [ ] Windows VM restore drills for every advertised recovery workflow.


## Engineering quality requirements

Release readiness also requires satisfying [Engineering Quality Gates](QUALITY-GATES.md), including clean x64/ARM64 builds, verified playback, measured performance, accessibility checks and clean-install testing. Unverified items remain blockers.
