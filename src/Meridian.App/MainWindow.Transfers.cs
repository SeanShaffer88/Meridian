using Meridian.Core.Transfers;
using Meridian.Models;
using Meridian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Storage;

namespace Meridian;

/// <summary>
/// Slice 5 Part B: the Transfers strip (queue + history + undo), the queue
/// wiring behind Copy/Move/Shelf/drag-and-drop, and the File hash tool.
/// The engine lives in Meridian.Core.Transfers; the coordinator in
/// Services/TransferCoordinator.cs owns running it. Everything here only
/// renders coordinator state and forwards user choices back to it.
/// </summary>
public sealed partial class MainWindow
{
    private readonly TransferCoordinator _transfers = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian"));
    private bool _verifyCopiesWithChecksum;
    private CollisionPolicy _nameCollisionPolicy = CollisionPolicy.Ask;
    private bool _transfersStripCollapsed;
    private bool _showTransferHistory;
    private Guid? _shelfJobId;
    private readonly Dictionary<Guid, TextBlock> _transferDetailLabels = new();
    private readonly Dictionary<Guid, ProgressBar> _transferProgressBars = new();
    private readonly Dictionary<Guid, TransferProgressUpdate> _latestTransferUpdates = new();

    private sealed record DragSession(PaneState SourcePane, string SourceFolder, List<FileEntry> Entries);
    private sealed record DropPlan(TransferJobKind Kind, string Destination, IReadOnlyList<string> SourcePaths);
    private DragSession? _dragSession;
    private IReadOnlyList<string>? _externalDragPaths;

    // ----- wiring -----------------------------------------------------------

    private void InitializeTransfers()
    {
        _transfers.Changed += Transfers_Changed;
        _transfers.ProgressChanged += Transfers_ProgressChanged;
        _transfers.JobSettled += Transfers_JobSettled;
    }

    private async Task InitializeTransfersAsync()
    {
        await _transfers.LoadAsync();
        RefreshTransfersStrip();
    }

    private static CollisionPolicy ParseCollisionPolicy(string? value) =>
        Enum.TryParse<CollisionPolicy>(value, ignoreCase: false, out var policy) ? policy : CollisionPolicy.Ask;

    private void Transfers_Changed(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(RefreshTransfersStrip);

    private void Transfers_ProgressChanged(object? sender, TransferProgressUpdate update)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _latestTransferUpdates[update.Job.Id] = update;
            if (_transferProgressBars.TryGetValue(update.Job.Id, out var bar))
            {
                bar.Maximum = Math.Max(1, update.Progress.TotalBytes);
                bar.Value = update.Progress.BytesCopied;
                bar.IsIndeterminate = false;
            }
            if (_transferDetailLabels.TryGetValue(update.Job.Id, out var detail))
                detail.Text = TransferDetailText(update.Job);
            CancelCopyButton.IsEnabled = _transfers.ActiveJob is not null;
            UpdateTransferStatusSummary(update);
        });
    }

    private void Transfers_JobSettled(object? sender, TransferJob job)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_shelfJobId == job.Id)
            {
                _shelfJobId = null;
                CancelShelfButton.IsEnabled = false;
            }
            await RefreshPanesForSettledJobAsync(job);
            ReportSettledJob(job);
            RefreshTransfersStrip();
        });
    }

    /// <summary>While a job runs, the status bar carries the compact summary:
    /// what is happening, how far through, and roughly how long is left.</summary>
    private void UpdateTransferStatusSummary(TransferProgressUpdate update)
    {
        if (update.Job.State != TransferJobState.Running) return;
        var verb = update.Job.Kind switch
        {
            TransferJobKind.Move => "Moving",
            TransferJobKind.Delete => "Deleting",
            _ => "Copying",
        };
        var summary = update.Progress.TotalBytes > 0
            ? $"{verb}\u2026 {update.Progress.BytesCopied * 100.0 / update.Progress.TotalBytes:0}%"
            : $"{verb}\u2026";
        if (update.EstimatedRemaining is { } remaining)
            summary += $" \u00b7 about {FormatDuration(remaining)} left";
        SetStatus(summary);
    }

    private void ReportSettledJob(TransferJob job)
    {
        var past = job.Kind switch
        {
            TransferJobKind.Move => "Moved",
            TransferJobKind.Delete => "Deleted",
            _ => "Copied",
        };
        var destination = string.IsNullOrWhiteSpace(job.DestinationFolder) ? "" : $" to {job.DestinationFolder}";
        switch (job.State)
        {
            case TransferJobState.Completed:
                SetStatus($"{past} {job.CompletedItems:N0} of {job.TotalItems:N0} item(s){destination}.", StatusSeverity.Success);
                break;
            case TransferJobState.CompletedWithErrors:
                var firstError = job.Items.FirstOrDefault(item => item.State == TransferItemState.Failed)?.Error;
                SetStatus($"{past} {job.CompletedItems:N0} of {job.TotalItems:N0} item(s){destination}, with errors."
                    + (firstError is null ? "" : $" First problem: {firstError}"), StatusSeverity.Warning);
                break;
            case TransferJobState.Cancelled:
                SetStatus("Transfer cancelled. Files already finished stay in place; the half-copied file was removed.", StatusSeverity.Warning);
                break;
            case TransferJobState.Failed:
                var failure = job.Items.FirstOrDefault(item => item.State == TransferItemState.Failed)?.Error
                    ?? "The transfer could not run.";
                SetStatus($"Transfer failed \u00b7 {failure}", StatusSeverity.Error);
                break;
        }
    }

    private async Task RefreshPanesForSettledJobAsync(TransferJob job)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddFolder(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path)) folders.Add(path);
        }
        AddFolder(job.DestinationFolder);
        foreach (var source in job.Sources) AddFolder(Path.GetDirectoryName(source));
        foreach (var item in job.Items)
        {
            AddFolder(Path.GetDirectoryName(item.SourcePath));
            AddFolder(item.DestinationPath is null ? null : Path.GetDirectoryName(item.DestinationPath));
        }
        foreach (var pane in new[] { _left, _right })
        {
            if (!pane.IsHome && folders.Contains(pane.CurrentPath))
                await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
        }
    }

    // ----- the strip --------------------------------------------------------

    private void TransfersTab_Click(object sender, RoutedEventArgs e)
    {
        _showTransferHistory = false;
        RefreshTransfersStrip();
    }

    private void HistoryTab_Click(object sender, RoutedEventArgs e)
    {
        _showTransferHistory = true;
        RefreshTransfersStrip();
    }

    private void TransfersCollapse_Click(object sender, RoutedEventArgs e)
    {
        _transfersStripCollapsed = !_transfersStripCollapsed;
        RefreshTransfersStrip();
        _ = PersistSettingsAsync();
    }

    private void RefreshTransfersStrip()
    {
        var hasContent = _transfers.Jobs.Count > 0 || _transfers.History.Count > 0;
        TransfersPanel.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
        TransfersTabButton.IsChecked = !_showTransferHistory;
        HistoryTabButton.IsChecked = _showTransferHistory;
        TransfersBodyScroll.Visibility = _transfersStripCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TransfersCollapseButton.Content = _transfersStripCollapsed ? "\u2303" : "\u2304";
        CancelCopyButton.IsEnabled = _transfers.ActiveJob is not null;
        TransfersHeaderSummary.Text = _transfers.ActiveJob is { } active
            ? TransferJobTitle(active)
            : _transfers.Jobs.Count > 0
                ? $"{_transfers.Jobs.Count:N0} transfer(s) waiting"
                : "";

        _transferDetailLabels.Clear();
        _transferProgressBars.Clear();
        TransfersRows.Children.Clear();
        if (_showTransferHistory) BuildHistoryRows();
        else BuildJobRows();
    }

    private void BuildJobRows()
    {
        if (_transfers.Jobs.Count == 0)
        {
            TransfersRows.Children.Add(new TextBlock
            {
                Text = "Nothing is running. Copies and moves you start appear here; finished ones move to History.",
                FontSize = 12,
                Foreground = ThemeBrush("MeridianTextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        foreach (var job in _transfers.Jobs)
            TransfersRows.Children.Add(BuildJobRow(job));
    }

    private Border BuildJobRow(TransferJob job)
    {
        var panel = new StackPanel { Spacing = 4 };

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = TransferJobTitle(job),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);

        if (job.State is TransferJobState.Running or TransferJobState.WaitingOnConflict)
        {
            var pause = new Button { Content = "Pause" };
            ToolTipService.SetToolTip(pause, "Pause this transfer. Everything already copied stays copied, and Resume carries on from the same place.");
            AutomationProperties.SetName(pause, $"Pause transfer to {job.DestinationFolder}");
            pause.Click += async (s, e) => await _transfers.PauseJobAsync(job);
            buttons.Children.Add(pause);
        }
        if (job.State == TransferJobState.Paused)
        {
            var resume = new Button { Content = "Resume" };
            ToolTipService.SetToolTip(resume, "Carry on with this transfer from where it stopped. Progress so far is kept.");
            AutomationProperties.SetName(resume, $"Resume transfer to {job.DestinationFolder}");
            resume.Click += (s, e) => _transfers.ResumeJob(job);
            buttons.Children.Add(resume);
        }
        if (job.State == TransferJobState.Queued
            && !_transfers.HasPendingStartRequest(job.Id)
            && !ReferenceEquals(_transfers.ActiveJob, job))
        {
            var start = new Button { Content = "Start" };
            ToolTipService.SetToolTip(start, "Start this transfer now. It was saved from an earlier session, and saved transfers never restart on their own.");
            AutomationProperties.SetName(start, $"Start transfer to {job.DestinationFolder}");
            start.Click += (s, e) => _transfers.ResumeJob(job);
            buttons.Children.Add(start);
        }
        var cancel = new Button { Content = "Cancel" };
        ToolTipService.SetToolTip(cancel, "Stop this transfer. Files already finished stay where they are; a half-copied file is removed.");
        AutomationProperties.SetName(cancel, $"Cancel transfer to {job.DestinationFolder}");
        cancel.Click += async (s, e) => await _transfers.CancelJobAsync(job);
        buttons.Children.Add(cancel);

        panel.Children.Add(header);

        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = Math.Max(1, job.TotalBytes),
            Value = job.BytesCopied,
            IsIndeterminate = job.State == TransferJobState.Running && job.TotalBytes == 0,
            Foreground = ThemeBrush("MeridianAccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(bar, $"Progress of transfer to {job.DestinationFolder}");
        _transferProgressBars[job.Id] = bar;
        panel.Children.Add(bar);

        var detail = new TextBlock
        {
            Text = TransferDetailText(job),
            FontSize = 11,
            Foreground = ThemeBrush("MeridianTextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _transferDetailLabels[job.Id] = detail;
        panel.Children.Add(detail);

        foreach (var item in job.Items.Where(item => item.State == TransferItemState.WaitingOnConflict))
            panel.Children.Add(BuildConflictRow(job, item));

        return new Border
        {
            Background = ThemeBrush("MeridianChromeBrush"),
            BorderBrush = ThemeBrush("MeridianBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = panel
        };
    }

    private Border BuildConflictRow(TransferJob job, TransferItem item)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{Path.GetFileName(item.SourcePath)} \u2014 name already exists",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeBrush("MeridianWarningBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"A file with this name is already in {Path.GetDirectoryName(item.DestinationPath)}. Choose what to do with the incoming file; the transfer waits here until you do.",
            FontSize = 11,
            Foreground = ThemeBrush("MeridianTextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        var applyToAll = new ToggleSwitch { Header = "Apply to all remaining", IsOn = false };
        AutomationProperties.SetName(applyToAll, $"Apply to all remaining conflicts for {Path.GetFileName(item.SourcePath)}");
        ToolTipService.SetToolTip(applyToAll, "Use the same choice for every other file in this transfer whose name also already exists, including ones that have not been reached yet.");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var replace = new Button { Content = "Replace" };
        ToolTipService.SetToolTip(replace, "Replace the existing file with the incoming one. The existing file is overwritten and cannot be brought back from here.");
        AutomationProperties.SetName(replace, $"Replace the existing {Path.GetFileName(item.SourcePath)}");
        replace.Click += async (s, e) => await ResolveConflictFromUiAsync(job, item, CollisionDecisionKind.Replace, applyToAll.IsOn);
        var keepBoth = new Button { Content = "Keep both" };
        ToolTipService.SetToolTip(keepBoth, "Keep both files. The incoming file is saved under a numbered name, like \u201cname (2)\u201d.");
        AutomationProperties.SetName(keepBoth, $"Keep both copies of {Path.GetFileName(item.SourcePath)}");
        keepBoth.Click += async (s, e) => await ResolveConflictFromUiAsync(job, item, CollisionDecisionKind.Rename, applyToAll.IsOn);
        var skip = new Button { Content = "Skip" };
        ToolTipService.SetToolTip(skip, "Skip this file. The existing file stays as it is and the incoming file is not copied.");
        AutomationProperties.SetName(skip, $"Skip {Path.GetFileName(item.SourcePath)}");
        skip.Click += async (s, e) => await ResolveConflictFromUiAsync(job, item, CollisionDecisionKind.Skip, applyToAll.IsOn);
        buttons.Children.Add(replace);
        buttons.Children.Add(keepBoth);
        buttons.Children.Add(skip);
        panel.Children.Add(buttons);
        panel.Children.Add(applyToAll);

        return new Border
        {
            Background = ThemeBrush("MeridianPanelBrush"),
            BorderBrush = ThemeBrush("MeridianWarningBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = panel
        };
    }

    private async Task ResolveConflictFromUiAsync(
        TransferJob job, TransferItem item, CollisionDecisionKind action, bool applyToAll)
    {
        try
        {
            await _transfers.ResolveConflictAsync(job, item, action, applyToAll);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetStatus("That choice could not be applied \u00b7 " + ex.Message, StatusSeverity.Error);
        }
    }

    private void BuildHistoryRows()
    {
        if (_transfers.History.Count == 0)
        {
            TransfersRows.Children.Add(new TextBlock
            {
                Text = "No finished transfers yet. Copies and moves you run are recorded here, and many can be undone.",
                FontSize = 12,
                Foreground = ThemeBrush("MeridianTextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        foreach (var record in _transfers.History)
            TransfersRows.Children.Add(BuildHistoryRow(record));
    }

    private Border BuildHistoryRow(TransferHistoryRecord record)
    {
        var completed = record.Items.Count(item => item.FinalState == TransferItemState.Completed);
        var failed = record.Items.Count(item => item.FinalState == TransferItemState.Failed);
        var past = record.Kind switch
        {
            TransferJobKind.Move => "Moved",
            TransferJobKind.Delete => "Deleted",
            _ => "Copied",
        };
        var destination = string.IsNullOrWhiteSpace(record.DestinationFolder) ? "" : $" to {record.DestinationFolder}";
        var result = record.FinalState switch
        {
            TransferJobState.Completed => "Finished",
            TransferJobState.CompletedWithErrors => "Finished with errors",
            TransferJobState.Cancelled => "Cancelled",
            TransferJobState.Failed => "Failed",
            _ => record.FinalState.ToString(),
        };

        var texts = new StackPanel { Spacing = 2 };
        texts.Children.Add(new TextBlock
        {
            Text = $"{past} {completed:N0} of {record.Items.Count:N0} item(s){destination}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        texts.Children.Add(new TextBlock
        {
            Text = $"{record.FinishedUtc.ToLocalTime():g} \u00b7 {result}" + (failed > 0 ? $" \u00b7 {failed:N0} failed" : ""),
            FontSize = 11,
            Foreground = ThemeBrush("MeridianTextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(texts);

        var plan = _transfers.GetUndoPlan(record);
        var undo = new Button { Content = "Undo", IsEnabled = plan.IsAvailable, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(undo, plan.Reason);
        AutomationProperties.SetName(undo, $"Undo {past.ToLowerInvariant()} transfer from {record.FinishedUtc.ToLocalTime():g}");
        undo.Click += async (s, e) =>
        {
            var inverse = await _transfers.EnqueueUndoAsync(record);
            if (inverse is null)
            {
                SetStatus("Undo is no longer available for that transfer. Open History again to see why.", StatusSeverity.Warning);
                RefreshTransfersStrip();
                return;
            }
            _showTransferHistory = false;
            RefreshTransfersStrip();
            SetStatus("Undo queued in Transfers. It runs like any other transfer and is recorded in History too.");
        };
        Grid.SetColumn(undo, 1);
        row.Children.Add(undo);

        return new Border
        {
            Background = ThemeBrush("MeridianChromeBrush"),
            BorderBrush = ThemeBrush("MeridianBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = row
        };
    }

    private static string TransferJobTitle(TransferJob job)
    {
        var count = job.Items.Count > 0 ? job.Items.Count : job.Sources.Count;
        var items = count == 1 ? "item" : "items";
        return job.Kind switch
        {
            TransferJobKind.Move => job.State == TransferJobState.Running
                ? $"Moving {count:N0} {items} to {job.DestinationFolder}"
                : $"Move {count:N0} {items} to {job.DestinationFolder}",
            TransferJobKind.Delete => job.State == TransferJobState.Running
                ? $"Deleting {count:N0} {items}"
                : $"Delete {count:N0} {items}",
            _ => job.State == TransferJobState.Running
                ? $"Copying {count:N0} {items} to {job.DestinationFolder}"
                : $"Copy {count:N0} {items} to {job.DestinationFolder}",
        };
    }

    private string TransferDetailText(TransferJob job)
    {
        var bytes = $"{FormatBytesCompact(job.BytesCopied)} of {FormatBytesCompact(job.TotalBytes)}";
        switch (job.State)
        {
            case TransferJobState.Paused:
                return $"{bytes} \u00b7 Paused \u2014 progress is kept";
            case TransferJobState.Queued:
                return $"{bytes} \u00b7 Waiting to start";
            case TransferJobState.WaitingOnConflict:
                return $"{bytes} \u00b7 Waiting for your choice below";
            case TransferJobState.Running:
                if (_latestTransferUpdates.TryGetValue(job.Id, out var update))
                {
                    var parts = new List<string> { bytes };
                    if (update.BytesPerSecond is > 0)
                        parts.Add($"{FormatBytesCompact((long)update.BytesPerSecond.Value)}/s");
                    if (update.EstimatedRemaining is { } remaining)
                        parts.Add($"about {FormatDuration(remaining)} left");
                    return string.Join(" \u00b7 ", parts);
                }
                return $"{bytes} \u00b7 Working\u2026";
            default:
                return bytes;
        }
    }

    private static string FormatBytesCompact(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 10) return "a few seconds";
        if (duration.TotalSeconds < 60) return $"{Math.Round(duration.TotalSeconds)} seconds";
        if (duration.TotalMinutes < 2) return "1 minute";
        if (duration.TotalHours < 1) return $"{Math.Round(duration.TotalMinutes)} minutes";
        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;
        return minutes > 0 ? $"{hours} hours {minutes} minutes" : $"{hours} hours";
    }

    // ----- drag and drop ------------------------------------------------------

    private void PaneList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var entries = e.Items.OfType<FileEntry>().ToList();
        if (entries.Count == 0) return;
        PaneState? pane = null;
        string? sourceFolder = null;
        if (ReferenceEquals(sender, LeftItems)) { pane = _left; sourceFolder = _left.CurrentPath; }
        else if (ReferenceEquals(sender, RightItems)) { pane = _right; sourceFolder = _right.CurrentPath; }
        else
        {
            foreach (var candidate in new[] { _left, _right })
            {
                var column = candidate.Columns.FirstOrDefault(c => ReferenceEquals(c.List, sender));
                if (column is not null)
                {
                    pane = candidate;
                    sourceFolder = column.FolderPath;
                    break;
                }
            }
        }
        if (pane is null || sourceFolder is null) return;
        _dragSession = new DragSession(pane, sourceFolder, entries);
        _externalDragPaths = null;
        try { e.Data.SetText(string.Join(Environment.NewLine, entries.Select(entry => entry.FullPath))); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        _ = PopulateDragDataAsync(e.Data, entries);
    }

    private static async Task PopulateDragDataAsync(DataPackage data, List<FileEntry> entries)
    {
        try
        {
            var items = new List<IStorageItem>();
            foreach (var entry in entries)
            {
                if (entry.IsDirectory) items.Add(await StorageFolder.GetFolderFromPathAsync(entry.FullPath));
                else items.Add(await StorageFile.GetFileFromPathAsync(entry.FullPath));
            }
            data.SetStorageItems(items);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // The text form of the drag still works inside Meridian; only the
            // cross-app form of this drag is unavailable.
        }
    }

    private void PaneList_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
    {
        _dragSession = null;
        _externalDragPaths = null;
    }

    private void LeftList_DragOver(object sender, DragEventArgs e) =>
        HandlePaneDragOver(_left, LeftItems, _left.CurrentPath, e);

    private void RightList_DragOver(object sender, DragEventArgs e) =>
        HandlePaneDragOver(_right, RightItems, _right.CurrentPath, e);

    private void LeftList_Drop(object sender, DragEventArgs e) =>
        _ = HandlePaneDropAsync(_left, LeftItems, _left.CurrentPath, e);

    private void RightList_Drop(object sender, DragEventArgs e) =>
        _ = HandlePaneDropAsync(_right, RightItems, _right.CurrentPath, e);

    private void SidebarItem_DragOver(object sender, DragEventArgs e)
    {
        if (sender is Button { Tag: string target })
            HandleDropTargetDragOver(e, target, list: null);
    }

    private void SidebarItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is Button { Tag: string target })
            _ = HandleDropAsync(e, target, list: null);
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        if (sender is Button { Tag: string target })
            HandleDropTargetDragOver(e, target, list: null);
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        if (sender is Button { Tag: string target })
            _ = HandleDropAsync(e, target, list: null);
    }

    private void HandlePaneDragOver(PaneState pane, ListView list, string backgroundDestination, DragEventArgs e) =>
        HandleDropTargetDragOver(e, backgroundDestination, list);

    private async void HandleDropTargetDragOver(DragEventArgs e, string backgroundDestination, ListView? list)
    {
        var deferral = e.GetDeferral();
        try
        {
            var plan = await PlanDropAsync(e, backgroundDestination, list);
            if (plan is null)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.DragUIOverride.Caption = "Cannot drop here";
                e.DragUIOverride.IsCaptionVisible = true;
                return;
            }
            e.AcceptedOperation = plan.Kind == TransferJobKind.Move ? DataPackageOperation.Move : DataPackageOperation.Copy;
            e.DragUIOverride.Caption = $"{plan.Kind} to {FriendlyFolderName(plan.Destination)}";
            e.DragUIOverride.IsCaptionVisible = true;
            e.Handled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task HandlePaneDropAsync(PaneState pane, ListView list, string backgroundDestination, DragEventArgs e) =>
        await HandleDropAsync(e, backgroundDestination, list);

    private async Task HandleDropAsync(DragEventArgs e, string backgroundDestination, ListView? list)
    {
        var deferral = e.GetDeferral();
        try
        {
            var plan = await PlanDropAsync(e, backgroundDestination, list);
            if (plan is null)
            {
                SetStatus("Nothing was dropped \u2014 that place is not a folder Meridian can copy or move into.", StatusSeverity.Warning);
                return;
            }
            var kind = plan.Kind;
            if ((e.AcceptedOperation & DataPackageOperation.Move) != 0) kind = TransferJobKind.Move;
            else if ((e.AcceptedOperation & DataPackageOperation.Copy) != 0) kind = TransferJobKind.Copy;
            var job = TransferJob.Create(kind, plan.SourcePaths, plan.Destination, _nameCollisionPolicy, _verifyCopiesWithChecksum);
            await _transfers.EnqueueAsync(job);
            SetStatus($"Queued {kind.ToString().ToLowerInvariant()} \u00b7 {plan.SourcePaths.Count:N0} item(s) to {plan.Destination}. Progress is in the Transfers strip below.");
            e.Handled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            SetStatus("Drop failed \u00b7 " + ex.Message, StatusSeverity.Error);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>Works out whether a drop is allowed, where it lands, and
    /// whether it copies or moves — before anything is queued. Returns null
    /// for drops Meridian refuses (Home, the source folder itself, a folder
    /// into itself).</summary>
    private async Task<DropPlan?> PlanDropAsync(DragEventArgs e, string? backgroundDestination, ListView? list)
    {
        var destination = backgroundDestination;
        if (list is not null && TryHitFolderRow(list, e, out var folderRow))
            destination = folderRow;
        if (string.IsNullOrWhiteSpace(destination)
            || string.Equals(destination, HomePath, StringComparison.OrdinalIgnoreCase))
            return null;

        var sources = await ResolveDragSourcePathsAsync(e);
        if (sources.Count == 0) return null;

        var sourceFolder = _dragSession?.SourceFolder ?? Path.GetDirectoryName(sources[0]);
        if (!string.IsNullOrWhiteSpace(sourceFolder)
            && string.Equals(NormalizePath(sourceFolder), NormalizePath(destination), StringComparison.OrdinalIgnoreCase))
            return null;
        foreach (var source in sources)
        {
            if (string.Equals(NormalizePath(source), NormalizePath(destination), StringComparison.OrdinalIgnoreCase))
                return null;
            if (Directory.Exists(source) && IsSameOrDescendantPath(source, destination))
                return null;
        }
        return new DropPlan(DecideDropKind(e.Modifiers, sources[0], destination), destination, sources);
    }

    private async Task<IReadOnlyList<string>> ResolveDragSourcePathsAsync(DragEventArgs e)
    {
        if (_dragSession is { } session)
            return session.Entries.Select(entry => entry.FullPath).ToList();
        if (_externalDragPaths is { } cached) return cached;
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var paths = items.Select(item => item.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToList();
                _externalDragPaths = paths;
                return paths;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
        }
        return [];
    }

    private static TransferJobKind DecideDropKind(DragDropModifiers modifiers, string sourcePath, string destination)
    {
        if ((modifiers & DragDropModifiers.Control) != 0) return TransferJobKind.Copy;
        if ((modifiers & DragDropModifiers.Shift) != 0) return TransferJobKind.Move;
        return string.Equals(Path.GetPathRoot(sourcePath), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase)
            ? TransferJobKind.Move
            : TransferJobKind.Copy;
    }

    private static bool TryHitFolderRow(ListView list, DragEventArgs e, out string folderPath)
    {
        folderPath = string.Empty;
        var point = e.GetPosition(list);
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (list.ContainerFromIndex(i) is not ListViewItem container) continue;
            var bounds = container.TransformToVisual(list)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, container.ActualWidth, container.ActualHeight));
            if (bounds.Contains(point) && list.Items[i] is FileEntry { IsDirectory: true } entry)
            {
                folderPath = entry.FullPath;
                return true;
            }
        }
        return false;
    }

    private static string FriendlyFolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    /// <summary>True when candidate is the folder itself or lives underneath it.</summary>
    internal static bool IsSameOrDescendantPath(string folder, string candidate)
    {
        var relative = Path.GetRelativePath(NormalizePath(folder), NormalizePath(candidate));
        return !Path.IsPathRooted(relative) &&
            (relative == "." || (relative != ".." &&
             !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
    }

    // ----- File hash tool -----------------------------------------------------

    private async void FileHash_Click(object sender, RoutedEventArgs e)
    {
        var files = GetSelectedEntries(_activePane).Where(entry => !entry.IsDirectory).ToList();
        if (files.Count == 0)
        {
            SetStatus("Select one or more files in the active pane to see their SHA-256 hashes.", StatusSeverity.Warning);
            return;
        }

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hashLabels = new List<(FileEntry Entry, TextBlock Label)>();
        var panel = new StackPanel { Spacing = 10, MinWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = "A SHA-256 hash is a fingerprint of a file's contents. Two files with the same hash have the same contents; if even one byte differs, the hash is completely different.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MeridianTextSecondaryBrush")
        });
        foreach (var file in files)
        {
            var row = new StackPanel { Spacing = 2 };
            var header = new Grid { ColumnSpacing = 8 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = file.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            var copyButton = new Button { Content = "Copy" };
            ToolTipService.SetToolTip(copyButton, $"Copy the SHA-256 hash of {file.Name} to the clipboard");
            AutomationProperties.SetName(copyButton, $"Copy hash of {file.Name}");
            copyButton.Click += (s, args) =>
            {
                if (!hashes.TryGetValue(file.FullPath, out var hash))
                {
                    SetStatus($"The hash of {file.Name} is still being computed.", StatusSeverity.Warning);
                    return;
                }
                var package = new DataPackage();
                package.SetText(hash);
                Clipboard.SetContent(package);
                SetStatus($"Copied the SHA-256 hash of {file.Name} to the clipboard.", StatusSeverity.Success);
            };
            Grid.SetColumn(copyButton, 1);
            header.Children.Add(copyButton);
            row.Children.Add(header);
            var label = new TextBlock
            {
                Text = "Computing\u2026",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap
            };
            row.Children.Add(label);
            hashLabels.Add((file, label));
            panel.Children.Add(row);
        }

        var compareBox = new TextBox
        {
            Header = "Compare with a hash you already have",
            PlaceholderText = "Paste a SHA-256 hash here\u2026"
        };
        AutomationProperties.SetName(compareBox, "Hash to compare");
        ToolTipService.SetToolTip(compareBox, "Paste a full SHA-256 hash (64 letters and numbers) and choose Compare to see whether it matches one of these files.");
        var compareResult = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var compareButton = new Button { Content = "Compare", HorizontalAlignment = HorizontalAlignment.Left };
        ToolTipService.SetToolTip(compareButton, "Check the pasted hash against the files above");
        AutomationProperties.SetName(compareButton, "Compare pasted hash");
        compareButton.Click += (s, args) =>
        {
            var input = new string(compareBox.Text.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
            if (input.Length != 64 || input.Any(c => !Uri.IsHexDigit(c)))
            {
                compareResult.Text = "That does not look like a full SHA-256 hash. It should be 64 characters, using only 0\u20139 and a\u2013f.";
                return;
            }
            var match = hashes.FirstOrDefault(pair => string.Equals(pair.Value, input, StringComparison.Ordinal));
            if (match.Key is not null)
                compareResult.Text = $"Matches {Path.GetFileName(match.Key)}";
            else if (hashes.Count < files.Count)
                compareResult.Text = "Some hashes are still being computed. Wait a moment, then compare again.";
            else
                compareResult.Text = "Does not match any of these files.";
        };
        panel.Children.Add(compareBox);
        panel.Children.Add(compareButton);
        panel.Children.Add(compareResult);

        var dialog = new ContentDialog
        {
            Title = "File hash \u00b7 SHA-256",
            Content = new ScrollViewer { MaxHeight = 520, Content = panel },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        _ = ComputeHashesAsync(files, hashLabels, hashes);
        await dialog.ShowAsync();
    }

    private static async Task ComputeHashesAsync(
        List<FileEntry> files,
        List<(FileEntry Entry, TextBlock Label)> hashLabels,
        Dictionary<string, string> hashes)
    {
        foreach (var (entry, label) in hashLabels)
        {
            try
            {
                var hash = await FileHash.Sha256HexAsync(entry.FullPath);
                hashes[entry.FullPath] = hash;
                label.Text = hash;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                label.Text = "Could not read this file: " + ex.Message;
            }
        }
    }
}
