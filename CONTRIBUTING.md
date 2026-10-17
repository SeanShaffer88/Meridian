# Contributing

Thanks for considering Meridian. The project is early-stage; correctness and safe file behavior matter more than adding features quickly.

## Before submitting
- Explain the user problem and expected behavior.
- Keep changes focused and avoid unnecessary dependencies.
- Add tests for file operations, error handling, and regressions.
- Never claim a test passed unless it was run; include environment and command output when relevant.
- Document limitations and recovery behavior.
- Avoid telemetry, advertising, paywalls, or mandatory paid services.

## Local checks
Use Windows 11, the .NET SDK version pinned in `global.json` (currently `10.0.402`) and the WinUI/Windows App SDK build prerequisites. SDK roll-forward is disabled. The application is Windows-specific; cross-compiling ARM64 on x64 does not verify ARM64 execution. Record `dotnet --info` with local evidence.

From the repository root in PowerShell:

```powershell
./scripts/test.ps1 -Configuration Release
./scripts/build.ps1 -Configuration Release -Runtime win-x64
./scripts/build.ps1 -Configuration Release -Runtime win-arm64
```

`test.ps1` restores the core test project in locked mode, runs it without another restore and fails on a nonzero result. `build.ps1` restores the app and its project dependencies in locked mode for the selected x64/ARM64 runtime, then publishes self-contained output without another restore under `src/Meridian.App/bin/<platform>/Release/net10.0-windows10.0.22000.0/<runtime>/publish/`. It does not build the Inno Setup installer or execute runtime tests. Run each architecture build separately.

Commit NuGet lock files with dependency changes. Runtime-specific restores use `packages.win-x64.lock.json` or `packages.win-arm64.lock.json`; non-runtime restores use `packages.lock.json`. To intentionally update dependencies, run the corresponding restore without `--locked-mode`, review the resolved versions/content hashes and commit the affected lock files. Generate the app locks with the matching `Platform`, `RuntimeIdentifier` and `SelfContained=true` properties used by CI. Restore audits direct/transitive dependencies and fails on NU1900–NU1905 warnings, including known vulnerabilities and unavailable audit information. A clean audit does not replace license review or a complete security assessment.

For retained test evidence:

```powershell
dotnet test tests/Meridian.Core.Tests/Meridian.Core.Tests.csproj -c Release --logger "trx;LogFileName=core-tests.trx" --results-directory TestResults
```

Launch the published `Meridian.exe` with the entire output folder present, particularly `Meridian.pri`. `EnableMsixTooling` is required for resource publication even though the app is unpackaged. A successful compilation alone previously missed a real startup crash. CI now asserts the PRI on both architectures and checks x64 startup for ten seconds; repeat actual workflow tests with disposable data.

## Installer and CI

The [workflow](.github/workflows/windows-ci.yml) publishes first, then invokes Inno Setup 6 with `packaging/Meridian.iss`, passing absolute `PublishDir`, `OutputDir` and `AllowedArch` (`x64` or `arm64`) definitions. Output names are `Meridian-Setup-win-x64.exe` and `Meridian-Setup-win-arm64.exe`; artifacts retain the complete publish folder. Current installer metadata is 0.1.0, not evidence of a verified release. Signing, clean install, upgrade and uninstall still require validation.

CI uploads TRX results and architecture-specific installer/portable artifacts for 14 days. It runs on PRs targeting main, pushes to main and manual dispatch; a push to another branch alone is not a separate push-triggered run. Inspect completed jobs for the exact application commit.

The separate [Windows validation workflow](.github/workflows/windows-validation.yml) runs on pushes to `feature/timevault-foundation` and manual dispatch. It uses the same pinned SDK and locked dependency restores, runs core tests and builds both x64 and ARM64; it does not package installers or verify runtime startup. Both workflows request only read access to repository contents.

The `meridian-distribution-win-x64` and `meridian-distribution-win-arm64` artifacts each contain the installer, a portable ZIP and `SHA256SUMS-win-<architecture>.txt`. CI creates the ZIP from the complete publish folder, including hidden files, and verifies both file hashes before upload. Keep the extracted distribution files together and run `./scripts/verify-checksums.ps1 -ManifestPath <distribution-folder>/SHA256SUMS-win-x64.txt` (or `win-arm64`) before use. The verifier rejects malformed/duplicate entries, missing files and mismatched bytes. Checksums establish agreement with the supplied manifest, not publisher authenticity; signing and trusted distribution remain separate requirements. `./scripts/test-packaging.ps1` exercises packaging and rejection paths with disposable fixtures retained under ignored `TestResults/`.

## Documentation and review

Update [STATUS](docs/STATUS.md), [verification](docs/VERIFICATION_REPORT.md), [changelog](CHANGELOG.md), user instructions and affected feature documents when behavior changes. Distinguish backend APIs, UI integration, CI checks and hands-on verification. Keep planned design requirements unchecked until their full scope has evidence. Check relative links/anchors and do not add screenshot links before captures exist.

Report exact commands, commit, Windows build/architecture, SDK information, pass/fail counts and limitations. Never infer codec support, real VSS access, performance or crash recovery from a build. Review local logs and paths for private information before sharing. Follow [SECURITY](SECURITY.md) for vulnerabilities and the [Safety Contract](docs/SAFETY_CONTRACT.md) for mutating features.
