# Architecture

- **Meridian.App** — WinUI 3 presentation, pane navigation, file launching, and user actions.
- **Meridian.Core** — filesystem listing and conservative copy logic without UI dependencies.
- **Meridian.Core.Tests** — unit tests for ordering, formatting, copy behavior, and overwrite protection.

The UI performs directory enumeration on a background task and marshals the results back to the UI thread. A cancellation token is used for superseded directory reads. Copy operations stage a file or directory in the destination folder and rename it into place only after the copy completes; this reduces exposure to incomplete final names but is not a full transactional guarantee.

The current copy workflow has no progress reporting or UI cancellation. Do not treat this initial design as a completed safety review.

## Workspace persistence

The app stores the last successfully browsed left- and right-pane paths in `%LOCALAPPDATA%/Meridian/workspace.json`. The file is local to the current Windows user and contains paths only. Writes use a temporary file followed by a replacement move; malformed, missing, or unreadable saved state falls back to the default Documents/Downloads locations. Workspace persistence is best-effort and must not prevent browsing if the preference file cannot be read or written.
