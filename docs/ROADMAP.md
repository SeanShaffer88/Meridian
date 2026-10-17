# Roadmap

Meridian is an early-stage, offline-first Windows file manager. This roadmap is a feature inventory and an ordering guide, not a claim that any unchecked item is implemented. Features will be delivered incrementally, with security, responsiveness, accessibility, tests, and honest Windows CI verification required alongside functionality.

## Delivery principles

- [ ] Keep the core experience fast, dependable, keyboard-friendly, and useful for everyday file management.
- [ ] Keep the product free and open source, with no ads, telemetry, paywalls, mandatory cloud accounts, or required paid services.
- [ ] Prefer Windows-native APIs and optional integrations; make external dependencies explicit and audit their licenses.
- [ ] Treat destructive actions, elevation, network credentials, archives, links, and metadata as security-sensitive.
- [ ] Maintain portable and installable deployment paths; portable mode should keep settings and history beside the executable when feasible.
- [ ] Do not mark features complete until implementation, relevant automated tests, and appropriate Windows verification are recorded.

## Phase 0 — Foundation and verification

- [x] Initial dual-pane shell and path navigation
- [x] Background directory enumeration
- [x] Conservative copy path that refuses overwrite
- [x] Core unit-test source
- [ ] Build and run on Windows 11; resolve compiler/runtime issues and record actual CI results
- [ ] Expand safety tests: nested directory copy, collisions, self/descendant copy prevention, cancellation and staging cleanup, disappearing files, and access-denied cases
- [ ] Establish operation contracts, structured error reporting, cancellation semantics, and path/reparse-point safety
- [ ] Audit dependencies, permissions, threat model, and supported Windows versions

## Phase 1 — Everyday workflow

- [ ] Multi-tab browsing, tab reordering, locking, pinning, duplication, close-left/right, and optional color accents
- [ ] Flexible one-, two-, and four-pane layouts, including horizontal/vertical splits and adjustable split ratios
- [ ] Bookmarks/favorites and named workspaces; persist and restore tabs, pane layout, locations, and focus
- [ ] Folder tree sidebar, with shared focus-synchronized or independent per-pane trees
- [ ] Core view modes: icon sizes, list, compact list, tiles, content, and details/grid
- [ ] Live type-ahead filtering, sorting, selection, and useful keyboard shortcuts
- [ ] Central transfer queue with progress, pause/cancel, safe collision policies, and background I/O
- [ ] New folder, rename, copy, move, delete, properties, and open-with flows with clear error handling
- [ ] Portable configuration and settings persistence; installer/package path

## Phase 2 — Navigation and organization depth

- [ ] Miller Columns / cascading folder navigation
- [ ] Mini/pruned tree containing visited or pinned directories
- [ ] Drop shelf/stack for gathering files from multiple locations before a batch operation
- [ ] Flat/branch view across nested directories
- [ ] Per-folder view preferences stored in a local database (columns, sorting, icon size, and supported visual options), rather than depending on fragile registry Bags
- [ ] Color labels, tags, and ratings stored locally; investigate optional NTFS alternate data stream (ADS) storage with explicit portability and privacy warnings
- [ ] File-age visualization using restrained tinting or age bars
- [ ] Disk-usage treemap/sunburst visualization
- [ ] Saved/virtual search folders with live criteria

## Phase 3 — Robust file operations

- [ ] Reorderable transfer queue, pause/resume where technically safe, speed limits, and configurable collision defaults (overwrite, overwrite older, skip, auto-rename)
- [ ] Operation journal, recovery guidance, and undo for operations where reliable reversal is possible
- [ ] Copy-time integrity verification with selectable SHA-256 and other appropriate checksums; document MD5/SHA-1/CRC32 limitations and use cases
- [ ] Directory comparison by relative path, size, timestamp, and optional hash; preview before two-way synchronization
- [ ] Batch rename with live before/after preview, collision detection, regex, case conversion, padded counters, and safe rename plans
- [ ] Import/export filename mapping from text or CSV; optional metadata-based naming for EXIF, ID3, video, and document fields where supported
- [ ] Split and merge files into configurable chunks, with integrity sidecars and clear format documentation
- [ ] Create hard links, file/directory symbolic links, and junctions with privilege, target, and compatibility warnings
- [ ] Asynchronous large-folder deletion with cancellation and honest progress reporting
- [ ] Secure deletion research and feasibility assessment; explain SSD, wear-leveling, snapshots, and backups make guaranteed unrecoverability impossible in many cases
- [ ] Locked-file diagnostics; process-handle discovery first, with closing handles or terminating processes gated by explicit confirmation and strong safety checks
- [ ] Elevated file operations through a narrowly scoped, auditable helper only if needed; never bypass UAC or weaken Windows security controls

## Phase 4 — Search, indexing, and metadata

- [ ] Fast filename/path search and advanced query filters
- [ ] Optional integration with Voidtools Everything through a documented IPC/API boundary
- [ ] Investigate native USN Journal/MFT indexing only with clear permissions, indexing limits, lifecycle handling, and a non-indexed fallback
- [ ] Literal and regex full-text search across supported text/document formats, with matched-line previews
- [ ] Duplicate finder by name, size, and cryptographic hash; optional perceptual image/audio matching as a later extension
- [ ] Safe duplicate-selection helpers such as keep oldest/newest, always with review before deletion
- [ ] ADS explorer to enumerate, inspect, extract, edit, or remove streams, with warnings for security markers such as Zone.Identifier
- [ ] Extensible metadata columns for hashes, Git status, audio/video properties, PDF page counts, PE architecture/headers, and other formats when reliable providers exist

## Phase 5 — Preview and viewing

- [ ] Quick preview popup (Spacebar-style) for supported images, text, Markdown, code, and document formats
- [ ] Dockable universal viewer with text/source syntax highlighting, line numbers, word wrap, and code folding
- [ ] Hex/binary viewer with byte/ASCII representation, search, and endianness display options
- [ ] Media preview/playback for common audio and video formats
- [ ] PDF, Office, ePub, and SVG preview through carefully selected, license-audited components or system handlers
- [ ] Folder peek/popover with top-level contents and optional thumbnails
- [ ] Optional 3D preview for STL, OBJ, and glTF after core preview architecture is stable
- [ ] Keep preview handlers isolated from the main UI where practical and avoid executing active content just to preview it

## Phase 6 — Archives, devices, and virtual filesystems

- [ ] ZIP browsing and safe extraction, followed by other supported formats such as 7z, RAR (subject to licensing/availability), TAR/GZ, ISO, CAB, and WIM
- [ ] In-archive search and direct open/extract workflows; archive modification only where the format and library safely support it
- [ ] SMB/network share browsing with resilient cancellation, timeouts, and offline/error handling
- [ ] Optional remote clients for SFTP, FTP/FTPS, WebDAV, and SCP, with secure credential handling and explicit protocol support boundaries
- [ ] Optional cloud-storage providers (OneDrive, Google Drive, Amazon S3, Nextcloud, Backblaze B2) behind provider interfaces; never require cloud integration for local file management
- [ ] Improve Android MTP/PTP and supported Apple-device browsing/transfers where Windows APIs permit
- [ ] WSL distribution navigation and \wsl$ support; investigate safe read/browse support for EXT4 VHDX images
- [ ] Investigate virtual views for Windows Registry hives and running processes, with strict read-only defaults and prominent warnings

## Phase 7 — Developer and power-user tools

- [ ] Embedded terminal panel for PowerShell, Command Prompt, Git Bash, or WSL, with explicit working-directory synchronization
- [ ] Git repository badges/columns for branch, working-tree status, staged/unstaged changes, ahead/behind, and diff actions
- [ ] Path-copy utilities: Windows, POSIX, UNC, quoted path, filename, directory, file URI, and SHA-256
- [ ] Integrated side-by-side text diff and practical binary comparison
- [ ] Directory compare/sync workflow with clear conflict previews and conservative defaults
- [ ] Optional Windows Sandbox/container launch for a selected folder, only when the required Windows edition/features are available and the user explicitly confirms host-folder exposure
- [ ] Custom toolbars and launchers for external tools/scripts with documented, safely escaped path/selection variables
- [ ] Optional scripting API for automation and rename pipelines, designed with explicit trust boundaries, capability limits, and no silent execution of untrusted scripts
- [ ] Keyboard-centric command set, including configurable shortcuts and optional Norton/Total Commander-style function keys or Vim-like navigation

## Phase 8 — Customization and shell integration

- [ ] Dark/light/high-contrast themes, restrained red accent option, accessibility settings, and reduced-motion considerations
- [ ] Native Windows 11 materials such as Mica/Acrylic where supported and appropriate
- [ ] Custom folder icons and supported per-folder appearance preferences
- [ ] Configurable toolbars, command palette, external launchers, and context-menu actions
- [ ] Shell integration controls that respect Windows security and policy; investigate compact/classic context-menu options without unsupported shell patching or fragile global interception
- [ ] Complete portable mode: local settings, configuration, and history beside the executable when selected; document unavoidable OS-level traces and avoid promising literal zero footprint
- [ ] Installable deployment with clean install, upgrade, repair, and uninstall behavior

## Cross-cutting quality gates

- [ ] Accessibility and keyboard-only audit
- [ ] Large-directory, network-share, slow-drive, cancellation, and low-resource benchmarks
- [ ] Security review of path traversal, reparse points, archive extraction, links, previews, scripts, credentials, and destructive actions
- [ ] Dependency/license/security audit and attribution updates
- [ ] Automated unit/integration tests plus Windows 11 x64 CI; evaluate ARM64 when practical
- [ ] Portable and installable deployment testing
- [ ] Reproducible release artifacts, checksums, versioning, release notes, and documented rollback
- [ ] Keep documentation aligned with actual behavior and clearly label planned, implemented, tested, and platform-dependent features

## Scope notes

Some requested capabilities depend on Windows edition, permissions, file-system behavior, third-party format support, or external software. They will be evaluated rather than promised unconditionally. In particular, no application can guarantee secure erasure from every SSD or storage system; closing another process's file handles can destabilize that process; cloud/remote integrations introduce credentials and network failure modes; and registry/process views should default to read-only. These constraints are part of feature design, not reasons to hide limitations from users.
