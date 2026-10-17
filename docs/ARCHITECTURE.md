# Architecture

Baseline and evidence: [STATUS.md](STATUS.md). Target: Windows 11, x64/ARM64, .NET 10 and WinUI 3. Current architecture does not imply satisfaction of release safety gates.

## Components

| Component | Responsibilities |
| --- | --- |
| `src/Meridian.App` | WinUI window/dialogs, navigation, workspace persistence, shell launch/Recycle Bin requests and archive/viewer/TimeVault orchestration |
| `src/Meridian.Core/Services` | Directory reads/sorting, file operations, Drop Shelf, Folder DNA, advisory operation preview and sequential batch copy |
| `src/Meridian.Core/Archives` | Entry path policy, capabilities, ZIP/TAR catalogs, source planning, creation, prefix reads and extraction |
| `src/Meridian.Core/Viewer` | Extension routing, strict text decoding, bounded reads and shared path checks |
| `src/Meridian.Core/TimeVault` | Snapshot models, Windows WMI discovery, volume/path resolution, historical listing and backend-only export/receipt |
| `src/Meridian.Core/Foresight` | Relationship hints, dependency evidence/impact models and bounded JSON string-reference reading |
| `src/Meridian.Core/Consequences` | Advisory plan and receipt contracts, separate from the UI's current operation-preview service |
| `src/Meridian.Core/StorageReclaimer` | Read-only inventory, selection, preview and revalidation; no cleanup execution |
| `tests/Meridian.Core.Tests` | Core success, bounds, corruption, collision, cancellation and selected link regression tests; not a full UI/integration suite |

The core has no WinUI dependency, but several services are Windows-specific. VSS discovery uses Windows WMI through System.Management; passing path/model tests does not prove real snapshot access.

## Concurrency and operations

Directory enumeration runs off the UI thread; cancellation supersedes obsolete reads. UI collections are updated on the UI thread. Copy, batch copy, archive work and snapshot queries are orchestrated asynchronously/background where implemented. Cancellation is cooperative and differs by workflow; archive inspection/preflight is not a fully cancellable background-job UI.

Copy stages files/trees in the destination directory and publishes by no-overwrite move. File timestamps are copied. Managed failures attempt cleanup; crashes, cleanup failures and concurrent path replacement remain outside a transaction guarantee. Rename handles case-only changes through a unique temporary sibling. Move/rename use Windows MoveFileExW with zero flags: no replacement or copy/delete fallback is allowed. Windows determines whether a rename is supported; differing drive letters alone are not a volume-identity test. Cross-volume moves are refused before creating a copy. Long paths are converted to extended Windows paths for the native call. See [file-operation policy](FILE_OPERATIONS.md). New-folder creation now uses the core operation layer: shared new-name validation, ancestor inspection and CreateDirectoryW's fail-if-existing behavior. The UI captures its original pane/parent and refreshes only if that pane remains there. Shell launching and Recycle Bin deletion remain separate UI paths and do not automatically share every core guard.

Drop Shelf copies sequentially and reports completed items and stopping errors/cancellation. Earlier outputs remain; its transient result is not a durable journal. Consequence Engine contracts do not govern every operation. Archive trees and creation outputs are staged; full ZIP extraction verifies length/CRC. Catalogs and prefix reads have different integrity guarantees; see [Archive Engine](ARCHIVE_ENGINE.md). Preview ancestor checks do not establish race-free handle identity; see [threat model](THREAT_MODEL.md).

## Persistence and diagnostics

`src/Meridian.App/Services/WorkspaceStore.cs` stores `%LOCALAPPDATA%\Meridian\workspace.json`: left/right locations, bookmark names/paths, paired tab names/locations and active-tab index. Drop Shelf, sort/filter settings, durable operation history and alternate layouts are not included. Installed and portable copies share this per-user location.

Writes serialize to a unique temporary sibling, then replace the preferences file. This preference replacement is separate from no-overwrite user file operations. A semaphore serializes writes in one store instance, not across processes. Missing/malformed/unreadable state falls back where handled; unavailable tab paths can also produce navigation errors. UI persistence is best-effort, without a versioned migration database or cross-process transaction guarantee.

`App.xaml.cs` records the latest startup/unhandled exception to `%LOCALAPPDATA%\Meridian\last-error.log` with a UTC timestamp. It does not mark the exception handled or provide recovery; native failures before managed initialization may not create a log. The local file is overwritten on the next recorded failure, may include paths and is not a user-action journal.

## Deployment

Project files are authoritative for dependency versions. The app references Windows App SDK 2.5.1; the core references System.Management 10.0.0. `EnableMsixTooling=true` enables this unpackaged app's published XAML resource index; it does not make the distribution an MSIX package. Publish self-contained with explicit runtime/platform and retain the entire output, including `Meridian.pri`.

[Windows CI](../.github/workflows/windows-ci.yml) runs core tests, x64/ARM64 build/publish and PRI assertions, a published x64 startup smoke, then Inno Setup packaging/artifact uploads. ARM64 is cross-built, not runtime-tested by that x64 smoke. See [contributing](../CONTRIBUTING.md), [verification](VERIFICATION_REPORT.md) and [local data](USER_GUIDE.md#local-data-and-privacy).
