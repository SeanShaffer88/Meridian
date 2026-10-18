# High-throughput engineering pipeline

**Policy:** Applies to Meridian and is the default delivery model for future projects. Speed is measured as verified user-facing capability, never commit count. All planned features and existing security, reliability, accessibility, compatibility, and data-safety gates remain in force.

## Execution topology

1. **Plan a cohesive batch:** record user-visible acceptance criteria, dependencies, owners, risk, and affected interfaces before edits. Prefer one integrated feature slice plus related hardening over isolated micro-commits.
2. **Parallelize independent work:** use separate branches or worktrees for Core/Filesystem, Windows UI, Advanced Features, and Quality/Release. Establish shared API contracts first. Avoid simultaneous edits to the same files. A single integration owner resolves conflicts and verifies behavior.
3. **Fast local feedback:** run format/lint/static analysis and impacted unit tests on each workstream; run full core tests and Windows app compilation before integration. Never interpret a targeted pass as release readiness.
4. **Integration gate:** require review, complete core regression suite, Windows x64 and ARM64 builds, package integrity checks, and CI success at the exact candidate commit before merging. No bypass for schedule pressure. Security-sensitive changes additionally require adversarial and cancellation/failure-path tests.
5. **Release gate:** retain all requirements in [ENGINEERING_QUALITY_GATES.md](ENGINEERING_QUALITY_GATES.md) and [RELEASE_GATE.md](RELEASE_GATE.md). CI success does not replace manual UI/accessibility, clean-machine, ARM64 runtime, security, or performance verification.

## Workstream boundaries

| Workstream | Owns | Integrates through |
| --- | --- | --- |
| Core & Filesystem | Archive codecs, path policies, operation execution, recovery | Stable core APIs, unit and fault-injection tests |
| Windows Experience | Navigation, accessibility, viewer presentation | Core contracts, UI smoke tests |
| Advanced Features | TimeVault, Foresight, Storage Reclaimer | Read-only boundaries and permission contracts |
| Quality & Distribution | CI, benchmarks, supply chain, packaging, docs | All workstreams; cannot waive release gates |

Workstreams run concurrently only when available execution capacity and stable interfaces permit. Parallelism is not evidence that work has been performed.

## Measured throughput

Record one row per integrated batch in the tracker below. Count **accepted, tested features** rather than commits. Use actual timestamps and test artifacts; leave unmeasured values blank.

| Batch | Feature acceptance criteria met | Start / end (UTC) | Lead time | Tests passed / failed | CI candidate SHA + run | Escaped regressions | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| _Unmeasured baseline_ | _Not yet established_ | | | | | | Establish baseline before reporting acceleration |

Secondary measures: CI wall-clock duration, time to first failing test, reopened defects, security findings, Windows x64/ARM64 verification coverage, cold-start latency, idle working set, and cancellation latency. Compare against a dated baseline and identical test scope. Do not claim a speedup or performance target without measurements.

## Batch completion checklist

- [ ] Feature acceptance criteria implemented, including error/cancellation paths
- [ ] Changed APIs documented and independently owned branches integrated
- [ ] Targeted tests and full core regression suite pass
- [ ] Windows x64 and ARM64 build/package CI passes for exact candidate commit
- [ ] Security, accessibility, and data-safety impact reviewed
- [ ] Evidence linked in PR; unresolved risks and deferred release gates explicit
- [ ] Merge authorized only after required gates pass

**Exception policy:** If a gate fails, fix the cause and rerun the affected tests plus integration gates. Never suppress tests, delete planned features, or represent unverified work as complete to improve throughput.
