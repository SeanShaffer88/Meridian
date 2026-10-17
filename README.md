# Meridian

Meridian is a free, open-source, offline-first dual-pane file manager for Windows 11. The goal is a dependable daily-use tool with conservative file operations, clear feedback, and no ads, telemetry, mandatory accounts, or paid dependencies.

> **Status: active development; not yet release-ready.** CI builds and tests the project on Windows runners. Interactive testing on Windows 11 x64 and ARM64 devices, packaging validation, and the release gate are still required.

## Implemented foundation

- Dual-pane browsing with editable paths, parent/back/forward navigation, and refresh.
- Background directory enumeration with directory-first sorting and cancellation of superseded reads.
- Basic file launching through the Windows shell.
- Local bookmarks and persistence of the last browsed locations.
- New-folder creation with name validation and duplicate checks.
- In-place file and folder rename, including case-only changes, with validation and no-overwrite behavior.
- Cross-pane file and directory copy with staging, cancellation, timestamp preservation, and refusal to overwrite existing destinations.
- Reparse points are rejected by copy and rename operations rather than followed.
- Dark UI with restrained red accents. The app does not implement telemetry or cloud services.

This is a functional foundation, not a claim that all advanced file-manager features are complete. See the [Roadmap](docs/ROADMAP.md) and [Product Vision](docs/PRODUCT_VISION.md).

## Platform support

- **Target OS:** Windows 11 only, minimum build 22000.
- **Architectures:** x64 and ARM64.
- **Framework:** .NET 10 and WinUI 3.
- Windows 10 is not a supported target.
- ARM64 runtime behavior still needs validation on Windows 11 ARM64 hardware or an appropriate test environment.

## Build and test

Run from PowerShell at the repository root:

```powershell
./scripts/test.ps1
./scripts/build.ps1 -Configuration Release -Runtime win-x64
./scripts/build.ps1 -Configuration Release -Runtime win-arm64
```

The Windows CI workflow runs core tests and publishes self-contained app outputs for both architectures. Successful workflow runs attach downloadable `meridian-portable-win-x64` and `meridian-portable-win-arm64` artifacts for 14 days. These are unpackaged portable publish folders, **not signed installers**. Download them from the run's Artifacts section after all jobs succeed.

The build script's publish path is under `src/Meridian.App/bin/Release/net10.0-windows10.0.22000.0/<runtime>/publish`.

## Safety limitations

- Meridian runs with the current user's privileges and does not request administrator elevation.
- Copy refuses to overwrite existing destinations and stages data before exposing the final destination name; this is not a fully transactional guarantee.
- Copy cancellation is checked between chunks and directory entries. Cleanup is attempted on failure, but a failed cleanup may leave a `.meridian-*.partial` item.
- Copy integrity is not yet verified with hashes; access-denied, disappearing-source, large-transfer, and other filesystem edge cases still need broader validation.
- Do not use a development build with irreplaceable data. Test first with disposable folders and keep independent backups.

## Research and architecture

- [Competitive research and feature matrix](docs/COMPETITIVE_RESEARCH.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Threat model](docs/THREAT_MODEL.md)
- [Performance notes](docs/PERFORMANCE.md)
- [Release gate](docs/RELEASE_GATE.md)
- [Roadmap](docs/ROADMAP.md)

Competitor notes are desk research, not hands-on benchmarking.

## License

MIT. See [LICENSE](LICENSE). Third-party dependencies remain under their own licenses; see [CREDITS.md](CREDITS.md).
