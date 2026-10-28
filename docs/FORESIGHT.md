# Meridian Foresight

**Status: early backend prototype on feature branch; not wired to the UI. No AI model is integrated.**

Foresight is the proposed intelligence and consequence-analysis layer for Meridian. It should combine deterministic evidence, optional AI interpretation, and verified recovery-aware execution. AI must never receive unrestricted authority to mutate files.

## First implemented milestone: relationship hints

`RelationshipAnalyzer.Analyze(paths, maxPaths)` accepts caller-supplied absolute paths and returns immutable, explainable hints for matching filename stems or shared parent directories. It is bounded to at most 10,000 input paths, defaults to 1,000, does not enumerate directories or open file contents, and performs no writes or network calls.

**Important:** matching names and locations are weak correlations, not verified project dependencies. A zero-result analysis is not proof that an operation is safe.

The `ForesightImpactAnalyzer.Preview(targetPath, contextPaths)` prototype also returns only hints associated with a proposed target and explicitly disclaims verified dependency coverage. It neither opens files nor authorizes an operation.

The `DependencyImpactAnalyzer` additionally accepts explicit, provenance-bearing dependency edges from future trusted parsers and reports incoming references to a proposed target. **It does not discover or verify edges itself**. The separate JSON string-reference reader below is a bounded extraction prototype, not a verified dependency adapter or automatic connection to this analyzer.

## Proposed architecture

1. **Evidence adapters:** opt-in, bounded filesystem metadata, known shortcut references, project manifests and format-specific parsers. Each adapter has an explicit capability and privacy budget.
2. **Evidence graph:** immutable findings with provenance, timestamps, confidence class and explicit unknowns. Distinguish verified links, heuristic hints and AI-generated hypotheses.
3. **Impact planner:** predict consequences of proposed actions, including conflicts, dangling references, storage changes, privacy boundaries and recovery constraints.
4. **Optional intelligence:** local-first natural-language explanations and suggestions. Remote models require explicit per-scope consent and transparent data disclosure. No model-supplied filesystem commands are executable.
5. **Execution boundary:** the separate deterministic operation engine rechecks paths, identities and permissions. Any scope drift aborts; all mutating actions must satisfy the [Safety Contract](SAFETY_CONTRACT.md).
6. **Recovery:** durable operation receipts and tested inverse operations where technically possible; snapshots are never presumed to exist.

## Milestones and exit criteria

- [x] Read-only relationship hint model and unit tests committed (included in the Windows CI core suite; no interactive UI or measured benchmark).
- [x] Add indexed candidate discovery, output caps, cooperative cancellation and optional time budget (committed; Windows performance verification outstanding).
- [ ] Read-only Windows shortcut and supported project-manifest evidence adapters, with adversarial fixtures.
- [x] Initial metadata-only target impact preview and explicit uncertainty labels committed (core tests included in the verified Windows CI baseline; no UI integration).
- [x] Bounded incoming-dependency evidence model committed (core tests included in the verified Windows CI baseline; no UI integration).
- [ ] Verified dependency evidence adapters and broader impact reporting.
- [ ] UI preview that never implicitly initiates an operation.
- [ ] Optional local AI interpretation behind an opt-in switch, privacy review and offline tests.
- [ ] Verified operation journal, recovery drills and Windows x64/ARM64 security gates before any Foresight-guided mutation.

## Product rule

**Foresight can recommend; it cannot authorize.** A human-approved plan is still revalidated by the non-AI execution layer. Never call a heuristic prediction a verified guarantee.

## Experimental JSON reference extraction

The branch includes `JsonManifestReferenceReader`, an explicitly invoked, read-only parser that inspects only named JSON string properties. It limits file size, JSON depth and extracted reference count, and rejects reparse points in the manifest path's existing ancestors. It does **not** verify that a property is a real dependency, and its path checks are not race-free. No automatic directory scan or AI model is involved.

## Relationship output budget

The metadata-only relationship analyzer now defaults to a maximum of **10,000 emitted relationships** (configurable from 1 to 100,000). If the limit would be exceeded, it fails explicitly rather than returning a misleading partial result. The path input ceiling remains separate (default 1,000; maximum 10,000). This bounds retained relationship objects. Candidate discovery now indexes and caches filenames and parent directories, avoiding all-pairs comparison for sparse inputs while preserving input-order results and filename-stem precedence. Dense matching groups still produce quadratic numbers of valid relationships, so the output limit remains essential. Cooperative cancellation is now supported during input validation, indexing and result emission through an optional cancellation token; it throws rather than returning partial findings. An optional elapsed-time budget (up to five minutes) is now checked at cancellation checkpoints; timeout throws without returning partial findings. This is a cooperative budget, not preemptive termination of a blocking input enumerator. UI cancellation wiring and measured Windows performance benchmarks remain future work.
