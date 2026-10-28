# Consequence Map — bounded explanation prototype

**Status: core-only prototype, not a UI feature or a dependency detector.**

`ConsequenceMap.Explain(plan)` turns the existing read-only `OperationPreview.Plan` copy preflight into a grouped overview of ready/blocked items and why items were blocked. It displays a maximum of 100 detailed entries and reports how many items were omitted from the display, without discarding their counts.

The result always sets `SafeToExecuteWithoutRecheck = false`. File contents, relative dependencies, application-specific links, and file identities are **not** analyzed here. In particular, "ready" means only that the existing preflight identified no known blocker at that instant.

Example:

```csharp
var plan = OperationPreview.PreviewCopy(sourcePaths, destinationFolder);
var impact = ConsequenceMap.Explain(plan, displayLimit: 25);
Console.WriteLine($"{impact.ReadyItems} ready; {impact.BlockedItems} blocked");
foreach (var risk in impact.Risks)
    Console.WriteLine($"{risk.Count}: {risk.Explanation}");
```

No filesystem modifications, no execution queue, and no GUI are introduced. Unit tests use synthetic plans and require no destructive setup. This deliberately limited milestone supports comparison testing of explanation quality before investing in more sophisticated relationship detection.

Follow-up: show a clear visual before/after plan; test whether users detect conflicts faster than with the existing list; introduce deeper dependency analysis only for verifiable formats, with explicit "unknown" results.
