# Implementation status

Reviewed 2026-10-17 on `feature/timevault-foundation`. Fully CI-verified application baseline: `2eef02a859058f91cab78615a43de4cd5a327039`; the subsequent move-safety increment below has separate local evidence. Work is in [draft PR #37](https://github.com/SeanShaffer88/Meridian/pull/37). This is a dated baseline, not a statement about later commits or the latest released version. No stable release is verified.

## Evidence levels

- **Implemented:** code exists; UI integration is identified separately below.
- **CI verified:** the specified automated checks passed on the specified commit.
- **Runtime smoke:** a window started and the process remained alive for ten seconds. This does not verify workflows.
- **Device/workflow verified:** requires recorded hands-on evidence; this baseline does not establish it.
- **Planned:** an intention or release requirement, not working functionality.

## Capability inventory

| Area | Implemented at this baseline | Integration and limits |
| --- | --- | --- |
| Navigation | Dual panes, back/forward/up, refresh, independent filtering and sorting, paired workspace tabs, bookmarks | App UI; no folder tree, archive-as-folder navigation or alternative pane layouts |
| File operations | Staged copy with cancellation, rename, move, new folder, properties, confirmed Recycle Bin request | Mostly single selected item; no queue, pause/resume, general undo or checksum-verified copy. Move/rename use a Windows rename without copy/delete fallback; unsupported cross-volume moves are refused |
| Drop Shelf | In-memory collected paths, pruning, location navigation, advisory preflight, confirmed sequential batch copy, progress/cancel and partial-result display | App UI; successful earlier items remain when a later item fails or the batch is cancelled; no durable journal |
| Folder DNA | Summary of the already-loaded directory listing | App UI; immediate items only, no recursive disk usage scan |
| Viewer | Bounded strict text/hex preview, image display, Windows audio/video controls with no autoplay | App UI compiled; actual decoding/playback, accessibility and lifecycle scenarios remain unverified. PDF renderer absent |
| Archives | ZIP and TAR/TGZ inspection, file/folder source planning, archive creation and staged whole-tree extraction | App UI; formats/limits in [Archive Engine](ARCHIVE_ENGINE.md). Single-entry ZIP readers/extractors are backend APIs, not UI entry selection |
| TimeVault | WMI snapshot discovery, drive/volume matching, picker and limited historical directory listing | Read-only UI; real VSS access unverified. Single-file export and receipt contracts are backend only |
| Foresight | Bounded relationship hints, target/dependency impact contracts, opt-in JSON string-reference reader | Backend only; no AI model, UI, autonomous mutation or verified dependency discovery |
| Consequence Engine | Advisory batch-copy plan and outcome receipt contracts | Backend only; separate from the existing Drop Shelf preview/executor; no durable journal or universal undo |
| Storage Reclaimer | Bounded nonrecursive inventory, immutable plans, selection/preview/revalidation | Backend only; no deletion, cleanup UI, Windows servicing or elevation |
| Distribution | Self-contained x64/ARM64 publish folders and per-user Inno Setup installers | CI builds artifacts; unsigned, clean-install/upgrade/uninstall unverified. Portable uses per-user settings |

The [roadmap](ROADMAP.md) and [product vision](PRODUCT_VISION.md) include extensive future scope. Their design requirements must not be read as current guarantees.

## Verified baseline

[Windows CI run 39063293051](https://github.com/SeanShaffer88/Meridian/actions/runs/39063293051) passed core tests, x64/ARM64 build/publish, resource-index checks, installer generation and artifact upload, plus x64 published startup. Local verification passed 339 core tests (zero failures/skips), both architecture builds/publish, and a fresh-output x64 startup smoke on Windows 11 Home x64 build 10.0.26300. NuGet's advisory query reported no known vulnerable dependencies on 2026-10-17; this is not a complete audit.

See [verification evidence and remaining gaps](VERIFICATION_REPORT.md). No ARM64 device execution, clean-machine install, complete interactive workflow, media codec matrix, accessibility audit, performance benchmark or independent security review is established.

## Subsequent move-safety increment

The unsafe cross-volume copy/delete fallback has been removed. Move and rename require a no-replacement Windows rename; cross-volume failures preserve sources without publishing a copy. Four new regressions cover same-volume drive aliases, locked-source failure and long Unicode paths. Local suite: 343 passed, zero failures/skips. Separate generated-fixture C: to E: checks refused file and folder moves with source contents intact and destination empty. This is limited evidence, not proof against concurrent path swaps or complete filesystem compatibility. See [FILE_OPERATIONS.md](FILE_OPERATIONS.md) and the [verification report](VERIFICATION_REPORT.md) for CI separation.

## Documentation maintenance

When behavior changes, update the relevant feature document, [user guide](USER_GUIDE.md), README availability labels, changelog and this baseline as applicable. Record the exact application commit and CI run; never carry an old passing result forward as proof for a new revision. Keep release requirements separate from evidence, and split partially implemented roadmap items instead of checking off the whole ambition.
