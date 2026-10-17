# Release Gate

Do not publish a stable release until every applicable item has evidence.

- [ ] Windows x64 build succeeds from a clean checkout with documented SDK versions.
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
