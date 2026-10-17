# Meridian Archive Engine — no external archive manager required

**Status: early backend prototype on the feature branch.** Built-in .NET backends support bounded ZIP and TAR/TGZ cataloging, creation, and extraction, with Windows core tests. The compatibility table below remains a target contract, not a claim that every listed format is implemented.

Complete ZIP extraction checks the streamed payload against both its declared length and CRC-32 before reporting success. Single-file and tree staging discard failed output; the direct single-file API removes its partial file on managed failure. Catalog listings and bounded prefix previews do not verify complete payload integrity. CRC detects accidental corruption; it does not authenticate an archive.

TAR/TGZ cataloging reads regular-file payloads in 8 KiB chunks, checks cancellation between reads and rejects premature EOF. This adds a full payload read for uncompressed TAR cataloging; it does not checksum TAR payloads. Synchronous filesystem reads and parser metadata processing remain outside the between-chunk cancellation guarantee. Extraction reuses one 8 KiB payload buffer across files.

The [verified baseline](STATUS.md) includes the app commands **Inspect Archive**, **Create Archive** and **Extract Archive** for ZIP/TAR/TGZ. Both architecture builds and selected core regressions pass CI; hands-on archive workflows and broad format compatibility are not verified. These dialogs are not archive-as-folder navigation.

## Implemented capability matrix

| Format | Inspect | Whole-tree extraction | Creation | Entry prefix preview |
| --- | --- | --- | --- | --- |
| ZIP | UI and backend catalog | UI, staged new folder | UI, selected files/folders planned into a file map | Backend only |
| TAR | UI and backend catalog | UI, staged new folder | UI, regular-file map | Not implemented |
| TAR.GZ / TGZ | UI and backend catalog | UI, staged new folder | UI creates TAR.GZ; backend also accepts TGZ | Not implemented |
| Standalone GZIP, BZIP2, XZ, Zstandard, 7z, RAR | Not implemented | Not implemented | Not implemented | Not implemented |

Single-entry ZIP direct/staged extraction APIs exist but are not connected to entry-selection UI. No archive update, encrypted read/write, virtual-folder navigation or separate full integrity-test command exists. The .NET ZIP backend is not evidence that every ZIP64/codec/encryption combination is tested. TAR accepts supported regular-file/directory records and rejects links/special entries; comprehensive PAX/GNU dialect and metadata preservation remain unverified. Routing uses extensions and parser behavior, not a general signature-based capability detector.

## Current limits and semantics

| API/workflow | Default limit or behavior |
| --- | --- |
| ZIP and TAR catalogs | 10,000 entries, 4 GiB aggregate declared uncompressed bytes; catalog API entry limit configurable from 1 to 100,000 |
| ZIP tree extraction | 10,000 entries / 4 GiB total by default; caller-configurable arguments, streamed length/CRC enforcement |
| TAR tree extraction | Uses default bounded catalog and revalidates streamed entries; no separate caller budget arguments |
| ZIP single-entry extraction | 256 MiB selected-entry default; catalog limits also apply |
| ZIP prefix reader | 64 KiB default, caller range 1 byte–1 MiB; no complete-entry integrity guarantee |
| Source planner used by UI | 10,000 files, 4 GiB total, default 10,000 traversed directories; rejects duplicate/overlapping selected sources and links; empty directories omitted |
| Creators | ZIP uses a default 4 GiB byte budget and bounded source map; TAR accepts 1–10,000 files and a 4 GiB aggregate budget |
| TAR trailing data | Remaining decompressed padding must be zero and no more than 1 MiB; draining forces gzip trailer checks |

Catalog size/count checks do not constitute complete memory/CPU defenses: a ZIP central directory may be parsed before application limits are enforced, and TGZ listing itself decompresses data. Compression-ratio, time, path-depth and nested-archive budgets are not fully implemented. Metadata and paths can change between checks and opening; no handle-identity guarantee is made.

Tree extraction refuses an existing final folder and publishes a staged directory only after successful processing. Managed failures attempt cleanup of `.meridian-extract-*` or `.meridian-tar-extract-*`; crash/cleanup failures can leave data behind. Direct single-entry ZIP extraction is a backend exception to whole-tree staging: a partial destination exists during copying and is removed on managed failure where possible.

Creation publishes a new archive without replacing an existing output. API progress supports byte reporting for creation; the current UI displays status/cancellation but does not wire the creators' byte-progress callbacks. Tree extraction reports per-entry progress, capped below 100 until publication. Progress-observer exception handling differs by API; do not assume arbitrary observers are harmless across every ZIP path. Cancellation is cooperative and cannot undo an already-published result.

## Product contract

Meridian must browse and extract supported archives without asking users to install WinRAR, 7-Zip, or command-line tools. Any native libraries required for decoding must be packaged with Meridian itself for Windows 11 x64 and ARM64. An optional system-installed archive manager must never be a prerequisite.

An archive is a read-only virtual folder by default. Browsing, searching entries, viewing metadata, previewing bounded content, and selecting entries for extraction should use the same workspace conventions as local folders. Creation, updating, and encryption are separate capabilities, advertised only when the embedded backend supports them.

## Compatibility model

| Family | Intended capability | Caveat |
| --- | --- | --- |
| ZIP / ZIP64 | Browse, extract, create | Encryption methods and uncommon codecs require separate validation |
| 7z | Browse, extract, create where backend permits | Codec, encryption, solid archive and memory limits vary |
| RAR / RAR5 | Browse and extract when licensing-compatible backend permits | Writing RAR must not be promised |
| TAR, PAX, GNU TAR | Browse, extract, create | Preserve and validate entry type and metadata |
| GZIP, BZIP2, XZ, Zstandard | Decode compressed streams and recognize tar combinations | These are not all multi-entry containers |
| Other formats | Capability discovery via embedded backend | No blanket claim that every archive or codec works |

Choose and audit a maintained, redistributable backend with compatible licensing, signed or reproducibly sourced binaries, and proven x64/ARM64 support. Investigate libarchive and other alternatives, including license obligations and real RAR compatibility, before selecting a production dependency. Do not download codecs or execute external programs at runtime.

## Safety invariants

1. **Treat archive metadata as hostile.** Reject absolute paths, drive prefixes, UNC paths, traversal components, alternate data streams, ambiguous separators, reserved device names, and destination escapes. Normalize before planning and revalidate before each write.
2. **Do not follow archive links by default.** Reject symlinks, hardlinks, device nodes, junction-like entries and unsupported special metadata unless an explicit audited safe policy exists.
3. **No overwrite by default.** Stage output, enforce destination identity/collision rules, and publish without replacing existing files. Clearly report partial extraction.
4. **Bound work.** Enforce configurable per-entry and total uncompressed size, compression ratio, entry count, path depth, CPU/time, memory and nested archive limits. Abort on decompression bombs and malformed input.
5. **Never execute content.** Archive previews use bounded read-only streams. Passwords remain in memory only as needed, never in logs or telemetry.
6. **Cancellation and receipts.** Support cooperative cancellation, cleanup of partial staging data, progress reporting and an auditable summary of extracted, skipped and rejected entries.
7. **No trust from extensions.** Detect format from signatures and report mismatch. Use constrained parsers and fuzz malformed archive headers.
8. **Explicit per-format support.** Distinguish browse, extract, create, modify, encrypted read and encrypted write. Unsupported features must fail clearly.

## Delivery sequence

1. Define archive entry/provider and capability contracts with deterministic path-policy tests (initial entry-name policy committed).
2. Build read-only ZIP listing (initial backend committed) and bounded streaming extraction against a bundled or built-in backend, with hostile archive fixtures.
3. Integrate archive navigation and preview into the dual-pane workspace.
4. Add multi-format backends with explicit codec coverage, x64/ARM64 packaging, security tests and license audit.
5. Add archive creation, optional encryption and advanced operations after independent safety review.

**Release gate:** No archive format is advertised as implemented until it passes Windows build/runtime tests, malicious-input regression tests, package-without-external-tools verification, and architecture-specific packaging checks.
