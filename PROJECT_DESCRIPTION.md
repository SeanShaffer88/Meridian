# Meridian — Project Description

Meridian is an early Windows 11 file-manager preview built with .NET 10 and WinUI 3 for x64 and ARM64. It includes dual-pane navigation, paired tabs/bookmarks, filtering/sorting, conservative file actions, Folder DNA and an in-memory Drop Shelf with reviewed batch copying. The development branch also integrates bounded previews, Windows media controls, ZIP/TAR/TGZ archive commands and read-only TimeVault snapshot discovery/listing, plus backend analysis and cleanup-planning prototypes.

## Product principles
- Fast navigation and responsive UI.
- Safe defaults around destructive file operations.
- Offline-first; no telemetry, ads, paywalls, or mandatory services.
- Minimal dependencies and transparent security posture.
- Feature depth earned through tests and measured performance, not unchecked feature count.

At the [documented application baseline](docs/STATUS.md), Windows CI passed core tests, x64/ARM64 build/publish and installer generation, plus an x64 published startup check. This is not a finished or stable release: clean installation, interactive workflows, ARM64 runtime, accessibility, performance and complete security validation remain open. Backend-only modules and the long-term roadmap must not be advertised as working UI features. See the [documentation index](docs/README.md), [user guide](docs/USER_GUIDE.md) and [verification report](docs/VERIFICATION_REPORT.md).
