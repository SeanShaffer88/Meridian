# Preview user guide

Applies to the development baseline in [Implementation status](STATUS.md), not a stable release. Use disposable folders and independent backups while release gates remain open.

## Install or run

Windows 11 build 22000 or newer is the target; choose x64 for Intel/AMD or ARM64 for Windows on ARM. Windows 10 is outside the support target. Choose a successful [Windows CI run](https://github.com/SeanShaffer88/Meridian/actions/workflows/windows-ci.yml), check its commit/branch, and download the matching `meridian-portable-win-x64`, `meridian-portable-win-arm64`, `meridian-installer-win-x64` or `meridian-installer-win-arm64` artifact. GitHub sign-in may be required; artifacts expire after 14 days.

Extract the artifact before use. Keep the entire portable folder together, including `Meridian.pri`, runtime libraries and subfolders, and launch `Meridian.exe`. The self-contained publish is intended to avoid a separate .NET installation; clean-machine verification remains open. The unsigned per-user installer targets `%LOCALAPPDATA%\Programs\Meridian`, adds a Start menu shortcut and offers an optional desktop shortcut. It does not request administrator privileges. Installation, upgrade and uninstall behavior still require hands-on testing.

## Navigation and selection

Enter a directory path and press Enter in either pane. Double-click a folder to enter it; double-click a file invokes its Windows shell association and can execute applications or scripts. Use **Preview** for the separate read-only preview path. Back, Forward, Up and Refresh operate on folder listings. Filters inspect only the already-loaded listing; they are not a whole-drive search. Each pane can sort by name, type, size or modified date in either direction.

Paired workspace tabs keep two folder locations together. Bookmarks store named locations. The preview allows up to 128 tabs and 512 bookmarks. Saved state is limited to 1 MiB; malformed, excessive or invalid entries are ignored as a whole on restoration, while a failed save leaves the previous file in place and reports a warning. The lists allow extended selection, but most everyday actions operate on the selected item; do not assume every toolbar command processes all highlighted items. Archive creation explicitly reads the active pane's selection.

| Shortcut | Action |
| --- | --- |
| F5 | Refresh both panes |
| F6 | Switch active pane |
| Ctrl+L | Focus active path |
| Ctrl+F | Focus active filter |
| Alt+Left / Alt+Right | Back / forward in active pane |
| Alt+Up | Parent folder |

## File operations

Copy and move use the opposite pane's current directory and refuse destination-name collisions. Copy stages data before final publication; **Cancel copy** requests cooperative cancellation. Rename supports case-only changes without intentional overwrite. New folder creates exactly one new folder in the captured parent and refuses existing items. New folder and Rename reject reserved Windows device names (including superscript COM/LPT names), trailing periods, surrounding whitespace and invalid components rather than silently normalizing them. Properties shows basic metadata without calculating recursive folder size. Delete asks for confirmation and requests the Windows Recycle Bin; availability and recovery depend on the location and filesystem.

Move and rename require a Windows rename without copy/delete fallback and without replacing an existing destination. A transfer between different volumes is refused with an explanatory error, without creating a copy or removing the source. Different drive letters can identify the same volume, so Windows decides whether the rename is supported. Use Copy to retain the original; no checksum-verified cross-volume move, general undo or backup is provided. See [file-operation policy](FILE_OPERATIONS.md).

Copy/move/rename reject reparse-point paths and existing linked ancestors. These checks are path based and can race concurrent changes; they do not prove canonical identity or protect against every alias.

## Drop Shelf and Folder DNA

**Shelf selected** collects a path without moving it. Remove, Clear and Prune missing change the shelf only. Show location navigates to a collected item. **Preview shelf → active folder** checks known conflicts; **Copy shelf…** repeats preflight and asks for confirmation before sequential copying. **Cancel batch** stops at cooperative checkpoints. Earlier completed copies remain after cancellation or later failure, and the result dialog reports partial outcomes. The shelf is lost on restart. A preview does not lock the filesystem.

**Folder DNA** summarizes the current loaded listing: immediate counts, known file sizes, modification dates and type groups. It does not scan nested directories, calculate recursive folder sizes or hash files.

## Viewer

Select a regular file and choose **Preview**. Text previews read up to 64 KiB with strict UTF-8 or BOM-marked UTF-16 decoding; invalid/binary content falls back to hex. Hex display is capped at 4 KiB. An incomplete character at a prefix boundary can trigger fallback. Images larger than 64 MiB on disk are refused; decoded-pixel limits and parser isolation are not implemented. Audio/video use Windows media controls with autoplay disabled and depend on installed codecs; extension routing is not proof of playback support. PDF shows an explanatory unsupported-renderer message. No external application or codec installer is launched by preview.

## Archives

- **Inspect Archive:** select `.zip`, `.tar`, `.tar.gz` or `.tgz` to list validated entries. This is a dialog, not archive navigation in a pane.
- **Create Archive:** select files/folders in the active pane, choose ZIP, TAR or TAR.GZ in the confirmation dialog. One source uses its filename stem; multiple sources use `Selection`, with the chosen archive extension, in that pane's directory. The dialog has no custom filename field. Existing output is refused. Folder selection is recursive; empty directories are not preserved by the file-map planner. **Cancel Create** requests cancellation.
- **Extract Archive:** select a supported archive and confirm extraction into a new sibling directory named from the archive stem. Existing output folders are refused. **Cancel Extract** requests cancellation. The UI extracts the whole tree, not individual selected entries.

ZIP payload length/CRC and TAR/TGZ trailing-data/gzip checks are corruption checks, not authenticity guarantees. No archive update, encryption, 7z/RAR decoder, standalone gzip, BZIP2, XZ or Zstandard support exists. See [format coverage, budgets and staging](ARCHIVE_ENGINE.md).

## TimeVault and experimental backends

**TimeVault** requests existing Windows VSS snapshot metadata and can list the active folder's historical counterpart read-only. The UI shows at most 250 entries from a backend listing capped at 10,000; the displayed subset is not a complete search. No snapshots may be available, and access can be denied or a snapshot can disappear. Drive-letter paths are the current scope. The UI cannot export/open historical files or create/delete snapshots. VSS snapshots are not independent backups. Real VSS workflow verification remains open.

Foresight, Consequence Engine contracts and Storage Reclaimer inventory/planning are backend prototypes without their own UI. Storage Reclaimer does not delete files or run Windows servicing. Vault encryption, restore-point management, AI automation and remote-transfer providers are not implemented.

## Local data and privacy

| Location | Contents and behavior |
| --- | --- |
| `%LOCALAPPDATA%\Meridian\workspace.json` | Pane paths, bookmark names/paths, tab names/paths and active-tab index; updated as workspace state is saved |
| `%LOCALAPPDATA%\Meridian\last-error.log` | Most recent successfully recorded unhandled/startup exception as bounded JSON (128 KiB maximum), with UTC timestamp and truncation indicators. Replaced on the next successful record; failed logging can leave an older record. Not a full operation journal. Can include local paths |
| In memory | Drop Shelf and transient operation results; not restored after restart |

Portable and installed copies use the same per-user state location. There is no implemented telemetry or mandatory account. Local/UNC paths can cause normal Windows network filesystem access, and opening a file delegates to its configured application; offline-first does not mean every OS or external handler is network-isolated. Review diagnostics before sharing them. Close Meridian and back up `workspace.json` before removing it to reset saved state. Uninstall does not reverse file operations or promise removal of per-user preferences/logs.

## Troubleshooting and recovery

| Symptom | Next step |
| --- | --- |
| App exits before a window opens | Check `last-error.log` and artifact commit. Builds before the `2eef02a` packaging fix could omit the XAML resource index. Re-extract a complete successful current artifact; copying only the executable is insufficient |
| Empty/missing saved location | Enter an existing folder path; missing or invalid saved locations may fall back or report a navigation error. Persistence is best-effort |
| Copy/archive conflict | Choose another destination/name, or inspect the existing item. There is no overwrite option in these flows |
| Cancel/error/interruption | Inspect final destinations and staging leftovers. Cleanup is best-effort; earlier batch results remain. Do not delete a leftover until you know no operation is using it and have checked its contents |
| Media fails | The extension may lack a Windows codec. Record the container/codec and exact error; Meridian does not install codecs automatically |
| TimeVault empty/denied | Accessible VSS snapshots may not exist or permissions may prevent access. Meridian does not create snapshots or silently elevate |
| Unexpected move/delete outcome | Inspect both locations and the Recycle Bin as applicable. Keep backups; there is no universal rollback |

Staging names include `.meridian-*.partial` for copy, `.meridian-extract-*` / `.meridian-tar-extract-*` for tree extraction and other `.meridian-*.tmp` names for staged archive/recovery operations. These names are diagnostic hints, not permission to bulk-delete matching files.

For a bug report, include the commit/artifact, Windows build/architecture, reproducible disposable-data steps, exact error and expected outcome. Remove private paths/content. Report vulnerabilities through [SECURITY.md](../SECURITY.md).
