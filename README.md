<div align="center">

# ◈ Meridian

### Beyond File Management.

**An offline-first Windows 11 file manager with dual-pane browsing, Folder DNA, and safer copy previews.**

Free · Open source · Offline-first · Windows 11

[![Windows CI](https://github.com/SeanShaffer88/Meridian/actions/workflows/windows-ci.yml/badge.svg)](https://github.com/SeanShaffer88/Meridian/actions/workflows/windows-ci.yml)
[![MIT License](https://img.shields.io/badge/license-MIT-2563eb)](LICENSE)
![Windows 11](https://img.shields.io/badge/Windows%2011-x64%20%7C%20ARM64-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![WinUI 3](https://img.shields.io/badge/interface-WinUI%203-444444)

**[Get a preview build](#try-meridian)** · **[See what works](#what-you-can-use-today)** · **[Explore the vision](#what-makes-meridian-different)** · **[Contribute](#help-shape-meridian)** · **[Engineering standards](docs/QUALITY-GATES.md)**

</div>

> [!WARNING]
> **Meridian is an early development preview, not a finished release.** Automated Windows x64/ARM64 builds and core tests exist, but hands-on runtime, installer, and accessibility testing is incomplete. **Back up important data and start with disposable test folders.** Preview installers are unsigned.

---

## Start here

| Your goal | Next step |
| --- | --- |
| Try a Windows preview | Follow [Try Meridian](#try-meridian) for CI artifacts, architecture selection, and launch requirements. |
| See implemented features | Read [What you can use today](#what-you-can-use-today); future ideas are listed separately. |
| Understand a folder | Explore [Folder DNA](#folder-dna-understand-a-folder-at-a-glance), based on the loaded directory listing. |
| Collect files before copying | Try [Drop Shelf and Operation Simulator](#drop-shelf--operation-simulator-collect-first-decide-later) using disposable test files. |
| Build or contribute | Use the [source build guide](#build-from-source) and [contribution guidance](CONTRIBUTING.md). |

**Preview scope:** Windows 11, C#/.NET 10, and WinUI 3. Automated x64/ARM64 packaging exists; hands-on runtime and accessibility acceptance remain incomplete. See [verification status](docs/VERIFICATION_REPORT.md) before relying on a workflow.

## File management should feel different

Most file managers answer one question: **Where is my file?**

Meridian is being built to answer more:

- **What do I have?** See a folder's makeup without opening every file.
- **How is it connected?** Work across locations without losing your context.
- **What will happen?** Understand the consequences of an operation before making changes.
- **Can I do this again?** Eventually turn repetitive work into understandable, reusable workflows.

The goal is not to clone File Explorer with more buttons. It's to make everyday file work **clearer, more capable, and safer**.

## What you can use today

These features are **implemented on the main development branch**. They are not a claim of complete real-device validation.

| Experience | What it does |
| --- | --- |
| **Two-pane workspace** | Browse two folders side by side, navigate history, open items, and refresh without arranging multiple windows. |
| **Session restore** | The two panes reopen where you left them; bookmarks are restored where paths are available. |
| **Instant filtering** | Filter either pane's current listing by filename or type, independently and without scanning the whole drive. The reusable core filter can also match full paths, but the pane UI deliberately excludes that option. |
| **Independent sorting** | Sort each pane by name, type, size, or modified date, ascending or descending. |
| **Keyboard-first navigation** | Switch panes, focus location or filter fields, refresh, and navigate history with shortcuts. |
| **Folder DNA — first signature experience** | See a quick profile of the active folder: file/folder counts, known file sizes, date range, and largest type groups. |
| **Drop Shelf** | Collect paths from unrelated folders without moving the originals; remove, clear, or show the location of a collected item. The shelf is in-memory and does not survive an app restart. |
| **Operation Simulator + confirmed batch copy** | Preview shelf items against the active pane’s destination folder, review ready/blocked counts and collisions, then explicitly confirm a no-overwrite sequential batch copy. |
| **Conservative file actions** | Copy, move, rename, create folders, view basic properties, and request Recycle Bin deletion. |
| **Universal Viewer** | Preview text (UTF-8/UTF-16), hex, and images, and play audio/video with Windows media controls and no autoplay. Reads are bounded and read-only; playback behavior is not yet verified on real devices. |
| **Archives** | Inspect ZIP/TAR/TGZ contents read-only, create an archive from selected files and folders, or extract into a new no-overwrite folder under entry-policy and size limits, with cancellation. |
| **TimeVault (read-only)** | Discover existing Windows shadow copies and list a snapshot's version of the current folder. Discovery only — no recovery operations, and real VSS access is unverified. |
| **Windows preview packages** | CI produces portable application folders and per-user installers for x64 and ARM64. |

### Folder DNA: understand a folder at a glance

**Instead of just listing files, Meridian can describe the folder you're looking at.**

Open **Folder DNA** to see an immediate summary of the *already-loaded* directory listing:

- How many immediate files and subfolders it contains
- How much space its immediate files account for
- The oldest and newest file modification dates
- Which file types contribute the most bytes

It does **not** recursively calculate folder sizes, hash files, or scan nested directories. That's intentional: insight shouldn't unexpectedly trigger an expensive drive-wide operation.

### Drop Shelf + Operation Simulator: collect first, decide later

Working across folders no longer has to mean moving files immediately. Select an item, choose **Shelf selected**, then browse elsewhere and collect another. **Preview shelf → active folder** evaluates the entire collection against the destination shown in your active pane, identifying known conflicts without copying, moving, or deleting anything. The preview shows up to 100 individual results while counting the whole batch.

**Batch copying is now available:** Use **Copy shelf…** to recheck the batch and explicitly confirm the destination and item count. Copying is sequential, refuses known preflight blockers, and does not overwrite existing destination files. Originals remain in place. If a later item fails, earlier successful copies are not automatically rolled back. The shelf does not persist between launches. Preview results are advisory; the copy engine rechecks the filesystem at execution time. Per-item batch progress and cancellation are available: the status area reports completed/total items, and **Cancel batch** requests cancellation during execution at supported checkpoints. The initial confirmation preflight is not cancellable from this button on `main` (see PR #41). A cancelled batch can leave earlier completed copies in the destination; it does not automatically roll them back.

### Universal Viewer and media player

The [Universal Viewer](docs/UNIVERSAL_VIEWER.md) includes bounded text/hex preview, image decoding, and native Windows audio/video controls without autoplay. It passes Windows x64/ARM64 compilation; **actual playback and interactive preview behavior remain unverified**; PDF rendering and stronger decoder safeguards remain incomplete.

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

        Bookmarks + restored session
```

*Conceptual text diagram, not a screenshot of the running application.*

## What makes Meridian different

The ideas below define the direction of the project. **Unless explicitly listed as implemented above, they are not yet available on the main branch.** Some may exist only in open pull requests.

| Signature idea | The experience we're working toward |
| --- | --- |
| **Drop Shelf improvements** | Build on the implemented temporary collection with persistence options, clearer batch management, and stronger workflow integration. |
| **Operation Simulator improvements** | Extend the implemented read-only copy preflight into richer previews, additional operations, and safe execution with explicit confirmation. |
| **Smart Collections** | Create dynamic, rule-based views without rearranging the files on disk. |
| **File Time Machine** | Understand recorded changes and available recovery paths, without pretending an operation log is a backup. |
| **Relationship View** | Discover explainable connections between files, projects, and metadata. |
| **Workflow Recipes** | Save understandable, reviewable sequences for repetitive tasks. |
| **Adaptive navigation** | Choose the best way to explore: dual pane, Miller columns, comparisons, galleries, and more. |
| **Optional local intelligence** | Get useful suggestions without mandatory cloud processing or silent file modifications. |

**Our rule for innovation:** a feature should solve a real problem, stay understandable, and earn its complexity. Speed, privacy, accessibility, and file safety matter more than spectacle.

### Meridian TimeVault — explore yesterday without risking today

**TimeVault is in development, not a released recovery tool.** It includes Windows WMI snapshot discovery, volume matching, a toolbar snapshot picker with a limited read-only folder listing, and an unconnected backend-only single-file export service. None of these features has completed real Windows VSS verification.

The proposed experience includes a full read-only **Shadow Copy Explorer**, a **File Time Machine** timeline, **Recovery Studio** with explicit user confirmation, snapshot-wide search, a **Difference Lens** for comparing versions, verified export checksums, and snapshot health diagnostics. These remain planned.

TimeVault works only when Windows exposes accessible snapshots. VSS is **not a backup system**: snapshots can disappear, may require elevated access, and must not be treated as guaranteed recovery points. TimeVault will not silently create, delete, or modify snapshots.

Explore the [TimeVault engineering plan](docs/TIMEVAULT.md).

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

### Installation and troubleshooting

- **No download under Artifacts?** Select a completed, successful workflow run. GitHub may require sign-in, and artifacts expire after the workflow's configured retention period (currently 14 days).
- **Windows displays an unknown-publisher warning?** The preview installer is unsigned. Verify the repository and build provenance before deciding whether to run it; don't disable system-wide security protections.
- **App does not start?** Confirm Windows 11 build 22000+, choose the architecture matching your device, extract the *entire* portable artifact before launching `Meridian.exe`, and include the OS build, architecture, artifact run URL, and exact error when reporting a bug.
- **A file operation fails?** Check source/destination access and collisions. Meridian intentionally refuses known overwrites; after a partial batch, inspect the destination before retrying. Do not delete a `.meridian-*.partial` path before determining whether it contains recoverable data.
- **Portable settings?** The current workspace file is stored under `%LOCALAPPDATA%\\Meridian\\workspace.json`; a completely self-contained portable settings mode is not yet verified.

See [known limitations and verification status](docs/VERIFICATION_REPORT.md), [security reporting](SECURITY.md), and [contributing instructions](CONTRIBUTING.md).

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

**An Operation Simulator preview is not a filesystem lock or guarantee**—conditions can change between preview and execution. The underlying operation must still enforce its safety rules.

For detailed risks and verification requirements, see the [threat model](docs/THREAT_MODEL.md) and [release gate](docs/RELEASE_GATE.md).

## Build from source

Meridian uses **C#**, **.NET 10**, **WinUI 3**, and the Windows App SDK.

On a Windows development machine, install the .NET 10 SDK and the WinUI 3 / Windows App SDK prerequisites. From the repository root in PowerShell:

```powershell
./scripts/test.ps1
./scripts/build.ps1 -Configuration Release -Runtime win-x64

# To cross-publish for Windows on ARM (native device testing still required):
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

## Project stewardship and support

**Author & Principal Architect:** Sean Shaffer  
**Contact & security inquiries:** [seanshaffer88@proton.me](mailto:seanshaffer88@proton.me)  
**Repository & portfolio:** [github.com/SeanShaffer88](https://github.com/SeanShaffer88)  
**Optional project support:** [Buy Me a Coffee](https://buymeacoffee.com/seanspawn88)

Sponsorship is voluntary and does not affect Meridian's open-source license, access to features, or security-report handling. For sensitive vulnerability reports, use the contact email rather than posting exploit details in a public issue.

## Help shape Meridian

Meridian is **MIT-licensed**, free, and open source. The most useful contributions right now are reproducible bug reports, accessibility feedback, real Windows 11 x64/ARM64 testing, and carefully scoped pull requests.

Start with the [roadmap](docs/ROADMAP.md), [architecture](docs/ARCHITECTURE.md), [threat model](docs/THREAT_MODEL.md), and [release checklist](docs/RELEASE_GATE.md). See [CHANGELOG.md](CHANGELOG.md) for recorded changes and [CREDITS.md](CREDITS.md) for third-party acknowledgments.

**Have an unconventional idea that makes working with files meaningfully better?** Open a focused issue describing the workflow, proposed behavior, and important safety cases.

---

<div align="center">

**Meridian** · *Your files. A better way to think about them.*

[License](LICENSE) · [Roadmap](docs/ROADMAP.md) · [GitHub Issues](https://github.com/SeanShaffer88/Meridian/issues)

</div>
