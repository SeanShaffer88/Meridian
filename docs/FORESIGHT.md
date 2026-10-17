# Meridian Foresight

**Status: early backend prototype on feature branch; not wired to the UI. No AI model is integrated.**

Foresight is the proposed intelligence and consequence-analysis layer for Meridian. It should combine deterministic evidence, optional AI interpretation, and verified recovery-aware execution. AI must never receive unrestricted authority to mutate files.

## First implemented milestone: relationship hints

`RelationshipAnalyzer.Analyze(paths, maxPaths)` accepts caller-supplied absolute paths and returns immutable, explainable hints for matching filename stems or shared parent directories. It is bounded to at most 10,000 input paths, defaults to 1,000, does not enumerate directories or open file contents, and performs no writes or network calls.

**Important:** matching names and locations are weak correlations, not verified project dependencies. A zero-result analysis is not proof that an operation is safe.

The `ForesightImpactAnalyzer.Preview(targetPath, contextPaths)` prototype also returns only hints associated with a proposed target and explicitly disclaims verified dependency coverage. It neither opens files nor authorizes an operation.

The `DependencyImpactAnalyzer` additionally accepts explicit, provenance-bearing dependency edges from future trusted parsers and reports incoming references to a proposed target. **It does not discover or verify edges itself**; no format-specific evidence adapter exists yet.

## Proposed architecture

1. **Evidence adapters:** opt-in, bounded filesystem metadata, known shortcut references, project manifests and format-specific parsers. Each adapter has an explicit capability and privacy budget.
2. **Evidence graph:** immutable findings with provenance, timestamps, confidence class and explicit unknowns. Distinguish verified links, heuristic hints and AI-generated hypotheses.
3. **Impact planner:** predict consequences of proposed actions, including conflicts, dangling references, storage changes, privacy boundaries and recovery constraints.
4. **Optional intelligence:** local-first natural-language explanations and suggestions. Remote models require explicit per-scope consent and transparent data disclosure. No model-supplied filesystem commands are executable.
5. **Execution boundary:** the separate deterministic operation engine rechecks paths, identities and permissions. Any scope drift aborts; all mutating actions must satisfy the [Safety Contract](SAFETY_CONTRACT.md).
6. **Recovery:** durable operation receipts and tested inverse operations where technically possible; snapshots are never presumed to exist.

## Milestones and exit criteria

- [x] Read-only relationship hint model and unit tests committed (not yet run on Windows).
- [ ] Make the analysis cancellation-aware and efficient on large input sets.
- [ ] Read-only Windows shortcut and supported project-manifest evidence adapters, with adversarial fixtures.
- [x] Initial metadata-only target impact preview and explicit uncertainty labels committed (not yet Windows-tested).
- [x] Bounded incoming-dependency evidence model committed (not yet Windows-tested).
- [ ] Verified dependency evidence adapters and broader impact reporting.
- [ ] UI preview that never implicitly initiates an operation.
- [ ] Optional local AI interpretation behind an opt-in switch, privacy review and offline tests.
- [ ] Verified operation journal, recovery drills and Windows x64/ARM64 security gates before any Foresight-guided mutation.

## Product rule

**Foresight can recommend; it cannot authorize.** A human-approved plan is still revalidated by the non-AI execution layer. Never call a heuristic prediction a verified guarantee.
