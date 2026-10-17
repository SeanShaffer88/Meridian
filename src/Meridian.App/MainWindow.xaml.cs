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
    private readonly ObservableCollection<string> _shelfPaths = [];
    private readonly ObservableCollection<BookmarkSnapshot> _bookmarks = [];
    private readonly List<WorkspaceTab> _workspaceTabs = [];
    private int _activeWorkspaceTabIndex;
    private int _nextWorkspaceTabNumber = 1;
    private bool _switchingWorkspaceTab;
    private PaneState _activePane;
    private bool _restoringWorkspace;
    private CancellationTokenSource? _copyCancellation;
    private CancellationTokenSource? _batchCancellation;
    private bool _batchPreparing;

    public MainWindow()
    {
        InitializeComponent();
        BookmarksBox.ItemsSource = _bookmarks;
        ShelfItemsBox.ItemsSource = _shelfPaths;
        _left = new PaneState("Left", LeftPath, LeftFilter, LeftItems, LeftStatus);
        _right = new PaneState("Right", RightPath, RightFilter, RightItems, RightStatus);
        _activePane = _left;
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
            GlobalStatus.Text = "Workspace could not be saved · " + ex.Message;
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
                    Content = "×",
                    Tag = tab,
                    MinWidth = 28,
                    Padding = new Thickness(4, 2, 4, 2)
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
        GlobalStatus.Text = $"Opened {tab.Name}";
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
        GlobalStatus.Text = $"Switched to {tab.Name}";
    }

    private async Task AddBookmarkAsync()
    {
        var path = _activePane.CurrentPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        if (_bookmarks.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            GlobalStatus.Text = "That location is already bookmarked.";
            return;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(name)) name = path;
        _bookmarks.Add(new BookmarkSnapshot(name, path));
        await PersistWorkspaceAsync();
        GlobalStatus.Text = $"Bookmarked · {name}";
    }

    private async void AddBookmark_Click(object sender, RoutedEventArgs e) => await AddBookmarkAsync();

    private async void RemoveBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (BookmarksBox.SelectedItem is not BookmarkSnapshot selected)
        {
            GlobalStatus.Text = "Select a saved location to remove.";
            return;
        }

        _bookmarks.Remove(selected);
        BookmarksBox.SelectedItem = null;
        await PersistWorkspaceAsync();
        GlobalStatus.Text = $"Removed bookmark · {selected.Name}";
    }

    private async void BookmarksBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BookmarksBox.SelectedItem is not BookmarkSnapshot selected) return;
        await NavigateAsync(_activePane, selected.Path);
        BookmarksBox.SelectedItem = null;
    }

    private async Task NavigateAsync(PaneState pane, string requestedPath, bool addHistory = true)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedPath.Trim().Trim('"')));
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Folder not found: {fullPath}");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SetPaneStatus(pane, ex.Message); GlobalStatus.Text = "Navigation failed · " + ex.Message; pane.PathBox.Text = pane.CurrentPath; return;
        }
        if (addHistory && !string.IsNullOrWhiteSpace(pane.CurrentPath) && !string.Equals(pane.CurrentPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            pane.BackHistory.Push(pane.CurrentPath); pane.ForwardHistory.Clear();
        }
        pane.ReadCancellation?.Cancel(); pane.ReadCancellation?.Dispose(); pane.ReadCancellation = new CancellationTokenSource();
        var token = pane.ReadCancellation.Token;
        pane.CurrentPath = fullPath; pane.PathBox.Text = fullPath; SetPaneStatus(pane, "Loading folder…");
        try
        {
            var entries = await Task.Run(() => DirectoryReader.Read(fullPath, token), token);
            if (token.IsCancellationRequested) return;
            pane.AllItems = entries;
            ApplyFilter(pane);
            GlobalStatus.Text = "Ready · Local-only · No telemetry";
            if (!_restoringWorkspace) await PersistWorkspaceAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { SetPaneStatus(pane, ex.Message); GlobalStatus.Text = "Could not read folder · " + ex.Message; }
    }

    private void ApplyFilter(PaneState pane)
    {
        var query = pane.FilterBox.Text.Trim();
        var matches = DirectoryReader.Filter(pane.AllItems, query, pane.SortField, pane.Descending);

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
        var target = pane.BackHistory.Pop();
        if (!string.IsNullOrWhiteSpace(pane.CurrentPath)) pane.ForwardHistory.Push(pane.CurrentPath);
        await NavigateAsync(pane, target, addHistory: false);
    }
    private async Task NavigateForwardAsync(PaneState pane)
    {
        if (pane.ForwardHistory.Count == 0) return;
        var target = pane.ForwardHistory.Pop();
        if (!string.IsNullOrWhiteSpace(pane.CurrentPath)) pane.BackHistory.Push(pane.CurrentPath);
        await NavigateAsync(pane, target, addHistory: false);
    }

    private bool _zipInspectionOpen;

    private async void InspectZip_Click(object sender, RoutedEventArgs e)
    {
        if (_zipInspectionOpen || _zipCreationBusy || _zipExtractionBusy || _previewDialogOpen) return;
        if (_activePane.List.SelectedItem is not FileEntry selected || selected.IsDirectory ||
            !string.Equals(Path.GetExtension(selected.FullPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            GlobalStatus.Text = "Select a ZIP file to inspect.";
            return;
        }
        _zipInspectionOpen = true;
        try
        {
            var entries = await Task.Run(() => Meridian.Core.Archives.ZipArchiveCatalog.List(selected.FullPath));
            var totalBytes = entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.UncompressedBytes);
            var fileCount = entries.Count(entry => !entry.IsDirectory);
            var folderCount = entries.Count - fileCount;
            var listing = string.Join(Environment.NewLine,
                entries.Select(entry => (entry.IsDirectory ? "[DIR] " : "[FILE] ") +
                    entry.Path + " (" + entry.UncompressedBytes.ToString("N0") + " bytes)"));
            await new ContentDialog
            {
                Title = "ZIP contents · " + selected.Name + " · " + entries.Count.ToString("N0") +
                    " entries (" + fileCount.ToString("N0") + " files, " +
                    folderCount.ToString("N0") + " folders) · " + totalBytes.ToString("N0") + " bytes",
                Content = new TextBox { Text = listing, IsReadOnly = true,
                    AcceptsReturn = true, MinWidth = 540, Height = 380 },
                CloseButtonText = "Close",
                XamlRoot = LeftPath.XamlRoot
            }.ShowAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            GlobalStatus.Text = "ZIP inspection failed · " + ex.Message;
        }
        finally { _zipInspectionOpen = false; }
    }

    private bool _zipExtractionBusy;
    private CancellationTokenSource? _zipExtractionCancellation;

    private async void ExtractZip_Click(object sender, RoutedEventArgs e)
    {
        if (_zipExtractionBusy || _zipCreationBusy || _zipInspectionOpen || _previewDialogOpen) return;
        if (_activePane.List.SelectedItem is not FileEntry selected || selected.IsDirectory) return;
        _zipExtractionBusy = true;
        try
        {
        var archivePath = selected.FullPath;
        var parent = _activePane.CurrentPath;
        var folder = Path.GetFileNameWithoutExtension(archivePath);
        var plan = await Task.Run(() => Meridian.Core.Archives.ZipArchiveCatalog.List(archivePath));
        var dialog = new ContentDialog
        {
            Title = "Extract ZIP?",
            Content = new TextBlock { Text = plan.Count + " entries to " + Path.Combine(parent, folder) },
            PrimaryButtonText = "Extract",
            CloseButtonText = "Cancel",
            XamlRoot = LeftPath.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _zipExtractionCancellation = new CancellationTokenSource();
        GlobalStatus.Text = "Extracting ZIP · " + selected.Name;
        var token = _zipExtractionCancellation.Token;
        var receipt = await Task.Run(() => Meridian.Core.Archives.ZipTreeExtractor.ExtractAsync(
            archivePath, parent, folder, cancellationToken: token), token);
        GlobalStatus.Text = "Extracted " + receipt.FileCount + " files.";
        }
        catch (OperationCanceledException)
        {
            GlobalStatus.Text = "ZIP extraction cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            GlobalStatus.Text = "ZIP extraction failed · " + ex.Message;
        }
        finally
        {
            _zipExtractionCancellation?.Dispose();
            _zipExtractionCancellation = null;
            _zipExtractionBusy = false;
        }
    }

    private void CancelZipExtraction_Click(object sender, RoutedEventArgs e)
    {
        _zipExtractionCancellation?.Cancel();
        if (_zipExtractionCancellation is not null)
            GlobalStatus.Text = "Cancelling ZIP extraction…";
    }

    private bool _zipCreationBusy;

    private async void CreateZip_Click(object sender, RoutedEventArgs e)
    {
        if (_zipCreationBusy || _zipExtractionBusy || _zipInspectionOpen || _previewDialogOpen) return;
        if (_activePane.List.SelectedItem is not FileEntry selected || selected.IsDirectory)
        {
            GlobalStatus.Text = "Select a regular file to archive.";
            return;
        }
        var destination = Path.Combine(_activePane.CurrentPath,
            Path.GetFileNameWithoutExtension(selected.Name) + ".zip");
        var confirmation = new ContentDialog
        {
            Title = "Create ZIP?",
            Content = new TextBlock { Text = "Source: " + selected.FullPath +
                Environment.NewLine + "Destination: " + destination +
                Environment.NewLine + "Existing archives will not be overwritten.",
                TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = LeftPath.XamlRoot
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        _zipCreationBusy = true;
        try
        {
            GlobalStatus.Text = "Creating ZIP · " + selected.Name;
            await Task.Run(() => Meridian.Core.Archives.ZipArchiveCreator.CreateAsync(
                destination, new Dictionary<string, string> { [selected.Name] = selected.FullPath }));
            GlobalStatus.Text = "ZIP created · " + destination;
            await NavigateAsync(_activePane, _activePane.CurrentPath, addHistory: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            GlobalStatus.Text = "ZIP creation failed · " + ex.Message;
        }
        finally { _zipCreationBusy = false; }
    }

    private bool _previewDialogOpen;

    private async void PreviewSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_previewDialogOpen) return;
        if (_activePane.List.SelectedItem is not FileEntry selected || selected.IsDirectory)
        {
            GlobalStatus.Text = "Select a file to preview.";
            return;
        }

        var path = selected.FullPath;
        var decision = Meridian.Core.Viewer.ViewerRouter.Classify(path);
        var panel = new StackPanel { Spacing = 8, MinWidth = 540 };
        panel.Children.Add(new TextBlock
        {
            Text = decision.Reason,
            TextWrapping = TextWrapping.Wrap
        });
        Microsoft.UI.Xaml.Controls.MediaPlayerElement? player = null;
        Windows.Media.Playback.MediaPlayer? mediaSession = null;
        _previewDialogOpen = true;
        try
        {
            path = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(path);
            if (decision.Kind is Meridian.Core.Viewer.ViewerKind.Video or Meridian.Core.Viewer.ViewerKind.Audio)
            {
                player = new Microsoft.UI.Xaml.Controls.MediaPlayerElement
                {
                    AreTransportControlsEnabled = true,
                    AutoPlay = false,
                    Height = decision.Kind == Meridian.Core.Viewer.ViewerKind.Video ? 320 : 100
                };
                mediaSession = new Windows.Media.Playback.MediaPlayer();
                player.SetMediaPlayer(mediaSession);
                var mediaFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                mediaSession.Source = Windows.Media.Core.MediaSource.CreateFromStorageFile(mediaFile);
                var playbackStatus = new TextBlock
                {
                    Text = "Ready · press Play to start",
                    TextWrapping = TextWrapping.Wrap
                };
                mediaSession.MediaFailed += (_, args) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                        playbackStatus.Text = "Playback failed · the format or codec may be unsupported. " +
                            args.ErrorMessage);
                };
                panel.Children.Add(player);
                panel.Children.Add(playbackStatus);
            }
            else if (decision.Kind == Meridian.Core.Viewer.ViewerKind.Image)
            {
                const long maximumImageFileBytes = 64L * 1024 * 1024;
                if (new FileInfo(path).Length > maximumImageFileBytes)
                    throw new IOException("Image exceeds the 64 MiB preview file limit. Open it in a dedicated image editor.");
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenReadAsync();
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(stream);
                panel.Children.Add(new Microsoft.UI.Xaml.Controls.Image
                {
                    Source = bitmap,
                    MaxHeight = 440,
                    MaxWidth = 780,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                });
            }
            else if (decision.Kind is Meridian.Core.Viewer.ViewerKind.Text or Meridian.Core.Viewer.ViewerKind.Hex)
            {
                var bytes = await Meridian.Core.Viewer.SafeFilePreview.ReadPrefixAsync(path,
                    maxBytes: decision.Kind == Meridian.Core.Viewer.ViewerKind.Hex ? 4096 : 65536);
                string display;
                if (decision.Kind == Meridian.Core.Viewer.ViewerKind.Hex)
                {
                    display = Meridian.Core.Viewer.SafeFilePreview.HexDump(bytes);
                }
                else
                {
                    if (Meridian.Core.Viewer.PreviewTextDecoder.TryDecode(bytes, out var decoded))
                        display = decoded;
                    else
                        display = "Text encoding is unsupported or binary content detected. Showing the first 4 KiB as hexadecimal.\n\n" +
                            Meridian.Core.Viewer.SafeFilePreview.HexDump(bytes.AsSpan(0, Math.Min(bytes.Length, 4096)));
                }
                panel.Children.Add(new ScrollViewer
                {
                    MaxHeight = 420,
                    Content = new TextBox
                    {
                        Text = display,
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.NoWrap,
                        MinHeight = 220
                    }
                });
            }
            else
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "This format does not yet have an integrated renderer. No external application will be launched.",
                    TextWrapping = TextWrapping.Wrap
                });
            }

            await new ContentDialog
            {
                Title = "Preview · " + selected.Name,
                Content = panel,
                CloseButtonText = "Close",
                XamlRoot = LeftPath.XamlRoot
            }.ShowAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.COMException or System.Security.SecurityException)
        {
            GlobalStatus.Text = "Preview unavailable · " + ex.Message;
        }
        finally
        {
            _previewDialogOpen = false;
            if (player is not null)
            {
                mediaSession?.Pause();
                player.SetMediaPlayer(null);
                mediaSession?.Dispose();
            }
        }
    }

    private async Task OpenSelectedAsync(PaneState pane)
    {
        if (pane.List.SelectedItem is not FileEntry selected) return;
        if (selected.IsDirectory) { await NavigateAsync(pane, selected.FullPath); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = selected.FullPath, UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { GlobalStatus.Text = "Could not open file · " + ex.Message; }
    }
    private async Task MoveSelectedAsync(PaneState source, PaneState destination)
    {
        if (source.List.SelectedItem is not FileEntry selected)
        {
            GlobalStatus.Text = $"Select an item in the {source.Name.ToLowerInvariant()} pane first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(destination.CurrentPath)) return;

        var kind = selected.IsDirectory ? "folder" : "file";
        var dialog = new ContentDialog
        {
            Title = "Move item?",
            Content = $"Move this {kind} to the other pane's current folder?\n\nFrom: {selected.FullPath}\nTo: {destination.CurrentPath}\n\nExisting items will not be overwritten.",
            PrimaryButtonText = "Move",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var sourcePath = selected.FullPath;
        var sourceFolder = source.CurrentPath;
        var destinationPath = destination.CurrentPath;
        try
        {
            var movedTo = await Task.Run(() => FileOperations.MoveItem(sourcePath, destinationPath));
            if (string.Equals(source.CurrentPath, sourceFolder, StringComparison.OrdinalIgnoreCase))
                await NavigateAsync(source, sourceFolder, addHistory: false);
            if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase))
                await NavigateAsync(destination, destinationPath, addHistory: false);
            GlobalStatus.Text = $"Moved successfully · {Path.GetFileName(movedTo)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            GlobalStatus.Text = "Move failed · " + ex.Message;
        }
    }

    private async Task CopySelectedAsync(PaneState source, PaneState destination)
    {
        if (_copyCancellation is not null) { GlobalStatus.Text = "A copy is already in progress."; return; }
        if (source.List.SelectedItem is not FileEntry selected) { GlobalStatus.Text = $"Select an item in the {source.Name.ToLowerInvariant()} pane first."; return; }
        if (string.IsNullOrWhiteSpace(destination.CurrentPath)) return;
        if (string.Equals(Path.GetFullPath(selected.FullPath), Path.GetFullPath(destination.CurrentPath), StringComparison.OrdinalIgnoreCase))
        { GlobalStatus.Text = "The destination is the selected folder. Choose another destination."; return; }

        var destinationPath = destination.CurrentPath;
        var cancellation = new CancellationTokenSource();
        _copyCancellation = cancellation;
        CancelCopyButton.IsEnabled = true;
        GlobalStatus.Text = $"Copying {selected.Name}…"; SetPaneStatus(source, "Copy in progress…");
        try
        {
            var copiedTo = await Task.Run(() => FileOperations.CopyItem(selected.FullPath, destinationPath, cancellation.Token), cancellation.Token);
            if (string.Equals(destination.CurrentPath, destinationPath, StringComparison.OrdinalIgnoreCase)) await NavigateAsync(destination, destinationPath, addHistory: false);
            SetPaneStatus(source, "Ready"); GlobalStatus.Text = $"Copied successfully · {Path.GetFileName(copiedTo)}";
        }
        catch (OperationCanceledException)
        {
            SetPaneStatus(source, "Copy cancelled");
            GlobalStatus.Text = "Copy cancelled · Partial staging data was cleaned up where possible.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { SetPaneStatus(source, "Copy failed"); GlobalStatus.Text = "Copy failed · " + ex.Message; }
        finally
        {
            if (ReferenceEquals(_copyCancellation, cancellation)) _copyCancellation = null;
            CancelCopyButton.IsEnabled = false;
            cancellation.Dispose();
        }
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath))
        {
            GlobalStatus.Text = "Choose a folder before creating a new folder.";
            return;
        }

        var nameBox = new TextBox { PlaceholderText = "Folder name", MinWidth = 280 };
        var dialog = new ContentDialog
        {
            Title = "Create new folder",
            Content = nameBox,
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
            GlobalStatus.Text = "Folder name is invalid. Enter a single folder name without path separators.";
            return;
        }

        var target = Path.Combine(_activePane.CurrentPath, name);
        if (File.Exists(target) || Directory.Exists(target))
        {
            GlobalStatus.Text = $"Cannot create folder · '{name}' already exists.";
            return;
        }

        try
        {
            Directory.CreateDirectory(target);
            var pane = _activePane;
            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            GlobalStatus.Text = $"Created folder · {name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            GlobalStatus.Text = "Could not create folder · " + ex.Message;
        }
    }

    private async void RenameSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_activePane.List.SelectedItem is not FileEntry selected)
        {
            GlobalStatus.Text = "Select a file or folder to rename.";
            return;
        }

        var nameBox = new TextBox { Text = selected.Name, MinWidth = 280, SelectionStart = 0, SelectionLength = selected.Name.Length };
        var dialog = new ContentDialog
        {
            Title = "Rename item",
            Content = nameBox,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var renamedTo = FileOperations.RenameItem(selected.FullPath, nameBox.Text);
            var pane = _activePane;
            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            GlobalStatus.Text = string.Equals(renamedTo, selected.FullPath, StringComparison.Ordinal)
                ? "Name unchanged."
                : $"Renamed · {Path.GetFileName(renamedTo)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            GlobalStatus.Text = "Could not rename item · " + ex.Message;
        }
    }

    private async void PropertiesSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_activePane.List.SelectedItem is not FileEntry selected)
        {
            GlobalStatus.Text = "Select a file or folder to view its properties.";
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
            GlobalStatus.Text = "Could not read properties · " + ex.Message;
        }
    }

    private static void AddPropertyRow(StackPanel panel, string label, string value)
    {
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) });
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

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_activePane.List.SelectedItem is not FileEntry selected)
        {
            GlobalStatus.Text = "Select a file or folder to delete.";
            return;
        }

        var kind = selected.IsDirectory ? "folder" : "file";
        var dialog = new ContentDialog
        {
            Title = "Move to Recycle Bin?",
            Content = $"Move this {kind} to the Recycle Bin?\n\n{selected.FullPath}\n\nYou can restore it from the Recycle Bin unless Windows prevents doing so.",
            PrimaryButtonText = "Move to Recycle Bin",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            if (selected.IsDirectory)
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(selected.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(selected.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

            var pane = _activePane;
            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            GlobalStatus.Text = $"Moved to Recycle Bin · {selected.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            GlobalStatus.Text = "Could not move item to Recycle Bin · " + ex.Message;
        }
    }

    private async void FolderDna_Click(object sender, RoutedEventArgs e)
    {
        var pane = _activePane;
        if (string.IsNullOrWhiteSpace(pane.CurrentPath))
        {
            GlobalStatus.Text = "Choose a folder to profile.";
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

    private void AddToShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_activePane.List.SelectedItem is not FileEntry selected)
        {
            GlobalStatus.Text = "Select a file or folder to add to the Drop Shelf.";
            return;
        }
        if (_dropShelf.Add(selected.FullPath))
        {
            _shelfPaths.Add(Path.GetFullPath(selected.FullPath));
            GlobalStatus.Text = $"Added to Drop Shelf · {_shelfPaths.Count:N0} collected";
        }
        else GlobalStatus.Text = "This item is already on the Drop Shelf.";
    }

    private void RemoveFromShelf_Click(object sender, RoutedEventArgs e)
    {
        if (ShelfItemsBox.SelectedItem is not string selected)
        {
            GlobalStatus.Text = "Select a Drop Shelf item to remove.";
            return;
        }
        _dropShelf.Remove(selected);
        _shelfPaths.Remove(selected);
        GlobalStatus.Text = $"Removed from Drop Shelf · {_shelfPaths.Count:N0} remaining";
    }

    private async void PreviewShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_dropShelf.Paths.Count == 0)
        {
            GlobalStatus.Text = "Add items to the Drop Shelf before previewing a batch.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath))
        {
            GlobalStatus.Text = "Open the intended destination folder in the active pane.";
            return;
        }

        try
        {
            var sources = _dropShelf.Paths.ToArray();
            var destination = _activePane.CurrentPath;
            var plan = await Task.Run(() => OperationPreview.PreviewCopy(sources, destination));
            var lines = plan.Items.Take(100).Select(item =>
                $"[{item.Result}] {Path.GetFileName(item.Source)}\n  {item.Explanation}\n  → {item.Target ?? "(unavailable)"}");
            var summary = $"Destination: {plan.DestinationDirectory}\n" +
                $"Collected: {plan.Items.Count:N0} · Ready: {plan.ReadyCount:N0} · " +
                $"Blocked: {plan.Items.Count - plan.ReadyCount:N0}\n\n" +
                string.Join("\n\n", lines) +
                (plan.Items.Count > 100 ? $"\n\n…and {plan.Items.Count - 100:N0} more items not shown." : "") +
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
            GlobalStatus.Text = "Could not preview shelf · " + ex.Message;
        }
    }

    private async void CopyShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCancellation is not null || _batchPreparing) return;
        if (_dropShelf.Paths.Count == 0)
        {
            GlobalStatus.Text = "Add files to the Drop Shelf first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_activePane.CurrentPath))
        {
            GlobalStatus.Text = "Open the destination folder in the active pane.";
            return;
        }
        var sources = _dropShelf.Paths.ToArray();
        var destination = _activePane.CurrentPath;
        _batchPreparing = true;
        try
        {
            var plan = await Task.Run(() => OperationPreview.PreviewCopy(sources, destination));
            if (plan.HasBlockers)
            {
                GlobalStatus.Text = $"Copy refused · {plan.Items.Count - plan.ReadyCount:N0} blocked. Use Preview shelf to inspect.";
                return;
            }
            var confirmation = new ContentDialog
            {
                Title = "Confirm Drop Shelf batch copy",
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
            using var cancellation = new CancellationTokenSource();
            _batchCancellation = cancellation;
            CancelShelfButton.IsEnabled = true;
            try
            {
                GlobalStatus.Text = "Copying Drop Shelf items…";
                var progress = new Progress<BatchCopy.Progress>(state =>
                {
                    if (_batchCancellation == cancellation)
                        GlobalStatus.Text = $"Batch copy · {state.Completed:N0}/{state.Total:N0} · {Path.GetFileName(state.CurrentSource)}";
                });
                var result = await Task.Run(() => BatchCopy.Execute(sources, destination, cancellation.Token, progress));
                await NavigateAsync(_activePane, destination, addHistory: false);
                GlobalStatus.Text = result.Succeeded
                    ? $"Drop Shelf copied · {result.CompletedTargets.Count:N0} items"
                    : $"Batch stopped after {result.CompletedTargets.Count:N0} items · " +
                      (result.Cancelled ? "Cancelled" : result.Failure);
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
            finally
            {
                _batchCancellation = null;
                CancelShelfButton.IsEnabled = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        {
            GlobalStatus.Text = "Batch copy did not start · " + ex.Message;
        }
        finally
        {
            _batchPreparing = false;
        }
    }

    private void CancelShelf_Click(object sender, RoutedEventArgs e)
    {
        _batchCancellation?.Cancel();
        GlobalStatus.Text = "Cancelling batch copy…";
    }

    private void PruneShelf_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCancellation is not null || _batchPreparing)
        {
            GlobalStatus.Text = "Wait until the batch copy is finished before pruning the shelf.";
            return;
        }
        var removed = _dropShelf.RemoveMissing();
        _shelfPaths.Clear();
        foreach (var path in _dropShelf.Paths) _shelfPaths.Add(path);
        GlobalStatus.Text = removed == 0
            ? "Drop Shelf · No missing references found."
            : $"Drop Shelf · Removed {removed:N0} missing references; no files were deleted.";
    }

    private void ClearShelf_Click(object sender, RoutedEventArgs e)
    {
        _dropShelf.Clear();
        _shelfPaths.Clear();
        GlobalStatus.Text = "Drop Shelf cleared · No files were changed.";
    }

    private async void ShowShelfLocation_Click(object sender, RoutedEventArgs e)
    {
        if (ShelfItemsBox.SelectedItem is not string selected)
        {
            GlobalStatus.Text = "Select a Drop Shelf item first.";
            return;
        }
        if (!File.Exists(selected) && !Directory.Exists(selected))
        {
            GlobalStatus.Text = "Shelf item is missing · Nothing was changed.";
            return;
        }
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(selected));
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            GlobalStatus.Text = "The selected item's parent folder is unavailable.";
            return;
        }
        await NavigateAsync(_activePane, parent);
        var match = _activePane.Items.FirstOrDefault(item =>
            string.Equals(item.FullPath, selected, StringComparison.OrdinalIgnoreCase));
        if (match is not null) _activePane.List.SelectedItem = match;
        GlobalStatus.Text = $"Showing Drop Shelf item · {Path.GetFileName(selected)}";
    }

    private void CancelCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_copyCancellation is null) return;
        _copyCancellation.Cancel();
        GlobalStatus.Text = "Cancelling copy…";
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
        _activePane = ReferenceEquals(_activePane, _left) ? _right : _left;
        _activePane.List.Focus(FocusState.Programmatic);
        GlobalStatus.Text = $"Active pane · {_activePane.Name}";
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
    private void LeftPane_PointerPressed(object sender, PointerRoutedEventArgs e) => _activePane = _left;
    private void RightPane_PointerPressed(object sender, PointerRoutedEventArgs e) => _activePane = _right;
    private void LeftItems_PointerPressed(object sender, PointerRoutedEventArgs e) => _activePane = _left;
    private void RightItems_PointerPressed(object sender, PointerRoutedEventArgs e) => _activePane = _right;
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
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { GlobalStatus.Text = "Could not open Explorer · " + ex.Message; }
    }
    private async void LeftItems_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await OpenSelectedAsync(_left);
    private async void RightItems_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await OpenSelectedAsync(_right);
    private async void LeftPath_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await NavigateAsync(_left, LeftPath.Text); } }
    private async void RightPath_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await NavigateAsync(_right, RightPath.Text); } }


    private bool _timeVaultBusy;

    private async void TimeVault_Click(object sender, RoutedEventArgs e)
    {
        if (_timeVaultBusy) return;
        _timeVaultBusy = true;
        GlobalStatus.Text = "TimeVault · discovering existing snapshots…";
        try
        {
            Meridian.Core.TimeVault.IShadowCopyCatalog catalog = new Meridian.Core.TimeVault.WindowsShadowCopyCatalog();
            var snapshots = await catalog.ListAsync();
            var selectedPath = _activePane.CurrentPath;
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = "Existing snapshots only · read-only · no recovery operations",
                TextWrapping = TextWrapping.Wrap
            });
            var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a snapshot" };
            foreach (var snapshot in snapshots.OrderByDescending(item => item.CreatedAt))
            {
                selector.Items.Add(new ComboBoxItem
                {
                    Content = $"{(snapshot.CreatedAt == DateTimeOffset.MinValue ? "Unknown date" : snapshot.CreatedAt.ToLocalTime().ToString("g"))} · {snapshot.OriginalVolume}",
                    Tag = snapshot
                });
            }
            panel.Children.Add(selector);
            var listing = new TextBlock
            {
                Text = snapshots.Count == 0
                    ? "No accessible shadow copies reported by Windows."
                    : "Select a snapshot, then inspect the active pane's folder.",
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            };
            var inspect = new Button { Content = "Inspect current folder (read-only)", IsEnabled = snapshots.Count > 0 };
            inspect.Click += async (_, _) =>
            {
                if (selector.SelectedItem is not ComboBoxItem { Tag: Meridian.Core.TimeVault.ShadowCopy selected }) return;
                inspect.IsEnabled = false;
                listing.Text = "Reading snapshot folder…";
                try
                {
                    var entries = await new Meridian.Core.TimeVault.SnapshotDirectoryReader().ListAsync(selected, selectedPath);
                    listing.Text = entries.Count == 0
                        ? "Snapshot folder is empty."
                        : string.Join("\n", entries.Take(250).Select(item => (item.IsDirectory ? "[Folder] " : "[File] ") + item.Name))
                            + (entries.Count > 250 ? $"\n… and {entries.Count - 250} more entries (display limited)." : "");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                    PlatformNotSupportedException or System.ComponentModel.Win32Exception)
                {
                    listing.Text = "Cannot inspect this snapshot folder: " + ex.Message;
                }
                finally { inspect.IsEnabled = true; }
            };
            panel.Children.Add(inspect);
            panel.Children.Add(new ScrollViewer { MaxHeight = 340, Content = listing });
            await new ContentDialog
            {
                Title = $"TimeVault · {snapshots.Count} snapshot(s)",
                Content = panel,
                CloseButtonText = "Close",
                XamlRoot = LeftPath.XamlRoot
            }.ShowAsync();
            GlobalStatus.Text = $"TimeVault · {snapshots.Count} snapshot(s) discovered";
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or
            System.Runtime.InteropServices.COMException or UnauthorizedAccessException or
            IOException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            GlobalStatus.Text = "TimeVault discovery unavailable · " + ex.Message;
            await new ContentDialog
            {
                Title = "TimeVault discovery unavailable",
                Content = "Windows could not enumerate shadow copies. This may be caused by permissions, WMI availability, or VSS configuration.\n\n" + ex.Message,
                CloseButtonText = "Close",
                XamlRoot = LeftPath.XamlRoot
            }.ShowAsync();
        }
        finally { _timeVaultBusy = false; }
    }

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
