# Credits and dependency policy

Meridian is distributed as free and open-source software under the repository's [MIT License](LICENSE).

The application uses the .NET platform, WinUI 3, and the Windows App SDK. Test sources use xUnit and Microsoft.NET.Test.Sdk. These dependencies retain their own terms and notices; verify current versions, support status, and license obligations before release.

## Direct references and build tools

At application baseline `2eef02a`, project files specify Windows App SDK 2.5.1, System.Management 10.0.0, Microsoft.NET.Test.Sdk 17.12.0, xUnit 2.9.2 and xunit.runner.visualstudio 2.8.2. The app targets .NET 10. These are direct package versions, not a full transitive bill of materials. WinUI components arrive through Windows App SDK; ZIP/TAR/gzip implementations use .NET libraries rather than an installed external archive manager.

Inno Setup 6 builds the unsigned per-user installer. CI uses GitHub Actions checkout/setup-dotnet/upload-artifact actions and Chocolatey to install the packaging tool on the runner. Build-time downloads are separate from application runtime behavior. The complete distribution, including copied native/runtime components, requires a license and notices review before release; this file is not a completed legal compliance report.

The 2026-10-17 NuGet advisory query, including transitive packages, reported no known vulnerable dependencies. That result is dated and does not establish future safety, complete license compliance or supported-lifecycle status. See [verification](docs/VERIFICATION_REPORT.md) and retain package notices when packaging.

- Prefer maintained packages with clear licenses.
- Minimize dependency count and avoid runtime downloads.
- Review transitive dependencies and known vulnerabilities before releases.
- Do not add telemetry, advertising, paywalls, or mandatory paid services.
