# Verification Report

**Status: NOT READY / UNVERIFIED**

Date: 2026-10-17

| Claim | Evidence | Result | Limitation |
|---|---|---|---|
| XML files parse | Static parser check in authoring environment | Pass | Does not validate WinUI XAML schema or generated code |
| XAML event handlers match code-behind names | Static name scan | Pass | Does not prove delegate signatures or XAML compilation |
| Project references resolve | Static path check | Pass | Does not prove MSBuild accepts the solution |
| Core unit tests pass | GitHub Actions run [38993509004](https://github.com/SeanShaffer88/Meridian/actions/runs/38993509004), commit `049120a2`; Windows CI test job passed | Pass in CI | This is automated test evidence, not a substitute for manual runtime testing |
| Windows x64 app publishes | GitHub Actions run [38993509004](https://github.com/SeanShaffer88/Meridian/actions/runs/38993509004), commit `049120a2`; x64 publish job passed | Pass in CI | Does not prove the app launches or behaves correctly on a real device |
| Windows ARM64 app publishes | GitHub Actions run [38993509004](https://github.com/SeanShaffer88/Meridian/actions/runs/38993509004), commit `049120a2`; ARM64 publish job passed | Pass in CI | Does not prove runtime behavior on physical ARM64 hardware |
| Archive integrity baseline verified | Windows CI [39061684075](https://github.com/SeanShaffer88/Meridian/actions/runs/39061684075), commit `d0d9d597` | Pass: core tests, x64 and ARM64 builds, publish and installers | Does not establish interactive runtime or clean-install behavior |
| Published x64 app starts | Local Windows 11 Home x64, build 10.0.26300, host SEAN-MSI | Limited startup smoke passed after resource packaging fix | A window is created and the process remains running; interactive workflows and clean-install behavior remain unverified |
| Performance budgets are met | Not measured | Unverified | Requires representative workloads |
| Security review is complete | Not completed | Not ready | See threat model and release gate |

## Required next steps
1. Inspect CI for each subsequent feature-branch revision and resolve failures before release.
2. Launch the app on Windows 11 x64; test ARM64 on an appropriate device when available.
3. Test navigation, file launch, copy cancellation, timestamp preservation, and staging cleanup with disposable data.
4. Test access denied, disappearing entries, disk full, conflicts, long paths, Unicode, reparse points, network paths, and interruption.
5. Measure startup, navigation, idle CPU/memory, and copy performance.
6. Update this report with actual machine/runtime details and evidence.

## Local release-hardening verification — 2026-10-17

- Windows host `SEAN-MSI`: Release core suite passed, 339 tests, zero failures or skips. Results are emitted to `TestResults/*.trx` (ignored local artifacts).
- Release x64 application build passed with zero warnings and zero errors using the workspace-local .NET 10 SDK.
- Release ARM64 application build also passed locally with zero warnings and zero errors. Cross-compilation is not an ARM64 device test.
- NuGet vulnerability query across the solution, including transitive dependencies, reported no vulnerable packages from `https://api.nuget.org/v3/index.json` on 2026-10-17. This is advisory-feed evidence, not a complete dependency/license or security audit.
- Five junction-ancestor regression cases exercise copy/move source and destination paths and rename. Four fail against the preceding implementation; all pass with full ancestor checks. Junction creation is asserted, so missing link privileges cannot silently pass these tests.
- Copy, move and rename now reject existing reparse-point ancestors before work starts. This remains a path-based check, not a guarantee against concurrent path replacement or hard-link aliases.
- Archive extraction retains the originating pane, refreshes it only if its location is unchanged, labels ZIP/TAR progress consistently, and defaults confirmation to Cancel. Interactive focus/navigation behavior still needs device testing.
- No interactive workflow, clean-install, ARM64 device, accessibility, disk-full, crash-recovery or performance results are claimed by this local verification.

## Remaining first-release blockers

The release acceleration plan in [ROADMAP](ROADMAP.md#release-acceleration-plan) defines the first release; the complete long-term feature inventory remains future work. Before a stable release, collect runtime and installer evidence, finish the filesystem adversarial/failure matrix, measure performance, audit dependencies, and complete accessibility checks. Cross-volume moves still require stronger destination verification and source-change handling before claiming the Safety Contract is satisfied. Keep PR #37 unmerged until explicitly authorized.

## Published startup blocker resolved

The original publish output omitted the application XAML resource index and exited during main-window loading with status 0xC000027B. Enabling MSIX build tooling for the unpackaged application includes Meridian.pri in publish output. A locally published x64 app then created the Meridian window and remained running. The app now includes WinUI control resources and valid Folder/MoveToFolder symbols. Startup failures write one local last-error.log under LocalApplicationData/Meridian; diagnostics remain on the device and may include exception paths.

CI now requires the application resource index for both architectures and runs a bounded x64 main-window startup smoke check. This detects packaging/startup failures; it does not replace workflow, accessibility or clean-machine testing.
