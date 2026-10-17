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
> **Meridian is an early development preview, not a finished release.** Windows x64/ARM64 build, publish, installer and core-test CI passed for application baseline `2eef02a`, including an x64 published-app startup check; later application changes require new evidence. Hands-on runtime, clean-install and accessibility testing remain unverified. **Back up important data and start with disposable test folders.** Preview installers are unsigned.

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

See the [documentation index](docs/README.md), [dated implementation status](docs/STATUS.md), [user guide](docs/USER_GUIDE.md) and [verification report](docs/VERIFICATION_REPORT.md). Design goals and implemented features are tracked separately.

## Complete feature list

Inspired by the clear, category-by-category feature reference used by classic dual-pane file managers, this directory covers **what Meridian has today, what exists in the development branch, and what is on the roadmap**. It is intentionally comprehensive, but it is **not** a claim that every listed feature is available.

**Availability key:** **[MAIN]** implemented in the main branch · **[DEV]** implemented UI or backend prototype code in [draft PR #37](https://github.com/SeanShaffer88/Meridian/pull/37), not released · **[PLANNED]** not implemented · **[REQUIREMENT]** design or release criterion. Even [MAIN] and [DEV] items do **not** imply successful Windows 11 x64/ARM64 runtime verification.

### Jump to a feature category

[General interface](#general-features-and-interface) · [File operations](#file-operations-and-organization) · [Viewer and media](#built-in-viewer-images-audio-and-video) · [Archives](#archives-and-compressed-files) · [Search and intelligence](#search-discovery-and-file-intelligence) · [Recovery](#recovery-history-and-system-maintenance) · [Network and devices](#network-devices-and-extensions) · [Remote transfers](#network-transfer-and-remote-server-roadmap) · [Selection](#advanced-file-selection-and-navigation) · [Metadata](#file-metadata-permissions-and-diagnostics) · [Security and quality](#reliability-security-and-release-readiness)

**Quick distinction:** The feature branch includes ZIP/TAR/TGZ inspection, creation and staged extraction commands. Archive-as-folder navigation remains planned. The media player compiles in both architecture builds; actual playback remains unverified. No external archive application will be required for formats Meridian eventually supports.

### General features and interface

- **[MAIN]** Windows 11 desktop application built with WinUI 3 and .NET 10; x64 and ARM64 are intended targets.
- **[MAIN]** Side-by-side file panes for working across two locations.
- **[MAIN]** Folder navigation, refresh, backward and forward history.
- **[MAIN]** Paired workspace tabs, bookmarks and session restoration.
- **[MAIN]** Per-pane filename filtering and independent sorting.
- **[MAIN]** Active-pane keyboard shortcuts and basic file-selection workflow.
- **[MAIN]** File and folder properties.
- **[DEV]** Integrated preview command for selected files.
- **[PLANNED]** Folder trees, Miller columns, gallery and timeline views.
- **[PLANNED]** Thumbnail grids, customizable columns, multi-selection refinements and richer file badges.
- **[PLANNED]** Configurable toolbars, context menus, themes, layouts and keyboard shortcuts.
- **[PLANNED]** Command Nexus: searchable command palette and reusable commands.
- **[PLANNED]** Workspace Constellations: named layouts and saved working contexts.
- **[PLANNED]** Accessibility enhancements, high-contrast and keyboard-first refinements.
- **[REQUIREMENT]** Fast startup, responsive large-folder navigation, no mandatory account or telemetry.

### File operations and organization

- **[MAIN]** Copy files and folders with conservative no-overwrite behavior.
- **[MAIN]** Move and rename items; create folders.
- **[MAIN]** Delete to Windows Recycle Bin with confirmation where supported.
- **[MAIN]** Staged copying, progress and cancellation for supported copy workflows.
- **[MAIN]** Drop Shelf: collect paths from different folders in a temporary in-memory list.
- **[MAIN]** Operation Simulator: advisory preflight for shelf-based copy operations.
- **[DEV]** Consequence Engine planning and receipt contracts; not a universal undo mechanism.
- **[PLANNED]** Transfer queue with pause/resume, retry policies and detailed per-file receipts.
- **[PLANNED]** Advanced batch rename with tokens, patterns, previews and collision detection.
- **[PLANNED]** Directory comparison and synchronization with dry-run conflict review.
- **[PLANNED]** File splitting/joining and content-based comparison.
- **[PLANNED]** Duplicate detection with hash verification and human-reviewed removal.
- **[PLANNED]** File integrity manifests, checksum generation and verification.
- **[PLANNED]** Tags, rules, virtual collections and non-destructive organization.
- **[PLANNED]** FlowForge: inspectable automation recipes with explicit execution approval.
- **[REQUIREMENT]** Never silently overwrite; revalidate operations at execution time and explain partial success.

### Built-in viewer, images, audio and video

- **[DEV]** Universal Viewer prototype integrated with the selected-file Preview command.
- **[DEV]** Bounded read-only text and hexadecimal previews.
- **[DEV]** Strict UTF-8 and UTF-16 text decoding with binary-content fallback.
- **[DEV]** Image preview with a compressed-file size guard; decoded-pixel limits are not yet established.
- **[DEV]** Audio and video playback prototype using Windows media components, transport controls and no autoplay.
- **[DEV]** Shared conservative path checks that reject reparse-point ancestors; these are not race-free identity guarantees.
- **[DEV]** Unsupported formats receive an explanatory message instead of silently launching another application.
- **[PLANNED]** Dedicated full-window viewer, zoom, rotation, slideshow and image metadata.
- **[PLANNED]** PDF rendering, document previews and archive-entry previews.
- **[PLANNED]** Video subtitles, playlists, playback-speed controls and explicit codec compatibility reporting.
- **[PLANNED]** HexLab: binary search, offsets, signatures, byte-level comparisons and large-file navigation.
- **[REQUIREMENT]** Bounded reads, cancellation, safe decoding and no automatic execution of viewed content.

### Archives and compressed files

- **[DEV]** Conservative archive-entry path validator that rejects traversal, rooted paths, reserved device names and unsafe components.
- **[DEV]** Integrated ZIP/TAR/TGZ inspection, archive creation from selected files/folders, and staged extraction into a new folder using built-in .NET support.
- **[DEV]** ZIP/TAR catalog limits on entry count and declared sizes; duplicate paths, file/parent collisions and unsupported special entries rejected.
- **[PLANNED]** Browse supported archives like virtual folders inside either pane.
- **[DEV]** Whole-tree staged extraction into a new sibling folder; individual-entry/group extraction UI remains planned.
- **[DEV]** ZIP creation and extraction with streamed CRC/length checks; archive update remains planned.
- **[PLANNED]** 7z browsing/extraction and creation where the chosen embedded backend supports it.
- **[PLANNED]** RAR/RAR5 browsing and extraction where supported by an appropriately licensed bundled decoder; **RAR creation is not promised**.
- **[DEV]** TAR and TAR.GZ/TGZ catalog, creation and extraction for supported regular-file/directory entries; broad TAR dialect coverage, standalone compressed streams and BZIP2/XZ/Zstandard remain planned.
- **[PLANNED]** Password-protected archive reading and encryption options, explicitly scoped by format.
- **[DEV]** ZIP CRC/length and TAR/TGZ trailer checks, progress, cancellation, collision rejection and staged publication; broader integrity tooling remains planned.
- **[PLANNED]** Compression-ratio, nesting, total-output, memory and time budgets to resist archive bombs.
- **[REQUIREMENT]** **No separate WinRAR, 7-Zip or other archive-manager installation required.** Ship supported decoding components with Meridian; audit licenses and Windows x64/ARM64 packaging.
- **[REQUIREMENT]** No universal-format promise: advertise exact browse, extract, create and encryption capabilities by format.

#### Archive format support at a glance

This table describes **current implementation versus intended coverage**, not formats that Meridian can already open in the interface. **Catalog** means listing entries without extracting them.

| Format family | Catalog today | Extraction today | Creation today | Intended direction |
| --- | --- | --- | --- | --- |
| ZIP / ZIP64 | **[DEV]** inspection command | **[DEV]** staged extraction | **[DEV]** creation | Virtual-folder navigation and tested codec/ZIP64 coverage |
| 7z | No | No | No | Embedded backend; codec-dependent |
| RAR / RAR5 | No | No | No | Browse and extract if license and decoder permit; no RAR writing promise |
| TAR / PAX / GNU TAR | **[DEV]** bounded catalog | **[DEV]** staged extraction | **[DEV]** creation | Virtual-folder navigation and broader metadata compatibility |
| GZIP / BZIP2 / XZ / Zstandard | **[DEV]** TAR.GZ/TGZ only | **[DEV]** TAR.GZ/TGZ only | **[DEV]** TAR.GZ/TGZ only | Standalone streams and other codecs remain planned |
| Other archive families | No | No | No | Evaluate individually and publish a tested capability matrix |

Catalog listings inspect declared metadata; complete ZIP extraction validates payload length and CRC, and TAR/TGZ processing checks trailing data and gzip checksums. These checks do not authenticate archives or protect against every malformed input. Encryption and uncommon codecs remain unsupported; interactive and architecture-specific runtime verification remains open.

See [Archive Engine design and compatibility](docs/ARCHIVE_ENGINE.md).

### Search, discovery and file intelligence

- **[MAIN]** Folder DNA: immediate metadata summary of the loaded folder listing.
- **[DEV]** Foresight: explainable read-only relationship candidates based on filenames and directories.
- **[DEV]** Foresight: bounded inputs, relationship limits, cancellation and optional time budgets.
- **[DEV]** Foresight: optional bounded JSON reference inspection and dependency-impact hints from explicit evidence.
- **[PLANNED]** OmniSearch: multi-folder, drive and scoped metadata search.
- **[PLANNED]** Content search, saved queries, filters and optional local indexing.
- **[PLANNED]** Search inside supported archives and historical snapshots.
- **[PLANNED]** FileGraph: interactive map of related files, projects and verified references.
- **[PLANNED]** File DNA: signatures, MIME/extension mismatches, hashes and provenance.
- **[PLANNED]** Storage X-Ray: directory-size visualization, treemaps and growth analysis.
- **[PLANNED]** Smart Collections based on saved metadata rules.
- **[PLANNED]** Optional local-first natural-language suggestions with visible evidence.
- **[REQUIREMENT]** Suggestions never grant authority to delete, move or rewrite files.

### Recovery, history and system maintenance

- **[DEV]** TimeVault: read-only Windows Volume Shadow Copy metadata discovery through WMI.
- **[DEV]** TimeVault: volume matching, scoped snapshot paths, toolbar snapshot picker and limited read-only historical folder listing.
- **[DEV]** TimeVault: backend-only single-file export with no-overwrite staging and cancellation.
- **[DEV]** TimeVault: export receipt model; not yet a complete recovery UI.
- **[DEV]** Storage Reclaimer: bounded, read-only temporary-file inventory and advisory cleanup planning.
- **[DEV]** Storage Reclaimer: selection validation and revalidation of preview metadata.
- **[PLANNED]** TimeVault historical search, comparison, version timeline and Recovery Studio interface.
- **[PLANNED]** Windows File History integration and clearly separated recovery sources.
- **[PLANNED]** Restore Center for supported Windows System Restore operations.
- **[PLANNED]** Storage Reclaimer cleanup interface with confirmations and protected-path safeguards.
- **[PLANNED]** Meridian Vaults: encrypted, lockable storage with audited key handling.
- **[PLANNED]** Operation journal and constrained undo where reversal can be verified.
- **[REQUIREMENT]** Never confuse shadow copies with backups or System Restore points; never promise universal recovery.

### Network, devices and extensions

- **[PLANNED]** Improved SMB and Windows network-share workflows.
- **[PLANNED]** SFTP and other remote providers with credential isolation.
- **[PLANNED]** Optional cloud-storage providers and removable/mobile-device integrations.
- **[PLANNED]** Device Bridge capability-aware provider architecture.
- **[PLANNED]** Git and developer-focused metadata, diffs and repository navigation.
- **[PLANNED]** Extensible viewers, providers and automation commands with permission boundaries.
- **[REQUIREMENT]** No implicit upload, background remote access or silent privilege escalation.

### Network transfer and remote-server roadmap

These are **planned**, not present in Meridian today. Remote file handling will require separate provider security and credential-management reviews.

- **[PLANNED]** SFTP browser with saved connection profiles and host-key verification.
- **[PLANNED]** FTPS support with TLS certificate verification; no insecure downgrade by default.
- **[PLANNED]** Optional FTP compatibility for legacy servers, with prominent plaintext-credential warnings.
- **[PLANNED]** HTTP(S) download-to-folder with progress, cancellation and safe destination handling.
- **[PLANNED]** Resume interrupted downloads when the server supports verified range requests.
- **[PLANNED]** Background transfer queue with bounded concurrency, retries and bandwidth controls.
- **[PLANNED]** Proxy configuration and authenticated connection handling.
- **[PLANNED]** Server-to-server transfer only when explicitly supported and approved; no implicit credential forwarding.
- **[PLANNED]** Compare local and remote directory trees before synchronization.
- **[REQUIREMENT]** Credentials must not appear in logs, URLs shown to other users, or exported operation receipts.

### Advanced file selection and navigation

- **[PLANNED]** Select files by wildcard, extension, size, age, attributes or saved rules.
- **[PLANNED]** Invert selection, select all with matching extension and restore previous selection.
- **[PLANNED]** Breadcrumb address bar with editable path, path history and folder suggestions.
- **[MAIN]** Named directory bookmarks; frequent-destination ranking and pinned workspaces remain planned.
- **[PLANNED]** Navigate into archives and supported virtual providers without extracting them.
- **[MAIN]** Live per-pane filtering of loaded items; match highlighting and persistent filter state remain planned.
- **[PLANNED]** Keyboard-accessible file preview, focused-pane indicators and accessible selection counts.
- **[PLANNED]** Multi-tab drag-and-drop with explicit copy/move intent and collision previews.

### File metadata, permissions and diagnostics

- **[PLANNED]** File attributes and timestamp editor with per-field preview and validation.
- **[PLANNED]** Windows ACL inspection and carefully scoped permission editing.
- **[PLANNED]** Alternate data stream inspector with read-only defaults.
- **[PLANNED]** File signature identification independent of extension.
- **[PLANNED]** EXIF, audio tags and video metadata inspection without executing handlers.
- **[PLANNED]** NTFS hardlink and symbolic-link inspection with clear target disclosure.
- **[PLANNED]** Disk and directory statistics with cancellable, bounded scans.
- **[PLANNED]** Optional file-lock diagnostics with clearly disclosed system requirements.
- **[PLANNED]** Copy verification using checksums and detailed mismatch reports.
- **[REQUIREMENT]** Privileged or destructive metadata changes require separate approval and cannot be inferred from read-only inspection.

### Reliability, security and release readiness

- **[MAIN]** Offline-first application design with conservative write defaults.
- **[DEV]** Explicit safety contracts, threat model and release-blocking quality gates.
- **[DEV]** Read-only bounded scanners, preview path validation and security-oriented regression tests.
- **[REQUIREMENT]** Windows 11 x64 and ARM64 compilation, packaging and hands-on runtime validation.
- **[REQUIREMENT]** Verified automated tests, dependency/license audits and malicious-input testing.
- **[REQUIREMENT]** Accessible dialogs, informative errors, safe cancellation and recovery documentation.
- **[REQUIREMENT]** Genuine screenshots captured from verified Windows builds; no mockups represented as implemented features.

For implementation details and limitations, see the [roadmap](docs/ROADMAP.md), [safety contract](docs/SAFETY_CONTRACT.md), [release gate](docs/RELEASE_GATE.md), [Universal Viewer](docs/UNIVERSAL_VIEWER.md), [Foresight](docs/FORESIGHT.md) and [TimeVault](docs/TIMEVAULT.md).

## Feature screenshots

Meridian's feature gallery will show **real captures from verified Windows builds**, never conceptual mockups presented as working functionality. Screenshots are added individually as features become demonstrable and pass the relevant verification gates.

| Feature | What the screenshot should demonstrate | Capture status |
| --- | --- | --- |
| Dual-pane workspace | Both file panes, tabs, navigation and selection | Awaiting verified Windows capture |
| Folder DNA | Folder summary based on the visible directory listing | Awaiting verified Windows capture |
| Drop Shelf | Collected paths from multiple locations | Awaiting verified Windows capture |
| Operation Simulator | Explicit copy preflight and collision review | Awaiting verified Windows capture |
| Universal Viewer — text / hex | Actual bounded preview in a running build | Awaiting verified Windows capture |
| Universal Viewer — image | Native image preview, with source file identified | Awaiting verified Windows capture |
| Universal Viewer — video | Playback controls and a real media file in the running app | Awaiting verified Windows playback validation |
| TimeVault | Snapshot picker and actual read-only snapshot directory listing | Awaiting verified Windows/VSS capture |
| Archive Portal | Real in-app archive browsing | Not yet implemented |
| Search, vaults and recovery tools | Each completed feature in its actual application view | Not yet implemented |

Once a screenshot is verified, replace that feature's status with a linked image and short caption; record the corresponding commit and platform in [Screenshot guidelines](docs/SCREENSHOTS.md). Screenshots must not expose personal filenames, credentials or private data.

## What you can use today

The everyday features below exist on `main`; the dated status document separately identifies additions in PR #37. They are not a claim of complete real-device validation.

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

### Universal Viewer and media player (development branch)

The [Universal Viewer prototype](docs/UNIVERSAL_VIEWER.md) includes bounded text/hex preview, image decoding, and native Windows audio/video controls. It passes Windows x64/ARM64 compilation; **actual playback and interactive preview behavior remain unverified**; PDF rendering and stronger decoder safeguards remain incomplete. A real screenshot will be added here only after verification.

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
| **Meridian TimeVault** | Explore Windows VSS snapshots, historical file versions, safe recovery exports, timeline views, and snapshot differences. Foundation code exists on the [TimeVault development branch](https://github.com/SeanShaffer88/Meridian/pull/37); a read-only snapshot picker/listing exists, while the full historical explorer and recovery-export UI remain planned. |
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

**Important limitations:** There is no general undo engine, automatic restore point, dedicated audit log, post-copy checksum verification, or guaranteed transactional rollback. Move now requires a Windows rename without copy fallback: transfers between volumes are refused without copying/removing the source. Use Copy to retain the original; a verified cross-volume transfer engine remains planned. Reparse-point and filesystem edge cases still require additional validation.

**An Operation Simulator preview is not a filesystem lock or guarantee**—conditions can change between preview and execution. The underlying operation must still enforce its safety rules.

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
- **Copy failed?** Check for `.meridian-*.partial` leftovers; verify contents and that no operation is using them before removing anything.
- **Startup failed?** Check `%LOCALAPPDATA%\Meridian\last-error.log` if present and keep the complete published folder, including `Meridian.pri`.
- **Settings/privacy:** `%LOCALAPPDATA%\Meridian\workspace.json` stores paths, bookmark/tab labels and active-tab selection. Portable copies also use this location. Diagnostics can contain paths; see the [user guide](docs/USER_GUIDE.md#local-data-and-privacy).

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
