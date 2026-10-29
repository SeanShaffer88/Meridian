namespace Meridian.Services;

/// <summary>
/// Resolves whether each pane's preview block starts expanded, across the
/// settings change that replaced the single right-side details strip with
/// one preview block per pane. A per-pane value saved by a build that has
/// the blocks always wins for its pane. While a pane has no saved value,
/// the old strip's single DetailsStripVisible value decides for it — so a
/// settings file written before the blocks existed lands with both blocks
/// in the state the strip had, and from the first save onwards the
/// per-pane values rule.
/// </summary>
public static class PreviewBlockMigration
{
    public static (bool LeftExpanded, bool RightExpanded) Resolve(
        bool? leftSaved, bool? rightSaved, bool legacyDetailsStripVisible) =>
        (leftSaved ?? legacyDetailsStripVisible, rightSaved ?? legacyDetailsStripVisible);
}
