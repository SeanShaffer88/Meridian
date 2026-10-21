# Context Capsules — Core prototype

**Status: implemented core prototype only; no UI, persistence, or automatic operations.**

A Context Capsule is a named, bounded set of filesystem path references for a task that spans folders. It does not own files and never modifies the filesystem. Use it to preserve the locations a user intends to revisit, even when a drive is disconnected or a path has disappeared.

```csharp
var capsule = ContextCapsule.Capture("Project review", new[]
{
    @"C:\\Users\\Example\\Downloads\\notes.txt",
    @"D:\\Projects\\Atlas"
});
var states = capsule.Inspect(); // Present or Unavailable; neither outcome prunes references.
```

Limits: 120-character names and at most 1,000 **consumed input entries** (including duplicates), avoiding unbounded lazy enumeration. Duplicate normalized paths are removed case-insensitively, preserving first-seen order. Invalid paths fail the capture rather than being silently omitted. Cancellation can interrupt capture and inspection.

**Important:** `Unavailable` combines missing, disconnected and inaccessible paths; it does not diagnose the reason, prove identity, or guarantee that a path is safe to operate on. No snapshot, recovery guarantee, mutation plan, metadata capture, persistence schema or on-screen Capsule UX is included yet.

## Next prototype gates

1. Non-destructive UI for naming and reopening Capsules, with missing/offline paths visible and never silently dropped.
2. Versioned, size-bounded, atomic local persistence with corruption recovery and explicit import/export.
3. Measure time to resume a cross-folder task versus existing tabs/bookmarks for several users.
4. Later integrate explanation-only Consequence Map previews, then an independently guarded operation journal. Never execute AI suggestions or Capsule references directly without action-time filesystem validation.

Run `./scripts/test.ps1 -Configuration Release` on Windows with .NET 10 to exercise the Core tests. Do not mark Windows device behavior verified until tested.
