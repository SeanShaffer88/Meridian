<div align="center">

# ◈ Meridian

### Beyond File Management.

**A new kind of workspace for exploring, understanding, and organizing what lives on your computer.**

Free · Open source · Offline-first · Windows 11

[![Windows CI](https://github.com/SeanShaffer88/Meridian/actions/workflows/windows-ci.yml/badge.svg)](https://github.com/SeanShaffer88/Meridian/actions/workflows/windows-ci.yml)
[![MIT License](https://img.shields.io/badge/license-MIT-2563eb)](LICENSE)
![Windows 11](https://img.shields.io/badge/Windows%2011-x64%20%7C%20ARM64-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![WinUI 3](https://img.shields.io/badge/interface-WinUI%203-444444)

**[Get a preview build](#try-meridian)** · **[See what works](#what-you-can-use-today)** · **[Explore the vision](#what-makes-meridian-different)** · **[Contribute](#help-shape-meridian)**

</div>

> [!WARNING]
> **Meridian is an early development preview, not a finished release.** Windows x64/ARM64 CI build and core-test workflows are configured, but successful results for the latest development changes and hands-on runtime, installer, and accessibility testing have not been verified. **Back up important data and start with disposable test folders.** Preview installers are unsigned.

---

## File management should feel different

Most file managers answer one question: **Where is my file?**

Meridian is being built to answer more:

- **What do I have?** See a folder's makeup without opening every file.
- **How is it connected?** Work across locations without losing your context.
- **What will happen?** Understand the consequences of an operation before making changes.
- **Can I do this again?** Eventually turn repetitive work into understandable, reusable workflows.
- **What used to be here?** Explore available historical snapshots and recover earlier files with TimeVault (in development).

The goal is not to clone File Explorer with more buttons. It's to make everyday file work **clearer, more capable, and safer**.

## Complete feature & innovation atlas

**Status legend — color used sparingly to convey meaning:**

| Label | Status | Meaning |
| --- | --- | --- |
| **[MAIN]** | ![Implemented](https://img.shields.io/badge/implemented-22863a?style=flat-square) | Implemented on the main development branch; not necessarily hardware-verified |
| **[BRANCH]** | ![Development](https://img.shields.io/badge/in_development-0969da?style=flat-square) | Prototype or code in [PR #37](https://github.com/SeanShaffer88/Meridian/pull/37); not released |
| **[PLANNED]** | ![Planned](https://img.shields.io/badge/planned-8250df?style=flat-square) | Design or roadmap only |
| **[PRINCIPLE]** | ![Quality gate](https://img.shields.io/badge/quality_gate-6e7781?style=flat-square) | Engineering requirement, not a product feature |

Text labels remain in the tree for accessibility, monochrome display, and plain-text copies. Subitems inherit their parent's status unless noted. **None of these labels guarantees real Windows hardware verification.**

```text
MERIDIAN — BEYOND FILE MANAGEMENT
|
+-- 01  Everyday file workspace
|   +-- [MAIN] Two-pane browsing, folder navigation, history and refresh
|   +-- [MAIN] Paired workspace tabs, bookmarks and session restoration
|   +-- [MAIN] Per-pane filtering and independent sorting
|   +-- [MAIN] Keyboard shortcuts and active-pane navigation
|   +-- [MAIN] Copy, move, rename, create folder and basic properties
|   +-- [MAIN] Confirmed Recycle Bin deletion (Windows-dependent)
|   +-- [PLANNED] Flexible pane layouts, folder trees and view modes
|   +-- [PLANNED] Advanced keyboard customization and shell integration
|
+-- 02  Understand and organize
|   +-- [MAIN] Folder DNA: immediate counts, sizes, dates and file types
|   +-- [MAIN] Drop Shelf: temporary cross-folder path collection
|   +-- [MAIN] Operation Simulator: advisory shelf-copy preflight
|   +-- [MAIN] Confirmed sequential batch copy, progress and cancellation
|   +-- [BRANCH] Consequence Engine: advisory plan/receipt contracts
|   +-- [PLANNED] Virtual/Smart Collections, tags and saved rules
|   +-- [PLANNED] FileGraph / Relationship View and explainable links
|   +-- [PLANNED] Workspace Constellations and adaptive navigation
|
+-- 03  Historical exploration — TimeVault
|   +-- [BRANCH] Windows VSS snapshot metadata and WMI discovery
|   +-- [BRANCH] Drive-volume GUID matching and scoped path resolution
|   +-- [BRANCH] Toolbar snapshot picker and limited read-only folder list
|   +-- [BRANCH] Backend-only, single-file no-overwrite recovery export
|   |   +-- Staging file, final atomic no-overwrite publication
|   |   +-- Cancellation and best-effort staging cleanup
|   |   +-- Path, alternate-stream and reparse-point validation
|   +-- [BRANCH] Backend export receipt model (not a UI history)
|   +-- [PLANNED] Full Shadow Copy Explorer and historical file viewer
|   +-- [PLANNED] File Time Machine timeline and snapshot search
|   +-- [PLANNED] Recovery Studio UI, checksums and Difference Lens
|   +-- [PLANNED] Snapshot health and timeline collections
|
+-- 04  Search, inspect and automate
|   +-- [PLANNED] OmniSearch: scoped search and optional indexing
|   +-- [PLANNED] Restore Center: System Restore points and protection
|   +-- [BRANCH] Storage Reclaimer: read-only cleanup plan and bounded directory inventory
|   +-- [PLANNED] Storage Reclaimer: Windows scanning and cleanup UI
|   +-- [PLANNED] Meridian Vaults: encrypted, lockable file storage
|   +-- [PLANNED] Storage X-Ray: disk-usage visualization
|   +-- [PLANNED] HexLab: bounded binary and text inspection
|   +-- [PLANNED] Archive Portal: safe archive browsing/extraction
|   +-- [PLANNED] Integrity Guardian: opt-in checksum verification
|   +-- [PLANNED] FlowForge / Workflow Recipes: inspectable automation
|   +-- [PLANNED] Command Nexus: searchable command registry
|   +-- [PLANNED] Optional explainable, local-first intelligence
|
+-- 05  Connectivity and extension
|   +-- [PLANNED] Device Bridge: removable, mobile and remote providers
|   +-- [PLANNED] Network shares, SFTP and optional cloud connectors
|   +-- [PLANNED] Developer tools, Git integration and comparisons
|   +-- [PLANNED] Customizable views, themes and launchers
|
+-- 06  Safety, privacy and engineering considerations
    +-- [MAIN] No-overwrite defaults and explicit confirmations
    +-- [MAIN] Staged copies and cancellable operations where supported
    +-- [MAIN] Local-first workflow; no required account or telemetry
    +-- [BRANCH] VSS read-only discovery and bounded folder enumeration
    +-- [BRANCH] Recovery path validation and no-overwrite staging
    +-- [PRINCIPLE] No silent elevation or snapshot modification
    +-- [PRINCIPLE] Revalidate at execution; previews are not locks
    +-- [PRINCIPLE] Treat paths, links, archives and metadata as untrusted
    +-- [PRINCIPLE] Bound scans, memory use and background work
    +-- [PRINCIPLE] Accessibility, keyboard use and error clarity
    +-- [PRINCIPLE] Document cancellation, partial success and limits
    +-- [PRINCIPLE] Test on Windows 11 x64 and ARM64 before release
    +-- [PRINCIPLE] Audit dependencies, licenses, packaging and security
```

**Important distinctions:** The Drop Shelf is currently in-memory; Folder DNA analyzes only the loaded folder listing; preflight results can become stale; batch copy can leave earlier completed items after a failure or cancellation; and TimeVault's backend exporter is **not** a recovery button in the app. The VSS path checks are best-effort and do **not** eliminate filesystem race conditions. See the [detailed roadmap](docs/ROADMAP.md), [TimeVault engineering notes](docs/TIMEVAULT.md), [threat model](docs/THREAT_MODEL.md) and [release gate](docs/RELEASE_GATE.md).

## Feature screenshots

As Meridian's features become usable and **verified in a running Windows build**, this README will include genuine screenshots of the interface—not mockups presented as completed functionality.

The screenshot gallery will cover the dual-pane workspace, Folder DNA, Drop Shelf, Operation Simulator, Universal Viewer (including video playback), TimeVault and subsequent tools as each reaches a demonstrable milestone.

**Screenshots are pending:** the current branch's Windows runtime and media playback have not yet been verified, so there are no authenticated captures to publish. See [Screenshot guidelines](docs/SCREENSHOTS.md) for the capture, review and maintenance process.

## What you can use today

These features are **implemented on the main development branch**. They are not a claim of complete real-device validation.

| Experience | What it does |
| --- | --- |
| **Two-pane workspace** | Browse two folders side by side, navigate history, open items, and refresh without arranging multiple windows. |
| **Paired workspace tabs** | Keep two locations together in a tab; restore saved sessions and bookmarks where paths are available. |
| **Instant filtering** | Filter either pane's current listing by name, path, or type, independently and without scanning the whole drive. |
| **Independent sorting** | Sort each pane by name, type, size, or modified date, ascending or descending. |
| **Keyboard-first navigation** | Switch panes, focus location or filter fields, refresh, and navigate history with shortcuts. |
| **Folder DNA — first signature experience** | See a quick profile of the active folder: file/folder counts, known file sizes, date range, and largest type groups. |
| **Drop Shelf** | Collect paths from unrelated folders without moving the originals; remove, clear, or show the location of a collected item. The shelf is in-memory and does not survive an app restart. |
| **Operation Simulator + confirmed batch copy** | Preview shelf items against the active pane’s destination folder, review ready/blocked counts and collisions, then explicitly confirm a no-overwrite sequential batch copy. |
| **Conservative file actions** | Copy, move, rename, create folders, view basic properties, and request Recycle Bin deletion. |
| **Windows preview packages** | CI is configured to produce portable application folders and per-user installers for x64 and ARM64 when runs succeed. |

### Folder DNA: understand a folder at a glance

**Instead of just listing files, Meridian can describe the folder you're looking at.**

Open **Folder DNA** to see an immediate summary of the *already-loaded* directory listing:

- How many immediate files and subfolders it contains
- How much space its immediate files account for
- The oldest and newest file modification dates
- Which file types contribute the most bytes

It does **not** recursively calculate folder sizes, hash files, or scan nested directories. That's intentional: insight shouldn't unexpectedly trigger an expensive drive-wide operation.

### Drop Shelf + Operation Simulator: collect first, decide later

Working across folders no longer has to mean moving files immediately. Select an item, choose **Shelf selected**, then browse elsewhere and collect another. **Preview shelf → active folder** evaluates the entire collection against the destination shown in your active pane, identifying known conflicts without copying, moving, or deleting anything. The preview lists up to 100 individual results while counting the whole batch.

**Batch copying is now available:** Use **Copy shelf…** to recheck the batch and explicitly confirm the destination and item count. Copying is sequential, refuses known preflight blockers, and does not overwrite existing destination files. Originals remain in place. If a later item fails, earlier successful copies are not automatically rolled back. The shelf does not persist between launches. Preview results are advisory; the copy engine rechecks the filesystem at execution time. Live batch progress and cancellation are available: the status area reports completed/total items, and **Cancel batch** requests cancellation at a safe checkpoint. A cancelled batch can leave earlier completed copies in the destination; it does not automatically roll them back.

### A workspace that follows your thinking

```text
     LEFT PANE                         RIGHT PANE
  ┌────────────────────┐            ┌────────────────────┐
  │ Downloads          │            │ Project            │
  │                    │            │                    │
  │  archive.zip       │  Copy →    │  assets/           │
  │  design.png        │  Move →    │  documents/        │
  │  notes.txt         │            │  source/           │
  └────────────────────┘            └────────────────────┘
     Filter · Sort                     Filter · Sort

        Bookmarks + paired workspace tabs
```

*Conceptual text diagram, not a screenshot of the running application.*

## What makes Meridian different

The ideas below define the direction of the project. **Unless explicitly listed as implemented above, they are not yet available on the main branch.** Some may exist only in open pull requests.

| Signature idea | The experience we're working toward |
| --- | --- |
| **Drop Shelf improvements** | Build on the implemented temporary collection with persistence options, clearer batch management, and stronger workflow integration. |
| **Consequence Engine** | Extend the existing copy preflight into explainable operation plans, explicit revalidation and outcome receipts. Initial advisory plan and receipt contracts are in [development](docs/CONSEQUENCE_ENGINE.md); they are not yet wired into the interface. |
| **Smart Collections** | Create dynamic, rule-based views without rearranging the files on disk. |
| **Meridian TimeVault** | Explore Windows VSS snapshots, historical file versions, safe recovery exports, timeline views, and snapshot differences. Foundation code exists on the [TimeVault development branch](https://github.com/SeanShaffer88/Meridian/pull/37); the viewer and recovery UI are not implemented yet. |
| **Relationship View** | Discover explainable connections between files, projects, and metadata. |
| **Workflow Recipes** | Save understandable, reviewable sequences for repetitive tasks. |
| **Adaptive navigation** | Choose the best way to explore: dual pane, Miller columns, comparisons, galleries, and more. |
| **Optional local intelligence** | Get useful suggestions without mandatory cloud processing or silent file modifications. |

**Our rule for innovation:** a feature should solve a real problem, stay understandable, and earn its complexity. Speed, privacy, accessibility, and file safety matter more than spectacle.

### Meridian TimeVault — explore yesterday without risking today

**TimeVault is in development, not a released recovery tool.** PR #37 includes Windows WMI snapshot discovery, volume matching, a toolbar snapshot picker with a limited read-only folder listing, and an unconnected backend-only single-file export service. None of these features has completed real Windows VSS verification.

The proposed experience includes a full read-only **Shadow Copy Explorer**, a **File Time Machine** timeline, **Recovery Studio** with explicit user confirmation, snapshot-wide search, a **Difference Lens** for comparing versions, verified export checksums, and snapshot health diagnostics. These remain planned.

TimeVault will work only when Windows exposes accessible snapshots. VSS is **not a backup system**: snapshots can disappear, may require elevated access, and must not be treated as guaranteed recovery points. TimeVault will not silently create, delete, or modify snapshots.

Explore the [TimeVault engineering plan](docs/TIMEVAULT.md) and [development pull request](https://github.com/SeanShaffer88/Meridian/pull/37).

The planned **Restore Center**, **Storage Reclaimer**, and **Meridian Vaults** add system recovery management, transparent Windows cleanup, and genuinely encrypted storage—not merely hidden folders. Only a **backend-only, read-only cleanup plan and bounded single-directory inventory** are prototyped in PR #37; Windows cleanup scanning, deletion, restore-point management and vault encryption are **not implemented**. Privileged actions and vault security require separate verification. See the [system utilities design](docs/ROADMAP.md#proposed-system-utilities--restore-center-storage-reclaimer-and-meridian-vaults).

The [expanded engineering roadmap](docs/ROADMAP.md#meridian-expansion-program--approved-design-scope) now tracks 16 major initiatives, including FileGraph, Storage X-Ray, FlowForge, OmniSearch, HexLab, File DNA, Archive Portal, Integrity Guardian, Virtual Collections, Device Bridge and Command Nexus. **These are planned capabilities, not currently available features.**

See the full [innovation-first roadmap](docs/ROADMAP.md).

## Try Meridian

**Requirements:** Windows 11 (build 22000 or newer), x64 or ARM64. Windows 10 is not supported.

1. Open the [Windows CI workflow](https://github.com/SeanShaffer88/Meridian/actions/workflows/windows-ci.yml) and select a **successful** run.
2. Find **Artifacts** and choose the package matching your PC:
   - **Intel or AMD:** `meridian-portable-win-x64` or `meridian-installer-win-x64`
   - **Windows on ARM:** `meridian-portable-win-arm64` or `meridian-installer-win-arm64`
3. Extract the downloaded artifact. Run the included installer, or launch Meridian from the extracted portable application folder.
4. Open two **test folders** and explore. Keep independent backups of important files.

> [!NOTE]
> GitHub may require sign-in to download workflow artifacts. CI artifacts expire (currently after 14 days). Installers are **unsigned**, so Windows may warn about the publisher. Only run an artifact if you trust its source. Portable mode does **not yet guarantee** that all settings remain beside the executable.

### Your first two minutes

1. Enter one folder path in the **left** pane and another in the **right**; press **Enter**.
2. Double-click a folder to enter it, or use **Back**, **Forward**, and **Up**.
3. Try each pane's **Filter** and **Sort** controls.
4. Open **Folder DNA** to understand the current folder.
5. Add a few disposable files from different locations with **Shelf selected**, then use **Preview shelf → active folder** to inspect potential conflicts without changing anything.
6. Select a disposable test file and try **Copy L → R** or **Copy R → L**.
7. Save frequently used locations with **bookmarks** or a **new workspace tab**.

### Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `F5` | Refresh both panes |
| `F6` | Switch active pane |
| `Ctrl+L` | Focus the active pane's path |
| `Ctrl+F` | Focus the active pane's filter |
| `Alt+Left` / `Alt+Right` | Back / forward in the active pane |
| `Alt+Up` | Go to the parent folder |

These are current shortcuts, not a claim that configurable key bindings or a universal command palette are implemented.

## Built to respect your files

Meridian is designed around conservative defaults, not irreversible surprises.

| Current behavior | What to know |
| --- | --- |
| **No silent overwrites** | Copy and move reject destination-name collisions. |
| **Staged copying** | Copy writes to temporary `.meridian-*.partial` paths before finalizing. Cleanup is attempted after errors, but is not guaranteed. |
| **Copy cancellation** | Cancellation is checked during supported copy operations; cleanup can still fail. |
| **Recycle Bin requests** | Delete asks Windows to use the Recycle Bin after confirmation. Availability and recovery vary by filesystem and location. |
| **No automatic elevation** | Meridian operates using the current user's access rights. |
| **Local-first design** | No required account, advertising, or telemetry. |

**Important limitations:** There is no general undo engine, automatic restore point, dedicated audit log, post-copy checksum verification, or guaranteed transactional rollback. Cross-volume moves copy first and then remove the source; if removal fails, **both copies may remain**. Reparse-point and filesystem edge cases still require additional validation.

**A future Operation Simulator preview is not a filesystem lock or guarantee**—conditions can change between preview and execution. The underlying operation must still enforce its safety rules.

**Safety is a release blocker, not a preference.** Meridian's [Safety Contract](docs/SAFETY_CONTRACT.md) defines risk tiers, no-overwrite rules, mandatory previews and revalidation, verified recovery claims, failure testing, and strict gates for cleanup, restore management and encrypted vaults. These are **targets**, not claims that the preview already implements them.

For detailed risks and verification requirements, see the [threat model](docs/THREAT_MODEL.md) and [release gate](docs/RELEASE_GATE.md).

## Build from source

Meridian uses **C#**, **.NET 10**, **WinUI 3**, and the Windows App SDK.

On a Windows development machine, install the .NET 10 SDK and the WinUI 3 / Windows App SDK prerequisites. From the repository root in PowerShell:

```powershell
./scripts/test.ps1
./scripts/build.ps1 -Configuration Release -Runtime win-x64

# For Windows on ARM:
./scripts/build.ps1 -Configuration Release -Runtime win-arm64
```

Build output is published under:

```text
src/Meridian.App/bin/<x64|ARM64>/Release/net10.0-windows10.0.22000.0/<win-x64|win-arm64>/publish/
```

Automated builds are not a substitute for testing actual devices, Windows shell behavior, or installation and removal.

## Removing Meridian or recovering from an operation

- **Installer:** Use **Settings → Apps → Installed apps** or the installed uninstaller, where available. Full uninstall verification is still pending.
- **Portable package:** Close Meridian and delete the extracted application folder. Some settings may remain in per-user application data.
- **Deleted a file?** Check the Windows Recycle Bin; restoration is not guaranteed.
- **Copied, moved, or renamed something?** Inspect both locations before manually reversing it. Meridian does not currently provide universal undo.
- **Copy failed?** Check for `.meridian-*.partial` leftovers; verify what they contain before removing anything.

Uninstalling Meridian does **not** reverse file operations.

## Help shape Meridian

Meridian is **MIT-licensed**, free, and open source. The most useful contributions right now are reproducible bug reports, accessibility feedback, real Windows 11 x64/ARM64 testing, and carefully scoped pull requests.

Start with the [roadmap](docs/ROADMAP.md), [architecture](docs/ARCHITECTURE.md), [threat model](docs/THREAT_MODEL.md), and [release checklist](docs/RELEASE_GATE.md). See [CHANGELOG.md](CHANGELOG.md) for recorded changes and [CREDITS.md](CREDITS.md) for third-party acknowledgments.

**Have an unconventional idea that makes working with files meaningfully better? That's exactly the kind of idea Meridian wants to explore.**

---

<div align="center">

**Meridian** · *Your files. A better way to think about them.*

[License](LICENSE) · [Roadmap](docs/ROADMAP.md) · [GitHub Issues](https://github.com/SeanShaffer88/Meridian/issues)

</div>
