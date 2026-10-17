# Meridian Archive Engine — no external archive manager required

**Status: design / planned. No integrated archive decoder or writer has been shipped or verified.**

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

1. Define archive entry/provider and capability contracts with deterministic path-policy tests.
2. Build read-only ZIP listing and bounded streaming extraction against a bundled or built-in backend, with hostile archive fixtures.
3. Integrate archive navigation and preview into the dual-pane workspace.
4. Add multi-format backends with explicit codec coverage, x64/ARM64 packaging, security tests and license audit.
5. Add archive creation, optional encryption and advanced operations after independent safety review.

**Release gate:** No archive format is advertised as implemented until it passes Windows build/runtime tests, malicious-input regression tests, package-without-external-tools verification, and architecture-specific packaging checks.
