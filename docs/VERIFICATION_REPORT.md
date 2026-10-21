# Verification report

**Status: early preview; stable-release gate not met.** Updated 2026-10-21.

This report separates GitHub CI evidence from unperformed device tests. Historical evidence from the original foundation is retained below but is not a statement about the present commit.

| Revision | CI evidence | What passed | What it does not prove |
| --- | --- | --- | --- |
| `91131ddcff5cae1254b8eb57735799b64607e709` (merged PR #39) | [run 39587149653](https://github.com/SeanShaffer88/Meridian/actions/runs/39587149653) | Core tests; Windows x64 and ARM64 publishing jobs | Integrated GUI behavior or native ARM64 runtime |
| `427f086e7019317c03d0aaca370460568d30f341` (merged PR #40; current `main` at this review) | [run 39587543706](https://github.com/SeanShaffer88/Meridian/actions/runs/39587543706) | Core-tests job passed when last inspected | x64/ARM64 publish jobs were still running at last inspection; do not infer completion |
| `049120a2` (historical foundation) | [run 38993509004](https://github.com/SeanShaffer88/Meridian/actions/runs/38993509004) | Historical core tests and x64/ARM64 publish jobs | Present main revision |

A Windows CI **ARM64 build** is cross-publishing on the workflow's `windows-latest` runner, not a native ARM64 functional test. CI output is not evidence that a clean installer launches, uninstalls cleanly, or supports all relevant accessibility features.

## Still required

- Execute on clean Windows 11 x64 and physical/virtual ARM64 where possible; verify startup, navigation, file actions, and installer/uninstaller.
- Exercise disposable-path tests for collisions, long paths, UNC paths, inaccessible files, links/junctions, cancellation, disk-full and interrupted operations.
- Verify Narrator, keyboard-only workflow, high contrast, DPI/text scaling, and accessible feedback.
- Collect cold-start, directory-filter latency (1k/10k/100k items), copy and cancellation response, CPU and memory metrics.
- Test a fully integrated commit after merging open PRs; separate historical PR successes are not additive.
- Refresh this report with exact commit, CI run, job IDs, test counts and device/OS evidence when available.

See [release gate](RELEASE_GATE.md), [threat model](THREAT_MODEL.md), and [performance plan](PERFORMANCE.md).
