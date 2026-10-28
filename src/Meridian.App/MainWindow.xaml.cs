using System.Collections.ObjectModel;
using Meridian.Models;
using Meridian.Services;
using Microsoft.VisualBasic.FileIO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.System;

namespace Meridian;

public sealed partial class MainWindow : Window
{
    private readonly PaneState _left;
    private readonly PaneState _right;
    private readonly WorkspaceStore _workspaceStore = new();
    private readonly DropShelf _dropShelf = new();
    private readonly List<ContextCapsule> _capsules = [];
    private readonly ContextCapsuleStore _capsuleStore = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian", "capsules.json"));
    private bool _capsuleStorageUnavailable;
    private bool _capsuleSaving;
    private readonly ObservableCollection<string> _shelfPaths = [];
    private readonly ObservableCollection<BookmarkSnapshot> _bookmarks = [];
    private readonly List<WorkspaceTab> _workspaceTabs = [];
    private int _activeWorkspaceTabIndex;
    private int _nextWorkspaceTabNumber = 1;
    private bool _switchingWorkspaceTab;
    private PaneState _activePane;
    private OrganizeSection _openOrganizeSection = OrganizeSection.None;
    private bool _restoringWorkspace;
    private CancellationTokenSource? _copyCancellation;
    private CancellationTokenSource? _batchCancellation;
    private bool _batchPreparing;
    private string? _pinnedError;

    public MainWindow()
    {
        InitializeComponent();
        BookmarksBox.ItemsSource = _bookmarks;
        ShelfItemsBox.ItemsSource = _shelfPaths;
        _left = new PaneState("Left", LeftPath, LeftFilter, LeftItems, LeftStatus);
        _right = new PaneState("Right", RightPath, RightFilter, RightItems, RightStatus);
        _activePane = _left;
        UpdateActivePaneVisuals();
        LeftItems.ItemsSource = _left.Items;
        RightItems.ItemsSource = _right.Items;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents) || !Directory.Exists(documents)) documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloads)) downloads = documents;
        _ = InitializeAsync(documents, downloads);
    }

    private async Task InitializeAsync(string documents, string downloads)
    {
        try
        {
            _capsules.AddRange(await _capsuleStore.LoadAsync());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            // Never replace unreadable saved data with a new empty document.
            _capsuleStorageUnavailable = true;
            SetStatus("Saved Capsules could not be loaded; saving is disabled to preserve the original file · " + ex.Message, StatusSeverity.Error);
        }
        var saved = await _workspaceStore.LoadAsync();
        _bookmarks.Clear();
        foreach (var bookmark in saved?.Bookmarks ?? []) _bookmarks.Add(bookmark);

        var defaultLeft = saved?.LeftPath is { Length: > 0 } savedLeft && Directory.Exists(savedLeft) ? savedLeft : documents;
        var defaultRight = saved?.RightPath is { Length: > 0 } savedRight && Directory.Exists(savedRight) ? savedRight : downloads;
        if (saved?.Tabs is { Length: > 0 })
        {
            foreach (var tab in saved.Tabs)
            {
                if (tab is null) continue;
                var tabName = string.IsNullOrWhiteSpace(tab.Name) ? $"Workspace {_workspaceTabs.Count + 1}" : tab.Name;
                _workspaceTabs.Add(new WorkspaceTab(tabName, tab.LeftPath ?? defaultLeft, tab.RightPath ?? defaultRight));
            }
        }
        if (_workspaceTabs.Count == 0)
            _workspaceTabs.Add(new WorkspaceTab("Workspace 1", defaultLeft, defaultRight));
        _activeWorkspaceTabIndex = Math.Clamp(saved?.ActiveTabIndex ?? 0, 0, _workspaceTabs.Count - 1);
        _nextWorkspaceTabNumber = _workspaceTabs.Count + 1;
        RefreshWorkspaceTabs();

        var activeTab = _workspaceTabs[_activeWorkspaceTabIndex];
        var leftPath = Directory.Exists(activeTab.LeftPath) ? activeTab.LeftPath : defaultLeft;
        var rightPath = Directory.Exists(activeTab.RightPath) ? activeTab.RightPath : defaultRight;
        activeTab.LeftPath = leftPath;
        activeTab.RightPath = rightPath;

        _restoringWorkspace = true;
        try
        {
            await NavigateAsync(_left, leftPath, addHistory: false);
            await NavigateAsync(_right, rightPath, addHistory: false);
        }
        finally
        {
            _restoringWorkspace = false;
        }

        await PersistWorkspaceAsync();
    }

    private async Task PersistWorkspaceAsync()
    {
        if (_restoringWorkspace) return;
        try
        {
            StoreCurrentWorkspaceTab();
            var tabs = _workspaceTabs.Select(tab => new WorkspaceTabSnapshot(tab.Name, tab.LeftPath, tab.RightPath)).ToArray();
            await _workspaceStore.SaveAsync(new WorkspaceSnapshot(_left.CurrentPath, _right.CurrentPath, _bookmarks.ToArray(), tabs, _activeWorkspaceTabIndex));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Workspace persistence is a convenience; file browsing must continue if it fails.
            SetStatus("Workspace could not be saved · " + ex.Message, StatusSeverity.Error);
        }
    }

    private string NextWorkspaceTabName()
    {
        string name;
        do { name = $"Workspace {_nextWorkspaceTabNumber++}"; }
        while (_workspaceTabs.Any(tab => string.Equals(tab.Name, name, StringComparison.OrdinalIgnoreCase)));
        return name;
    }

    private void StoreCurrentWorkspaceTab()
    {
        if (_workspaceTabs.Count == 0 || _activeWorkspaceTabIndex < 0 || _activeWorkspaceTabIndex >= _workspaceTabs.Count) return;
        var active = _workspaceTabs[_activeWorkspaceTabIndex];
        if (!string.IsNullOrWhiteSpace(_left.CurrentPath)) active.LeftPath = _left.CurrentPath;
        if (!string.IsNullOrWhiteSpace(_right.CurrentPath)) active.RightPath = _right.CurrentPath;
    }

    private void RefreshWorkspaceTabs()
    {
        WorkspaceTabsPanel.Children.Clear();
        for (var i = 0; i < _workspaceTabs.Count; i++)
        {
            var tab = _workspaceTabs[i];
            var tabButton = new Button
            {
                Content = (i == _activeWorkspaceTabIndex ? "▸ " : "") + tab.Name,
                Tag = tab,
                MinWidth = 110,
                Padding = new Thickness(10, 4, 10, 4)
            };
            tabButton.Click += WorkspaceTab_Click;
            WorkspaceTabsPanel.Children.Add(tabButton);
            if (_workspaceTabs.Count > 1)
            {
                var closeButton = new Button
                {
                    Content = new SymbolIcon { Symbol = Symbol.Cancel },
                    Tag = tab,
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(0)
                };
                ToolTipService.SetToolTip(closeButton, "Close " + tab.Name);
                closeButton.Click += CloseWorkspaceTab_Click;
                WorkspaceTabsPanel.Children.Add(closeButton);
            }
        }
    }

    private async void AddWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (_switchingWorkspaceTab) return;
        StoreCurrentWorkspaceTab();
        var leftPath = string.IsNullOrWhiteSpace(_left.CurrentPath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : _left.CurrentPath;
        var rightPath = string.IsNullOrWhiteSpace(_right.CurrentPath) ? leftPath : _right.CurrentPath;
        var tab = new WorkspaceTab(NextWorkspaceTabName(), leftPath, rightPath);
        _workspaceTabs.Add(tab);
        _activeWorkspaceTabIndex = _workspaceTabs.Count - 1;
        RefreshWorkspaceTabs();
        await PersistWorkspaceAsync();
        SetStatus($"Opened {tab.Name}");
    }

    private async void WorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (_switchingWorkspaceTab || (sender as Button)?.Tag is not WorkspaceTab tab) return;
        var index = _workspaceTabs.IndexOf(tab);
        if (index < 0 || index == _activeWorkspaceTabIndex) return;
        StoreCurrentWorkspaceTab();
        _activeWorkspaceTabIndex = index;
        RefreshWorkspaceTabs();
        await LoadWorkspaceTabAsync(tab);
    }

    private async void CloseWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (_switchingWorkspaceTab || (sender as Button)?.Tag is not WorkspaceTab tab) return;
        var index = _workspaceTabs.IndexOf(tab);
        if (index < 0 || _workspaceTabs.Count <= 1) return;

        var wasActive = index == _activeWorkspaceTabIndex;
        if (wasActive) StoreCurrentWorkspaceTab();
        _workspaceTabs.RemoveAt(index);
        if (index < _activeWorkspaceTabIndex) _activeWorkspaceTabIndex--;
        else if (wasActive) _activeWorkspaceTabIndex = Math.Min(index, _workspaceTabs.Count - 1);
        RefreshWorkspaceTabs();

        if (wasActive)
            await LoadWorkspaceTabAsync(_workspaceTabs[_activeWorkspaceTabIndex]);
        else
            await PersistWorkspaceAsync();
    }

    private async Task LoadWorkspaceTabAsync(WorkspaceTab tab)
    {
        if (_switchingWorkspaceTab) return;
        _switchingWorkspaceTab = true;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(documents)) documents = profile;
        var downloads = Path.Combine(profile, "Downloads");
        if (!Directory.Exists(downloads)) downloads = documents;
        var leftPath = Directory.Exists(tab.LeftPath) ? tab.LeftPath : documents;
        var rightPath = Directory.Exists(tab.RightPath) ? tab.RightPath : downloads;
        _left.List.SelectedItem = null;
        _right.List.SelectedItem = null;
        _restoringWorkspace = true;
        try
        {
            await NavigateAsync(_left, leftPath, addHistory: false);
            await NavigateAsync(_right, rightPath, addHistory: false);
        }
        finally
        {
            _restoringWorkspace = false;
            _switchingWorkspaceTab = false;
        }
        tab.LeftPath = _left.CurrentPath;
        tab.RightPath = _right.CurrentPath;
        await PersistWorkspaceAsync();
        SetStatus($"Switched to {tab.Name}");
    }

    private async Task AddBookmarkAsync()
    {
        var path = _activePane.CurrentPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        if (_bookmarks.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("That location is already bookmarked.", StatusSeverity.Warning);
            return;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(name)) name = path;
        _bookmarks.Add(new BookmarkSnapshot(name, path));
        await PersistWorkspaceAsync();
        SetStatus($"Bookmarked · {name}", StatusSeverity.Success);
    }

    private async void AddBookmark_Click(object sender, RoutedEventArgs e) => await AddBookmarkAsync();

    private async void RemoveBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (BookmarksBox.SelectedItem is not BookmarkSnapshot selected)
        {
            SetStatus("Select a saved location to remove.", StatusSeverity.Warning);
            return;
        }

        _bookmarks.Remove(selected);
        BookmarksBox.SelectedItem = null;
        await PersistWorkspaceAsync();
        SetStatus($"Removed bookmark · {selected.Name}", StatusSeverity.Success);
    }

    private async void BookmarksBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BookmarksBox.SelectedItem is not BookmarkSnapshot selected) return;
        await NavigateAsync(_activePane, selected.Path);
        BookmarksBox.SelectedItem = null;
    }

    private async Task<bool> NavigateAsync(PaneState pane, string requestedPath, bool addHistory = true)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedPath.Trim().Trim('"')));
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Folder not found: {fullPath}");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SetPaneStatus(pane, ex.Message); SetStatus("Navigation failed · " + ex.Message, StatusSeverity.Error); pane.PathBox.Text = pane.CurrentPath; return false;
        }
        pane.ReadCancellation?.Cancel(); pane.ReadCancellation?.Dispose(); pane.ReadCancellation = new CancellationTokenSource();
        var token = pane.ReadCancellation.Token;
        SetPaneStatus(pane, "Loading folder…");
        try
        {
            var entries = await Task.Run(() => DirectoryReader.Read(fullPath, token), token);
            if (token.IsCancellationRequested) return false;
            if (addHistory && !string.IsNullOrWhiteSpace(pane.CurrentPath) && !string.Equals(pane.CurrentPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                pane.BackHistory.Push(pane.CurrentPath);
                pane.ForwardHistory.Clear();
            }
            pane.CurrentPath = fullPath;
            pane.PathBox.Text = fullPath;
            UpdatePaneHeader(pane);
            pane.AllItems = entries;
            ApplyFilter(pane);
            SetStatus("Ready · Local-only · No telemetry");
            if (!_restoringWorkspace) await PersistWorkspaceAsync();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!token.IsCancellationRequested)
            {
                SetPaneStatus(pane, ex.Message);
                SetStatus("Could not read folder · " + ex.Message, StatusSeverity.Error);
                pane.PathBox.Text = pane.CurrentPath;
            }
            return false;
        }
    }

    private void ApplyFilter(PaneState pane)
    {
        var query = pane.FilterBox.Text.Trim();
        var matches = DirectoryReader.Filter(pane.AllItems, query, pane.SortField, pane.Descending, includeFullPath: false);

        pane.Items.Clear();
        foreach (var item in matches)
            pane.Items.Add(item);

        SetPaneStatus(pane, string.IsNullOrEmpty(query)
            ? $"{pane.AllItems.Count:N0} items"
            : $"{matches.Count:N0} of {pane.AllItems.Count:N0} items · filtered");
    }

    private void LeftSort_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSort(_left, LeftSort, LeftSortDescending);
    private void RightSort_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSort(_right, RightSort, RightSortDescending);
    private void LeftSortDirection_Changed(object sender, RoutedEventArgs e) => UpdateSort(_left, LeftSort, LeftSortDescending);
    private void RightSortDirection_Changed(object sender, RoutedEventArgs e) => UpdateSort(_right, RightSort, RightSortDescending);

    private void UpdateSort(PaneState? pane, ComboBox sort, ToggleButton direction)
    {
        if (pane is null || sort is null || direction is null) return;
        pane.SortField = (DirectorySortField)Math.Clamp(sort.SelectedIndex, 0, 3);
        pane.Descending = direction.IsChecked == true;
        ApplyFilter(pane);
    }

    private void LeftFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter(_left);
    private void RightFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter(_right);

    private async Task NavigateUpAsync(PaneState pane)
    {
        if (string.IsNullOrWhiteSpace(pane.CurrentPath)) return;
        var parent = Directory.GetParent(pane.CurrentPath)?.FullName;
        if (parent is not null) await NavigateAsync(pane, parent);
    }
    private async Task NavigateBackAsync(PaneState pane)
    {
        if (pane.BackHistory.Count == 0) return;
        var target = pane.BackHistory.Peek();
        var previous = pane.CurrentPath;
        if (await NavigateAsync(pane, target, addHistory: false))
        {
            pane.BackHistory.Pop();
            if (!string.IsNullOrWhiteSpace(previous)) pane.ForwardHistory.Push(previous);
        }
    }
    private async Task NavigateForwardAsync(PaneState pane)
    {
        if (pane.ForwardHistory.Count == 0) return;
        var target = pane.ForwardHistory.Peek();
        var previous = pane.CurrentPath;
        if (await NavigateAsync(pane, target, addHistory: false))
        {
            pane.ForwardHistory.Pop();
            if (!string.IsNullOrWhiteSpace(previous)) pane.BackHistory.Push(previous);
        }
    }
    private async Task OpenSelectedAsync(PaneState pane)
    {
        if (pane.List.SelectedItem is not FileEntry selected) return;
        if (selected.IsDirectory) { await NavigateAsync(pane, selected.FullPath); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = selected.FullPath, UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { SetStatus("Could not open file · " + ex.Message, StatusSeverity.Error); }
    }
    private async Task MoveSelectedAsync(PaneState source, PaneState destination)
    {
        var selection = GetSelectedEntries(source);
        if (selection.Count == 0)
        {
            SetStatus($"Select an item in the {source.Name.ToLowerInvariant()} pane first.", StatusSeverity.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(destination.CurrentPath)) return;

        ContentDialog dialog;
        if (selection.Count == 1)
        {
            var selected = selection[0];
            var kind = selected.IsDirectory ? "folder" : "file";
            dialog = new ContentDialog
            {
                Title = "Move item?",
                Content = $"Move this {kind} to the other pane's current folder?\n\nFrom: {selected.FullPath}\nTo: {destination.CurrentPath}\n\nExisting items will not be overwritten.\n\nIf you change your mind, you can move it back from the destination folder.",
                PrimaryButtonText = "Move",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
        }
        else
        {
            dialog = new ContentDialog
            {
                Title = "Move items?",
                Content = $"Move {selection.Count:N0} items to the other pane's current folder?\n\nFrom: {source.CurrentPath}\nTo: {destination.CurrentPath}\n\nExisting items will not be overwritten. If an item cannot be moved, the move stops and items already moved stay moved.\n\nYou can move items back from the destination folder afterwards.",
                PrimaryButtonText = "Move",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
        }
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ClearPinnedError();
        var sourcePaths = selection.Select(item => item.FullPath).ToArray();
        var sourceFolder = source.CurrentPath;
        var destinationPath = destination.CurrentPath;
        var movedCount = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var sourcePath in sourcePaths)
                {
                    FileOperations.MoveItem(sourcePath, destinationPath);
                    movedCount++;
                }
            });
            if (string.Equals(source.CurrentPath, sourceFolder, StringComparison.OrdinalIgnoreCase))
                await NavigateAsync(source, sourceFolder, addHistory: false);
            if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase))
                await NavigateAsync(destination, destinationPath, addHistory: false);
            SetStatus(sourcePaths.Length == 1
                ? $"Moved {Path.GetFileName(sourcePaths[0])} to {destinationPath}"
                : $"Moved {movedCount:N0} items to {destinationPath}", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (movedCount > 0)
            {
                if (string.Equals(source.CurrentPath, sourceFolder, StringComparison.OrdinalIgnoreCase))
                    await NavigateAsync(source, sourceFolder, addHistory: false);
                if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase))
                    await NavigateAsync(destination, destinationPath, addHistory: false);
            }
            SetStatus(movedCount > 0
                ? $"Move stopped after {movedCount:N0} of {sourcePaths.Length:N0} items · {ex.Message}"
                : "Move failed · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async Task CopySelectedAsync(PaneState source, PaneState destination)
    {
        if (_copyCancellation is not null) { SetStatus("A copy is already in progress.", StatusSeverity.Warning); return; }
        var selection = GetSelectedEntries(source);
        if (selection.Count == 0) { SetStatus($"Select an item in the {source.Name.ToLowerInvariant()} pane first.", StatusSeverity.Warning); return; }
        if (string.IsNullOrWhiteSpace(destination.CurrentPath)) return;

        var sourcePaths = selection.Select(item => item.FullPath).ToArray();
        var destinationPath = destination.CurrentPath;
        // A folder can never be copied into itself. Single copies check here;
        // batch copies are also refused by the BatchCopy preflight before anything is copied.
        foreach (var sourcePath in sourcePaths)
        {
            if (Directory.Exists(sourcePath) &&
                string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            { SetStatus("The destination is the selected folder. Choose another destination.", StatusSeverity.Warning); return; }
        }

        var cancellation = new CancellationTokenSource();
        _copyCancellation = cancellation;
        CancelCopyButton.IsEnabled = true;
        ClearPinnedError();
        SetStatus(sourcePaths.Length == 1 ? $"Copying {selection[0].Name}…" : $"Copying {sourcePaths.Length:N0} items…");
        if (sourcePaths.Length == 1) ShowOperationProgress($"Copying {selection[0].Name}", null);
        SetPaneStatus(source, "Copy in progress…");
        try
        {
            if (sourcePaths.Length == 1)
            {
                var copiedTo = await Task.Run(() => FileOperations.CopyItem(sourcePaths[0], destinationPath, cancellation.Token), cancellation.Token);
                if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase)) await NavigateAsync(destination, destinationPath, addHistory: false);
                SetPaneStatus(source, "Ready"); SetStatus($"Copied {Path.GetFileName(copiedTo)} to {destinationPath}", StatusSeverity.Success);
            }
            else
            {
                var progress = new Progress<BatchCopy.Progress>(state =>
                {
                    if (_copyCancellation == cancellation)
                        ShowOperationProgress($"Copying {state.Completed:N0}/{state.Total:N0} items · {Path.GetFileName(state.CurrentSource)}",
                            state.Total > 0 ? state.Completed * 100.0 / state.Total : 0);
                });
                var result = await Task.Run(() => BatchCopy.Execute(sourcePaths, destinationPath, cancellation.Token, progress), cancellation.Token);
                if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase)) await NavigateAsync(destination, destinationPath, addHistory: false);
                if (result.Succeeded)
                {
                    SetPaneStatus(source, "Ready");
                    SetStatus($"Copied {result.CompletedTargets.Count:N0} items to {destinationPath}", StatusSeverity.Success);
                }
                else if (result.Cancelled)
                {
                    SetPaneStatus(source, "Copy cancelled");
                    SetStatus($"Copy cancelled · {result.CompletedTargets.Count:N0} of {sourcePaths.Length:N0} items copied. Items already copied stay copied.", StatusSeverity.Warning);
                }
                else
                {
                    SetPaneStatus(source, "Copy failed");
                    SetStatus($"Copy stopped after {result.CompletedTargets.Count:N0} of {sourcePaths.Length:N0} items · {result.Failure}", StatusSeverity.Error);
                }
            }
        }
        catch (OperationCanceledException)
        {
            SetPaneStatus(source, "Copy cancelled");
            SetStatus("Copy cancelled · Partial staging data was cleaned up where possible.", StatusSeverity.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException or InvalidOperationException)
        { SetPaneStatus(source, "Copy failed"); SetStatus("Copy failed · " + ex.Message, StatusSeverity.Error); }
        finally
        {
            if (ReferenceEquals(_copyCancellation, cancellation)) _copyCancellation = null;
            CancelCopyButton.IsEnabled = false;
            HideOperationProgress();
            cancellation.Dispose();
        }
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath))
        {
            SetStatus("Choose a folder before creating a new folder.", StatusSeverity.Warning);
            return;
        }

        var nameBox = new TextBox { PlaceholderText = "Folder name", MinWidth = 280 };
        var newFolderContent = new StackPanel { Spacing = 8 };
        newFolderContent.Children.Add(new TextBlock
        {
            Text = $"The new folder is created inside:\n{_activePane.CurrentPath}\n\nNothing else changes. You can rename or delete the folder afterwards.",
            TextWrapping = TextWrapping.Wrap
        });
        newFolderContent.Children.Add(nameBox);
        var dialog = new ContentDialog
        {
            Title = "Create new folder",
            Content = newFolderContent,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var name = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains(Path.DirectorySeparatorChar) ||
            name.Contains(Path.AltDirectorySeparatorChar) ||
            Path.IsPathRooted(name))
        {
            SetStatus("Folder name is invalid. Enter a single folder name without path separators.", StatusSeverity.Warning);
            return;
        }

        var target = Path.Combine(_activePane.CurrentPath, name);
        if (File.Exists(target) || Directory.Exists(target))
        {
            SetStatus($"Cannot create folder · '{name}' already exists.", StatusSeverity.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(target);
            var pane = _activePane;
            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            SetStatus($"Created folder · {name}", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            SetStatus("Could not create folder · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async void RenameSelected_Click(object sender, RoutedEventArgs e) => await RenameSelectedAsync(_activePane);

    private async Task RenameSelectedAsync(PaneState pane)
    {
        if (pane.List.SelectedItem is not FileEntry selected)
        {
            SetStatus("Select a file or folder to rename.", StatusSeverity.Warning);
            return;
        }

        var nameBox = new TextBox { Text = selected.Name, MinWidth = 280, SelectionStart = 0, SelectionLength = selected.Name.Length };
        var renameContent = new StackPanel { Spacing = 8 };
        renameContent.Children.Add(new TextBlock
        {
            Text = "Only the name changes. The item stays in the same folder with its contents unchanged, and you can rename it again at any time.",
            TextWrapping = TextWrapping.Wrap
        });
        renameContent.Children.Add(nameBox);
        var dialog = new ContentDialog
        {
            Title = "Rename item",
            Content = renameContent,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var renamedTo = FileOperations.RenameItem(selected.FullPath, nameBox.Text);
            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            var unchanged = string.Equals(renamedTo, selected.FullPath, StringComparison.Ordinal);
            SetStatus(unchanged
                ? "Name unchanged."
                : $"Renamed · {Path.GetFileName(renamedTo)}",
                unchanged ? StatusSeverity.Info : StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            SetStatus("Could not rename item · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async void PropertiesSelected_Click(object sender, RoutedEventArgs e) => await PropertiesSelectedAsync(_activePane);

    private async Task PropertiesSelectedAsync(PaneState pane)
    {
        if (pane.List.SelectedItem is not FileEntry selected)
        {
            SetStatus("Select a file or folder to view its properties.", StatusSeverity.Warning);
            return;
        }

        try
        {
            FileSystemInfo info = selected.IsDirectory
                ? new DirectoryInfo(selected.FullPath)
                : new FileInfo(selected.FullPath);
            info.Refresh();
            if (!info.Exists)
                throw new FileNotFoundException("The item no longer exists.", selected.FullPath);

            var size = selected.IsDirectory
                ? "Not calculated for folders"
                : FormatFileSize(((FileInfo)info).Length);
            var details = new StackPanel { Spacing = 8, MaxWidth = 520 };
            AddPropertyRow(details, "Name", string.IsNullOrWhiteSpace(info.Name) ? info.FullName : info.Name);
            var extension = Path.GetExtension(info.Name);
            AddPropertyRow(details, "Type", selected.IsDirectory ? "Folder" : (string.IsNullOrWhiteSpace(extension) ? "File (no extension)" : extension + " file"));
            AddPropertyRow(details, "Location", info.FullName);
            AddPropertyRow(details, "Size", size);
            AddPropertyRow(details, "Created", info.CreationTime.ToString("g"));
            AddPropertyRow(details, "Modified", info.LastWriteTime.ToString("g"));
            AddPropertyRow(details, "Last accessed", info.LastAccessTime.ToString("g"));
            AddPropertyRow(details, "Attributes", info.Attributes.ToString());

            var dialog = new ContentDialog
            {
                Title = "Properties · " + info.Name,
                Content = new ScrollViewer { Content = details, MaxHeight = 440, MaxWidth = 560 },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            SetStatus("Could not read properties · " + ex.Message, StatusSeverity.Error);
        }
    }

    private static void AddPropertyRow(StackPanel panel, string label, string value)
    {
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MeridianDialogLabelBrush"] });
        row.Children.Add(new TextBlock { Text = value, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
        panel.Children.Add(row);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]} ({bytes:N0} bytes)";
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync(_activePane);

    private async Task DeleteSelectedAsync(PaneState pane)
    {
        var selection = GetSelectedEntries(pane);
        if (selection.Count == 0)
        {
            SetStatus("Select a file or folder to delete.", StatusSeverity.Warning);
            return;
        }

        ContentDialog dialog;
        if (selection.Count == 1)
        {
            var selected = selection[0];
            var kind = selected.IsDirectory ? "folder" : "file";
            dialog = new ContentDialog
            {
                Title = "Move to Recycle Bin?",
                Content = $"Move this {kind} to the Recycle Bin?\n\n{selected.FullPath}\n\nYou can restore it from the Recycle Bin unless Windows prevents doing so.",
                PrimaryButtonText = "Move to Recycle Bin",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
        }
        else
        {
            dialog = new ContentDialog
            {
                Title = "Move to Recycle Bin?",
                Content = $"Move {selection.Count:N0} items to the Recycle Bin?\n\nFrom: {pane.CurrentPath}\n\nYou can restore them from the Recycle Bin unless Windows prevents doing so. If an item cannot be moved, the delete stops and items already moved stay in the Recycle Bin.",
                PrimaryButtonText = "Move to Recycle Bin",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
        }

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ClearPinnedError();
        var deletedCount = 0;
        try
        {
            foreach (var item in selection)
            {
                if (item.IsDirectory)
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                deletedCount++;
            }

            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            SetStatus(selection.Count == 1
                ? $"Moved to Recycle Bin · {selection[0].Name}"
                : $"Moved {deletedCount:N0} items to Recycle Bin", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            if (deletedCount > 0) await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            SetStatus(deletedCount > 0
                ? $"Delete stopped after {deletedCount:N0} of {selection.Count:N0} items · {ex.Message}"
                : "Could not move item to Recycle Bin · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async void FolderDna_Click(object sender, RoutedEventArgs e)
    {
        var pane = _activePane;
        if (string.IsNullOrWhiteSpace(pane.CurrentPath))
        {
            SetStatus("Choose a folder to profile.", StatusSeverity.Warning);
            return;
        }

        // Analyze the complete cached listing, not the filtered visible subset.
        // No filesystem access, recursive traversal, or hashing is performed here.
        var snapshot = FolderDna.Analyze(pane.AllItems);
        var types = snapshot.Types.Take(8).Select(group =>
            $"{group.Label}: {group.Count:N0} files · {DirectoryReader.FormatSize(group.Bytes)}");
        var oldest = snapshot.OldestModifiedUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "Unknown";
        var newest = snapshot.NewestModifiedUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "Unknown";
        var details = $"Location: {pane.CurrentPath}\n\n" +
            $"Folders: {snapshot.Folders:N0}\n" +
            $"Files: {snapshot.Files:N0}\n" +
            $"Known file size: {DirectoryReader.FormatSize(snapshot.KnownFileBytes)}\n" +
            $"Oldest file modified: {oldest}\n" +
            $"Newest file modified: {newest}\n\n" +
            "Largest file-type groups:\n" +
            (snapshot.Types.Count == 0 ? "No files" : string.Join("\n", types)) +
            (snapshot.Types.Count > 8 ? $"\n…and {snapshot.Types.Count - 8:N0} more types" : "") +
            "\n\nOnly immediate files are included. Folder sizes and nested contents are not scanned.";
        var dialog = new ContentDialog
        {
            Title = "Folder DNA · Current folder",
            Content = new ScrollViewer
            {
                Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap },
                MaxHeight = 460
            },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void AddToShelf_Click(object sender, RoutedEventArgs e) => AddSelectedToShelf(_activePane);

    private void AddSelectedToShelf(PaneState pane)
    {
        var selection = GetSelectedEntries(pane);
        if (selection.Count == 0)
        {
            SetStatus("Select a file or folder to add to the Drop Shelf.", StatusSeverity.Warning);
            return;
        }
        var added = 0;
        foreach (var item in selection)
        {
            if (_dropShelf.Add(item.FullPath))
            {
                _shelfPaths.Add(Path.GetFullPath(item.FullPath));
                added++;
            }
        }
        var shelfAdded = selection.Count == 1 ? added == 1 : added > 0;
        SetStatus(selection.Count == 1
            ? (added == 1 ? $"Added to Drop Shelf · {_shelfPaths.Count:N0} collected" : "This item is already on the Drop Shelf.")
            : added > 0
                ? $"Added {added:N0} items to Drop Shelf · {_shelfPaths.Count:N0} collected"
                : "Those items are already on the Drop Shelf.",
            shelfAdded ? StatusSeverity.Success : StatusSeverity.Warning);
    }

    private void RemoveFromShelf_Click(object sender, RoutedEventArgs e)
    {
        if (ShelfItemsBox.SelectedItem is not string selected)
        {
            SetStatus("Select a Drop Shelf item to remove.", StatusSeverity.Warning);
            return;
        }
        _dropShelf.Remove(selected);
        _shelfPaths.Remove(selected);
        SetStatus($"Removed from Drop Shelf · {_shelfPaths.Count:N0} remaining", StatusSeverity.Success);
    }

    private async void PreviewShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_dropShelf.Paths.Count == 0)
        {
            SetStatus("Add items to the Drop Shelf before previewing a batch.", StatusSeverity.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath))
        {
            SetStatus("Open the intended destination folder in the active pane.", StatusSeverity.Warning);
            return;
        }

        try
        {
            var sources = _dropShelf.Paths.ToArray();
            var destination = _activePane.CurrentPath;
            var plan = await Task.Run(() => OperationPreview.PreviewCopy(sources, destination));
            var impact = ConsequenceMap.Explain(plan);
            var risks = impact.Risks.Count == 0
                ? "No known blockers identified at preview time."
                : string.Join("\n", impact.Risks.Select(risk => $"• {risk.Count:N0} {risk.Status}: {risk.Explanation}"));
            var lines = impact.DisplayedItems.Select(item =>
                $"[{item.Result}] {Path.GetFileName(item.Source)}\n  {item.Explanation}\n  → {item.Target ?? "(unavailable)"}");
            var summary = $"Destination: {plan.DestinationDirectory}\n" +
                $"Collected: {impact.TotalItems:N0} · Ready: {impact.ReadyItems:N0} · " +
                $"Blocked: {impact.BlockedItems:N0}\n\nRisk summary:\n{risks}\n\n" +
                string.Join("\n\n", lines) +
                (impact.OmittedItems > 0 ? $"\n\n…and {impact.OmittedItems:N0} more items not shown." : "") +
                "\n\nREAD-ONLY PREVIEW: No files were copied, moved, or deleted. " +
                "A preview cannot reserve paths or guarantee a later operation will succeed.";
            var dialog = new ContentDialog
            {
                Title = "Operation Simulator · Drop Shelf batch",
                Content = new ScrollViewer
                {
                    Content = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap },
                    MaxHeight = 480
                },
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            SetStatus("Could not preview shelf · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async void CopyShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCancellation is not null || _batchPreparing) return;
        if (_dropShelf.Paths.Count == 0)
        {
            SetStatus("Add files to the Drop Shelf first.", StatusSeverity.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath))
        {
            SetStatus("Open the destination folder in the active pane.", StatusSeverity.Warning);
            return;
        }
        var sources = _dropShelf.Paths.ToArray();
        var destination = _activePane.CurrentPath;
        var destinationPane = _activePane;
        using var cancellation = new CancellationTokenSource();
        _batchCancellation = cancellation;
        CancelShelfButton.IsEnabled = true;
        _batchPreparing = true;
        try
        {
            var plan = await Task.Run(() => OperationPreview.PreviewCopy(sources, destination, cancellation.Token), cancellation.Token);
            if (plan.HasBlockers)
            {
                SetStatus($"Copy refused · {plan.Items.Count - plan.ReadyCount:N0} blocked. Use Preview shelf to inspect.", StatusSeverity.Error);
                return;
            }
            var confirmation = new ContentDialog
            {
                Title = "Copy Drop Shelf items?",
                Content = new TextBlock
                {
                    Text = $"Copy {plan.Items.Count:N0} collected items to:\n{plan.DestinationDirectory}\n\n" +
                           "Files will be copied, not moved. Existing destinations will never be overwritten. " +
                           "A later error or cancellation can leave earlier items copied; there is no automatic rollback. " +
                           "The destination is rechecked during execution.",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Copy items",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            cancellation.Token.ThrowIfCancellationRequested();
            ClearPinnedError();
            SetStatus("Copying Drop Shelf items…");
                var progress = new Progress<BatchCopy.Progress>(state =>
                {
                    if (_batchCancellation == cancellation)
                        ShowOperationProgress($"Batch copy · {state.Completed:N0}/{state.Total:N0} items · {Path.GetFileName(state.CurrentSource)}",
                            state.Total > 0 ? state.Completed * 100.0 / state.Total : 0);
                });
                var result = await Task.Run(() => BatchCopy.Execute(sources, destination, cancellation.Token, progress));
                await NavigateAsync(destinationPane, destination, addHistory: false);
                if (result.Succeeded)
                    SetStatus($"Copied {result.CompletedTargets.Count:N0} items to {destination}", StatusSeverity.Success);
                else if (result.Cancelled)
                    SetStatus($"Batch copy cancelled · {result.CompletedTargets.Count:N0} of {sources.Length:N0} items copied. Items already copied stay copied.", StatusSeverity.Warning);
                else
                    SetStatus($"Batch stopped after {result.CompletedTargets.Count:N0} of {sources.Length:N0} items · {result.Failure}", StatusSeverity.Error);
                if (!result.Succeeded)
                {
                    var details = new ContentDialog
                    {
                        Title = result.Cancelled ? "Batch copy cancelled" : "Batch copy stopped",
                        Content = new ScrollViewer
                        {
                            MaxHeight = 360,
                            Content = new TextBlock
                            {
                                Text = $"Completed: {result.CompletedTargets.Count:N0} of {sources.Length:N0} items\n" +
                                       $"Destination: {destination}\n" +
                                       $"Stopped at: {result.FailedSource ?? "(unknown)"}\n" +
                                       $"Reason: {(result.Cancelled ? "Cancelled by request" : result.Failure ?? "Unknown failure")}\n\n" +
                                       "Earlier successful copies remain in the destination. Original files were not moved. " +
                                       "There is no automatic rollback.\n\nCompleted targets:\n" +
                                       string.Join("\n", result.CompletedTargets.Take(100)) +
                                       (result.CompletedTargets.Count > 100 ? $"\n…and {result.CompletedTargets.Count - 100:N0} more" : ""),
                                TextWrapping = TextWrapping.Wrap,
                                IsTextSelectionEnabled = true
                            }
                        },
                        CloseButtonText = "Close",
                        XamlRoot = Content.XamlRoot
                    };
                    await details.ShowAsync();
                }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Batch preparation cancelled · No copy started.", StatusSeverity.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        {
            SetStatus("Batch copy did not start · " + ex.Message, StatusSeverity.Error);
        }
        finally
        {
            _batchCancellation = null;
            CancelShelfButton.IsEnabled = false;
            HideOperationProgress();
            _batchPreparing = false;
        }
    }

    private async void SaveCapsule_Click(object sender, RoutedEventArgs e)
    {
        if (_capsuleSaving) return;
        _capsuleSaving = true;
        try { await SaveCapsuleAsync(); }
        catch (InvalidOperationException ex)
        {
            SetStatus("Could not open Capsule save · " + ex.Message, StatusSeverity.Error);
        }
        finally
        {
            _capsuleSaving = false;
        }
    }

    private async Task SaveCapsuleAsync()
    {
        if (_capsuleStorageUnavailable)
        {
            SetStatus("Capsule saving disabled: the existing saved file could not be read. Back it up and repair it before saving.", StatusSeverity.Warning);
            return;
        }
        if (_capsules.Count >= ContextCapsuleFormat.MaximumCapsules)
        {
            SetStatus("Capsule limit reached. No new Capsule was saved; existing Capsules are unchanged.", StatusSeverity.Warning);
            return;
        }
        if (_dropShelf.Paths.Count == 0)
        {
            SetStatus("Add files or folders to the Drop Shelf before saving a Capsule.", StatusSeverity.Warning);
            return;
        }

        var nameBox = new TextBox
        {
            Header = "Capsule name",
            Text = $"Task {_capsules.Count + 1}",
            MaxLength = ContextCapsule.MaximumNameLength
        };
        var capsuleContent = new StackPanel { Spacing = 8 };
        capsuleContent.Children.Add(new TextBlock
        {
            Text = "A Capsule saves the current Drop Shelf paths as references only — file contents are not copied. You can review saved Capsules at any time with View Capsules.",
            TextWrapping = TextWrapping.Wrap
        });
        capsuleContent.Children.Add(nameBox);
        var dialog = new ContentDialog
        {
            Title = "Save a Context Capsule",
            Content = capsuleContent,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var capsule = ContextCapsule.Capture(nameBox.Text, _dropShelf.Paths);
            var updated = _capsules.Concat(new[] { capsule }).ToArray();
            await _capsuleStore.SaveAsync(updated);
            _capsules.Add(capsule);
            var available = capsule.Inspect().Count(item => item.State == ContextCapsule.Presence.Present);
            SetStatus($"Capsule '{capsule.Name}' saved locally · {available}/{capsule.Paths.Count} paths currently available. Paths are references only.", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            SetStatus("Could not save Capsule · " + ex.Message, StatusSeverity.Error);
        }
    }

    private async void ViewCapsules_Click(object sender, RoutedEventArgs e)
    {
        var summary = _capsules.Count == 0
            ? "No Capsules saved. Add items to the Drop Shelf and choose Save Capsule."
            : string.Join("\n\n", _capsules.Select(capsule =>
            {
                var states = capsule.Inspect();
                var available = states.Count(item => item.State == ContextCapsule.Presence.Present);
                var preview = string.Join("\n", states.Take(12).Select(item =>
                    (item.State == ContextCapsule.Presence.Present ? "Available: " : "Unavailable: ") + item.Path));
                var omitted = states.Count > 12 ? $"\n… and {states.Count - 12} more references" : "";
                return $"{capsule.Name} · {available}/{states.Count} available\n{preview}{omitted}";
            }));
        var dialog = new ContentDialog
        {
            Title = "Context Capsules · read-only",
            Content = new ScrollViewer
            {
                MaxHeight = 440,
                MaxWidth = 720,
                Content = new TextBlock
                {
                    Text = summary,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void CancelShelf_Click(object sender, RoutedEventArgs e)
    {
        _batchCancellation?.Cancel();
        SetStatus("Cancelling batch copy…");
    }

    private void PruneShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCancellation is not null || _batchPreparing)
        {
            SetStatus("Wait until the batch copy is finished before pruning the shelf.", StatusSeverity.Warning);
            return;
        }
        var removed = _dropShelf.RemoveMissing();
        _shelfPaths.Clear();
        foreach (var path in _dropShelf.Paths) _shelfPaths.Add(path);
        SetStatus(removed == 0
            ? "Drop Shelf · No missing references found."
            : $"Drop Shelf · Removed {removed:N0} missing references; no files were deleted.",
            removed == 0 ? StatusSeverity.Info : StatusSeverity.Success);
    }

    private void ClearShelf_Click(object sender, RoutedEventArgs e)
    {
        _dropShelf.Clear();
        _shelfPaths.Clear();
        SetStatus("Drop Shelf cleared · No files were changed.", StatusSeverity.Success);
    }

    private async void ShowShelfLocation_Click(object sender, RoutedEventArgs e)
    {
        if (ShelfItemsBox.SelectedItem is not string selected)
        {
            SetStatus("Select a Drop Shelf item first.", StatusSeverity.Warning);
            return;
        }
        if (!File.Exists(selected) && !Directory.Exists(selected))
        {
            SetStatus("Shelf item is missing · Nothing was changed.", StatusSeverity.Warning);
            return;
        }
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(selected));
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            SetStatus("The selected item's parent folder is unavailable.", StatusSeverity.Warning);
            return;
        }
        await NavigateAsync(_activePane, parent);
        var match = _activePane.Items.FirstOrDefault(item =>
            string.Equals(item.FullPath, selected, StringComparison.OrdinalIgnoreCase));
        if (match is not null) _activePane.List.SelectedItem = match;
        SetStatus($"Showing Drop Shelf item · {Path.GetFileName(selected)}");
    }

    private void CancelCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_copyCancellation is null) return;
        _copyCancellation.Cancel();
        SetStatus("Cancelling copy…");
    }

    private async void RefreshShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!string.IsNullOrWhiteSpace(_left.CurrentPath)) await NavigateAsync(_left, _left.CurrentPath, addHistory: false);
        if (!string.IsNullOrWhiteSpace(_right.CurrentPath)) await NavigateAsync(_right, _right.CurrentPath, addHistory: false);
    }

    private void SwitchPaneShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SetActivePane(ReferenceEquals(_activePane, _left) ? _right : _left);
        _activePane.List.Focus(FocusState.Programmatic);
        SetStatus($"Active pane · {_activePane.Name}");
    }

    private void FocusActivePathShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _activePane.PathBox.Focus(FocusState.Programmatic);
        _activePane.PathBox.SelectAll();
    }

    private void FocusActiveFilterShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _activePane.FilterBox.Focus(FocusState.Programmatic);
        _activePane.FilterBox.SelectAll();
    }

    private async void BackShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await NavigateBackAsync(_activePane);
    }

    private async void ForwardShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await NavigateForwardAsync(_activePane);
    }

    private async void UpShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await NavigateUpAsync(_activePane);
    }

    private static void SetPaneStatus(PaneState pane, string message) => pane.Status.Text = message;

    // All global status messages go through SetStatus so severity styling and error
    // pinning behave identically everywhere. An error stays visible until the user
    // dismisses it or a new operation starts; routine messages never overwrite it.
    private void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        if (_pinnedError is not null && severity != StatusSeverity.Error) return;
        ApplyStatus(message, severity);
        _pinnedError = severity == StatusSeverity.Error ? message : null;
        DismissStatusButton.Visibility = _pinnedError is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyStatus(string message, StatusSeverity severity)
    {
        var (glyph, brushKey) = severity switch
        {
            StatusSeverity.Success => ("\uE73E", "MeridianSuccessBrush"),
            StatusSeverity.Warning => ("\uE7BA", "MeridianWarningBrush"),
            StatusSeverity.Error => ("\uE783", "MeridianErrorBrush"),
            _ => ("\uE946", "MeridianTextSecondaryBrush"),
        };
        StatusIcon.Glyph = glyph;
        StatusIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[brushKey];
        GlobalStatus.Text = message;
        GlobalStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            severity is StatusSeverity.Warning or StatusSeverity.Error ? brushKey : "MeridianTextSecondaryBrush"];
    }

    private void ClearPinnedError()
    {
        _pinnedError = null;
        DismissStatusButton.Visibility = Visibility.Collapsed;
    }

    private void DismissStatus_Click(object sender, RoutedEventArgs e)
    {
        ClearPinnedError();
        ApplyStatus("Ready · Local-only · No telemetry", StatusSeverity.Info);
    }

    private void ShowOperationProgress(string label, double? percent)
    {
        OperationProgressPanel.Visibility = Visibility.Visible;
        OperationProgressLabel.Text = label;
        if (percent is { } value)
        {
            OperationProgressBar.IsIndeterminate = false;
            OperationProgressBar.Value = Math.Clamp(value, 0, 100);
        }
        else
        {
            OperationProgressBar.IsIndeterminate = true;
        }
    }

    private void HideOperationProgress()
    {
        OperationProgressPanel.Visibility = Visibility.Collapsed;
        OperationProgressBar.IsIndeterminate = false;
    }

    private static List<FileEntry> GetSelectedEntries(PaneState pane) =>
        pane.List.SelectedItems.OfType<FileEntry>().ToList();

    private void LeftItems_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionStatus(_left);
    private void RightItems_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionStatus(_right);

    // Mirrors the counts ApplyFilter writes, without rebuilding the item list.
    private void UpdateSelectionStatus(PaneState pane)
    {
        var selected = GetSelectedEntries(pane);
        if (selected.Count > 0)
        {
            // FileEntry.SizeBytes is the file length for files and 0 for folders,
            // so folders count as selected items without adding to the size total.
            long totalBytes = 0;
            var hasFile = false;
            foreach (var entry in selected)
            {
                if (entry.IsDirectory) continue;
                hasFile = true;
                totalBytes += entry.SizeBytes;
            }
            SetPaneStatus(pane, hasFile
                ? $"{selected.Count:N0} selected · {DirectoryReader.FormatSize(totalBytes)}"
                : $"{selected.Count:N0} selected");
            return;
        }
        var query = pane.FilterBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            SetPaneStatus(pane, $"{pane.AllItems.Count:N0} items");
            return;
        }
        var matches = DirectoryReader.Filter(pane.AllItems, query, pane.SortField, pane.Descending, includeFullPath: false);
        SetPaneStatus(pane, $"{matches.Count:N0} of {pane.AllItems.Count:N0} items · filtered");
    }

    private async void LeftItems_KeyDown(object sender, KeyRoutedEventArgs e) => await HandleListKeyDownAsync(_left, e);
    private async void RightItems_KeyDown(object sender, KeyRoutedEventArgs e) => await HandleListKeyDownAsync(_right, e);

    private async Task HandleListKeyDownAsync(PaneState pane, KeyRoutedEventArgs e)
    {
        var ctrlDown = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (ctrlDown && e.Key == VirtualKey.A)
        {
            e.Handled = true;
            pane.List.SelectAll();
            return;
        }
        switch (e.Key)
        {
            case VirtualKey.Delete:
                e.Handled = true;
                await DeleteSelectedAsync(pane);
                break;
            case VirtualKey.F2:
                e.Handled = true;
                if (pane.List.SelectedItems.Count == 1) await RenameSelectedAsync(pane);
                else if (pane.List.SelectedItems.Count > 1) SetStatus("Select a single item to rename.", StatusSeverity.Warning);
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                await OpenSelectedAsync(pane);
                break;
        }
    }

    private void LeftContextMenu_Opening(object sender, object e) => UpdateContextMenu(_left, isLeftPane: true);
    private void RightContextMenu_Opening(object sender, object e) => UpdateContextMenu(_right, isLeftPane: false);

    private void UpdateContextMenu(PaneState pane, bool isLeftPane)
    {
        var copyItem = isLeftPane ? LeftCopyMenuItem : RightCopyMenuItem;
        var moveItem = isLeftPane ? LeftMoveMenuItem : RightMoveMenuItem;
        var renameItem = isLeftPane ? LeftRenameMenuItem : RightRenameMenuItem;
        var propertiesItem = isLeftPane ? LeftPropertiesMenuItem : RightPropertiesMenuItem;
        var shelfItem = isLeftPane ? LeftShelfMenuItem : RightShelfMenuItem;
        var deleteItem = isLeftPane ? LeftDeleteMenuItem : RightDeleteMenuItem;
        var other = isLeftPane ? _right : _left;
        var count = pane.List.SelectedItems.Count;
        var hasDestination = !string.IsNullOrWhiteSpace(other.CurrentPath);
        var direction = isLeftPane ? "right" : "left";
        copyItem.IsEnabled = count > 0 && hasDestination && _copyCancellation is null;
        moveItem.IsEnabled = count > 0 && hasDestination;
        renameItem.IsEnabled = count == 1;
        propertiesItem.IsEnabled = count == 1;
        shelfItem.IsEnabled = count > 0;
        deleteItem.IsEnabled = count > 0;
        copyItem.Text = count > 1 ? $"Copy {count:N0} items to {direction} pane" : $"Copy to {direction} pane";
        moveItem.Text = count > 1 ? $"Move {count:N0} items to {direction} pane" : $"Move to {direction} pane";
        shelfItem.Text = count > 1 ? $"Add {count:N0} items to Drop Shelf" : "Add to Drop Shelf";
        deleteItem.Text = count > 1 ? $"Delete {count:N0} items (Recycle Bin)" : "Delete (Recycle Bin)";
    }

    private async void RenameFromLeft_Click(object sender, RoutedEventArgs e) => await RenameSelectedAsync(_left);
    private async void RenameFromRight_Click(object sender, RoutedEventArgs e) => await RenameSelectedAsync(_right);
    private async void PropertiesFromLeft_Click(object sender, RoutedEventArgs e) => await PropertiesSelectedAsync(_left);
    private async void PropertiesFromRight_Click(object sender, RoutedEventArgs e) => await PropertiesSelectedAsync(_right);
    private async void DeleteFromLeft_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync(_left);
    private async void DeleteFromRight_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync(_right);
    private void AddToShelfFromLeft_Click(object sender, RoutedEventArgs e) => AddSelectedToShelf(_left);
    private void AddToShelfFromRight_Click(object sender, RoutedEventArgs e) => AddSelectedToShelf(_right);
    private void LeftPane_PointerPressed(object sender, PointerRoutedEventArgs e) => SetActivePane(_left);
    private void RightPane_PointerPressed(object sender, PointerRoutedEventArgs e) => SetActivePane(_right);
    private void LeftItems_PointerPressed(object sender, PointerRoutedEventArgs e) => SetActivePane(_left);
    private void RightItems_PointerPressed(object sender, PointerRoutedEventArgs e) => SetActivePane(_right);

    private void SetActivePane(PaneState pane)
    {
        _activePane = pane;
        UpdateActivePaneVisuals();
    }

    private void UpdateActivePaneVisuals()
    {
        SetPaneActiveVisual(LeftPaneRoot, LeftPaneHeader, ReferenceEquals(_activePane, _left));
        SetPaneActiveVisual(RightPaneRoot, RightPaneHeader, ReferenceEquals(_activePane, _right));
    }

    private static void SetPaneActiveVisual(Grid paneRoot, Grid paneHeader, bool isActive)
    {
        paneRoot.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[isActive ? "MeridianActivePaneBorderBrush" : "MeridianBorderBrush"];
        paneHeader.Background = isActive
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MeridianActivePaneBackgroundBrush"]
            : null;
    }

    private void UpdatePaneHeader(PaneState pane)
    {
        var nameBlock = ReferenceEquals(pane, _left) ? LeftFolderName : RightFolderName;
        var pathBlock = ReferenceEquals(pane, _left) ? LeftFolderPath : RightFolderPath;
        var path = pane.CurrentPath;
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name)) name = string.IsNullOrWhiteSpace(path) ? pane.Name + " pane" : path;
        nameBlock.Text = name;
        pathBlock.Text = path;
        if (!string.IsNullOrWhiteSpace(path)) ToolTipService.SetToolTip(pathBlock, path);
    }

    private async void CopyActivePane_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        if (ReferenceEquals(_activePane, _left)) await CopySelectedAsync(_left, _right);
        else await CopySelectedAsync(_right, _left);
    }

    private async void MoveActivePane_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        if (ReferenceEquals(_activePane, _left)) await MoveSelectedAsync(_left, _right);
        else await MoveSelectedAsync(_right, _left);
    }

    private void OrganizeSection_Click(object sender, RoutedEventArgs e)
    {
        var target = ReferenceEquals(sender, BookmarksSectionToggle) ? OrganizeSection.Bookmarks : OrganizeSection.Shelf;
        SetOrganizeSection(_openOrganizeSection == target ? OrganizeSection.None : target);
    }

    private void SetOrganizeSection(OrganizeSection section)
    {
        _openOrganizeSection = section;
        BookmarksSection.Visibility = section == OrganizeSection.Bookmarks ? Visibility.Visible : Visibility.Collapsed;
        ShelfSection.Visibility = section == OrganizeSection.Shelf ? Visibility.Visible : Visibility.Collapsed;
        BookmarksSectionToggle.IsChecked = section == OrganizeSection.Bookmarks;
        ShelfSectionToggle.IsChecked = section == OrganizeSection.Shelf;
    }
    private async void LeftUp_Click(object sender, RoutedEventArgs e) => await NavigateUpAsync(_left);
    private async void RightUp_Click(object sender, RoutedEventArgs e) => await NavigateUpAsync(_right);
    private async void LeftBack_Click(object sender, RoutedEventArgs e) => await NavigateBackAsync(_left);
    private async void RightBack_Click(object sender, RoutedEventArgs e) => await NavigateBackAsync(_right);
    private async void LeftForward_Click(object sender, RoutedEventArgs e) => await NavigateForwardAsync(_left);
    private async void RightForward_Click(object sender, RoutedEventArgs e) => await NavigateForwardAsync(_right);
    private async void CopyLeftToRight_Click(object sender, RoutedEventArgs e) => await CopySelectedAsync(_left, _right);
    private async void MoveLeftToRight_Click(object sender, RoutedEventArgs e) => await MoveSelectedAsync(_left, _right);
    private async void MoveRightToLeft_Click(object sender, RoutedEventArgs e) => await MoveSelectedAsync(_right, _left);
    private async void CopyRightToLeft_Click(object sender, RoutedEventArgs e) => await CopySelectedAsync(_right, _left);
    private async void RefreshBoth_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_left.CurrentPath)) await NavigateAsync(_left, _left.CurrentPath, addHistory: false);
        if (!string.IsNullOrWhiteSpace(_right.CurrentPath)) await NavigateAsync(_right, _right.CurrentPath, addHistory: false);
    }
    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{_activePane.CurrentPath}\"", UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { SetStatus("Could not open Explorer · " + ex.Message, StatusSeverity.Error); }
    }
    private async void LeftItems_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await OpenSelectedAsync(_left);
    private async void RightItems_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await OpenSelectedAsync(_right);
    private async void LeftPath_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await NavigateAsync(_left, LeftPath.Text); } }
    private async void RightPath_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await NavigateAsync(_right, RightPath.Text); } }

    private enum OrganizeSection { None, Bookmarks, Shelf }

    private enum StatusSeverity { Info, Success, Warning, Error }

    private sealed class WorkspaceTab(string name, string leftPath, string rightPath)
    {
        public string Name { get; } = name;
        public string LeftPath { get; set; } = leftPath;
        public string RightPath { get; set; } = rightPath;
    }

    private sealed class PaneState(string name, TextBox pathBox, TextBox filterBox, ListView list, TextBlock status)
    {
        public string Name { get; } = name;
        public TextBox PathBox { get; } = pathBox;
        public TextBox FilterBox { get; } = filterBox;
        public ListView List { get; } = list;
        public TextBlock Status { get; } = status;
        public ObservableCollection<FileEntry> Items { get; } = [];
        public IReadOnlyList<FileEntry> AllItems { get; set; } = Array.Empty<FileEntry>();
        public DirectorySortField SortField { get; set; } = DirectorySortField.Name;
        public bool Descending { get; set; }
        public Stack<string> BackHistory { get; } = new();
        public Stack<string> ForwardHistory { get; } = new();
        public string CurrentPath { get; set; } = string.Empty;
        public CancellationTokenSource? ReadCancellation { get; set; }
    }
}
