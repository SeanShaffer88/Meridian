# Security Policy

Meridian is in early development and is not yet suitable for high-risk or irreplaceable data.

Please do not publish exploitable vulnerabilities as public issues before maintainers have assessed them. Use GitHub private vulnerability reporting if enabled; otherwise contact the repository owner privately.

Relevant issues include unintended data loss or overwrite, path traversal, reparse-point traversal, unsafe preview/handler behavior, privilege escalation, and unexpected network access.

No response-time or remediation SLA is promised while the project is in early development.

## Report details

Include the affected commit/artifact, Windows build/architecture, exact behavior and a minimal reproduction with disposable data. Describe potential overwrite, execution, disclosure or privilege impact without including real private files, credentials or exploitable public attachments. Whether GitHub private reporting is enabled must be checked at reporting time; this document does not assert it is available.

## Current boundaries

The [threat model](docs/THREAT_MODEL.md) records existing controls and known gaps. In particular, path checks are not race-free, cross-volume moves lack sufficient destination-integrity/source-change handling, media/archive parsers run in process, and no general rollback or independent security review is complete. Passing CI does not resolve these limits.

Workspace state and the latest recorded startup/unhandled error are stored under `%LOCALAPPDATA%\Meridian`; they can reveal local paths and labels. No telemetry submission is implemented. Review and redact diagnostics before sharing them. See [local-data guidance](docs/USER_GUIDE.md#local-data-and-privacy) and [verification evidence](docs/VERIFICATION_REPORT.md).
