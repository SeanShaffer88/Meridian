# Roadmap

Meridian is an early-stage, offline-first file manager targeting Windows 11 only, across x64 and ARM64 architectures. This roadmap is a feature inventory and an ordering guide, not a claim that any unchecked item is implemented. Features will be delivered incrementally, with security, responsiveness, accessibility, tests, and honest Windows CI verification required alongside functionality.

## Current baseline and checkbox meaning

See [STATUS.md](STATUS.md) for the dated implemented/CI/runtime inventory. Checked items below identify delivered increments, not stable-release approval. Unchecked composite items can contain partial implementations; notes identify those parts. Build, automated-test, runtime and release completion are separate evidence levels.

## Meridian Foresight — signature intelligence initiative

The [Foresight design and implementation status](FORESIGHT.md) describes an initial **read-only relationship analyzer** committed on the feature branch, plus future evidence graphs, consequence analysis, optional AI interpretation, and recovery-aware plans. **No AI model, UI integration, or autonomous file operations are implemented.** Every mutation remains subject to the [Safety Contract](SAFETY_CONTRACT.md).

## North star — make file management fundamentally better

**Meridian's long-term goal is to become a workspace for interacting with everything stored on a computer, not merely an alternative to File Explorer.** Pursue ideas that make people ask, "Why don't all file managers work this way?" Originality is valuable only when it improves a real workflow.

- **Understand:** Reveal useful relationships, structure, history, size, and metadata without making users manually inspect hundreds of folders.
- **Navigate:** Support spatial, multi-location, and task-specific views while preserving fast, predictable keyboard-first navigation.
- **Organize without disruption:** Offer virtual collections, a drop shelf, tags, and reusable workspaces without requiring files to be physically relocated.
- **Act with confidence:** Preview complex changes, detect collisions, make progress visible, journal outcomes, and support undo only when genuinely reliable.
- **Automate transparently:** Make repeatable workflows and optional local intelligence useful without silently changing files or uploading private data.
- **Experiment safely:** Prototype unconventional interactions behind explicit opt-in controls, and keep stable daily-use paths reliable.

### Signature experience candidates

These describe broader intended experiences, not promised release dates. Smaller Drop Shelf, Folder DNA and advisory-copy increments already exist; consult [STATUS.md](STATUS.md) for their implemented scope. Validate expansions with usability feedback, measurable performance and safety tests.

| Idea | User benefit | Initial deliverable |
| --- | --- | --- |
| **Drop Shelf** | Gather items from unrelated folders into a temporary collection | Read-only collection first; preview before batch operations |
| **Folder DNA** | Understand a folder at a glance | Cancellable local breakdown by type, size, and age; no implicit recursive scan |
| **Smart Collections** | Reuse saved dynamic views without moving originals | Metadata-only rules with visible scope and refresh behavior |
| **Operation Simulator** | Know what a batch action will change | Conflict-aware dry-run plan with per-item outcomes; never claim atomicity across volumes |
| **File Time Machine** | Understand changes and possible recovery paths | Local operation journal; distinguish journal entries from filesystem snapshots and backups |
| **Relationship View** | Discover associated files and projects | Explainable filename/path/metadata associations; optional hashing |
| **Universal Command Palette** | Access complex tasks quickly | Searchable registered commands with keyboard shortcuts and explicit confirmation for destructive actions |
| **Workflow Recipes** | Repeat common organization tasks | Saveable, inspectable steps with preview and no automatic execution by default |
| **Adaptive Layouts** | Fit the interface to the task | Dual pane, Miller columns, comparison, gallery, and timeline views |
| **Optional Local Intelligence** | Natural-language discovery and organization suggestions | Opt-in, local-first, explainable suggestions; never silently modify or upload files |

### How to prioritize innovation

1. **Reliability before spectacle.** Finish release blockers and maintain usable everyday workflows; isolate experiments from production behavior.
2. **Reusable foundations.** Prefer a tested operation engine, command registry, workspace model, provider abstractions, and cancellable metadata services over isolated feature hacks.
3. **Safety by design.** Default to no overwrite, explicit previews, understandable errors, cancellation, and honest recovery limitations. No blanket claims of universal undo, guaranteed secure deletion, or zero OS footprint.
4. **Privacy and accessibility.** No mandatory accounts, ads, telemetry, paid services, or cloud inference. Keyboard access, assistive technology, and resource efficiency are first-class requirements.
5. **Evidence-based progress.** Track each capability as proposed, prototyped, implemented, CI-tested, and device-verified separately. Avoid unsupported performance claims.

This north star **expands rather than replaces** the existing feature inventory and phase ordering below. Continue incremental pull requests, x64/ARM64 CI, tests, and truthful release gating.

## Meridian expansion program — approved design scope

**Status: planned unless specifically identified as implemented elsewhere.** Approval of a concept does not mean it is shipped. Work remains gated by reliability, measured performance, accessibility, security and real Windows validation. No dates are promised.

| Initiative | First independently testable increment | Longer-term ambition |
| --- | --- | --- |
| **TimeVault Deep Recovery** | Read-only Windows VSS discovery and browsing | File History adapters, historical search, recovery receipts, and unified version timelines |
| **FileGraph** | Explainable local path/name/metadata associations | Interactive graph of duplicates, references and project relationships |
| **Storage X-Ray** | Cancellable directory-size scan with explicit scope | Treemap, growth trends, duplicate review and cleanup simulation |
| **FlowForge** | Typed workflow definitions and dry-run validation | Visual automation designer with preview and explicitly approved execution |
| **OmniSearch** | Bounded metadata query parser and streaming search | Optional local indexing, content search and TimeVault integration |
| **File DNA** | Read-only hashes, signatures and format mismatch reporting | Extensible metadata and provenance analysis |
| **Archive Portal** | Read-only ZIP virtual provider and extraction safety tests, with no external archive-manager installation | Self-contained bundled multi-format archive engine (ZIP, 7z, RAR extraction where permitted, TAR and compression streams), safe creation and encrypted-format support by capability; see [archive contract](ARCHIVE_ENGINE.md) |
| **Transfer Engine** | Structured transfer plans, progress and safe collision handling | Queues, selective pause/resume, verification and reports |
| **Workspace Constellations** | Named layout snapshots and safe restore | Task-oriented workspace collections and portable profiles |
| **File Provenance** | Local operation receipt with clearly defined coverage | Explainable file lineage without claiming to observe all external changes |
| **Virtual Collections** | Metadata-backed cross-folder groups | Rules, tags and dynamic collections without moving originals |
| **HexLab** | Bounded read-only hex and text encoding viewer | Binary diff, byte search and signature inspection |
| **Integrity Guardian** | Opt-in SHA-256 manifest creation and verification | Periodic local checks with privacy-conscious notifications |
| **Device Bridge** | Provider interface and reliable SMB/network handling | Optional MTP, removable-device and remote providers |
| **Command Nexus** | Searchable command registry with shortcuts | Context-aware keyboard-driven workflows |
| **Consequence Engine** | Immutable dry-run plan and collision report | Dependency-aware previews, durable operation receipts and constrained undo |

### Shared architecture contracts

- **Providers** expose capabilities, identity, path rules, cancellation and permissions explicitly; local, archive, snapshot, network and device sources must not be conflated.
- **Planning and execution are separate.** A preview never grants authority to execute; every action revalidates sources, targets, symlinks and destination state.
- **No universal undo promise.** Operation receipts record observed outcomes and only offer reversal when preconditions can be verified.
- **Read-only by default** for history, analysis, binary inspection and remote sources. Scans have bounded memory, scope, progress and cancellation.
- **No invisible privilege escalation or remote data transmission.** Elevated helpers, indexing, hashing and cloud integrations are opt-in and auditable.
- **Each increment needs tests and an honest status** (proposed, implemented, CI-tested, device-verified). Cross-volume operations are not atomic.
- **A small default interface, powerful optional surfaces.** Feature flags and modular services should prevent advanced features from slowing normal browsing.

### Suggested integration sequence

1. Stabilize everyday operations and add shared plan/receipt contracts for the Consequence Engine.
2. Complete TimeVault's read-only provider and introduce provider abstractions.
3. Deliver Command Nexus, File DNA, bounded OmniSearch and Storage X-Ray.
4. Add Virtual Collections, Workspace Constellations, HexLab and Archive Portal.
5. Layer FileGraph, Integrity Guardian, FlowForge and Device Bridge onto verified foundations.

See [TimeVault design](TIMEVAULT.md) for its separate safety gates.

## Delivery principles

- [ ] Keep the core experience fast, dependable, keyboard-friendly, and useful for everyday file management.
- [ ] Keep the product free and open source, with no ads, telemetry, paywalls, mandatory cloud accounts, or required paid services.
- [ ] Prefer Windows-native APIs and optional integrations; make external dependencies explicit and audit their licenses.
- [ ] Treat destructive actions, elevation, network credentials, archives, links, and metadata as security-sensitive.
- [ ] Maintain portable and installable deployment paths; portable mode should keep settings and history beside the executable when feasible.
- [ ] Do not mark features complete until implementation, relevant automated tests, and appropriate Windows verification are recorded.

## Release acceleration plan

Keep the first public release focused on a dependable daily-use workflow rather than implementing every advanced idea in this document. Work in this order:

1. Keep the current branch green: fix compile/test/publish failures and retain the Windows x64/ARM64 CI artifacts.
2. Finish and verify the everyday file workflow: dual-pane navigation, bookmarks/session state, new folder, rename, copy/cancel, move, delete with confirmation, and useful properties.
3. Keep the implemented paired workspace tabs stable and verify session restoration, including invalid/missing saved paths.
4. Deliver both deployment paths: a portable publish folder and a documented installable package. Do not call an unsigned package production-ready.
5. Complete the release gate: test on Windows 11 x64 and ARM64, run filesystem safety checks, verify keyboard/accessibility basics, and publish a versioned changelog/release.

Avoid starting large Phase 2/3 experiments until these release blockers are resolved.

## Phase 0 — Foundation and verification

- [x] Initial dual-pane shell and path navigation
- [x] Background directory enumeration
- [x] Conservative copy path that refuses overwrite
- [x] Core unit-test source
- [ ] Build and run on Windows 11 x64 and ARM64; resolve compiler/runtime issues and record actual CI results
- [x] Cover nested directory copy, destination collisions, self/descendant prevention, and cancellation before a copy starts
- [ ] Test cancellation during an active copy and staging cleanup, disappearing sources, access-denied cases, and reparse-point edge cases
- [ ] Establish operation contracts, structured error reporting, cancellation semantics, and path/reparse-point safety
- [ ] Audit dependencies, permissions, threat model, and supported Windows versions

## Phase 1 — Everyday workflow

- [x] Paired workspace tabs with independent left/right locations and session persistence; later: reordering, locking, pinning, close-left/right, and color accents
- [ ] Flexible one-, two-, and four-pane layouts, including horizontal/vertical splits and adjustable split ratios
- [x] Named directory bookmarks and paired tabs with saved paths/tab names and active-tab index; interactive restoration checks remain open
- [ ] Named workspace collections, alternate pane layouts and complete focus/layout restoration
- [ ] Folder tree sidebar, with shared focus-synchronized or independent per-pane trees
- [ ] Core view modes: icon sizes, list, compact list, tiles, content, and details/grid
- [x] Live case-insensitive filtering and independent name/type/size/modified sorting in each pane; F5/F6, Ctrl+L/Ctrl+F and Alt history/up shortcuts
- [ ] Type-ahead selection, configurable shortcuts and expanded command navigation
- [ ] Central transfer queue with progress, pause/cancel, safe collision policies, and background I/O
- [x] New folder creation with name validation and duplicate checks
- [x] Rename files and folders in place with validation and no-overwrite behavior
- [x] Basic cross-pane move flow with confirmation, destination collision checks, no-overwrite behavior, and cross-volume fallback (automated core tests added; Windows verification pending)
- [x] Confirmed deletion to the Windows Recycle Bin (subject to Windows/filesystem support)
- [x] Read-only properties dialog for basic file/folder metadata (folder size intentionally not recursively calculated)
- [x] Per-user Inno Setup installer and portable x64/ARM64 CI artifacts; portable settings beside executable remains planned

## Phase 2 — Navigation and organization depth

- [ ] Miller Columns / cascading folder navigation
- [ ] Mini/pruned tree containing visited or pinned directories
- [x] In-memory Drop Shelf with advisory preview, confirmed sequential copy, cancellation and partial-result reporting; persistence remains planned
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

- [x] Toolbar Preview dialog for bounded text/hex, images and Windows audio/video controls (build verified; runtime behavior pending)
- [ ] Spacebar quick preview and additional document renderers
- [ ] Dockable universal viewer with text/source syntax highlighting, line numbers, word wrap, and code folding
- [x] Bounded hexadecimal prefix display (4 KiB in UI)
- [ ] ASCII column, binary search, offsets/navigation and endianness controls
- [x] Windows media player controls integrated without autoplay
- [ ] Verify actual codecs/playback and resource/lifecycle behavior on x64/ARM64
- [ ] PDF, Office, ePub, and SVG preview through carefully selected, license-audited components or system handlers
- [ ] Folder peek/popover with top-level contents and optional thumbnails
- [ ] Optional 3D preview for STL, OBJ, and glTF after core preview architecture is stable
- [ ] Keep preview handlers isolated from the main UI where practical and avoid executing active content just to preview it

## Phase 6 — Archives, devices, and virtual filesystems

- [x] ZIP/TAR/TGZ catalog dialogs, source planning, creation and staged whole-tree extraction; selected hostile-input regressions in CI
- [ ] Archive-as-folder navigation, per-entry UI and complete runtime/safety/compatibility verification
- [ ] Additional embedded formats such as 7z, RAR (licensing permitting), ISO, CAB and WIM
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

## Proposed system utilities — Restore Center, Storage Reclaimer and Meridian Vaults

**Status: mostly PLANNED.** Storage Reclaimer has a backend-only read-only inventory/plan/selection/revalidation prototype; no cleanup executor or UI exists. Restore Center and Meridian Vaults remain unimplemented. They will be separately permissioned and opt-in; no feature should silently alter restore points, protected files or system cleanup targets.

### Restore Center — Windows System Restore manager

- [ ] Show System Protection availability, supported volumes, protection status, and restore-point metadata through supported Windows interfaces.
- [ ] Create a restore point on explicit request, reporting disabled protection, policy restrictions, privilege requirements and Windows creation throttling accurately.
- [ ] Provide guarded links or supported workflows for configuring protection, allocating storage, and initiating Windows' own restore experience; never imply that System Restore restores personal documents.
- [ ] If supported and justified, manage existing restore points with conspicuous confirmation, eligibility checks and warnings about loss of recovery options. No bulk deletion by default.
- [ ] Present a unified recovery dashboard distinguishing **System Restore points**, **VSS shadow copies**, **File History**, **Recycle Bin** and **independent backups**; these are different mechanisms and not interchangeable.
- [ ] Avoid automatic elevation, undocumented restore APIs, direct tampering with restore-point storage and claims of guaranteed rollback.

### Storage Reclaimer — transparent disk cleanup and Windows slimming

Inspired by the *category* of tools such as Auslogics Windows Slimmer; **no proprietary implementation or branding will be copied**.

- [ ] Scan supported Windows temporary locations, user caches, Recycle Bin, thumbnails, logs and application leftovers with per-category sizes and confidence labels.
- [ ] Provide a dry-run cleanup manifest: exact locations, estimated recoverable bytes, exclusions, access requirements, and reversibility.
- [ ] Add Windows update/component-store cleanup only through supported Windows servicing or Storage Sense mechanisms, with warnings when uninstall/rollback options are reduced.
- [ ] Consider optional hibernation-file management, optional-feature inventory, language packs and obsolete system components **only** where Windows exposes supported, auditable workflows.
- [ ] Exclude downloads, personal documents, active application data, browser sessions, restore points and backups from default deletion.
- [ ] Require explicit review for high-risk actions; detect active files and concurrent changes; report partial cleanup honestly.
- [ ] Keep a locally stored cleanup receipt and provide before/after measurements, cancellation where supported and safe rollback guidance without promising universal undo.
- [ ] Do not market registry cleaning, indiscriminate driver deletion, forced WinSxS deletion or unsupported servicing hacks as performance optimization.

### Meridian Vaults — real encrypted file storage

- [ ] Design encrypted vault containers or an audited OS-backed encrypted-storage provider; **hidden folders and ACLs alone are not encryption**.
- [ ] Evaluate Windows-native protection options (BitLocker/VHDX, EFS where available) against a portable, independently audited authenticated-encryption design. Document edition, filesystem, account and portability limits.
- [ ] Offer explicit vault creation, unlock, lock, idle auto-lock, read-only access, secure export and recovery-key setup.
- [ ] Use authenticated encryption, per-vault keys, a memory-hard password KDF, authenticated metadata, cryptographic randomness and tested recovery flows; do not invent cryptographic algorithms.
- [ ] Never store plaintext passwords or recovery keys, and do not claim guaranteed secure deletion of source files, temporary files, SSD blocks or backups.
- [ ] Account for crash consistency, concurrent writers, lock behavior, lost passwords, backups, corruption recovery, clipboard/thumbnail leakage and search-index exposure.
- [ ] Complete third-party crypto review, threat modeling, tamper/corruption tests and Windows integration testing **before** offering vaults for sensitive real-world data.

**Delivery gates:** start with read-only inventory and threat-model documents; add explicit action plans and testable services; integrate UI only after Windows CI and real-device tests. Restore-point management, privileged cleanup and vault encryption must not be released on the strength of unit tests alone.

## Cross-cutting quality gates

- [ ] Accessibility and keyboard-only audit
- [ ] Large-directory, network-share, slow-drive, cancellation, and low-resource benchmarks
- [ ] Security review of path traversal, reparse points, archive extraction, links, previews, scripts, credentials, and destructive actions
- [ ] Dependency/license/security audit and attribution updates
- [ ] Automated unit/integration tests plus Windows 11 x64 and ARM64 CI
- [ ] Portable and installable deployment testing
- [ ] Reproducible release artifacts, checksums, versioning, release notes, and documented rollback
- [x] Dated documentation baseline and capability/evidence separation added; ongoing updates required with every relevant change

## Scope notes

Some requested capabilities depend on Windows edition, permissions, file-system behavior, third-party format support, or external software. They will be evaluated rather than promised unconditionally. In particular, no application can guarantee secure erasure from every SSD or storage system; closing another process's file handles can destabilize that process; cloud/remote integrations introduce credentials and network failure modes; and registry/process views should default to read-only. These constraints are part of feature design, not reasons to hide limitations from users.
