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
Use Windows 11 and install the .NET 10 SDK and the Windows App SDK / WinUI 3 development prerequisites. From the repository root in PowerShell:

```powershell
./scripts/test.ps1 -Configuration Release
./scripts/build.ps1 -Configuration Release -Runtime win-x64
# Cross-publish ARM64 (does not prove native device functionality):
./scripts/build.ps1 -Configuration Release -Runtime win-arm64
```

The scripts return a nonzero error on restore/test/publish failure. For file-operation changes, also document tests on disposable folders covering collisions, cancellation, permissions and reparse points. CI is not a substitute for launch, installer, or assistive-technology testing. Record actual evidence in [the verification report](docs/VERIFICATION_REPORT.md); do not claim tests that were not run.
