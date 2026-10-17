using System.Collections.ObjectModel;
using Meridian.Models;
using Meridian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Meridian;

public sealed partial class MainWindow : Window
{
    private readonly PaneState _left;
    private readonly PaneState _right;
    private readonly WorkspaceStore _workspaceStore = new();
    private readonly ObservableCollection<BookmarkSnapshot> _bookmarks = [];
    private PaneState _activePane;
    private bool _restoringWorkspace;
    private CancellationTokenSource? _copyCancellation;

    public MainWindow()
    {
        InitializeComponent();
        BookmarksBox.ItemsSource = _bookmarks;
        _left = new PaneState("Left", LeftPath, LeftItems, LeftStatus);
        _right = new PaneState("Right", RightPath, RightItems, RightStatus);
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
        var leftPath = saved?.LeftPath is { Length: > 0 } savedLeft && Directory.Exists(savedLeft) ? savedLeft : documents;
        var rightPath = saved?.RightPath is { Length: > 0 } savedRight && Directory.Exists(savedRight) ? savedRight : downloads;

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
            await _workspaceStore.SaveAsync(new WorkspaceSnapshot(_left.CurrentPath, _right.CurrentPath, _bookmarks.ToArray()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Workspace persistence is a convenience; file browsing must continue if it fails.
            GlobalStatus.Text = "Workspace could not be saved · " + ex.Message;
        }
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
            pane.Items.Clear(); foreach (var entry in entries) pane.Items.Add(entry);
            SetPaneStatus(pane, $"{entries.Count:N0} items"); GlobalStatus.Text = "Ready · Local-only · No telemetry";
            if (!_restoringWorkspace) await PersistWorkspaceAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { SetPaneStatus(pane, ex.Message); GlobalStatus.Text = "Could not read folder · " + ex.Message; }
    }

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
    private async Task OpenSelectedAsync(PaneState pane)
    {
        if (pane.List.SelectedItem is not FileEntry selected) return;
        if (selected.IsDirectory) { await NavigateAsync(pane, selected.FullPath); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = selected.FullPath, UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { GlobalStatus.Text = "Could not open file · " + ex.Message; }
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

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var pane = _activePane;
        if (pane.List.SelectedItem is not FileEntry selected)
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
            var renamedPath = FileOperations.RenameItem(selected.FullPath, nameBox.Text);
            await NavigateAsync(pane, pane.CurrentPath, addHistory: false);
            var renamedName = Path.GetFileName(renamedPath);
            var renamedItem = pane.Items.FirstOrDefault(item => string.Equals(item.FullPath, renamedPath, StringComparison.OrdinalIgnoreCase));
            if (renamedItem is not null) pane.List.SelectedItem = renamedItem;
            GlobalStatus.Text = $"Renamed · {selected.Name} → {renamedName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            GlobalStatus.Text = "Could not rename item · " + ex.Message;
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
            GlobalStatus.Text = string.Equals(renamedTo, selected.FullPath, StringComparison.OrdinalIgnoreCase)
                ? "Name unchanged."
                : $"Renamed · {Path.GetFileName(renamedTo)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            GlobalStatus.Text = "Could not rename item · " + ex.Message;
        }
    }

    private void CancelCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_copyCancellation is null) return;
        _copyCancellation.Cancel();
        GlobalStatus.Text = "Cancelling copy…";
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

    private sealed class PaneState(string name, TextBox pathBox, ListView list, TextBlock status)
    {
        public string Name { get; } = name;
        public TextBox PathBox { get; } = pathBox;
        public ListView List { get; } = list;
        public TextBlock Status { get; } = status;
        public ObservableCollection<FileEntry> Items { get; } = [];
        public Stack<string> BackHistory { get; } = new();
        public Stack<string> ForwardHistory { get; } = new();
        public string CurrentPath { get; set; } = string.Empty;
        public CancellationTokenSource? ReadCancellation { get; set; }
    }
}
