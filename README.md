# Meridian

<div align="center">

<!-- Add assets/banner.png when a real project banner is available. -->
<!-- ![Project Banner](assets/banner.png) -->

**A free, open-source, offline-first file manager for Windows 11.** Browse two folders side by side, organize files without jumping between Explorer windows, and use deliberately cautious copy, move, rename, and delete actions.

[![Windows CI](https://github.com/SeanShaffer88/Meridian/actions/workflows/ci.yml/badge.svg)](https://github.com/SeanShaffer88/Meridian/actions)
[![Latest release](https://img.shields.io/github/v/release/SeanShaffer88/Meridian?display_name=tag&label=release)](https://github.com/SeanShaffer88/Meridian/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
![Windows 11](https://img.shields.io/badge/Windows%2011-x64%20%7C%20ARM64-0078D4)
![C%23 / .NET 10](https://img.shields.io/badge/C%23-.NET%2010-512BD4)
![WinUI 3](https://img.shields.io/badge/UI-WinUI%203-555555)

<!-- Add assets/demo.webp after recording a representative real-device demonstration. -->
<!-- ![Quick Demo Animation](assets/demo.webp) -->

*Demo and performance measurements are not yet published. No operation-speed claim is made without a benchmark.*

[Overview](#overview) · [Safety Guarantees](#safety-guarantees) · [Features](#features) · [Installation](#installation) · [Quick Start](#quick-start) · [Command Reference](#command-reference) · [Rollback](#rollback--uninstallation) · [Roadmap](#roadmap)

</div>

> [!WARNING]
> **Early development — not release-ready.** Automated Windows builds and core tests do not replace hands-on testing. Windows 11 x64 and ARM64 runtime verification, installer testing, and the [release gate](docs/RELEASE_GATE.md) remain outstanding. **Back up important files and try Meridian on disposable folders first.**

## Overview

Windows File Explorer works well for many tasks, but moving between two locations can mean opening and arranging separate windows. Meridian keeps a **left folder and a right folder in one window**, making it easier to see where files are coming from and where they are going.

Meridian is designed to stay local and predictable: it doesn't require an account or a cloud service, and it doesn't implement advertising or telemetry. Its file operations favor refusing a risky action over silently replacing existing data. It is **not** a system cleaner, registry editor, Windows debloater, or command-line tool.

| Default Windows experience | With Meridian |
| --- | --- |
| Arrange multiple Explorer windows to compare folders | Browse two folders in a single window |
| Navigate back to frequently used locations | Save bookmarks and paired workspace tabs |
| Copy or move through separate windows or clipboard steps | Use explicit left-to-right or right-to-left commands |
| Work with Explorer's built-in file actions | Use conservative, no-overwrite operations with confirmation where appropriate |
| Rely on Explorer's own workflow | Use a focused, dark-themed alternative alongside Explorer |

## Safety Guarantees

Meridian manages files **in your user account**. It does not modify Windows services, system policies, or the registry as part of a debloating workflow.

| Protection or behavior | Current implementation | Important limitation |
| --- | --- | --- |
| **Automatic restore points** | **Not available** | Meridian does not create Windows System Restore points. Restore points are not substitutes for file backups. |
| **Dry-run / `--simulate`** | **Not available** | There is no CLI or general simulation mode. Inspect source and destination before confirming. |
| **Audit logs** | **Not available** | No dedicated plain-text/JSON operation log or log-file path is implemented. |
| **No-overwrite copy / move** | **Implemented** | Destination collisions are rejected rather than silently replaced. |
| **Copy staging** | **Implemented** | Copy uses temporary `.meridian-*.partial` paths and attempts cleanup after errors; this is not a transaction guarantee. |
| **Cancellation** | **Copy cancellation implemented** | Checked during file chunks and directory traversal; cleanup may fail. |
| **Recycle Bin deletion** | **Implemented** | Deletion requests the Windows Recycle Bin, subject to Windows and filesystem support. |
| **Reparse-point safeguards** | **Implemented for copy and rename** | Reparse points (links that redirect filesystem access) are rejected for those operations. Other edge cases still need testing. |
| **Permissions** | **Current user** | No administrator elevation is requested by Meridian. |
| **Integrity verification** | **Not available** | There is no post-copy SHA-256/hash comparison yet. Keep independent backups. |

> [!CAUTION]
> **Move, rename, and delete change real files.** Deletion asks Windows to use the Recycle Bin, but recoverability depends on the drive and Windows behavior. A cross-volume move copies to the destination before removing the source; if cleanup fails, both copies may remain. Never treat a preview build or a Recycle Bin as your only backup.

<details>
<summary>Technical Deep Dive & Advanced Flags</summary>

- **System level touched:** User-accessible files and folders selected for operations; locally saved workspace/bookmark settings. No dedicated service, kernel driver, or registry modification engine.
- **CLI flags:** None. `--simulate`, `--restore-point`, and `--audit-log` are **not supported**.
- **Mechanics:** Copy stages the destination before publishing its final name, refuses existing destinations, preserves timestamps, and checks for cancellation. The app uses Windows shell/Recycle Bin behavior for supported actions.
- **Failure recovery:** If cleanup fails, inspect the affected directory for `.meridian-*.partial` entries. Verify files before manually removing anything.
- **Further reading:** [Threat model](docs/THREAT_MODEL.md) · [Architecture](docs/ARCHITECTURE.md).

</details>

## Features

### 📁 Dual-pane browsing

See two directories at once, enter a path directly, go back or forward, move to the parent folder, refresh, and open files through Windows. Folder enumeration runs in the background so browsing doesn't require a recursive scan of every subfolder.

<!-- Screenshot planned: assets/screenshots/dual-pane-browsing.png -->

<details>
<summary>Technical Deep Dive & Advanced Flags</summary>

- **System level touched:** Directories the user browses; file metadata exposed by the filesystem.
- **CLI flags:** None; navigation is controlled in the graphical interface.
- **Mechanics:** `DirectoryReader` enumerates immediate children, displays folders first, and cancels superseded reads. Folder sizes are not calculated recursively.
- **Code:** `src/Meridian.Core/Services/DirectoryReader.cs`.

</details>

### 🗂️ Bookmarks and paired workspace tabs

Save frequently visited folders, and create tabs that remember a left and right location together. Meridian restores the saved session where the paths are still available.

<!-- Screenshot planned: assets/screenshots/workspace-tabs.png -->

<details>
<summary>Technical Deep Dive & Advanced Flags</summary>

- **System level touched:** Per-user local application settings; bookmarked directory paths.
- **CLI flags:** None.
- **Mechanics:** Workspace snapshots preserve tab names, paired paths, bookmarks, and the active tab. Missing saved paths fall back to available user folders.
- **Code:** `WorkspaceStore` and `MainWindow` in `src/Meridian.App/`.

</details>

### 📋 Conservative copying and moving

Choose a file or folder and copy or move it between panes using clearly labeled commands. Existing destination names are protected: Meridian does not silently overwrite them.

<!-- Screenshot planned: assets/screenshots/file-operations.png -->

<details>
<summary>Technical Deep Dive & Advanced Flags</summary>

- **System level touched:** The selected source and destination directories.
- **CLI flags:** None. There is no background transfer-queue CLI.
- **Mechanics:** Copy uses staging and cancellation checks, and refuses destination collisions. Cross-volume moves copy first and only then attempt source removal. Copy and move do not provide a fully transactional rollback.
- **Filesystem artifacts:** A failed copy cleanup can leave a `.meridian-*.partial` item.
- **Status:** A central transfer queue, pause/resume, and hash verification are planned, **not implemented**.

</details>

### ✏️ Rename, new folder, properties, and Recycle Bin

Create folders, rename items, and inspect basic properties without leaving the file manager. Delete requires confirmation and requests the Windows Recycle Bin instead of directly issuing permanent deletion.

<!-- Screenshot planned: assets/screenshots/item-actions.png -->

<details>
<summary>Technical Deep Dive & Advanced Flags</summary>

- **System level touched:** Selected file/folder names and metadata; Windows Recycle Bin for supported deletes.
- **CLI flags:** None.
- **Mechanics:** New-folder and rename actions validate names and refuse collisions. Rename supports case-only changes and rejects reparse points. Properties are read-only and show basic metadata; folder size is not recursively computed.
- **Caveat:** Recycle Bin availability and behavior vary by location, device, and filesystem.

</details>

### 📦 Portable builds and per-user installers

Windows CI produces self-contained application folders and installable packages for both supported processor architectures. These are **preview artifacts**, not signed production releases.

<!-- Screenshot planned: assets/screenshots/installation.png -->

<details>
<summary>Technical Deep Dive & Advanced Flags</summary>

- **System level touched:** Portable publish output or, for an installer, the current user's installation directory and Start Menu shortcuts; an optional desktop shortcut may be offered.
- **CLI flags:** Meridian itself has no CLI flags. The PowerShell **build scripts** accept `-Configuration Release|Debug` and `-Runtime win-x64|win-arm64`.
- **Mechanics:** .NET 10 self-contained publish plus Inno Setup per-user packaging.
- **Artifact names:** `meridian-portable-win-x64`, `meridian-portable-win-arm64`, `meridian-installer-win-x64`, `meridian-installer-win-arm64`.
- **Limits:** Portable app output does **not yet guarantee** all settings remain beside the executable.

</details>

## Installation

**Requirements:** Windows 11 (build 22000 or newer), x64 or ARM64. Windows 10 is unsupported.

1. Open [GitHub Actions](https://github.com/SeanShaffer88/Meridian/actions) and select a **successful** Windows build.
2. Under **Artifacts**, download the installer or portable package matching your PC: `win-x64` for most Intel/AMD computers, `win-arm64` for Windows-on-ARM devices.
3. **Installer:** Extract the downloaded artifact, then run its installer. **Portable:** Extract the archive and launch Meridian from its published application folder.
4. Use disposable test folders first, and keep backups of anything important.

> [!NOTE]
> CI artifacts may require a GitHub sign-in and are retained for a limited time (configured for **14 days**). Installer builds are currently **unsigned**; Windows may display a publisher/security warning. Do not bypass a warning unless you trust the repository, the workflow, and the exact artifact you downloaded.

> [!TIP]
> Want to inspect the source instead? Follow [Build from source](#build-from-source). No mandatory cloud account is required to **run** Meridian.

### Build from source

Install the .NET 10 SDK and the Windows development prerequisites for the project's WinUI 3 / Windows App SDK setup. In PowerShell, from the repository root:

```powershell
./scripts/test.ps1
./scripts/build.ps1 -Configuration Release -Runtime win-x64
# Or, for Windows on ARM:
./scripts/build.ps1 -Configuration Release -Runtime win-arm64
```

The publish directory is:

```text
src/Meridian.App/bin/<x64|ARM64>/Release/net10.0-windows10.0.22000.0/<win-x64|win-arm64>/publish/
```

Builds and core tests running in CI **do not prove** that all UI interactions, devices, and installer/uninstaller flows work correctly on real hardware.

## Quick Start

1. **Browse:** Use the left and right path fields to open two existing folders. Press **Enter** after entering a path.
2. **Navigate:** Use the back, forward, and up buttons. Double-click a folder to open it.
3. **Select:** Click an item in the pane you want to work with.
4. **Copy:** Choose **Copy L → R** or **Copy R → L**, depending on the direction. Use **Cancel copy** if needed.
5. **Move or manage:** Use the labeled **Move**, **New folder**, **Rename**, **Properties**, and **Delete** commands. Read confirmation dialogs before proceeding.
6. **Save your workspace:** Add bookmarks or create a new workspace tab to remember both locations.

> [!TIP]
> Practice with two folders containing disposable sample files. Test a destination-name collision to see the no-overwrite behavior before trusting the workflow with important data.

## Command Reference

Meridian is a **GUI application**, not a command-line utility. This matrix describes the controls that exist today; it does not imply command-line switches or configurable keyboard bindings.

| GUI action | Purpose | Safety or behavior |
| --- | --- | --- |
| Left / right path field | Navigate to a folder | Invalid or missing paths report an error |
| Back / Forward / Up | Browse history or parent folder | Changes navigation only |
| Refresh both | Reload both directory listings | Does not modify files |
| Copy L → R / R → L | Copy the selected item | No overwrite; staged copy; cancellable |
| Cancel copy | Request cancellation of an active copy | Partial-item cleanup is attempted |
| Move L → R / R → L | Move the selected item | Confirmation and destination collision checks |
| New folder | Create a folder | Name validation and duplicate prevention |
| Rename | Rename the selected item | No overwrite; reparse-point restriction |
| Properties | Inspect file/folder details | Read-only |
| Delete | Request Recycle Bin deletion | Confirmation; recoverability not guaranteed |
| Add / Remove bookmark | Manage saved locations | Updates local saved workspace data |
| + New tab / tab close | Manage paired workspaces | Preserves tab locations in local settings |
| Open in Explorer | Open the current location in Windows Explorer | Uses Windows shell |

<details>
<summary>Developer commands and advanced flags</summary>

| PowerShell command | Parameters | Result |
| --- | --- | --- |
| `./scripts/test.ps1` | `-Configuration Debug|Release` (default `Release`) | Runs Meridian.Core tests |
| `./scripts/build.ps1` | `-Configuration Debug|Release` (default `Release`); `-Runtime win-x64|win-arm64` (default `win-x64`) | Restores and publishes the app |
| `dotnet test ./tests/Meridian.Core.Tests/Meridian.Core.Tests.csproj -c Release` | Standard .NET test options | Runs core tests directly |

There is **no** Meridian `--simulate`, `--verbose`, `--uninstall`, `--audit-log`, or `--restore-point` interface.

</details>

## Rollback & Uninstallation

**Uninstalling Meridian is not the same as undoing file operations.**

- **Installer build:** Use Windows **Settings → Apps → Installed apps** to uninstall Meridian if the installation is registered there, or use the uninstaller supplied with the installation. Verify uninstall behavior on your device; it has not yet passed the release gate.
- **Portable build:** Close Meridian and remove the extracted application folder. Workspace/bookmark settings may remain in per-user application data; portable settings isolation is not yet complete.
- **Undoing a deletion:** Check the Windows Recycle Bin and restore the item if present. Some filesystems or locations do not support recovery through it.
- **Undoing a copy:** Verify the destination, then manually remove the unwanted copy if appropriate.
- **Undoing a move or rename:** Verify both paths and manually move or rename the item back. Meridian has no operation journal or universal undo/rollback engine.
- **Failed copy:** Check for `.meridian-*.partial` leftovers. Confirm they are incomplete Meridian staging items before removing them.

> [!CAUTION]
> No automatic restore point, snapshot, or one-click rollback is created. Keep separate backups and do not assume uninstallation restores modified files.

## Roadmap

Meridian has a working foundation, but the long-term plan is broader than the current preview.

| Area | Status |
| --- | --- |
| Dual-pane browsing, bookmarks, paired workspace tabs | **Implemented** |
| Basic new folder, rename, properties, conservative copy/move, Recycle Bin delete | **Implemented; more real-device validation required** |
| Windows x64 and ARM64 CI packaging | **Implemented; hardware/installer verification pending** |
| Live filtering, advanced sorting, keyboard-first navigation | **In progress / planned** |
| Folder tree, flexible pane layouts, central transfer queue | **Planned** |
| Batch rename, search, duplicate detection, preview, archives | **Planned** |
| Network/cloud/device integrations and developer tools | **Planned** |

See the [complete roadmap](docs/ROADMAP.md), [product vision](docs/PRODUCT_VISION.md), and [release checklist](docs/RELEASE_GATE.md). An item appearing on the roadmap is **not** a promise that it already works.

## Architecture & Project Documentation

| Document | What you'll find |
| --- | --- |
| [Architecture](docs/ARCHITECTURE.md) | App structure and design decisions |
| [Threat model](docs/THREAT_MODEL.md) | File-operation and security risks |
| [Performance](docs/PERFORMANCE.md) | Performance notes and measurement limits |
| [Competitive research](docs/COMPETITIVE_RESEARCH.md) | Desk research, **not** hands-on benchmarks |
| [Release gate](docs/RELEASE_GATE.md) | Checks required before a public-ready release |
| [Changelog](CHANGELOG.md) | Recorded changes |

## Contributing

Bug reports, reproducible filesystem edge cases, accessibility feedback, and focused pull requests are welcome. Before proposing a large feature, review the [roadmap](docs/ROADMAP.md) and [architecture](docs/ARCHITECTURE.md). For a file-operation bug, describe the source/destination layout, whether links or network paths were involved, and the observed result; **do not share private paths or file contents unnecessarily**.

## License

Meridian is licensed under the [MIT License](LICENSE). Third-party components retain their own licenses; see [CREDITS.md](CREDITS.md).
