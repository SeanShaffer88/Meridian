# Meridian project instructions

Apply the global engineering policy with these project-specific revisions. These revisions take precedence over conflicting wording in the global policy.

## Evidence-driven execution

- Minimize avoidable rework; permit evidence-driven corrections and refactoring. Do not interpret "zero backtracking" as a ban on correcting an earlier decision.
- After two failures with identical symptoms, stop speculative edits, gather diagnostics, and continue when evidence supports a different approach. Ask for steering only when blocked by missing information, authority, or an external dependency.

## Measured performance

- Replace absolute zero-allocation and zero-overhead requirements with measurable budgets for Meridian's .NET/WinUI implementation. Measure idle CPU, memory, allocations in hot paths, logging overhead, I/O throughput and cancellation latency against representative workloads.
- Keep logging local and bounded. Set and record workload-specific budgets before claiming a performance gate passed; do not invent thresholds or measurements. Correctness, data integrity and actionable diagnostics take priority over micro-optimization.

## Verification boundaries

- Passing tests and builds establishes only the behavior exercised. Track interactive workflows, clean installation/upgrade/uninstall, ARM64 runtime, accessibility and recovery checks separately; never infer release readiness from CI alone.
- Preserve the evidence boundaries in docs/RELEASE_GATE.md, docs/ENGINEERING_QUALITY_GATES.md and docs/VERIFICATION_REPORT.md. Report unverified checks as open.

## Documentation scope

- Update documentation only when behavior changes require it or when explicitly requested. Keep required updates focused on the affected behavior.
