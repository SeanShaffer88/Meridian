# Meridian project instructions

Apply the global engineering policy with these project-specific revisions. These revisions take precedence over conflicting wording in the global policy.

## Development standard

This project follows SEAN's **Universal Windows Desktop Master Prompt** — the standing development standard for all of SEAN's Windows desktop projects, current and future. The full text is maintained by SEAN (ask him for the current version); this section is the in-repo summary, and the project's own instructions add project-specific requirements on top of it.

Core standards:

- Production quality only: no rough demos or placeholder features presented as finished; safe defaults for beginners, explained advanced controls for professionals.
- Accessibility is a requirement: keyboard navigation, accessible names, visible focus, sufficient contrast, no meaning by color alone, specific error messages with a fix path.
- Every consequential action explains its risks, limitations, and recovery options; destructive or irreversible actions are confirmed, previewed where feasible, and backed up or undoable where feasible.
- Data safety: local-first storage, offline-capable core features, versioned data formats, atomic writes, tested migrations, and no silent data loss.
- Verification honesty: compatibility and readiness claims are labeled Verified, Partially verified, or Unverified; never claim a check that did not run.

Standing defaults (a project brief may override a specific default):

- Product-family look: the black/red aesthetic matching InstallScope is preferred, not mandatory.
- Rust is the default for new projects and for new components where it fits; existing stacks are preserved — this is not a rewrite mandate.
- Support as many Windows versions as makes sense for the project; consider Windows 10 support when feasible without materially extending development time. Never claim support that has not been verified.
- Status and task reports include a Progress % and an ETA whenever they can be estimated responsibly.
- Public documentation avoids competitor comparisons by default (internal research may name competitors; license attributions for bundled components are kept).
- License: MIT for SEAN-owned projects; forks retain their upstream license.

## Evidence-driven execution

- Minimize avoidable rework; permit evidence-driven corrections and refactoring. Do not interpret "zero backtracking" as a ban on correcting an earlier decision.
- After two failures with identical symptoms, stop speculative edits, gather diagnostics, and continue when evidence supports a different approach. Ask for steering only when blocked by missing information, authority, or an external dependency.

## Measured performance

- Replace absolute zero-allocation and zero-overhead requirements with measurable budgets for Meridian's .NET/WinUI implementation. Measure idle CPU, memory, allocations in hot paths, logging overhead, I/O throughput and cancellation latency against representative workloads.
- Keep logging local and bounded. Set and record workload-specific budgets before claiming a performance gate passed; do not invent thresholds or measurements. Correctness, data integrity and actionable diagnostics take priority over micro-optimization.

## Verification boundaries

- Passing tests and builds establishes only the behavior exercised. Track interactive workflows, clean installation/upgrade/uninstall, ARM64 runtime, accessibility and recovery checks separately; never infer release readiness from CI alone.
- Preserve the evidence boundaries in docs/RELEASE_GATE.md, docs/QUALITY-GATES.md and docs/VERIFICATION_REPORT.md. Report unverified checks as open.

## Documentation scope

- Update documentation only when behavior changes require it or when explicitly requested. Keep required updates focused on the affected behavior.
