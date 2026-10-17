# Threat model

Scope: [development baseline](STATUS.md). This is a threat inventory, not an independent review or proof of compliance with the [Safety Contract](SAFETY_CONTRACT.md).

## Assets and boundaries

Protect user source/destination files, historical data, workspace paths, diagnostics and application integrity. Names, metadata, archives, media, JSON references and preferences can be malformed, changing or attacker-controlled. Meridian uses current-user permissions with no automatic elevation.

Normal file opening delegates to Windows shell associations and can execute code. Preview is separate and does not shell-launch selected content. UNC paths can access network filesystems; Windows and external handlers have their own behavior. No Meridian telemetry, remote provider, mandatory account, AI model or runtime codec downloader is implemented. This is a code inventory, not a completed observed-network privacy audit.

## Controls and residual risks

| Surface | Existing controls | Residual risk |
| --- | --- | --- |
| Copy/rename/move | No intentional overwrite, staged copy/cancel, existing ancestor checks, selected link/collision tests | Path swaps/aliases, crash/disk-full cleanup, source mutation and cross-volume copy-then-delete integrity; no universal rollback |
| New folder/delete/open | Name/conflict UI checks, confirmed Recycle Bin request, explicit shell open | These paths do not automatically share core guards. Handlers can execute code; Recycle Bin availability/recovery varies |
| Archives | Path normalization, collisions/special entries rejected, entry/byte budgets, staging, ZIP length/CRC and TAR/gzip checks | In-process parsers, no authentication or complete ratio/time/nesting isolation. ZIP metadata may allocate before application limits are checked |
| Text/hex | Bounded reads, strict decoding, repeated path inspection | No opened-handle identity guarantee; cooperative cancellation is not preemptive |
| Images/media | Pre-open checks, image compressed-size guard, no autoplay or automatic codec installation | In-process Windows decoders, no decoded-pixel cap, incomplete media budgets/lifecycle and hostile-content testing |
| TimeVault | Read-only discovery/listing UI; scoped guards and staged backend export | Real VSS unverified; backend export lacks checksum/handle-identity hardening. Snapshots can expire and are not backups |
| Foresight/Reclaimer | Explicit scope, bounded read-only analysis/plans and uncertainty labels | Hints and caller-supplied dependency edges are not verified facts or mutation authority; no cleanup executor or AI |
| Preferences/logs | Per-user storage, temporary preference writes, latest-exception log | Paths/labels may be sensitive; no encryption, multi-process lock or guaranteed crash log. Review before sharing |
| Distribution | CI artifacts, advisory query, architecture packaging and x64 startup checks | Unsigned installers, floating CI SDK/action tags, expiring artifacts; no full license/security/reproducibility audit |

## Verification priorities

1. Test path replacement, junction/symlink/hard-link aliases, UNC/device/long/Unicode paths, permissions and publication races.
2. Establish destination integrity and source stability before cross-volume source deletion; verify partial-result recovery.
3. Inject disk-full, cancellation during active I/O, interruption and cleanup failure; unit tests do not establish full integration coverage.
4. Exercise malformed archives/media and resource exhaustion; isolate risky parsers where practical.
5. Verify clean deployment, artifact provenance, dependency/license obligations, local-data disclosure and actual network behavior.
6. Complete accessible prompts and independent review before encryption or privileged features are enabled.

Use [SECURITY.md](../SECURITY.md) for private reporting and [VERIFICATION_REPORT.md](VERIFICATION_REPORT.md) for evidence and gaps.
