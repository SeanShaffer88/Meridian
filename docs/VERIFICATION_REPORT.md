# Verification report

Reviewed 2026-10-17. **Stable-release status: NOT READY.** Automated baseline checks and limited x64 startup passed; complete interactive, clean-machine and security validation remains open.

Application baseline: `2eef02a859058f91cab78615a43de4cd5a327039`, `feature/timevault-foundation`, [draft PR #37](https://github.com/SeanShaffer88/Meridian/pull/37). Documentation revisions do not retroactively change this tested application commit. Later application changes require new evidence.

## Evidence

| Check | Evidence | Result and limit |
| --- | --- | --- |
| Core tests | [Windows CI run 39063293051](https://github.com/SeanShaffer88/Meridian/actions/runs/39063293051), core-tests job; local Release run | Pass; local run: 339 passed, zero failed/skipped. Not a complete adversarial/integration matrix |
| x64/ARM64 build and publish | Same run, both architecture jobs | Pass; local builds/publish also passed with zero build warnings/errors. ARM64 is cross-compiled, not device-tested |
| XAML resource packaging | Same run, both PRI assertions | Pass; Meridian.pri included; previous omission caused main-window startup failure |
| Published x64 startup | Same run, startup smoke; fresh local output on SEAN-MSI, Windows 11 Home x64 10.0.26300 | Pass: process alive for ten seconds and a main window created. Does not verify navigation, media, VSS, dialogs or accessibility |
| Installers and portable artifacts | Same run, both architecture packaging/upload jobs | Generation/upload passed. Install/upgrade/uninstall and clean-system execution unverified |
| Junction ancestors | Five copy/move source/destination and rename regression cases | All pass; junction creation asserted rather than silently skipped. Four fail against preceding implementation. Not proof against path swaps |
| Dependency advisory query | Local `dotnet list Meridian.sln package --vulnerable --include-transitive`, NuGet v3, 2026-10-17 | No known vulnerable packages reported. Not a full dependency/support/license/security audit |
| ZIP/TAR/TGZ integrity | Core regression cases in passing suite | Selected corruption, limits, cancellation and cleanup covered. No blanket codec/ZIP64/PAX/GNU compatibility claim |

CI uploads TRX results and artifacts with 14-day retention; downloads can expire. Job logs identify the SDK selected by floating `10.0.x` setup. Local runs used the workspace .NET 10 SDK; the exact local patch was not preserved in this record and is not claimed.

## Resolved failures

Pre-fix publish omitted the app XAML resource index and exited with `0xC000027B`; managed diagnostics identified failure to locate `ms-appx:///MainWindow.xaml`. MSIX build tooling now includes Meridian.pri for the unpackaged app. WinUI control resources and valid Folder/MoveToFolder symbols are included. A fresh publish opened the window and CI reproduces the startup check.

Copy/move/rename now inspect existing reparse-point ancestors. Archive extraction retains the originating pane, refreshes it only if its path is unchanged and defaults confirmation to Cancel. Pane/focus behavior still needs hands-on testing.

## Subsequent move-safety increment (local evidence)

After the baseline above, Move/rename changed to MoveFileExW with no replacement/copy flags; the unsafe cross-volume copy/delete fallback is gone. The local Release core suite passed 343 tests, zero failures/skips. Four added cases cover same-volume drive aliases, failure with a source held against deletion, and long Unicode rename/move. The locked-source regression fails against the previous implementation because it leaves an unwanted destination copy.

An isolated local generated-fixture check between C: and E: on Windows 11 x64 refused both a file and a directory with Windows error 17; source payloads remained intact and the destination stayed empty. Fixture cleanup was limited to the newly created unique directories. These two checks are separate from the 343 core tests and do not establish ARM64, UNC or race-free behavior. CI for the new application revision must be inspected separately; no new-head CI pass is inferred from baseline results.

## ZIP tree integrity and observer failures (2026-10-17)

Complete ZIP extraction now streams explicit directory entries through the same length/CRC checks as file entries, rejecting nonempty or corrupt directory payloads before publication. Catalog listing remains metadata-only. Progress callbacks are advisory: ordinary observer failures cannot turn an already-published tree into a failed operation, and cancellation requested by an observer still aborts before publication and removes staging. Cancellation after publication does not undo a completed extraction.

Local Windows x64 validation with pinned SDK 10.0.402 and locked restore passed 462 core tests, zero failures/skips. Nine added regressions exercise observer failures during staging and after publication, cancellation from throwing observers, concurrent destination preservation, false directory CRCs and hidden stored directory data. These checks do not establish crash durability, real disk-full recovery, race-free path identity or general malformed ZIP compatibility. Latest-head Windows CI must be checked separately.

## Remaining blockers

| Area | Evidence still needed |
| --- | --- |
| Distribution | Clean install/launch without developer tools, upgrade/uninstall, per-user state, artifact hashes/version/signing policy |
| Everyday workflows | Navigation, missing paths, bookmark/tab restoration, rename, copy/cancel, move, delete and properties with disposable data |
| Data safety | Cross-volume moves are now refused; future support requires destination verification/source stability; disk-full/interruption/access-denied/collision races, UNC/long/Unicode paths, aliases/path swaps and recovery |
| Viewer | Real image/media play/pause/seek/failure/disposal, malformed inputs, resource limits and codec matrix |
| TimeVault | Controlled real VSS snapshots, volume mapping, denied/expired access and export drills before UI recovery |
| ARM64 | Runtime and installer execution on suitable Windows 11 ARM64 hardware/VM |
| Accessibility | Keyboard, focus, screen readers, high contrast, scaling and prompts |
| Performance | Reproducible [measurements](PERFORMANCE.md); startup survival is not a startup benchmark |
| Security/privacy | Dependency/license audit, parser/filesystem review and observed network behavior; no independent review completed |

Follow the focused [first-release sequence](ROADMAP.md#release-acceleration-plan) and [release gate](RELEASE_GATE.md). The extensive long-term roadmap is separate from first-release completion. Open gates must not be inferred to pass merely because source code exists.
