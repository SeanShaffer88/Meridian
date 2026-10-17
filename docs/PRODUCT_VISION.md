# Meridian Product Vision

> **Status: aspirational specification, not a feature-completion statement.** This document captures the intended direction for Meridian. The current application is an early-stage C#/.NET 10 and WinUI 3 project; most capabilities below are not implemented. The implementation and architecture may evolve as features are prototyped and evaluated.

## Supported platform

Meridian targets **Windows 11 only**, across its supported processor architectures: **x64 and ARM64**. Windows 10 is outside the support target. Architecture support must be verified with separate builds and, before release, runtime checks on suitable Windows 11 systems.

## Product goal

Meridian aims to combine the speed and workflow depth of classic power-user file managers with a modern Windows 11 interface. It should make everyday file management efficient without sacrificing safety, clarity, accessibility, or user control.

## Product principles

- **Local-first and offline-capable:** core local-file workflows must not require an account, cloud service, or network connection.
- **Free and open source:** no advertising, telemetry, paywalls, or mandatory paid services.
- **Safe by default:** explain destructive operations, avoid silent overwrites, use conservative defaults, and treat links, archives, credentials, elevation, and metadata as security-sensitive.
- **Responsive:** long directory scans, searches, previews, and transfers should run off the UI thread and support cancellation where feasible.
- **Transparent:** show meaningful progress and errors; never claim integrity verification, secure deletion, or recovery unless the implementation can substantiate it.
- **Native where practical:** favor supported Windows APIs, document optional integrations, and audit third-party dependencies and licenses.
- **Portable and installable:** aim to support both a conventional installation and a portable mode with clearly defined configuration locations.

## Intended capability areas

These are goals, not claims that the features currently exist.

### 1. Workspaces and navigation

- One-, two-, and four-pane layouts, with horizontal/vertical splits and adjustable proportions.
- Multi-tab browsing, tab pinning/locking, tab groups, and saved workspace profiles.
- Bookmarks and favorites, folder trees, optional pruned/mini trees, and Miller Columns.
- A drop shelf for gathering items from several folders before a batch operation.
- Keyboard-first navigation, customizable shortcuts, and optional Vim-style movement.

### 2. File operations and transfer queue

- A central background queue for copy and move operations, with progress, cancellation, and pause/resume only where safe.
- Clear collision choices such as skip, overwrite, overwrite-older, and auto-rename, with conservative defaults.
- Copy verification using suitable checksums such as SHA-256, with algorithm limitations explained.
- Rename, create-folder, delete, properties, and open-with workflows.
- Directory comparison and reviewed synchronization; links and junctions with target and privilege warnings.
- Operation history and undo where reliable reversal is possible.
- Secure-deletion research with honest limitations, especially on SSDs and storage with snapshots or wear-leveling.
- Locked-file diagnostics and, only if justified, a narrowly scoped and auditable elevated helper.

### 3. Search and metadata

- Fast filename/path search and optional integration with Voidtools Everything through a documented boundary.
- Literal and regular-expression content search with result previews.
- Duplicate discovery by name, size, hashes, and potentially perceptual media matching.
- Saved searches/virtual folders and rich metadata columns for formats such as Git repositories, media, images, PDFs, and executable files.
- Optional NTFS Alternate Data Stream inspection with explicit warnings for security-related streams.
- Any USN Journal or MFT indexing must have clear permissions, lifecycle handling, limitations, and a non-indexed fallback.

### 4. Views and organization

- Details, list, compact, tile, content, and icon views with useful sorting and filtering.
- Flat/branch browsing, disk-usage treemaps or sunbursts, color labels, ratings, and file-age visualization.
- Per-folder view preferences stored in a robust local format rather than depending on fragile shell view state.

### 5. Preview and data tools

- Spacebar quick preview for supported images, text, Markdown, code, and documents.
- Dockable text/source and hex viewers; common audio/video preview where supported.
- Carefully selected preview providers for PDF, Office, SVG, and potentially 3D formats.
- Batch rename with previews, regular expressions, counters, and optional metadata tokens.
- File split/join with documented formats and integrity checks; practical image and text conversion tools where dependencies and quality are acceptable.

### 6. Archives, devices, and remote storage

- Browse and safely extract common archive formats, expanding format support only when library licensing and safety are clear.
- Resilient support for Windows network shares and optional SFTP, FTP/FTPS, WebDAV, and cloud-storage providers.
- WSL path navigation, and investigation of supported mobile-device browsing.
- Treat archive traversal, symbolic links, remote credentials, timeouts, and interrupted transfers as security and reliability concerns.
- Registry/process virtual views, if pursued, should default to read-only and carry prominent warnings.

### 7. Developer and power-user workflows

- Optional embedded terminal with explicit working-directory synchronization.
- Git status and metadata columns, text/binary diff, and directory compare/sync.
- Path copying in Windows, POSIX, UNC, quoted, and URI forms, plus optional file hashes.
- Optional Windows Sandbox launch only when supported and after explicit confirmation of host-folder exposure.
- A scripting or extension system may be considered after core APIs, permission boundaries, and compatibility guarantees are established.

## Architecture direction

The current codebase is organized into a WinUI 3 application (src/Meridian.App), a UI-independent core (src/Meridian.Core), and core tests (tests/Meridian.Core.Tests). Future components should be introduced incrementally behind clear interfaces, with tests and measured justification.

The long-term design may use native Windows APIs and carefully chosen managed or native components. Rust, C++, embedded scripting runtimes, archive libraries, preview engines, and other substantial dependencies are **options to evaluate**, not current dependencies or commitments. Do not add a subsystem solely to match a proposed architecture diagram; first establish a concrete requirement, a security/licensing review, and a maintainable integration boundary.

## Delivery and acceptance

Features should be delivered in small, verifiable increments. Before marking a feature complete:

1. Implement the user-facing workflow and define its behavior for errors, cancellation, and partial failure.
2. Add automated tests for core logic and important edge cases.
3. Run the relevant Windows CI build and tests; record results rather than assuming success.
4. Check keyboard access, accessibility, responsiveness, and filesystem safety where applicable.
5. Update the README and roadmap so implemented, experimental, and planned capabilities remain clearly distinguished.

See [ROADMAP.md](ROADMAP.md) for the phased feature inventory, [ARCHITECTURE.md](ARCHITECTURE.md) for the current structure, and [THREAT_MODEL.md](THREAT_MODEL.md) for security considerations.
