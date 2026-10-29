using Meridian.Core.Transfers;
using Meridian.Services;
using Microsoft.UI.Xaml;

namespace Meridian;

/// <summary>
/// The window's half of live folder watching (WatchKit). The mechanics —
/// draining, deciding, debouncing — live in <see cref="FolderWatchService"/>
/// and Core; this partial owns only the per-pane bookkeeping: a watch exists
/// for exactly the folder a pane is showing, created and moved by
/// navigation, stopped at Home and at window close, and its refresh goes
/// through the same reload path as the Refresh button and F5
/// (NavigateAsync to the current path without touching history), so a
/// watch refresh preserves exactly what a manual refresh preserves.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Matches a pane's watch to the folder the pane now shows:
    /// stops the watch at Home (the sentinel is not a filesystem path and
    /// is never watched or recorded), restarts it when the folder changed,
    /// and leaves it alone when it already covers the folder — which is
    /// what a watch-triggered refresh itself looks like when it lands
    /// here. Called on every committed navigation.</summary>
    private void UpdatePaneWatch(PaneState pane)
    {
        if (pane.IsHome || string.IsNullOrWhiteSpace(pane.CurrentPath))
        {
            StopPaneWatch(pane);
            return;
        }

        if (pane.Watch is { } existing &&
            string.Equals(existing.Folder, pane.CurrentPath, StringComparison.OrdinalIgnoreCase))
            return;

        StopPaneWatch(pane);
        pane.Watch = FolderWatchService.TryStart(
            pane.CurrentPath,
            DispatcherQueue,
            includeDescendants: () => pane.Layout == PaneLayout.Columns,
            refreshDue: () => OnWatchRefreshDue(pane));
    }

    private static void StopPaneWatch(PaneState pane)
    {
        pane.Watch?.Dispose();
        pane.Watch = null;
    }

    /// <summary>A debounced watch refresh, on the UI thread. Everything is
    /// re-validated here: the debounce window is long enough for the pane
    /// to have navigated (a stale watch has nothing to say about the folder
    /// now shown), and while a transfer job is running the executor is
    /// writing into real folders — the transfers code already refreshes the
    /// affected panes when the job settles, so watch refreshes stand down
    /// instead of re-reading folders mid-copy.</summary>
    private void OnWatchRefreshDue(PaneState pane)
    {
        if (pane.IsHome || pane.Watch is not { } watch ||
            !string.Equals(watch.Folder, pane.CurrentPath, StringComparison.OrdinalIgnoreCase))
            return;
        if (_transfers.ActiveJob is { State: TransferJobState.Running }) return;
        _ = NavigateAsync(pane, pane.CurrentPath, addHistory: false);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        StopPaneWatch(_left);
        StopPaneWatch(_right);
        ThumbKitService.ShutdownShared();
    }
}
