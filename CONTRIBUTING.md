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
Use Windows 11, a .NET 10 SDK and the WinUI/Windows App SDK build prerequisites. The application is Windows-specific; cross-compiling ARM64 on x64 does not verify ARM64 execution. CI currently selects the latest `10.0.x` SDK, so record `dotnet --info` with local evidence rather than assuming an exact patch is pinned.

From the repository root in PowerShell:

```powershell
./scripts/test.ps1 -Configuration Release
./scripts/build.ps1 -Configuration Release -Runtime win-x64
./scripts/build.ps1 -Configuration Release -Runtime win-arm64
```

`test.ps1` runs the core test project and fails on a nonzero result. `build.ps1` restores the solution, selects the matching x64/ARM64 platform and publishes self-contained output under `src/Meridian.App/bin/<platform>/Release/net10.0-windows10.0.22000.0/<runtime>/publish/`. It does not build the Inno Setup installer or execute runtime tests. Run each architecture build separately.

For retained test evidence:

```powershell
dotnet test tests/Meridian.Core.Tests/Meridian.Core.Tests.csproj -c Release --logger "trx;LogFileName=core-tests.trx" --results-directory TestResults
```

Launch the published `Meridian.exe` with the entire output folder present, particularly `Meridian.pri`. `EnableMsixTooling` is required for resource publication even though the app is unpackaged. A successful compilation alone previously missed a real startup crash. CI now asserts the PRI on both architectures and checks x64 startup for ten seconds; repeat actual workflow tests with disposable data.

## Installer and CI

The [workflow](.github/workflows/windows-ci.yml) publishes first, then invokes Inno Setup 6 with `packaging/Meridian.iss`, passing absolute `PublishDir`, `OutputDir` and `AllowedArch` (`x64` or `arm64`) definitions. Output names are `Meridian-Setup-win-x64.exe` and `Meridian-Setup-win-arm64.exe`; artifacts retain the complete publish folder. Current installer metadata is 0.1.0, not evidence of a verified release. Signing, clean install, upgrade and uninstall still require validation.

CI uploads TRX results and architecture-specific installer/portable artifacts for 14 days. It runs on PRs targeting main, pushes to main and manual dispatch; a push to another branch alone is not a separate push-triggered run. Inspect completed jobs for the exact application commit.

## Documentation and review

Update [STATUS](docs/STATUS.md), [verification](docs/VERIFICATION_REPORT.md), [changelog](CHANGELOG.md), user instructions and affected feature documents when behavior changes. Distinguish backend APIs, UI integration, CI checks and hands-on verification. Keep planned design requirements unchecked until their full scope has evidence. Check relative links/anchors and do not add screenshot links before captures exist.

Report exact commands, commit, Windows build/architecture, SDK information, pass/fail counts and limitations. Never infer codec support, real VSS access, performance or crash recovery from a build. Review local logs and paths for private information before sharing. Follow [SECURITY](SECURITY.md) for vulnerabilities and the [Safety Contract](docs/SAFETY_CONTRACT.md) for mutating features.
